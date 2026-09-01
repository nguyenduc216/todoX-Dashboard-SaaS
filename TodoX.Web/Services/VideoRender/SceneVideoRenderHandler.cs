using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneVideoRenderInput
{
    public long ProjectId { get; set; }
    public long[] SceneIds { get; set; } = Array.Empty<long>();
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720P";
    public bool UseSharedReferenceImage { get; set; }
    public string? SharedReferenceImageUrl { get; set; }
    public string? SharedReferenceImageObjectKey { get; set; }
    public Guid? SharedReferenceImageMediaId { get; set; }
    public string? SharedReferenceImageFileName { get; set; }
    public string? SharedReferenceImageMimeType { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public string? ProviderConfigJson { get; set; }
    public string? CapabilityConfigJson { get; set; }

    public void ApplySharedReferenceImage(RVideoSceneImageReferenceSelection reference)
    {
        if (!reference.ReferenceRequested
            || (string.IsNullOrWhiteSpace(reference.Url) && string.IsNullOrWhiteSpace(reference.ObjectKey)))
        {
            throw new InvalidOperationException("RVIDEO_SHARED_REFERENCE_IMAGE_REQUIRED");
        }

        UseSharedReferenceImage = true;
        SharedReferenceImageUrl = reference.Url;
        SharedReferenceImageObjectKey = reference.ObjectKey;
        SharedReferenceImageMediaId = reference.MediaId;
        SharedReferenceImageFileName = reference.FileName;
        SharedReferenceImageMimeType = reference.MimeType;
    }

    public void ApplySharedReferenceImage(string? url, string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(objectKey))
        {
            throw new InvalidOperationException("RVIDEO_SHARED_REFERENCE_IMAGE_REQUIRED");
        }

        UseSharedReferenceImage = true;
        SharedReferenceImageUrl = url;
        SharedReferenceImageObjectKey = objectKey;
    }

    public void ApplySharedReferenceImage(RVideoSharedReferenceImageSnapshot snapshot)
    {
        if (snapshot is null
            || (string.IsNullOrWhiteSpace(snapshot.PublicUrl) && string.IsNullOrWhiteSpace(snapshot.ObjectKey)))
        {
            throw new InvalidOperationException("RVIDEO_SHARED_REFERENCE_IMAGE_REQUIRED");
        }

        UseSharedReferenceImage = true;
        SharedReferenceImageMediaId = snapshot.MediaId;
        SharedReferenceImageUrl = snapshot.PublicUrl;
        SharedReferenceImageObjectKey = snapshot.ObjectKey;
        SharedReferenceImageFileName = snapshot.FileName;
        SharedReferenceImageMimeType = snapshot.MimeType;
    }
}

public sealed class SceneVideoRenderSourceSnapshot
{
    public Guid? SelectedImageVersionId { get; set; }
    public string? SourceImageUrl { get; set; }
    public string? SourceImageObjectKey { get; set; }
}

