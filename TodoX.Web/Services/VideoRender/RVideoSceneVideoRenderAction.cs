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

    Task<SceneVideoSupersedeAcknowledgement> GetSupersedeAcknowledgementAsync(long projectId, long sceneId, CurrentUserSession user, Guid key, CancellationToken ct = default)
        => throw new NotSupportedException("Generation-aware supersede requires canonical implementation.");
    Task<RVideoSceneVideoRenderResult> SupersedeAsync(RVideoSceneVideoRenderRequest request, SceneVideoSupersedeAcknowledgement acknowledgement, CurrentUserSession user, CancellationToken ct = default)
        => throw new NotSupportedException("Generation-aware supersede requires canonical implementation.");

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
    private readonly SceneVideoOperationDraftService? _operations;
    private readonly IAiProviderModelService? _models;
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
        ILogger<RVideoSceneVideoRenderAction> logger,
        SceneVideoOperationDraftService? operations = null,
        IAiProviderModelService? models = null)
    {
        _operations = operations;
        _models = models;
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
            if (request.Metadata.Extra.TryGetValue("video_duration",out var durationText) && int.TryParse(durationText,out var selectedDuration)) duration=selectedDuration;
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
        // non-empty valid video prompt and no already-active/completed video for the scene.
        var report = await _eligibility.GetVideoRenderEligibilityAsync(request.ProjectId, new[] { scene.Id }, ct);
        var decision = report.Results.FirstOrDefault(x => x.SceneId == scene.Id);
        if (decision is null)
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_SCENE_REQUIRED", "Không tìm thấy scene.");
        if (decision.Status != VideoRenderEligibilityStatus.Eligible
            && decision.Status != VideoRenderEligibilityStatus.AlreadyCompleted)
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
        var modelCode = ResolveRequestedModel(meta);
        if (!string.IsNullOrWhiteSpace(modelCode))
        {
            input.ManualOverride = true;
            input.RequestedModelCode = modelCode;
            input.RequestedMode = ResolveRequestedMode(meta);
            if (meta.Extra.TryGetValue("video_duration", out var durStr) && int.TryParse(durStr, out var dur) && dur > 0)
                input.RequestedDurationSeconds = dur;
        }

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

    public Task<SceneVideoSupersedeAcknowledgement> GetSupersedeAcknowledgementAsync(long projectId, long sceneId,
        CurrentUserSession user, Guid key, CancellationToken ct = default)
        => (_operations ?? throw new InvalidOperationException("Operation ledger unavailable"))
            .ReadAcknowledgementAsync(projectId, sceneId, user, key, ct);

    public async Task<RVideoSceneVideoRenderResult> SupersedeAsync(RVideoSceneVideoRenderRequest request,
        SceneVideoSupersedeAcknowledgement acknowledgement, CurrentUserSession user, CancellationToken ct = default)
    {
        SceneVideoOperationDraftService.Validate(acknowledgement, user);
        if (request.ProjectId != acknowledgement.ProjectId || request.Scene.Id != acknowledgement.SceneId)
            throw new InvalidOperationException("Acknowledged scene differs from request.");
        var service = _operations ?? throw new InvalidOperationException("Operation ledger unavailable.");
        if(await service.TryResumeAsync(acknowledgement,user,ct) is Guid resumed) return RVideoSceneVideoRenderResult.Ok(resumed);
        var project = await _repo.GetProjectAsync(request.ProjectId, user, ct) ?? throw new UnauthorizedAccessException();
        var scene = project.Scenes.Single(s => s.Id == acknowledgement.SceneId);
        var report = await _eligibility.GetVideoRenderEligibilityAsync(project.Id, new[] { scene.Id }, ct);
        var decision = report.Results.Single(r => r.SceneId == scene.Id);
        if (decision.Status is VideoRenderEligibilityStatus.MissingImage or VideoRenderEligibilityStatus.InvalidPrompt)
            return RVideoSceneVideoRenderResult.Fail(decision.ErrorCode, decision.Message);
        var estimate = await EstimateAsync(request with { Scene = scene, Metadata=ScenePromptMetadata.FromScene(scene) }, ct);
        if (!estimate.CanStart) return RVideoSceneVideoRenderResult.Fail("RVIDEO_ESTIMATE_UNAVAILABLE", "Chưa thể tạo lượt video mới.");
        var oldJob = await _jobs.GetAsync(acknowledgement.OldJobId, ct) ?? throw new InvalidOperationException("Acknowledged operation missing.");
        var input = System.Text.Json.JsonSerializer.Deserialize<SceneVideoRenderWorkItemInput>(oldJob.InputJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Operation snapshot invalid.");
        input.ProjectId = scene.ProjectId;
        input.SceneId = scene.Id;
        input.SceneIndex = scene.SceneIndex;
        var settings = await _settings.GetAsync(project.Id, ct);
        var metadata = ScenePromptMetadata.FromScene(scene);
        var model = ResolveRequestedModel(metadata) ?? RVideoVideoModelPolicy.GetInitial().Model;
        var duration = scene.DurationSeconds;
        if (metadata.Extra.TryGetValue("video_duration",out var durationText) && int.TryParse(durationText,out var chosenDuration)) duration=chosenDuration;
        var catalog = _models is null ? null : await _models.GetModelByCodeAsync(input.ProviderId,model,ct);
        if(catalog is null || !catalog.Enabled || !catalog.AllowUserSelect || catalog.IsDeprecated
            || !string.Equals(catalog.ProviderCode,input.ProviderCode,StringComparison.OrdinalIgnoreCase)
            || !string.Equals(catalog.MediaType,"video",StringComparison.OrdinalIgnoreCase)
            || !catalog.SupportedDurations.Contains(duration))
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_MODEL_INVALID","Lựa chọn video hiện tại chưa thể sử dụng.");
        input.ModelName=model;
        input.RequestedModelCode=model;
        input.ManualOverride=true;
        input.RequestedMode=ResolveRequestedMode(metadata) ?? RVideoVideoModelPolicy.Models.FirstOrDefault(p=>string.Equals(p.ProviderCode,input.ProviderCode,StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Model,model,StringComparison.OrdinalIgnoreCase))?.Mode;
        input.RequestedDurationSeconds=duration;
        input.DurationSeconds=duration;
        input.AspectRatio=ResolveAspectRatio(request with { Metadata=metadata });
        input.Resolution=ResolveResolution(request with { Metadata=metadata }).Trim().ToLowerInvariant();
        if(!RVideoRules.SupportedAspectRatios.Contains(input.AspectRatio)
            || !RVideoRules.SupportedResolutions.Contains(input.Resolution,StringComparer.OrdinalIgnoreCase)
            || (catalog.SupportedResolutions.Count>0 && !catalog.SupportedResolutions.Contains(input.Resolution,StringComparer.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(input.RequestedMode) && catalog.SupportedModes.Count>0 && !catalog.SupportedModes.Contains(input.RequestedMode,StringComparer.OrdinalIgnoreCase)))
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_MODEL_INVALID","Video options are not supported by the selected model.");
        var voiceMode=RVideoRules.ResolveVoiceMode(settings);
        input.Voice=voiceMode==RVideoVoiceModes.Native ? RVideoRules.ResolveSceneVoiceText(scene) : null;
        input.VoiceInstruction=voiceMode==RVideoVoiceModes.Native ? RVideoRules.ResolveSceneVoiceInstruction(scene) : null;
        input.VideoPrompt=voiceMode==RVideoVoiceModes.Native
            ? RVideoRules.ComposeNativeVoicePrompt(scene.VideoPrompt,input.Voice,input.VoiceInstruction) : scene.VideoPrompt ?? "";
        input.ImagePrompt = scene.ImagePrompt;
        input.UseSharedReferenceImage=settings?.UseReferenceImageForAllScenes==true;
        input.ImageInputMode=input.UseSharedReferenceImage ? VideoSceneImageInputMode.SharedBaseImage : VideoSceneImageInputMode.SceneSource;
        input.SharedReferenceImageMediaId=null; input.SharedReferenceImageUrl=null; input.SharedReferenceImageObjectKey=null;
        input.SharedReferenceImageFileName=null; input.SharedReferenceImageMimeType=null;
        if(input.UseSharedReferenceImage)
        {
            var reference=RVideoSceneImageReferenceSelection.Resolve(settings!).ToSnapshot();
            input.SharedReferenceImageMediaId=reference.MediaId; input.SharedReferenceImageUrl=reference.PublicUrl;
            input.SharedReferenceImageObjectKey=reference.ObjectKey; input.SharedReferenceImageFileName=reference.FileName;
            input.SharedReferenceImageMimeType=reference.MimeType;
        }
        input.SourceImageVersionId=null; input.SelectedSourceImageVersionId=null;
        input.SourceImageUrl=null; input.SourceImageObjectKey=null;
        input.EstimatedUsd=null; input.TariffSnapshotJson=null; input.PricingRuleKey=null; input.CostSource=null;
        input.TrustedPayerContext = await _payers.BuildRVideoTrustedPayerContextAsync(project.Id, scene.Id, ct);
        input.CustomerPointRate = estimate.VideoRate;
        var source = await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
        if (!input.UseSharedReferenceImage)
        {
            input.SelectedSourceImageVersionId = source?.Id;
            input.SourceImageVersionId = source?.Id;
            input.SourceImageUrl = source?.PublicUrl ?? scene.StaticImageUrl;
            input.SourceImageObjectKey = source?.SourceFilePath ?? scene.StaticImagePath;
        }
        var draft = await service.PrepareAsync(acknowledgement, user, input, ct);
        var jobId = await service.ActivateAsync(acknowledgement, user, draft.TransitionId, ct);
        return RVideoSceneVideoRenderResult.Ok(jobId);
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

    public static (string? Model,string? Mode) ResolveRequestedSelection(ScenePromptMetadata meta)
    {
        if(!meta.Extra.TryGetValue("video_model",out var selected) || string.IsNullOrWhiteSpace(selected)) return(null,null);
        selected=selected.Trim();
        // Legacy metadata sanitizes pipe separators to slashes. Resolve known catalog keys
        // before parsing, so model codes containing slashes are not split heuristically.
        foreach(var policy in RVideoVideoModelPolicy.Models)
        {
            var key=RVideoSceneVideoModelOptionsService.BuildKey(policy.ProviderCode,policy.Model,policy.Mode);
            if(string.Equals(selected,key,StringComparison.OrdinalIgnoreCase)
                || string.Equals(selected,key.Replace('|','/'),StringComparison.OrdinalIgnoreCase))
                return(policy.Model,policy.Mode);
        }
        if(selected.StartsWith(RVideoVideoModelPolicy.ProviderCode+"/",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Unknown serialized scene video model selection.");
        var parts=selected.Split('|');
        if(parts.Length==1) return(parts[0],null); // Legacy stores the model code directly.
        if(parts.Length!=3 || !string.Equals(parts[0],RVideoVideoModelPolicy.ProviderCode,StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[1])) throw new ArgumentException("Invalid scene video model selection.");
        return(parts[1].Trim(),string.IsNullOrWhiteSpace(parts[2])?null:parts[2].Trim());
    }
    private static string? ResolveRequestedModel(ScenePromptMetadata meta) => ResolveRequestedSelection(meta).Model;
    private static string? ResolveRequestedMode(ScenePromptMetadata meta) => ResolveRequestedSelection(meta).Mode;

    private static string ResolveAspectRatio(RVideoSceneVideoRenderRequest request)
        => request.Metadata is not null && request.Metadata.Extra.TryGetValue("aspect_ratio", out var ar) && !string.IsNullOrWhiteSpace(ar)
            ? ar
            : "9:16";

    private static string ResolveResolution(RVideoSceneVideoRenderRequest request)
        => request.Metadata is not null && request.Metadata.Extra.TryGetValue("resolution", out var res) && !string.IsNullOrWhiteSpace(res)
            ? res
            : "720P";
}
