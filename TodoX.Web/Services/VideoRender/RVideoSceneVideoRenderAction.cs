using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// UI-facing action for the manual per-scene "Tạo lại video" (scene video render / re-render).
/// It does NOT introduce a second render implementation: it reuses the canonical eligibility guard
/// (<see cref="IVideoRenderEligibilityService"/>), the canonical video model policy
/// (<see cref="RVideoVideoModelPolicy"/>) and enqueues the shared <c>render_video_batch</c> work item
/// handled by <c>SceneVideoRenderHandler</c> through
/// <see cref="IRenderJobService.EnqueueForSceneIfNoneActiveAsync"/> so a double-click cannot create
/// two provider jobs for the same scene.
/// </summary>
public interface IRVideoSceneVideoRenderAction
{
    /// <summary>
    /// Cost/readiness preflight mirrored from the legacy "Render lại video" confirmation.
    /// Uses the canonical initial-point estimate for the scene duration.
    /// </summary>
    Task<RVideoSceneVideoRenderEstimate> EstimateAsync(RVideoSceneVideoRenderRequest request, CancellationToken ct = default);

    Task<RVideoSceneVideoRenderResult> EnqueueRerenderAsync(RVideoSceneVideoRenderRequest request, CurrentUserSession user, CancellationToken ct = default);
}

/// <summary>Baseline-aware inputs for a scene video (re-)render submission.</summary>
public sealed record RVideoSceneVideoRenderRequest(
    long ProjectId,
    VideoProjectSceneDto Scene,
    ScenePromptMetadata Metadata,
    Guid? ServiceId = null,
    Guid? AcknowledgedVersionId = null,
    Guid? AcknowledgedRenderJobId = null,
    bool ExplicitRiskAcknowledgement = false);

/// <summary>Cost preflight mirrored from the legacy video confirmation.</summary>
public sealed record RVideoSceneVideoRenderEstimate(
    decimal VideoRate,
    decimal EstimatedPoints,
    decimal AvailablePoints,
    decimal RemainingPoints,
    decimal MissingPoints,
    bool CanStart)
{
    public static readonly RVideoSceneVideoRenderEstimate Unavailable = new(0m, 0m, 0m, 0m, 0m, false);
}

public sealed record RVideoSceneVideoRenderResult(
    bool Success,
    bool AlreadyActive,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? RenderJobId = null)
{
    public static RVideoSceneVideoRenderResult Ok(Guid renderJobId)
        => new(true, false, null, null, renderJobId);
    public static RVideoSceneVideoRenderResult Active()
        => new(true, true, null, null, null);
    public static RVideoSceneVideoRenderResult Fail(string code, string message)
        => new(false, false, code, message);
}

public sealed class RVideoSceneVideoRenderAction : IRVideoSceneVideoRenderAction
{
    private readonly IRenderJobService _jobs;
    private readonly IRVideoTrustedPayerContextService _payers;
    private readonly IVideoRenderEligibilityService _eligibility;
    private readonly IRVideoInitialPointEstimateService _estimate;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly ISceneMediaVersioningService _versions;
    private readonly VideoRenderRepository _repo;
    private readonly ILogger<RVideoSceneVideoRenderAction> _logger;

    public RVideoSceneVideoRenderAction(
        IRenderJobService jobs,
        IRVideoTrustedPayerContextService payers,
        IVideoRenderEligibilityService eligibility,
        IRVideoInitialPointEstimateService estimate,
        RVideoJobSettingsRepository settings,
        ISceneMediaVersioningService versions,
        VideoRenderRepository repo,
        ILogger<RVideoSceneVideoRenderAction> logger)
    {
        _jobs = jobs;
        _payers = payers;
        _eligibility = eligibility;
        _estimate = estimate;
        _settings = settings;
        _versions = versions;
        _repo = repo;
        _logger = logger;
    }

