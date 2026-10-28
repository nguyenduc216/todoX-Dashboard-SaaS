using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// UI-facing action for the manual per-scene "Tạo lại ảnh" (scene image render / re-render).
/// It does NOT introduce a second render implementation: it drives the same canonical pipeline the
/// legacy RenderVideoJobs page uses — it creates a queued immutable scene-image version through
/// <see cref="ISceneMediaVersioningService"/> and enqueues the shared
/// <c>render_scene_image</c> work item handled by <c>SceneImageRenderWorkItemHandler</c>.
/// Permission, trusted-payer resolution, idempotency (logical request id) and the billing intent are
/// preserved exactly as in the legacy flow.
/// </summary>
public interface IRVideoSceneImageRenderAction
{
    /// <summary>
    /// Preflight cost/readiness estimate for one manual scene image (re-)render. Mirrors the legacy
    /// confirmation dialog: the UI shows the estimate and asks for confirmation before calling
    /// <see cref="EnqueueRerenderAsync"/>.
    /// </summary>
    Task<RVideoSceneImageRenderEstimate> EstimateAsync(RVideoSceneImageRenderRequest request, CancellationToken ct = default);

    Task<RVideoSceneImageRenderResult> EnqueueRerenderAsync(RVideoSceneImageRenderRequest request, CurrentUserSession user, CancellationToken ct = default);
}

/// <summary>Baseline-aware inputs for a scene image (re-)render submission.</summary>
public sealed record RVideoSceneImageRenderRequest(
    long ProjectId,
    VideoProjectSceneDto Scene,
    ScenePromptMetadata Metadata,
    string AspectRatio,
    Guid? ServiceId = null,
    Guid? CoreJobId = null);

/// <summary>Cost preflight mirrored from the legacy "Render lại hình" confirmation.</summary>
public sealed record RVideoSceneImageRenderEstimate(
    decimal ImageRate,
    decimal AvailablePoints,
    decimal RemainingPoints,
    decimal MissingPoints,
    bool CanStart)
{
    public static readonly RVideoSceneImageRenderEstimate Unavailable = new(0m, 0m, 0m, 0m, false);
}

public sealed record RVideoSceneImageRenderResult(
    bool Success,
    bool AlreadyActive,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? ImageVersionId = null,
    Guid? RenderJobId = null,
    string? LogicalRequestId = null)
{
    public static RVideoSceneImageRenderResult Ok(Guid imageVersionId, Guid renderJobId, string logicalRequestId)
        => new(true, false, null, null, imageVersionId, renderJobId, logicalRequestId);
    public static RVideoSceneImageRenderResult Active(string logicalRequestId)
        => new(true, true, null, null, null, null, logicalRequestId);
    public static RVideoSceneImageRenderResult Fail(string code, string message)
        => new(false, false, code, message);
}

public sealed class RVideoSceneImageRenderAction : IRVideoSceneImageRenderAction
{
    private readonly IRenderJobService _jobs;
    private readonly ISceneMediaVersioningService _versions;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly ISceneImageRenderService _sceneImages;
    private readonly IRVideoInitialPointEstimateService _estimate;
    private readonly VideoRenderRepository _repo;
    private readonly ILogger<RVideoSceneImageRenderAction> _logger;

    public RVideoSceneImageRenderAction(
        IRenderJobService jobs,
        ISceneMediaVersioningService versions,
        RVideoJobSettingsRepository settings,
        ISceneImageRenderService sceneImages,
        IRVideoInitialPointEstimateService estimate,
        VideoRenderRepository repo,
        ILogger<RVideoSceneImageRenderAction> logger)
    {
        _jobs = jobs;
        _versions = versions;
        _settings = settings;
        _sceneImages = sceneImages;
        _estimate = estimate;
        _repo = repo;
        _logger = logger;
    }