public sealed class SceneVideoRenderHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string JobTypeName = RenderJobTypes.RenderVideoBatch;
    public const string RoutingProviderCode = "configured_video_router";
    public const string RoutingModelCode = "scene_video_default";

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IVideoProviderRoutingService _routing;
    private readonly IRenderJobService _jobs;
    private readonly IVideoRenderPricingResolver _pricing;
    private readonly IPointPricingService _pointPricing;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IVideoRenderEligibilityService _eligibility;
    private readonly IVideoPromptValidator _promptValidator;
    private readonly IRVideoTrustedPayerContextService _payers;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneVideoRenderHandler> _logger;

    public string JobType => JobTypeName;

    public SceneVideoRenderHandler(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        RVideoJobSettingsRepository settings,
        IVideoProviderRoutingService routing,
        IRenderJobService jobs,
        IVideoRenderPricingResolver pricing,
        IPointPricingService pointPricing,
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IVideoRenderEligibilityService eligibility,
        IVideoPromptValidator promptValidator,
        IRVideoTrustedPayerContextService payers,
        IConfiguration config,
        ILogger<SceneVideoRenderHandler> logger)
    {
        _repo = repo;
        _versions = versions;
        _settings = settings;
        _routing = routing;
        _jobs = jobs;
        _pricing = pricing;
        _pointPricing = pointPricing;
        _factory = factory;
        _tenant = tenant;
        _eligibility = eligibility;
        _promptValidator = promptValidator;
        _payers = payers;
        _config = config;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneVideoRenderInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene video batch job input invalid.");
        if (input.ProjectId <= 0)
        {
            throw new InvalidOperationException("Missing projectId in render video batch job.");
        }
        if (input.SceneIds is null || input.SceneIds.Length == 0)
        {
            throw new InvalidOperationException("RVIDEO_VIDEO_SCENE_IDS_REQUIRED");
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");

        var route = await _routing.ResolveAsync(RVideoVideoModelPolicy.CapabilityCode, providerCapabilityId: null, fromUser: false, ct);
        if (!string.Equals(route.CapabilityCode, RVideoVideoModelPolicy.CapabilityCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("RVIDEO_VIDEO_CAPABILITY_ROUTE_INVALID");
        }
        input.ProviderConfigJson = route.ProviderConfigJson;
        input.CapabilityConfigJson = route.CapabilityConfigJson;

        var targetSceneIds = input.SceneIds.ToHashSet();
        var scenes = project.Scenes
            .Where(scene => targetSceneIds.Contains(scene.Id))
            .OrderBy(scene => scene.SceneIndex)
            .ToList();
        if (scenes.Count == 0)
        {
            return;
        }
        if (input.TrustedPayerContext is null && LegacyPointBillingFeatureFlags.IsEnabled(_config))
        {
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PAYER_CONTEXT_MISSING", "error",
                "Scene-video batch was blocked because trusted payer context is missing.",
                new { batchJobId = job.Id, input.ProjectId, errorCode = "rvideo_video_payer_context_mismatch" }, ct);
            throw new RenderJobTerminalFailureException("rvideo_video_payer_context_mismatch: trusted payer context is required.");
        }
        input.TrustedPayerContext = await _payers.ValidateAndBuildRVideoTrustedPayerContextAsync(
            input.ProjectId, scenes[0].Id, input.CustomerId, input.UserId, input.TrustedPayerContext, ct);

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_BATCH_STARTED", "info",
            $"Batch render video started for {scenes.Count} scenes.",
            new
            {
                batchJobId = job.Id,
                sceneCount = scenes.Count,
                route.ProviderCode,
                route.ModelName,
                input.AspectRatio,
                input.Resolution
            }, ct);

        var enqueued = 0;
        var validationFailed = new List<int>();
        foreach (var scene in scenes)
        {
            if (await EnqueueSceneChildJobAsync(project, scene, input, route, job, ct))
            {
                enqueued++;
            }
            else
            {
                validationFailed.Add(scene.SceneIndex);
            }
        }

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_BATCH_COMPLETED", "info",
            $"Batch job enqueued {enqueued} child scene-video jobs and skipped {validationFailed.Count} invalid scenes.",
            new
            {
                batchJobId = job.Id,
                totalRequested = scenes.Count,
                enqueued,
                validationFailed = validationFailed.Count,
                validationFailedSceneIndexes = validationFailed,
                provider = route.ProviderCode,
                model = route.ModelName,
                capability = route.CapabilityCode
            }, ct);
    }

    private async Task<bool> EnqueueSceneChildJobAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        SceneVideoRenderInput input,
        VideoProviderRoute route,
        RenderJobDto parentJob,
        CancellationToken ct)
    {
        var eligibility = await _eligibility.GetVideoRenderEligibilityAsync(project.Id, new[] { scene.Id }, ct);
        var sceneEligibility = eligibility.Results.FirstOrDefault();
        if (sceneEligibility is not null && sceneEligibility.Status != VideoRenderEligibilityStatus.Eligible)
        {
            await _repo.AddProjectEventAsync(project.Id,
                sceneEligibility.Status switch
                {
                    VideoRenderEligibilityStatus.AlreadyCompleted => "SCENE_VIDEO_ALREADY_COMPLETED_SKIPPED",
                    VideoRenderEligibilityStatus.AlreadyActive => "SCENE_VIDEO_ALREADY_ACTIVE_SKIPPED",
                    VideoRenderEligibilityStatus.MissingImage => "SCENE_VIDEO_MISSING_IMAGE_SKIPPED",
                    VideoRenderEligibilityStatus.InvalidPrompt => "SCENE_VIDEO_INVALID_PROMPT_SKIPPED",
                    _ => "SCENE_VIDEO_REQUEST_SKIPPED"
                },
                sceneEligibility.Status == VideoRenderEligibilityStatus.AlreadyCompleted ? "info" : "warning",
                sceneEligibility.Message,
                new { projectId = project.Id, sceneId = scene.Id, scene.SceneIndex, sceneEligibility.Status, sceneEligibility.ErrorCode },
                ct);
            return false;
        }

        var settings = await _settings.GetAsync(project.Id, ct);
        if (settings is null)
        {
            return false;
        }

        var selectedImage = input.UseSharedReferenceImage
            ? null
            : await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
        if (input.UseSharedReferenceImage
            && string.IsNullOrWhiteSpace(input.SharedReferenceImageUrl)
            && string.IsNullOrWhiteSpace(input.SharedReferenceImageObjectKey))
        {
            input.ApplySharedReferenceImage(RVideoSceneImageReferenceSelection.Resolve(settings).ToSnapshot());
        }
        var effectiveSource = input.UseSharedReferenceImage
            ? new RVideoEffectiveSceneImageSource(true, null, input.SharedReferenceImageUrl, input.SharedReferenceImageObjectKey, "shared_reference_image")
            : RVideoEffectiveSceneImageSourceResolver.Resolve(scene, settings, selectedImage, project);
        var sourceImageUrl = effectiveSource.SourceImageUrl;
        var sourceImageObjectKey = effectiveSource.SourceImageObjectKey;
        var sourceImageVersionId = effectiveSource.SelectedImageVersionId;
        if (string.IsNullOrWhiteSpace(sourceImageUrl) && string.IsNullOrWhiteSpace(sourceImageObjectKey))
        {
            await MarkSceneValidationFailedAsync(project.Id, scene, new VideoPromptValidationResult(
                false,
                route.ModelName ?? string.Empty,
                scene.VideoPrompt?.Trim() ?? string.Empty,
                VideoPromptValidator.CountUnicodeScalars(scene.VideoPrompt?.Trim() ?? string.Empty),
                VideoPromptValidator.ResolveMaxPromptCharacters(route.ModelName, route.CapabilityConfigJson),
                "scene_source_image_required",
                input.UseSharedReferenceImage
                    ? RVideoEffectiveSceneImageSourceResolver.MissingSharedReferenceMessage
                    : $"Scene {scene.SceneIndex:00}: cần ảnh nguồn đã chọn trước khi render video."), ct);
            return false;
        }

        var voiceMode = await ResolveVoiceModeAsync(project.Id, ct);
        var voiceText = RVideoRules.ResolveSceneVoiceText(scene);
        var voiceInstruction = RVideoRules.ResolveSceneVoiceInstruction(scene);
        var finalPrompt = voiceMode == RVideoVoiceModes.Native
            ? RVideoRules.ComposeNativeVoicePrompt(scene.VideoPrompt, voiceText, voiceInstruction)
            : scene.VideoPrompt;
        finalPrompt = RVideoSharedBaseImagePromptGuard.Apply(finalPrompt, input.UseSharedReferenceImage);
        var validation = _promptValidator.Validate(finalPrompt, route.ModelName, route.CapabilityConfigJson, scene.SceneIndex);
        if (!validation.IsValid)
        {
            await MarkSceneValidationFailedAsync(project.Id, scene, validation, ct);
            return false;
        }

        var resolvedPrice = _pricing.Resolve(
            new ProviderOptionDto
            {
                ProviderId = route.ProviderId,
                ProviderCapabilityId = route.ProviderCapabilityId,
                ProviderCode = route.ProviderCode,
                CapabilityCode = route.CapabilityCode,
                ModelName = route.ModelName,
                UnitCostPoints = route.UnitCostPoints
            },
            new AiProviderCapabilityDto
            {
                Id = route.ProviderCapabilityId,
                ProviderId = route.ProviderId,
                ProviderCode = route.ProviderCode,
                CapabilityCode = route.CapabilityCode,
                ModelName = route.ModelName,
                ConfigJson = route.CapabilityConfigJson
            },
            RVideoVideoModelPolicy.Models.FirstOrDefault(x =>
                string.Equals(x.ProviderCode, route.ProviderCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Model, route.ModelName, StringComparison.OrdinalIgnoreCase))
            ?? new RVideoVideoModelPolicyEntry(
                0,
                route.ProviderCode,
                route.ModelName ?? RVideoVideoModelPolicy.GetInitial().Model,
                null),
            input.AspectRatio,
            input.Resolution,
            scene.DurationSeconds);
        var pointServiceId = await ResolvePointServiceIdAsync(project.CoreJobId, ct);
        var pointEstimate = await _pointPricing.EstimateAsync(new PointPricingEstimateRequest(
            pointServiceId,
            0,
            scene.DurationSeconds >= 8 ? ServiceSellPriceQualityTiers.Premium : ServiceSellPriceQualityTiers.Standard,
            scene.DurationSeconds,
            scene.DurationSeconds >= 8 ? ServiceSellPriceQualityTiers.Premium : ServiceSellPriceQualityTiers.Standard,
            0,
            ServiceSellPriceQualityTiers.Standard,
            false), ct);

        await _repo.UpdateSceneAsync(
            scene.Id,
            VideoSceneStatuses.VideoQueued,
            errorMessage: null,
            title: scene.Title,
            scenePrompt: scene.ScenePrompt,
            imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt,
            ct: ct);

        var childInput = new SceneVideoRenderWorkItemInput
        {
            ParentJobId = parentJob.Id,
            ProjectId = project.Id,
            SceneId = scene.Id,
            SceneIndex = scene.SceneIndex,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            TrustedPayerContext = input.TrustedPayerContext,
            UseSharedReferenceImage = input.UseSharedReferenceImage,
            ImageInputMode = input.UseSharedReferenceImage
                ? VideoSceneImageInputMode.SharedBaseImage
                : VideoSceneImageInputMode.SceneSource,
            SourceImageVersionId = sourceImageVersionId,
            SelectedSourceImageVersionId = sourceImageVersionId,
            SourceImageUrl = sourceImageUrl,
            SourceImageObjectKey = sourceImageObjectKey,
            SourceImageType = effectiveSource.SourceLabel,
            SharedReferenceImageMediaId = input.SharedReferenceImageMediaId,
            SharedReferenceImageUrl = input.SharedReferenceImageUrl,
            SharedReferenceImageObjectKey = input.SharedReferenceImageObjectKey,
            SharedReferenceImageFileName = input.SharedReferenceImageFileName,
            SharedReferenceImageMimeType = input.SharedReferenceImageMimeType,
            ImagePrompt = scene.ImagePrompt,
            VideoPrompt = validation.TrimmedPrompt,
            Voice = voiceMode == RVideoVoiceModes.Native ? voiceText : null,
            VoiceInstruction = voiceMode == RVideoVoiceModes.Native ? voiceInstruction : null,
            ProviderId = route.ProviderId,
            ProviderCode = route.ProviderCode,
            ProviderConfigJson = input.ProviderConfigJson,
            ProviderCapabilityId = route.ProviderCapabilityId,
            CapabilityCode = route.CapabilityCode,
            CapabilityConfigJson = input.CapabilityConfigJson,
            ModelName = route.ModelName,
            MaxPromptCharacters = validation.MaxCharacterCount,
            AspectRatio = input.AspectRatio,
            Resolution = input.Resolution,
            DurationSeconds = scene.DurationSeconds,
            EstimatedUsd = resolvedPrice.ProviderEstimatedCostUsd,
            EstimatedPoints = pointEstimate.TotalPoints,
            PricingMode = resolvedPrice.Mode,
            PricingRuleKey = resolvedPrice.RuleKey,
            TariffSnapshotJson = resolvedPrice.TariffSnapshotJson,
            CostSource = resolvedPrice.CostSource,
            LogicalRequestId = BuildLogicalRequestId(parentJob.Id, scene.Id),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var childJob = await _jobs.EnqueueAsync(new RenderJobCreateModel
        {
            JobType = RenderJobTypes.RenderSceneVideo,
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            Input = childInput,
            Prompt = new { projectId = project.Id, sceneId = scene.Id, parentJobId = parentJob.Id },
            References = Array.Empty<object>(),
            LogCode = parentJob.LogCode,
            ProviderCode = route.ProviderCode,
            ModelCode = route.ModelName,
            MaxAttempts = 3,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.Pending
        }, ct);

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_CHILD_JOB_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} was enqueued as an independent scene-video child job.",
            new
            {
                batchJobId = parentJob.Id,
                childJobId = childJob.Id,
                sceneId = scene.Id,
                scene.SceneIndex,
                input.AspectRatio,
                input.Resolution,
                route.ProviderCode,
                route.ModelName,
                sourceImageVersionId,
                sourceImageType = effectiveSource.SourceLabel,
                hasSourceImage = !string.IsNullOrWhiteSpace(sourceImageUrl) || !string.IsNullOrWhiteSpace(sourceImageObjectKey),
                useSharedReferenceImage = input.UseSharedReferenceImage
            }, ct);

        return true;
    }

    private async Task<string> ResolveVoiceModeAsync(long projectId, CancellationToken ct)
    {
        var settings = await _settings.GetAsync(projectId, ct);
        return RVideoRules.ResolveVoiceMode(settings);
    }

    private async Task MarkSceneValidationFailedAsync(
        long projectId,
        VideoProjectSceneDto scene,
        VideoPromptValidationResult validation,
        CancellationToken ct)
    {
        await _repo.UpdateSceneAsync(
            scene.Id,
            VideoSceneStatuses.Failed,
            errorMessage: validation.Message,
            title: scene.Title,
            scenePrompt: scene.ScenePrompt,
            imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt,
            ct: ct);

        await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_PROMPT_VALIDATION_FAILED", "warning",
            validation.Message ?? $"Scene {scene.SceneIndex:00}: prompt video không hợp lệ.",
            new
            {
                sceneId = scene.Id,
                scene.SceneIndex,
                model = validation.ModelName,
                actualCharacters = validation.ActualCharacterCount,
                maxCharacters = validation.MaxCharacterCount,
                errorCode = validation.ErrorCode
            }, ct);
    }

    public static string BuildLogicalRequestId(Guid parentJobId, long sceneId)
        => $"render_job_scene_video-job-{parentJobId:N}-scene-{sceneId}";

    private static bool IsCompletedSelectedImageVersion(SceneImageVersionDto? version)
        => version is not null
           && version.Id != Guid.Empty
           && version.IsSelected
           && version.Status.Equals("completed", StringComparison.OrdinalIgnoreCase);

    private async Task<Guid?> ResolvePointServiceIdAsync(Guid? coreJobId, CancellationToken ct)
    {
        if (coreJobId is not Guid jobId)
        {
            return null;
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT service_id
              FROM render.render_jobs
             WHERE id=@jobId
               AND tenant_id=@tenant
             LIMIT 1;
            """,
            new { jobId, tenant = _tenant.TenantId });
    }
}