    public async Task<RVideoSceneVideoRenderEstimate> EstimateAsync(RVideoSceneVideoRenderRequest request, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(request.ProjectId, ct);
        if (project is null || request.Scene is null) return RVideoSceneVideoRenderEstimate.Unavailable;
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

            // Legacy video confirmation: points = duration * VideoRate, availability from the estimate.
            var duration = (decimal)request.Scene.DurationSeconds;
            var points = duration * estimate.VideoRate;
            var available = estimate.AvailablePoints;
            if (estimate.VideoRate <= 0 || duration <= 0 || available < 0)
                return RVideoSceneVideoRenderEstimate.Unavailable;
            return new RVideoSceneVideoRenderEstimate(
                estimate.VideoRate,
                points,
                available,
                Math.Max(0m, available - points),
                Math.Max(0m, points - available),
                points <= available);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RVIDEO_SCENE_VIDEO_ESTIMATE_FAILED projectId={ProjectId} sceneId={SceneId}", request.ProjectId, request.Scene?.Id);
            return RVideoSceneVideoRenderEstimate.Unavailable;
        }
    }

    public async Task<RVideoSceneVideoRenderResult> EnqueueRerenderAsync(RVideoSceneVideoRenderRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        if (user is null) return RVideoSceneVideoRenderResult.Fail("RVIDEO_USER_REQUIRED", "Phiên đăng nhập không hợp lệ.");
        if (request.Scene is null) return RVideoSceneVideoRenderResult.Fail("RVIDEO_SCENE_REQUIRED", "Không tìm thấy scene.");

        var scene = request.Scene;

        // An unresolved provider submission may already have created a task. Until a
        // snapshot acknowledgement can be checked atomically with enqueue, fail closed
        // even for direct callers; the UI warning alone is not authorization to submit.
        var versions = await _versions.ListSceneVideoVersionsAsync(scene.Id, user, ct: ct);
        var latest = versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
        if (latest is not null && string.Equals(latest.Status, "pending_reconciliation", StringComparison.OrdinalIgnoreCase))
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_SUBMIT_RECONCILIATION_PENDING",
                "Lần gửi video trước chưa được xác minh. Vui lòng tải lại và liên hệ hỗ trợ trước khi tạo attempt mới.");

        // Canonical eligibility guard: requires a selected (or shared-reference) source image, a
        // non-empty valid video prompt and no already-active video for the scene. Completed output is retained during an explicit rerender.
        var report = await _eligibility.GetVideoRenderEligibilityAsync(request.ProjectId, new[] { scene.Id }, ct);
        var decision = report.Results.FirstOrDefault(x => x.SceneId == scene.Id);
        if (decision is null)
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_SCENE_REQUIRED", "Không tìm thấy scene.");
        if (!RVideoRerenderRequestPolicy.CanProceed(decision.Status, PointBillingIntent.UserRerender))
            return RVideoSceneVideoRenderResult.Fail(decision.ErrorCode, decision.Message);

        var estimate = await EstimateAsync(request, ct);
        if (!estimate.CanStart)
            return RVideoSceneVideoRenderResult.Fail(estimate.VideoRate <= 0 ? "RVIDEO_ESTIMATE_UNAVAILABLE" : "INSUFFICIENT_POINTS",
                $"Không đủ điểm để render lại video. Thiếu {estimate.MissingPoints:0.##} điểm.");

        var settings = await _settings.GetAsync(request.ProjectId, ct);
        var trustedPayerContext = await _payers.BuildRVideoTrustedPayerContextAsync(request.ProjectId, scene.Id, ct);

        var input = new SceneVideoRenderInput
        {
            ProjectId = request.ProjectId,
            SceneIds = new[] { scene.Id },
            AspectRatio = ResolveAspectRatio(request),
            Resolution = ResolveResolution(request),
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            TrustedPayerContext = trustedPayerContext,
            BillingIntent = PointBillingIntent.UserRerender
        };

        ApplySharedReference(input, settings);

        var meta = request.Metadata ?? ScenePromptMetadata.FromScene(scene);
        if (!RVideoRerenderRequestPolicy.ApplySavedOverride(meta, input))
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_VIDEO_OVERRIDE_INVALID", "Model hoặc thời lượng video đã lưu không hợp lệ.");
        var modelCode = input.RequestedModelCode;

        var (job, alreadyActive) = await _jobs.EnqueueGuardedVideoRerenderAsync(new RenderJobCreateModel
        {
            JobType = SceneVideoRenderHandler.JobTypeName,
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            Input = input,
            Prompt = new { projectId = request.ProjectId, sceneId = scene.Id },
            LogCode = $"video-{request.ProjectId}",
            ProviderCode = RVideoVideoModelPolicy.ProviderCode,
            ModelCode = modelCode ?? RVideoVideoModelPolicy.GetInitial().Model,
            MaxAttempts = 1,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.Pending
        }, request.ProjectId, scene.Id, request.AcknowledgedVersionId, request.AcknowledgedRenderJobId, request.ExplicitRiskAcknowledgement, ct);

        if (alreadyActive)
            return RVideoSceneVideoRenderResult.Active();

        await _repo.AddProjectEventAsync(request.ProjectId, "RVIDEO_VIDEO_RERENDER_ENQUEUED", "info",
            $"Đã enqueue render lại video cho scene {scene.SceneIndex}.",
            new { projectId = request.ProjectId, sceneId = scene.Id, sceneIndex = scene.SceneIndex, jobId = job.Id, provider = RVideoVideoModelPolicy.ProviderCode, model = modelCode ?? RVideoVideoModelPolicy.GetInitial().Model }, ct);

        _logger.LogInformation("RVIDEO_SCENE_VIDEO_RERENDER_ENQUEUED projectId={ProjectId} sceneId={SceneId} jobId={JobId}",
            request.ProjectId, scene.Id, job.Id);

        return RVideoSceneVideoRenderResult.Ok(job.Id);
    }

    private static void ApplySharedReference(SceneVideoRenderInput input, RVideoJobSettingsDto? settings)
    {
        if (settings is null || !settings.UseReferenceImageForAllScenes) return;
        try
        {
            var reference = RVideoSceneImageReferenceSelection.Resolve(settings);
            if (reference.ReferenceRequested)
                input.ApplySharedReferenceImage(reference.ToSnapshot());
        }
        catch (InvalidOperationException)
        {
            // Reference requested but unavailable: eligibility already guarantees a usable image source.
        }
    }

    private static string ResolveAspectRatio(RVideoSceneVideoRenderRequest request)
        => request.Metadata is not null && request.Metadata.Extra.TryGetValue("aspect_ratio", out var ar) && !string.IsNullOrWhiteSpace(ar)
            ? ar
            : "9:16";

    private static string ResolveResolution(RVideoSceneVideoRenderRequest request)
        => request.Metadata is not null && request.Metadata.Extra.TryGetValue("resolution", out var res) && !string.IsNullOrWhiteSpace(res)
            ? res
            : "720P";
}