    public async Task<RVideoSceneImageRenderEstimate> EstimateAsync(RVideoSceneImageRenderRequest request, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(request.ProjectId, ct);
        if (project is null) return RVideoSceneImageRenderEstimate.Unavailable;
        try
        {
            var billingScenes = project.Scenes.OrderBy(x => x.SceneIndex).ToArray();
            var estimate = await _estimate.EstimateInitialRVideoPointsAsync(new RVideoInitialPointEstimateRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                request.ProjectId,
                request.ServiceId,
                project.CustomerId,
                project,
                billingScenes,
                billingScenes,
                await _settings.GetAsync(request.ProjectId, ct),
                async (scene, token) => await _versions.GetSelectedImageVersionAsync(scene.Id, token),
                TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Premium,
                TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Standard,
                TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Standard), ct);

            // Legacy "Render lại hình" charges a single image: estimated = ImageRate, availability from the estimate.
            var estimated = estimate.ImageRate;
            var available = estimate.AvailablePoints;
            if (estimated <= 0 || available < 0)
                return RVideoSceneImageRenderEstimate.Unavailable;
            return new RVideoSceneImageRenderEstimate(
                estimated,
                available,
                Math.Max(0m, available - estimated),
                Math.Max(0m, estimated - available),
                estimated <= available);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RVIDEO_SCENE_IMAGE_ESTIMATE_FAILED projectId={ProjectId} sceneId={SceneId}", request.ProjectId, request.Scene?.Id);
            return RVideoSceneImageRenderEstimate.Unavailable;
        }
    }

    public async Task<RVideoSceneImageRenderResult> EnqueueRerenderAsync(RVideoSceneImageRenderRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        if (user is null) return RVideoSceneImageRenderResult.Fail("RVIDEO_USER_REQUIRED", "Phiên đăng nhập không hợp lệ.");
        if (request.Scene is null) return RVideoSceneImageRenderResult.Fail("RVIDEO_SCENE_REQUIRED", "Không tìm thấy scene.");

        var scene = request.Scene;
        var draft = request.Metadata ?? ScenePromptMetadata.FromScene(scene);
        if (string.IsNullOrWhiteSpace(draft.ImagePrompt))
            return RVideoSceneImageRenderResult.Fail("SCENE_IMAGE_PROMPT_EMPTY", "Cần nhập prompt ảnh trước khi render.");

        // One active image version per scene: mirror the legacy guard (HasActiveImageVersionAsync).
        if (await _versions.HasActiveImageVersionAsync(scene.Id, ct))
            return RVideoSceneImageRenderResult.Fail("SCENE_IMAGE_ACTIVE", $"Scene {scene.SceneIndex} đang có phiên bản ảnh được xử lý.");

        var settings = await _settings.GetAsync(request.ProjectId, ct)
            ?? throw new InvalidOperationException("RVVIDEO_SETTINGS_NOT_FOUND");
        var reference = RVideoSceneImageReferenceSelection.Resolve(settings);

        var model = RVideoImageModelPolicy.GetInitial();
        var promptScene = new VideoProjectSceneDto
        {
            Id = scene.Id,
            ProjectId = scene.ProjectId,
            SceneIndex = scene.SceneIndex,
            Title = scene.Title,
            DurationSeconds = scene.DurationSeconds,
            ScenePrompt = draft.Serialize(),
            ImagePrompt = draft.ImagePrompt,
            VideoPrompt = draft.MotionPrompt
        };
        var prompt = SceneImagePromptBuilder.Build(promptScene, reference.CharacterPrompt);

        var referenceMediaId = await _sceneImages.ResolveCharacterReferenceMediaIdAsync(
            request.ProjectId, reference.Url, reference.ObjectKey,
            user.UserId, user.CustomerId,
            requireReference: reference.ReferenceRequested, ct: ct);

        if (reference.ReferenceRequested && referenceMediaId is null)
            return RVideoSceneImageRenderResult.Fail("RVIDEO_REFERENCE_IMAGE_UNAVAILABLE", "Không tìm thấy ảnh tham chiếu nhân vật.");

        var estimate = await EstimateAsync(request, ct);
        if (!estimate.CanStart)
            return RVideoSceneImageRenderResult.Fail(estimate.ImageRate <= 0 ? "RVIDEO_ESTIMATE_UNAVAILABLE" : "INSUFFICIENT_POINTS",
                $"Không đủ điểm để render lại ảnh. Thiếu {estimate.MissingPoints:0.##} điểm.");
var logicalRequestId = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", scene.Id, null);
        var operationId = Guid.NewGuid();

        SceneImageVersionDto version;
        try
        {
        version = await _versions.CreateQueuedImageVersionAsync(new SceneImageVersionCreateRequest(
            request.ProjectId, scene.Id, user.UserId, user.CustomerId,
            operationId, logicalRequestId, draft.ImagePrompt, prompt, draft.MotionPrompt, null,
            new
            {
                scene.Id,
                scene.ProjectId,
                scene.SceneIndex,
                scene.Title,
                scene.DurationSeconds,
                scene.ScenePrompt,
                imagePrompt = draft.ImagePrompt,
                videoPrompt = draft.MotionPrompt
            },
            new
            {
                characterId = reference.CharacterId,
                referenceMediaId,
                referenceUrl = reference.Url,
                referenceObjectKey = reference.ObjectKey,
                referenceSource = reference.Source,
                characterPrompt = reference.CharacterPrompt
            },
            new
            {
                capability = SceneImageRenderContext.RVideoCapabilityCode,
                aspectRatio = SceneImageRenderService.NormalizeAspectRatio(request.AspectRatio),
                outputFormat = "png",
                source = "scene_image_user_rerender",
                model = model.Model,
                model.Mode,
                model.Resolution,
                modelAttemptIndex = model.AttemptIndex,
                customerPointRate = estimate.ImageRate,
                customerPointQuality = string.Equals(model.Mode, "vip", StringComparison.OrdinalIgnoreCase)
                    ? TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Premium
                    : TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Standard,
                billingIntent = PointBillingIntent.UserRerender,
                billingOperationId = operationId
            }), ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "SCENE_IMAGE_ACTIVE")
        {
            return RVideoSceneImageRenderResult.Fail("SCENE_IMAGE_ACTIVE", $"Scene {scene.SceneIndex} đang có phiên bản ảnh được xử lý.");
        }

        RenderJobDto job;
        try
        {
        job = await _jobs.EnqueueAsync(new RenderJobCreateModel
        {
            JobType = SceneImageRenderWorkItemHandler.JobTypeName,
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            Input = new SceneImageRenderWorkItemInput
            {
                ParentJobId = operationId,
                ImageVersionId = version.Id,
                ProjectId = request.ProjectId,
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                UserId = user.UserId,
                CustomerId = user.CustomerId,
                CreatedBy = user.Email ?? user.UserId.ToString(),
                TrustedPayerContext = AiBillingPayerResolver.CreateTrustedBackgroundContext(user),
                Prompt = version.CompiledImagePromptSnapshot ?? prompt,
                AspectRatio = SceneImageRenderService.NormalizeAspectRatio(request.AspectRatio),
                CharacterId = reference.CharacterId,
                ReferenceMediaId = referenceMediaId,
                ReferenceObjectKey = reference.ObjectKey,
                ReferenceUrl = reference.Url,
                CapabilityCode = SceneImageRenderContext.RVideoCapabilityCode,
                LogicalRequestId = logicalRequestId,
                RequestedModel = model.Model,
                ModelAttemptIndex = model.AttemptIndex,
                BillingIntent = PointBillingIntent.UserRerender,
                BillingReferenceId = operationId,
                CustomerPointRate = estimate.ImageRate,
                CustomerPointQuality = string.Equals(model.Mode, "vip", StringComparison.OrdinalIgnoreCase)
                    ? TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Premium
                    : TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Standard
            },
            Prompt = new { projectId = request.ProjectId, sceneId = scene.Id, imageVersionId = version.Id },
            References = Array.Empty<object>(),
            LogCode = $"video-{request.ProjectId}",
            ProviderCode = SceneImageBatchRenderHandler.RoutingProviderCode,
            ModelCode = SceneImageBatchRenderHandler.RoutingModelCode,
            MaxAttempts = 100,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        }, ct);
        }
        catch (Exception ex)
        {
            // RVID-UI-V2-SCENE-RERENDER-HISTORY / TODOX-20261014-RVIDEO-RERENDER-HARDENING-001:
            // A committed job may exist even when event insertion throws. Never blindly fail the version.
            _logger.LogError(ex, "RVIDEO_SCENE_IMAGE_ENQUEUE_FAILED versionId={VersionId}", version.Id);
            try
            {
                var persistedJobId = await _versions.FindImageRenderJobAsync(version.Id, CancellationToken.None);
                if (persistedJobId is not null)
                    return RVideoSceneImageRenderResult.Ok(version.Id, persistedJobId.Value, logicalRequestId);
                // An empty SELECT is not proof that an in-flight INSERT cannot still commit.
                await _versions.MarkImageEnqueueUnknownAsync(version.Id, CancellationToken.None);
            }
            catch (Exception recoveryError)
            {
                _logger.LogError(recoveryError, "RVIDEO_SCENE_IMAGE_ENQUEUE_OUTCOME_UNKNOWN versionId={VersionId}", version.Id);
                // RVID-UI-V2-SCENE-RERENDER-HISTORY / TODOX-20261014-RVIDEO-RERENDER-HARDENING-001:
                // Lookup failure must not leave an untracked queued version when the status write is available.
                try
                {
                    await _versions.MarkImageEnqueueUnknownAsync(version.Id, CancellationToken.None);
                }
                catch (Exception statusError)
                {
                    _logger.LogError(statusError, "RVIDEO_SCENE_IMAGE_ENQUEUE_UNKNOWN_PERSIST_FAILED versionId={VersionId}", version.Id);
                }
            }
            return RVideoSceneImageRenderResult.Fail("RENDER_JOB_ENQUEUE_UNKNOWN", "Chưa xác định được trạng thái hàng đợi; cần đối soát trước khi thử lại.");
        }

        await _repo.AddProjectEventAsync(request.ProjectId, "RVIDEO_SCENE_IMAGE_RERENDER_ENQUEUED", "info",
            $"Đã enqueue render lại ảnh cho scene {scene.SceneIndex}.",
            new { projectId = request.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, imageVersionId = version.Id, jobId = job.Id, logicalRequestId }, ct);

        _logger.LogInformation("RVIDEO_SCENE_IMAGE_RERENDER_ENQUEUED projectId={ProjectId} sceneId={SceneId} versionId={VersionId} jobId={JobId}",
            request.ProjectId, scene.Id, version.Id, job.Id);

        return RVideoSceneImageRenderResult.Ok(version.Id, job.Id, logicalRequestId);
    }
}