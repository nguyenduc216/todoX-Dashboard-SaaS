using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
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
    bool ExplicitRiskAcknowledgement = false)
{
    public bool ForSupersede { get; init; }
    public RVideoSceneVideoOperationSettings? ConfirmedSettings { get; init; }
}

/// <summary>Cost preflight mirrored from the legacy video confirmation.</summary>
public sealed record RVideoSceneVideoRenderEstimate(
    decimal VideoRate,
    decimal EstimatedPoints,
    decimal AvailablePoints,
    decimal RemainingPoints,
    decimal MissingPoints,
    bool CanStart)
{
    public RVideoSceneVideoOperationSettings? OperationSettings { get; init; }
    public string? ErrorMessage { get; init; }
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
    private readonly IVideoProviderRoutingService? _routing;
    private readonly IPointPricingService? _pointPricing;
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
        IAiProviderModelService? models = null,
        IVideoProviderRoutingService? routing = null,
        IPointPricingService? pointPricing = null)
    {
        _routing = routing;
        _pointPricing = pointPricing;
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
        if (request.ForSupersede) return await EstimateSupersedeAsync(request, ct);
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
        string? modelCode;
        try { modelCode = ResolveRequestedModel(meta); }
        catch (ArgumentException)
        {
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_VIDEO_OVERRIDE_INVALID", "Model hoặc thời lượng video đã lưu không hợp lệ.");
        }
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
        if (request.ConfirmedSettings is not { } confirmed)
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_QUOTE_CHANGED", "Vui lòng báo giá lại và xác nhận cấu hình video.");
        var project = await _repo.GetProjectAsync(request.ProjectId, user, ct) ?? throw new UnauthorizedAccessException();
        var report = await _eligibility.GetVideoRenderEligibilityAsync(project.Id, new[] { request.Scene.Id }, ct);
        var decision = report.Results.Single(r => r.SceneId == request.Scene.Id);
        if (decision.Status is VideoRenderEligibilityStatus.MissingImage or VideoRenderEligibilityStatus.InvalidPrompt)
            return RVideoSceneVideoRenderResult.Fail(decision.ErrorCode, decision.Message);
        var current = await EstimateSupersedeAsync(request, ct);
        if (current.OperationSettings != confirmed)
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_QUOTE_CHANGED", "Cấu hình hoặc giá video đã thay đổi. Vui lòng báo giá lại và xác nhận.");
        if (!current.CanStart)
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_ESTIMATE_UNAVAILABLE", "Chưa thể tạo lượt video mới. Vui lòng kiểm tra số điểm.");
        var input = confirmed.CreateWorkItem();
        input.ConfirmedOperationSettings = confirmed;
        input.TrustedPayerContext = await _payers.BuildRVideoTrustedPayerContextAsync(request.ProjectId, request.Scene.Id, ct);
        try
        {
            var draft = await service.PrepareAsync(acknowledgement, user, input, ct);
            var jobId = await service.ActivateAsync(acknowledgement, user, draft.TransitionId, ct);
            return RVideoSceneVideoRenderResult.Ok(jobId);
        }
        catch (InvalidOperationException ex) when (ex.Message == "RVIDEO_QUOTE_CHANGED")
        {
            return RVideoSceneVideoRenderResult.Fail("RVIDEO_QUOTE_CHANGED", "Cấu hình video đã thay đổi. Vui lòng báo giá lại.");
        }
    }

    // FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261025-RVIDEO-PRICING-SNAPSHOT-002.
    // One saved-settings/point-rate snapshot supplies both consent and the generation-owned work item.
    private async Task<RVideoSceneVideoRenderEstimate> EstimateSupersedeAsync(RVideoSceneVideoRenderRequest request, CancellationToken ct)
    {
        try
        {
            if (_routing is null || _pointPricing is null || _models is null)
                throw new InvalidOperationException("Video pricing services unavailable.");
            var project = await _repo.GetProjectAsync(request.ProjectId, ct)
                ?? throw new InvalidOperationException("Video project unavailable.");
            var scene = project.Scenes.Single(s => s.Id == request.Scene.Id);
            // The page must return the saved scene explicitly; do not silently quote a stale DTO.
            if (scene.ScenePrompt != request.Scene.ScenePrompt || scene.VideoPrompt != request.Scene.VideoPrompt
                || scene.DurationSeconds != request.Scene.DurationSeconds)
                return RVideoSceneVideoRenderEstimate.Unavailable with { ErrorMessage = "Scene đã thay đổi. Vui lòng tải lại và báo giá lại." };
            var metadata = ScenePromptMetadata.FromScene(scene);
            var selection = ResolveRequestedSelection(metadata);
            var modelCode = selection.Model ?? RVideoVideoModelPolicy.GetInitial().Model;
            var mode = selection.Mode ?? RVideoVideoModelPolicy.Models.FirstOrDefault(p => p.Model == modelCode)?.Mode;
            var duration = scene.DurationSeconds;
            if (metadata.Extra.TryGetValue("video_duration", out var durationText)
                && (!int.TryParse(durationText, out duration) || duration <= 0))
                throw new InvalidOperationException("Invalid video duration.");
            var route = await _routing.ResolveAsync(RVideoVideoModelPolicy.CapabilityCode, ct: ct);
            var catalog = await _models.GetModelByCodeAsync(route.ProviderId, modelCode, ct);
            var aspect = ResolveAspectRatio(request with { Metadata = metadata });
            var resolution = ResolveResolution(request with { Metadata = metadata }).Trim().ToLowerInvariant();
            if (catalog is null || !catalog.Enabled || !catalog.AllowUserSelect || catalog.IsDeprecated
                || catalog.ProviderCode != route.ProviderCode || catalog.MediaType != "video"
                || route.ProviderCode != RVideoVideoModelPolicy.ProviderCode
                || route.CapabilityCode != RVideoVideoModelPolicy.CapabilityCode
                || !catalog.SupportedDurations.Contains(duration)
                || !RVideoRules.SupportedAspectRatios.Contains(aspect)
                || !RVideoRules.SupportedResolutions.Contains(resolution, StringComparer.OrdinalIgnoreCase)
                || (catalog.SupportedRatios.Count > 0 && !catalog.SupportedRatios.Contains(aspect, StringComparer.OrdinalIgnoreCase))
                || (catalog.SupportedResolutions.Count > 0 && !catalog.SupportedResolutions.Contains(resolution, StringComparer.OrdinalIgnoreCase))
                || (catalog.SupportedModes.Count > 0 && !catalog.SupportedModes.Contains(mode ?? "", StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Invalid video model/options.");
            // Same configured route tier and points service as the canonical scene-video handler.
            // Provider catalog cost is not the customer's point rate; do not convert or invent a markup.
            var tier = SceneVideoRenderHandler.ResolveQualityTier(route);
            var rate = await _pointPricing.ResolveRateAsync(request.ServiceId, PointPricingResourceTypes.Video, tier, ct);
            if (rate.Rate <= 0 || !string.Equals(rate.Unit, "per_second", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Video point rate must be configured per_second.");
            var settings = await _settings.GetAsync(project.Id, ct);
            var voiceMode = RVideoRules.ResolveVoiceMode(settings);
            var voice = voiceMode == RVideoVoiceModes.Native ? RVideoRules.ResolveSceneVoiceText(scene) : null;
            var instruction = voiceMode == RVideoVoiceModes.Native ? RVideoRules.ResolveSceneVoiceInstruction(scene) : null;
            var points = RVideoSceneVideoCompletionService.CalculateActualVideoPoints(duration, rate.Rate);
            var input = new SceneVideoRenderWorkItemInput
            {
                ProjectId = project.Id, SceneId = scene.Id, SceneIndex = scene.SceneIndex,
                ProviderId = route.ProviderId, ProviderCode = route.ProviderCode,
                ProviderCapabilityId = route.ProviderCapabilityId, CapabilityCode = route.CapabilityCode,
                ProviderConfigJson = route.ProviderConfigJson, CapabilityConfigJson = route.CapabilityConfigJson,
                ModelName = modelCode, RequestedModelCode = modelCode, RequestedMode = mode,
                DurationSeconds = duration, RequestedDurationSeconds = duration,
                ManualOverride = true, StrictRequestedSettings = true, AspectRatio = aspect, Resolution = resolution,
                ImagePrompt = scene.ImagePrompt, Voice = voice, VoiceInstruction = instruction,
                VideoPrompt = (voiceMode == RVideoVoiceModes.Native
                    ? RVideoRules.ComposeNativeVoicePrompt(scene.VideoPrompt, voice, instruction) : scene.VideoPrompt ?? "").Trim(),
                CustomerPointRate = rate.Rate, CustomerPointQuality = rate.QualityTier, EstimatedPoints = points,
                PricingMode = rate.Unit, CostSource = rate.Source,
                UseSharedReferenceImage = settings?.UseReferenceImageForAllScenes == true
            };
            input.ImageInputMode = input.UseSharedReferenceImage ? VideoSceneImageInputMode.SharedBaseImage : VideoSceneImageInputMode.SceneSource;
            if (input.UseSharedReferenceImage)
            {
                var source = RVideoSceneImageReferenceSelection.Resolve(settings!).ToSnapshot();
                input.SharedReferenceImageMediaId = source.MediaId; input.SharedReferenceImageUrl = source.PublicUrl;
                input.SharedReferenceImageObjectKey = source.ObjectKey; input.SharedReferenceImageFileName = source.FileName;
                input.SharedReferenceImageMimeType = source.MimeType;
            }
            else
            {
                var source = await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
                input.SelectedSourceImageVersionId = source?.Id; input.SourceImageVersionId = source?.Id;
                input.SourceImageUrl = source?.PublicUrl ?? scene.StaticImageUrl;
                input.SourceImageObjectKey = source?.SourceFilePath ?? scene.StaticImagePath;
            }
            if (input.UseSharedReferenceImage)
                input.VideoPrompt = RVideoSharedBaseImagePromptGuard.Apply(input.VideoPrompt, true);
            input.TariffSnapshotJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                provider = input.ProviderCode, model = input.ModelName, mode = input.RequestedMode,
                durationSeconds = duration, input.AspectRatio, input.Resolution,
                customerPointRate = rate.Rate, qualityTier = rate.QualityTier, rateUnit = rate.Unit,
                rateSource = rate.Source, serviceId = request.ServiceId, estimatedPoints = points
            });
            var quote = RVideoSceneVideoOperationSettings.Capture(scene, input, catalog.DisplayName, request.ServiceId, rate);
            var estimate = await _estimate.EstimateInitialRVideoPointsAsync(new RVideoInitialPointEstimateRequest(
                Guid.NewGuid(), Guid.NewGuid(), project.Id, request.ServiceId, project.CustomerId, project,
                new[] { scene }, Array.Empty<VideoProjectSceneDto>(), settings, null,
                ServiceSellPriceQualityTiers.Premium, tier, ServiceSellPriceQualityTiers.Standard), ct);
            return new(rate.Rate, points, estimate.AvailablePoints,
                Math.Max(0m, estimate.AvailablePoints - points), Math.Max(0m, points - estimate.AvailablePoints),
                !string.IsNullOrWhiteSpace(input.VideoPrompt) && points <= estimate.AvailablePoints)
                { OperationSettings = quote };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RVIDEO_SUPERSEDE_QUOTE_FAILED projectId={ProjectId} sceneId={SceneId}", request.ProjectId, request.Scene.Id);
            return RVideoSceneVideoRenderEstimate.Unavailable with { ErrorMessage = "Không thể báo giá cấu hình video này. Vui lòng kiểm tra model, thời lượng và bảng giá." };
        }
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
