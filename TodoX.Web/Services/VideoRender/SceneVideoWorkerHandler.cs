using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneVideoRenderWorkItemInput
{
    public Guid ParentJobId { get; set; }
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public int SceneIndex { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public Guid? SourceImageVersionId { get; set; }
    public Guid? SelectedSourceImageVersionId { get; set; }
    public string? SourceImageUrl { get; set; }
    public string? SourceImageObjectKey { get; set; }
    public string? SourceImageType { get; set; }
    public bool UseSharedReferenceImage { get; set; }
    public Guid? SharedReferenceImageMediaId { get; set; }
    public string? SharedReferenceImageUrl { get; set; }
    public string? SharedReferenceImageObjectKey { get; set; }
    public string? SharedReferenceImageFileName { get; set; }
    public string? SharedReferenceImageMimeType { get; set; }
    public string? ImagePrompt { get; set; }
    public string? VideoPrompt { get; set; }
    public string? Voice { get; set; }
    public string? VoiceInstruction { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string? ProviderConfigJson { get; set; }
    public long ProviderCapabilityId { get; set; }
    public string CapabilityCode { get; set; } = string.Empty;
    public string? CapabilityConfigJson { get; set; }
    public string? ModelName { get; set; }
    public int? MaxPromptCharacters { get; set; }
    public string AspectRatio { get; set; } = "9:16";
    public string Resolution { get; set; } = "720P";
    public int DurationSeconds { get; set; }
    public decimal CustomerPointRate { get; set; }
    public string CustomerPointQuality { get; set; } = ServiceSellPriceQualityTiers.Standard;
    public PointBillingIntent BillingIntent { get; set; } = PointBillingIntent.InitialRender;
    public Guid? BillingOperationId { get; set; }
    public decimal? EstimatedUsd { get; set; }
    public decimal EstimatedPoints { get; set; }
    public string? PricingMode { get; set; }
    public string? PricingRuleKey { get; set; }
    public string? TariffSnapshotJson { get; set; }
    public string? CostSource { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public VideoSceneImageInputMode ImageInputMode { get; set; } = VideoSceneImageInputMode.LegacySelectedSource;
    public Guid? ExistingSceneVideoVersionId { get; set; }
    public bool ReuseExistingSceneVideoVersion { get; set; }
}

public enum VideoSceneImageInputMode
{
    None,
    SceneSource,
    ReferenceOnly,
    LegacySelectedSource,
    SharedBaseImage
}

public static class RVideoReferenceOnlyPromptGuard
{
    public const string Text =
        "Use the supplied image as the fixed visual base for this scene. Preserve the same exact person, face, hairstyle, same exact outfit, background, room/set, products, props, furniture, layout, lighting, color palette, and camera framing. Do not redesign, replace, relocate, or reinterpret the environment or outfit. Animate only the subject's natural movements, expressions, gestures, speech, and product interaction required by this scene. The scene must remain visually continuous with the supplied image. Do not show the supplied image as a frozen still or separate opening shot. Begin immediately with natural motion inside this exact setup.";

    internal static readonly string[] BlockedTerms =
    [
        "move to another room",
        "change background",
        "different background",
        "at the beach",
        "in another office",
        "wearing a different outfit",
        "change clothes",
        "different location",
        "new environment",
        "wide shot in another place",
        "switch scene",
        "redesign the room",
        "new room",
        "another room"
    ];

    public static string Apply(string? prompt, bool useSharedReferenceImage)
    {
        var trimmed = prompt?.Trim() ?? string.Empty;
        if (!useSharedReferenceImage)
        {
            return trimmed;
        }

        if (Contains(trimmed))
        {
            if (!trimmed.StartsWith(Text, StringComparison.OrdinalIgnoreCase))
            {
                return Sanitize(trimmed);
            }

            var actionPrompt = Sanitize(trimmed[Text.Length..]);
            return string.IsNullOrWhiteSpace(actionPrompt)
                ? Text
                : $"{Text}\n\n{actionPrompt}";
        }

        var sanitized = Sanitize(trimmed);
        return string.IsNullOrWhiteSpace(sanitized)
            ? Text
            : $"{Text}\n\n{sanitized}";
    }

    public static bool Contains(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        return prompt.Contains("Use the supplied image as the fixed visual base for this scene", StringComparison.OrdinalIgnoreCase)
               || prompt.Contains("supplied reference image only to preserve the character", StringComparison.OrdinalIgnoreCase)
               || prompt.Contains("reference image only for character/identity consistency", StringComparison.OrdinalIgnoreCase)
               || (prompt.Contains("Do not reproduce the reference image as the first frame", StringComparison.OrdinalIgnoreCase)
                   && prompt.Contains("Start immediately inside the environment", StringComparison.OrdinalIgnoreCase));
    }

    private static string Sanitize(string prompt)
    {
        var sanitized = prompt;
        foreach (var term in BlockedTerms)
        {
            sanitized = sanitized.Replace(term, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        while (sanitized.Contains("  ", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return sanitized.Trim();
    }
}

public static class RVideoSharedBaseImagePromptGuard
{
    public static string Apply(string? prompt, bool useSharedReferenceImage)
        => RVideoReferenceOnlyPromptGuard.Apply(prompt, useSharedReferenceImage);
}

public sealed class SceneVideoWorkerHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DefaultMaxReconciliationRetries = 3;
    private const string KnownNoResourcesFailureClassification = "KNOWN_NO_RESOURCES";

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IAiImageBillingService _billing;
    private readonly IAiProviderService _providers;
    private readonly IAiProviderModelService _models;
    private readonly IVideoGenerationProviderAdapterResolver _providerAdapters;
    private readonly IMediaFileService _media;
    private readonly IVideoPromptValidator _promptValidator;
    private readonly IRenderJobService _jobs;
    private readonly IRVideoTrustedPayerContextService _payers;
    private readonly IRVideoJobService _rvideoJobs;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly IRVideoSceneMediaFinalizerService _finalizer;
    private readonly IRVideoSceneVideoCompletionService _completion;
    private readonly ILogger<SceneVideoWorkerHandler> _logger;
    private readonly VideoRenderOptions _options;

    public string JobType => RenderJobTypes.RenderSceneVideo;

    public SceneVideoWorkerHandler(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        IAiImageBillingService billing,
        IAiProviderService providers,
        IAiProviderModelService models,
        IVideoGenerationProviderAdapterResolver providerAdapters,
        IMediaFileService media,
        IVideoPromptValidator promptValidator,
        IRenderJobService jobs,
        IRVideoTrustedPayerContextService payers,
        IRVideoJobService rvideoJobs,
        TenantContext tenant,
        IConfiguration config,
        IRVideoSceneMediaFinalizerService finalizer,
        IRVideoSceneVideoCompletionService completion,
        IOptionsMonitor<VideoRenderOptions> options,
        ILogger<SceneVideoWorkerHandler> logger)
    {
        _repo = repo;
        _versions = versions;
        _billing = billing;
        _providers = providers;
        _models = models;
        _providerAdapters = providerAdapters;
        _media = media;
        _promptValidator = promptValidator;
        _jobs = jobs;
        _payers = payers;
        _rvideoJobs = rvideoJobs;
        _tenant = tenant;
        _config = config;
        _finalizer = finalizer;
        _completion = completion;
        _logger = logger;
        _options = options.CurrentValue;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        if (job.AttemptCount > job.MaxAttempts)
        {
            _logger.LogError(
                "RVIDEO_ATTEMPT_BUDGET_EXCEEDED jobId={JobId} jobType={JobType} attemptCount={AttemptCount} maxAttempts={MaxAttempts}",
                job.Id,
                job.JobType,
                job.AttemptCount,
                job.MaxAttempts);
            throw new RenderJobTerminalFailureException("Render job attempt budget exceeded.");
        }

        var input = JsonSerializer.Deserialize<SceneVideoRenderWorkItemInput>(job.InputJson, JsonOptions)
            ?? throw new InvalidOperationException("Scene video worker input invalid.");
        if (input.ProjectId <= 0 || input.SceneId <= 0 || string.IsNullOrWhiteSpace(input.LogicalRequestId))
        {
            throw new InvalidOperationException("Missing scene video worker snapshot.");
        }
        input.ImageInputMode = ResolveImageInputMode(input);
        if (input.ImageInputMode == VideoSceneImageInputMode.SharedBaseImage)
        {
            input.VideoPrompt = RVideoSharedBaseImagePromptGuard.Apply(input.VideoPrompt, useSharedReferenceImage: true);
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        var scene = project.Scenes.FirstOrDefault(x => x.Id == input.SceneId)
            ?? throw new InvalidOperationException("Video scene not found.");

        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_BILLING_PAYER_RESOLVE_BEGIN", "info",
            "Scene-video billing payer resolution started.",
            new
            {
                coreJobId = project.CoreJobId,
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                renderJobId = job.Id,
                customerId = input.CustomerId,
                payerSource = input.TrustedPayerContext?.Source,
                input.ProviderCode,
                modelName = input.ModelName,
                capabilityCode = input.CapabilityCode
            }, ct);
        try
        {
            input.TrustedPayerContext = await _payers.ValidateAndBuildRVideoTrustedPayerContextAsync(
                input.ProjectId, input.SceneId, input.CustomerId, input.UserId, input.TrustedPayerContext, ct);
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_BILLING_PAYER_RESOLVED", "info",
                "Scene-video billing payer resolved from persisted ownership.",
                new
                {
                    coreJobId = project.CoreJobId,
                    projectId = input.ProjectId,
                    sceneId = input.SceneId,
                    input.SceneIndex,
                    renderJobId = job.Id,
                    customerId = input.TrustedPayerContext.PayerCustomerId,
                    payerSource = input.TrustedPayerContext.Source,
                    input.ProviderCode,
                    modelName = input.ModelName,
                    capabilityCode = input.CapabilityCode
                }, ct);
        }
        catch (InvalidOperationException ex)
        {
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_BILLING_PAYER_FAILED", "error",
                "Scene-video billing payer could not be resolved.",
                new
                {
                    coreJobId = project.CoreJobId,
                    projectId = input.ProjectId,
                    sceneId = input.SceneId,
                    input.SceneIndex,
                    renderJobId = job.Id,
                    customerId = input.CustomerId,
                    payerSource = input.TrustedPayerContext?.Source,
                    input.ProviderCode,
                    modelName = input.ModelName,
                    capabilityCode = input.CapabilityCode,
                    errorCode = "rvideo_video_payer_context_mismatch"
                }, ct);
            await FailAsync(project.Id, scene, Guid.Empty, "rvideo_video_payer_context_mismatch", ex.Message, ct);
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PAYER_CONTEXT_INVALID", "error",
                "Scene-video worker rejected an invalid trusted payer context before billing.",
                new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, errorCode = "rvideo_video_payer_context_mismatch" }, ct);
            throw new RenderJobTerminalFailureException(ex.Message);
        }

        await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Video, VideoProjectStatuses.Rendering, ct);

        await HandleProviderVideoAsync(job, input, project, scene, ct);
    }

    private async Task HandleProviderVideoAsync(
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        CancellationToken ct)
    {
        var requestedSourceImageVersionId = input.SourceImageVersionId ?? input.SelectedSourceImageVersionId;
        var sourceVersion = await ResolveSourceImageVersionAsync(
            scene.Id,
            requestedSourceImageVersionId,
            input.UseSharedReferenceImage,
            input.SourceImageUrl,
            input.SourceImageObjectKey,
            input.SharedReferenceImageMediaId,
            ct);
        if (sourceVersion is null)
        {
            var hasExplicitSourceImageVersion = requestedSourceImageVersionId is Guid requestedId && requestedId != Guid.Empty;
            var errorCode = hasExplicitSourceImageVersion
                ? "RVIDEO_VIDEO_SOURCE_IMAGE_VERSION_NOT_FOUND"
                : "scene_source_image_required";
            var message = hasExplicitSourceImageVersion
                ? "Explicit completed source image version is required before rendering scene video."
                : "Scene source image is required before rendering scene video.";
            await FailAsync(project.Id, scene, Guid.Empty, errorCode, message, ct);
            throw new RenderJobTerminalFailureException(errorCode);
        }
        var sourceImageVersionId = ResolvePersistedSourceImageVersionId(input.UseSharedReferenceImage, sourceVersion);

        var validation = _promptValidator.Validate(
            input.VideoPrompt,
            input.ModelName,
            input.CapabilityConfigJson,
            input.SceneIndex);
        input.VideoPrompt = validation.TrimmedPrompt;
        input.MaxPromptCharacters = validation.MaxCharacterCount;
        if (!validation.IsValid)
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROMPT_VALIDATION_FAILED", "warning",
                validation.Message ?? $"Scene {input.SceneIndex:00}: prompt video không hợp lệ.",
                new
                {
                    jobId = job.Id,
                    input.SceneId,
                    input.SceneIndex,
                    model = validation.ModelName,
                    actualCharacters = validation.ActualCharacterCount,
                    maxCharacters = validation.MaxCharacterCount,
                    errorCode = validation.ErrorCode
                }, ct);
            await FailAsync(project.Id, scene, Guid.Empty, validation.ErrorCode, validation.Message ?? "Scene video prompt invalid.", ct);
            throw new RenderJobTerminalFailureException(validation.Message ?? "Scene video prompt invalid.");
        }

        var attemptVersions = await _versions.ListSceneVideoVersionsAsync(scene.Id, 0, 100, ct);
        if (attemptVersions.Any(v => v.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            && IsMatchingLogicalRequestId(v.LogicalRequestId, input.LogicalRequestId)))
        {
            return;
        }

        var catalogModels = (await _models.GetModelsAsync(
            input.ProviderCode,
            mediaType: "video",
            enabled: true,
            ct: ct))
            .Where(x => !x.IsDeprecated)
            .ToList();
        await EnrichCatalogDurationsAsync(input.ProviderId, catalogModels, ct);
        var candidateResolution = ResolveFallbackCandidateResolution(input, catalogModels);
        var candidates = candidateResolution.Candidates;
        if (attemptVersions.Count == 0)
        {
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FALLBACK_CANDIDATES_RESOLVED", "info",
                "Scene-video fallback candidates were resolved against the provider catalog.",
                new
                {
                    projectId = input.ProjectId,
                    sceneId = input.SceneId,
                    renderJobId = job.Id,
                    requestedDuration = input.DurationSeconds,
                    requestedResolution = input.Resolution,
                    candidateCount = candidates.Count,
                    candidates = candidateResolution.Diagnostics
                }, ct);
        }
        var attemptIndex = ResolveNextAttemptIndex(input.LogicalRequestId, attemptVersions);
        string? fallbackReason = null;
        while (attemptIndex < candidates.Count)
        {
            var candidate = candidates[attemptIndex];
            var policy = candidate.Policy;
            var attemptLogicalRequestId = BuildAttemptLogicalRequestId(input.LogicalRequestId, attemptIndex);
            var version = input.ReuseExistingSceneVideoVersion
                ? await _versions.GetSceneVideoVersionByLogicalRequestIdAsync(attemptLogicalRequestId, ct)
                : await _versions.GetRecoverableSceneVideoVersionAsync(scene.Id, attemptLogicalRequestId, ct);
            if (input.ExistingSceneVideoVersionId is Guid expectedVersionId && version?.Id != expectedVersionId)
            {
                version = null;
            }
            if (version is not null && !IsCompatibleVersion(version, input, policy, candidate.ProviderDurationSeconds, candidate.ProviderResolution))
            {
                await _versions.FailSceneVideoVersionAsync(
                    version.Id,
                    "RVIDEO_VIDEO_INVALID_FALLBACK_VERSION",
                    "Existing scene-video version has no valid provider/model/task contract.",
                    ct);
                version = null;
            }

            version ??= await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
                    input.ProjectId,
                    input.SceneId,
                    sourceImageVersionId,
                    input.UserId,
                    input.CustomerId,
                    job.Id,
                    attemptLogicalRequestId,
                    input.ImagePrompt,
                    input.VideoPrompt,
                    SceneSnapshot: new
                    {
                        scene.Id,
                        scene.ProjectId,
                        input.SceneIndex,
                        scene.Title,
                        input.DurationSeconds,
                        input.SourceImageUrl,
                        input.SourceImageObjectKey,
                        sourceVersion.PublicUrl,
                        sourceVersion.StorageKey,
                        input.SourceImageType,
                        attemptIndex,
                        sceneDurationSeconds = input.DurationSeconds,
                        providerDurationSeconds = candidate.ProviderDurationSeconds,
                        requestedResolution = input.Resolution,
                        providerResolution = candidate.ProviderResolution
                    },
                    RenderConfigSnapshot: new
                    {
                        input,
                        attemptIndex,
                        policy.Model,
                        policy.Mode,
                        sceneDurationSeconds = input.DurationSeconds,
                        providerDurationSeconds = candidate.ProviderDurationSeconds,
                        requestedResolution = input.Resolution,
                        providerResolution = candidate.ProviderResolution,
                        provider = input.ProviderCode,
                        capability = input.CapabilityCode
                    },
                    ProviderCode: input.ProviderCode,
                    RequestedModel: policy.Model,
                    ActualModel: policy.Model,
                    ProviderCapabilityId: input.ProviderCapabilityId), ct);

            if (!string.IsNullOrWhiteSpace(version.ProviderTaskId))
            {
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PROVIDER_REUSED", "info",
                    "Recovered the existing provider task for the same scene-video version.",
                    new
                    {
                        jobId = job.Id,
                        input.ProjectId,
                        input.SceneId,
                        sceneVideoVersionId = version.Id,
                        providerTaskId = version.ProviderTaskId,
                        providerCode = input.ProviderCode,
                        model = policy.Model
                    }, ct);
            }

            var tariffSnapshot = string.IsNullOrWhiteSpace(input.TariffSnapshotJson)
                ? JsonSerializer.Serialize(new
                {
                    model = policy.Model,
                    mode = policy.Mode,
                    providerCapabilityId = input.ProviderCapabilityId,
                    unitCostPoints = input.EstimatedPoints,
                    providerEstimatedCostUsd = input.EstimatedUsd,
                    costSource = input.CostSource ?? "configured_tariff",
                    pricingMode = input.PricingMode,
                    pricingRuleKey = input.PricingRuleKey,
                    capturedAtUtc = DateTimeOffset.UtcNow
                }, JsonOptions)
                : input.TariffSnapshotJson;

            var existingTaskId = await _versions.GetSceneVideoProviderTaskIdAsync(version.Id, ct);
            string? providerTaskId = string.IsNullOrWhiteSpace(version.ProviderTaskId)
                ? (string.IsNullOrWhiteSpace(existingTaskId) ? null : existingTaskId.Trim())
                : version.ProviderTaskId.Trim();
            string? providerVideoIdBase = string.IsNullOrWhiteSpace(version.ProviderVideoIdBase)
                ? null
                : version.ProviderVideoIdBase.Trim();
            string? taskId = providerVideoIdBase;
            if (IsUnknownSubmission(version, providerTaskId, providerVideoIdBase))
            {
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_RECONCILIATION_STARTED", "warning",
                    "Scene-video submission outcome is unknown and requires reconciliation before another submit.",
                    new
                    {
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        renderJobId = job.Id,
                        sceneVideoVersionId = version.Id,
                        providerCode = input.ProviderCode,
                        modelCode = policy.Model,
                        logicalRequestId = attemptLogicalRequestId,
                        providerTaskId,
                        providerVideoIdBase,
                        errorCode = version.ErrorMessage
                    }, ct);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PENDING_RECONCILIATION", "warning",
                    "Scene-video submit is pending reconciliation; no duplicate provider submit will be attempted.",
                    new
                    {
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        renderJobId = job.Id,
                        sceneVideoVersionId = version.Id,
                        providerCode = input.ProviderCode,
                        modelCode = policy.Model,
                        logicalRequestId = attemptLogicalRequestId,
                        providerTaskId,
                        providerVideoIdBase,
                        errorCode = "submit_outcome_unknown"
                    }, ct);
                throw new RenderJobPendingReconciliationException(
                    "Video provider submit outcome is unknown; reconciliation is required before another submit.");
            }
            AiImageBillingReservation reservation;
            if (string.IsNullOrWhiteSpace(taskId))
            {
                // Provider reconciliation still needs a durable record, but customer points are
                // charged only after TodoX has accepted the final provider result.
                var billingCost = _billing.BuildConfiguredCost(0, 1);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_BILLING_RESERVE_BEGIN", "info",
                    "Scene-video billing reservation started.",
                    new
                    {
                        coreJobId = project.CoreJobId,
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        renderJobId = job.Id,
                        customerId = input.TrustedPayerContext?.PayerCustomerId,
                        payerSource = input.TrustedPayerContext?.Source,
                        input.ProviderCode,
                        modelName = input.ModelName,
                        capabilityCode = input.CapabilityCode,
                        requiredPoints = billingCost.CustomerChargedPoints
                    }, ct);
                reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
                {
                    LogicalRequestId = attemptLogicalRequestId,
                    RenderJobId = job.Id.ToString("N"),
                    CustomerId = input.CustomerId,
                    UserId = input.UserId,
                    ProviderId = input.ProviderId,
                    ProviderCapabilityId = input.ProviderCapabilityId,
                    ProviderCode = input.ProviderCode,
                    CapabilityCode = input.CapabilityCode,
                    FeatureCode = "render_job_scene_video",
                    RequestedModel = policy.Model,
                    Cost = billingCost,
                    TrustedPayerContext = input.TrustedPayerContext,
                    TariffSnapshotJson = tariffSnapshot,
                    Metadata = new
                    {
                        parentJobId = input.ParentJobId,
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        candidate.ProviderDurationSeconds,
                        candidate.ProviderResolution,
                        input.AspectRatio,
                        attemptIndex
                    },
                }, ct);
            }
            else
            {
                reservation = await _billing.GetReservationAsync(attemptLogicalRequestId, ct)
                    ?? new AiImageBillingReservation(
                        true,
                        false,
                        "recovered",
                        "pending_reconciliation",
                        attemptLogicalRequestId,
                        0,
                        null,
                        null,
                        null);
            }

            if (!reservation.Ok)
            {
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_BILLING_RESERVE_FAILED", "warning",
                    "Scene-video billing reservation failed before provider submission.",
                    new
                    {
                        coreJobId = project.CoreJobId,
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        renderJobId = job.Id,
                        customerId = input.TrustedPayerContext?.PayerCustomerId,
                        payerSource = input.TrustedPayerContext?.Source,
                        input.ProviderCode,
                        modelName = input.ModelName,
                        capabilityCode = input.CapabilityCode,
                        requiredPoints = input.EstimatedPoints,
                        availablePoints = reservation.AvailablePoints,
                        errorCode = reservation.Status
                    }, ct);
                await FailAsync(project.Id, scene, version.Id, reservation.Status, reservation.ErrorMessage ?? "Unable to reserve billing.", ct);
                throw new RenderJobTerminalFailureException(reservation.ErrorMessage ?? "Unable to reserve billing.");
            }
            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_BILLING_RESERVED", "info",
                "Scene-video billing reservation succeeded.",
                new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, input.CustomerId, input.ProviderCode, requestedModel = policy.Model, mode = policy.Mode, input.CapabilityCode, requiredPoints = reservation.ChargedPoints }, ct);

            if (!reservation.ShouldSubmitProvider && string.IsNullOrWhiteSpace(taskId))
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot,
                    "missing_task_id", "Existing billing reservation has no provider_task_id.", ct, actualModel: policy.Model);
                throw new RenderJobPendingReconciliationException("Missing provider_task_id for scene video reconciliation.");
            }

            if (string.IsNullOrWhiteSpace(taskId))
            {
                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.VideoRendering,
                    errorMessage: null, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);

                try
                {
                    var imageInputMode = ResolveImageInputMode(input);
                    var sourceMedia = await ResolveSourceImageMediaAsync(sourceVersion, ct);
                    var sourceImageAsset = imageInputMode == VideoSceneImageInputMode.SharedBaseImage
                        ? new VideoProviderSourceImage(
                            input.SharedReferenceImageMediaId ?? sourceVersion.ResultMediaId,
                            input.SharedReferenceImageObjectKey ?? sourceVersion.StorageKey,
                            input.SharedReferenceImageUrl ?? sourceVersion.PublicUrl,
                            input.SharedReferenceImageFileName ?? sourceMedia?.FileName,
                            input.SharedReferenceImageMimeType ?? sourceMedia?.MimeType)
                        : new VideoProviderSourceImage(
                            sourceVersion.ResultMediaId,
                            sourceVersion.StorageKey,
                            sourceVersion.PublicUrl,
                            sourceMedia?.FileName,
                            sourceMedia?.MimeType);
                    var referenceImages = imageInputMode == VideoSceneImageInputMode.SharedBaseImage
                        ? new[]
                        {
                            new VideoProviderSourceImage(
                                input.SharedReferenceImageMediaId ?? sourceVersion.ResultMediaId,
                                input.SharedReferenceImageObjectKey ?? sourceVersion.StorageKey,
                                input.SharedReferenceImageUrl ?? sourceVersion.PublicUrl,
                                input.SharedReferenceImageFileName ?? sourceMedia?.FileName,
                                input.SharedReferenceImageMimeType ?? sourceMedia?.MimeType)
                        }
                        : Array.Empty<VideoProviderSourceImage>();
                    var providerPrompt = RVideoSharedBaseImagePromptGuard.Apply(input.VideoPrompt, input.UseSharedReferenceImage);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PROVIDER_RESOLVE_BEGIN", "info",
                        "Scene-video provider resolution started.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, input.ProviderCode, input.ModelName, input.CapabilityCode }, ct);
                    var adapter = _providerAdapters.Resolve(input.ProviderCode, input.CapabilityCode);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PROVIDER_RESOLVED", "info",
                        "Scene-video provider resolved.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, input.ProviderCode, input.ModelName, input.CapabilityCode }, ct);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SOURCE_UPLOAD_BEGIN", "info",
                        "Scene-video source image handoff started.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, sourceImageVersionId, sourceImageType = input.SourceImageType, hasSourceImage = !string.IsNullOrWhiteSpace(input.SourceImageUrl) || !string.IsNullOrWhiteSpace(input.SourceImageObjectKey) }, ct);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_BEGIN", "info",
                        "Scene-video provider submit started.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, input.ProviderCode, model = policy.Model, input.CapabilityCode }, ct);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_STARTED", "info",
                        "Scene-video provider submit started.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = policy.Model,
                            logicalRequestId = attemptLogicalRequestId
                        }, ct);
                    var submit = await adapter.SubmitAsync(new VideoProviderSubmitRequest(
                        input.ProviderId,
                        input.ProviderCapabilityId,
                        input.ProviderCode,
                        input.CapabilityCode,
                        policy.Model,
                        policy.Mode,
                        providerPrompt,
                        input.AspectRatio,
                         candidate.ProviderResolution,
                        candidate.ProviderDurationSeconds,
                        sourceImageAsset,
                        referenceImages,
                        (diagnostic, token) => AddHttpSubmitDiagnosticAsync(project.Id, job, input, version.Id, diagnostic, token)), ct);
                    providerTaskId = string.IsNullOrWhiteSpace(submit.ProviderTaskId) ? null : submit.ProviderTaskId.Trim();
                    providerVideoIdBase = RVideoVideoModelPolicy.Is79AiProvider(input.ProviderCode)
                        ? (string.IsNullOrWhiteSpace(submit.ProviderVideoIdBase) ? null : submit.ProviderVideoIdBase.Trim())
                        : providerTaskId;
                    taskId = providerVideoIdBase;
                    await _jobs.SetProviderIdentifiersAsync(job.Id, providerTaskId, providerVideoIdBase, ct);
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        await MarkPendingReconciliationAsync(
                            input,
                            version.Id,
                            attemptLogicalRequestId,
                            tariffSnapshot,
                            "missing_video_id_base",
                            "79AI accepted a provider task but did not return the id_base required for polling.",
                            CancellationToken.None,
                            null,
                            policy.Model,
                            providerVideoIdBase);
                        throw new RenderJobPendingReconciliationException(
                            "79AI accepted a provider task without the id_base required for polling.");
                    }

                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SOURCE_UPLOAD_SUCCESS", "info",
                        "Scene-video source image handoff completed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, sourceImageVersionId = sourceVersion.Id, imageInputMode = imageInputMode.ToString() }, ct);
                    await _versions.MarkSceneVideoVersionSubmittedAsync(
                        version.Id,
                        input.ProviderCode,
                        policy.Model,
                        input.ProviderCapabilityId,
                        providerTaskId!,
                        providerVideoIdBase,
                        ct);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMITTED", "info",
                        "Scene-video provider submit completed.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId,
                            providerVideoIdBase,
                            idBase = providerVideoIdBase
                        }, ct);
                    if (attemptIndex > 0)
                    {
                        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FALLBACK_SUBMITTED", "info",
                            "Scene-video fallback candidate was submitted to the provider.",
                            new
                            {
                                projectId = input.ProjectId,
                                sceneId = input.SceneId,
                                input.SceneIndex,
                                renderJobId = job.Id,
                                sceneVideoVersionId = version.Id,
                                provider = input.ProviderCode,
                                model = policy.Model,
                                mode = policy.Mode,
                                candidateIndex = attemptIndex,
                                status = "submitted",
                                providerTaskId = taskId,
                                errorCode = (string?)null,
                                errorMessage = (string?)null,
                                failureClassification = fallbackReason ?? "MODEL_PROVIDER_FAILURE"
                            }, ct);
                    }
                    await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_SUBMITTED", "info",
                        $"Scene {input.SceneIndex} submitted to its configured video provider.",
                        new { jobId = job.Id, input.SceneId, input.SceneIndex, taskId, model = policy.Model, input.ProviderCode, attemptIndex }, ct);
                }
                catch (Exception submitException) when (
                    submitException is VideoProviderTransientException
                    || submitException is Ai79TaskSubmitException)
                {
                    var structuredException = ExtractAi79SubmitException(submitException);
                    var canFallback = structuredException is not null
                        && IsDefinitivelyRejectedSubmit(structuredException);
                    if (canFallback)
                    {
                        var ai79Exception = structuredException!;
                        var failureClassification = ClassifyRVideoSubmitFailure(ai79Exception);
                        var nextCandidate = attemptIndex + 1 < candidates.Count
                            ? candidates[attemptIndex + 1]
                            : null;
                        await AddAi79SubmitDiagnosticsAsync(job, policy, ai79Exception, CancellationToken.None);
                        if (failureClassification == KnownNoResourcesFailureClassification)
                        {
                            await _repo.AddProjectEventAsync(project.Id, "RVIDEO_79AI_NO_RESOURCES", "warning",
                                "79AI confirmed that no video task was created because resources are unavailable.",
                                new
                                {
                                    projectId = input.ProjectId,
                                    sceneId = input.SceneId,
                                    input.SceneIndex,
                                    renderJobId = job.Id,
                                    provider = input.ProviderCode,
                                    model = policy.Model,
                                    httpStatusCode = (int?)ai79Exception.HttpStatusCode,
                                    error = "NOT_RESOURCES",
                                    countTasks = 0,
                                    fallbackAvailable = nextCandidate is not null,
                                    nextProvider = nextCandidate?.Policy.ProviderCode,
                                    nextModel = nextCandidate?.Policy.Model
                                }, CancellationToken.None);
                        }
                        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_FAILED", "warning",
                            "Scene-video provider submit failed for the current fallback candidate.",
                            new
                            {
                                projectId = input.ProjectId,
                                sceneId = input.SceneId,
                                input.SceneIndex,
                                renderJobId = job.Id,
                                sceneVideoVersionId = version.Id,
                                provider = input.ProviderCode,
                                model = policy.Model,
                                mode = policy.Mode,
                                candidateIndex = attemptIndex,
                                status = "failed",
                                providerTaskId = (string?)null,
                                failureClassification,
                                providerErrorCode = ai79Exception.ErrorCode,
                                errorMessage = ai79Exception.ErrorMessage,
                                httpStatusCode = (int?)ai79Exception.HttpStatusCode,
                                sanitizedResponseJson = SanitizeDiagnosticJson(ai79Exception.SanitizedResponseJson),
                                sanitizedRequestMetadataJson = SanitizeDiagnosticJson(ai79Exception.SanitizedRequestMetadataJson)
                            }, CancellationToken.None);
                        await _versions.FailSceneVideoVersionAsync(
                            version.Id,
                            ai79Exception.ErrorCode ?? failureClassification,
                            ai79Exception.ErrorMessage,
                            CancellationToken.None);
                        await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                        {
                            LogicalRequestId = attemptLogicalRequestId,
                            Success = false,
                            ActualModel = policy.Model,
                            ProviderUsageJson = ai79Exception.SanitizedResponseJson,
                            TariffSnapshotJson = tariffSnapshot,
                            ErrorMessage = ai79Exception.ErrorMessage
                        }, CancellationToken.None);
                        if (!ShouldFallback(failureClassification))
                        {
                            throw ai79Exception;
                        }

                        if (attemptIndex + 1 >= candidates.Count)
                        {
                            await AddFallbackExhaustedEventAsync(
                                project,
                                scene,
                                job,
                                input,
                                policy,
                                null,
                                failureClassification,
                                ai79Exception.ErrorCode,
                                ai79Exception.SanitizedResponseJson,
                                ai79Exception.ErrorMessage,
                                CancellationToken.None);
                            await FailAsync(project.Id, scene, version.Id, ai79Exception.ErrorCode ?? "provider_failure", ai79Exception.ErrorMessage, CancellationToken.None);
                            throw new RenderJobTerminalFailureException(ai79Exception.ErrorMessage, ai79Exception);
                        }

                        nextCandidate = candidates[attemptIndex + 1];
                        await AddFallbackStartedEventAsync(
                            project,
                            scene,
                            job,
                            input,
                            policy,
                            nextCandidate.Policy,
                            null,
                            failureClassification,
                            ai79Exception.ErrorCode,
                            CancellationToken.None);
                        fallbackReason = failureClassification;
                        attemptIndex++;
                        continue;
                    }

                    var ex = submitException as VideoProviderTransientException
                        ?? new VideoProviderTransientException(
                            submitException.Message,
                            structuredException?.ErrorCode,
                            submitException);
                    var diagnostics = BuildSubmitFailureDiagnostics(ex);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_FAILED", "warning",
                        "Scene-video provider submit did not complete.",
                        new
                        {
                            jobId = job.Id,
                            input.ProjectId,
                            input.SceneId,
                            input.SceneIndex,
                            errorCode = ex.ErrorCode,
                            diagnostics
                        }, CancellationToken.None);
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, ex.ErrorCode ?? "submit_transient", ex.Message, CancellationToken.None, null, policy.Model);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_UNKNOWN", "warning",
                        "Scene-video provider submit may have been accepted but no provider task ID was returned.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId = (string?)null,
                            errorCode = ex.ErrorCode
                        }, CancellationToken.None);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PENDING_RECONCILIATION", "warning",
                        "Scene-video submit is pending reconciliation; no duplicate provider submit will be attempted.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId = (string?)null,
                            errorCode = ex.ErrorCode ?? "submit_transient"
                        }, CancellationToken.None);
                    throw new RenderJobPendingReconciliationException(
                        "Video provider submit outcome is unknown; reconciliation is required before another submit.",
                        ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SOURCE_UPLOAD_FAILED", "error",
                        "Scene-video source image handoff or provider submit failed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, errorCode = ex.GetType().Name, imageInputMode = ResolveImageInputMode(input).ToString() }, CancellationToken.None);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_FAILED", "error",
                        "Scene-video provider submit failed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, errorCode = ex.GetType().Name, diagnostics = BuildSubmitFailureDiagnostics(ex) }, CancellationToken.None);
                    throw;
                }
            }

            if (string.IsNullOrWhiteSpace(taskId))
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "missing_video_id_base", "Missing provider_video_id_base for scene video reconciliation.", ct, providerTaskId, policy.Model, providerVideoIdBase);
                throw new RenderJobPendingReconciliationException("Missing provider_video_id_base for scene video reconciliation.");
            }

            try
            {
                var adapter = _providerAdapters.Resolve(input.ProviderCode, input.CapabilityCode);
                if (!string.IsNullOrWhiteSpace(existingTaskId))
                {
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_RECONCILIATION_STARTED", "info",
                        "Scene-video reconciliation resumed from the persisted provider task.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId,
                            providerVideoIdBase,
                            idBase = providerVideoIdBase
                        }, ct);
                }
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_POLL_STARTED", "info",
                    "Scene-video provider poll started.",
                    new
                    {
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        renderJobId = job.Id,
                        sceneVideoVersionId = version.Id,
                        providerCode = input.ProviderCode,
                        modelCode = policy.Model,
                        logicalRequestId = attemptLogicalRequestId,
                        providerTaskId,
                        providerVideoIdBase,
                        idBase = providerVideoIdBase
                    }, ct);
                var status = await adapter.PollAsync(new VideoProviderPollRequest(
                    input.ProviderId,
                    input.ProviderCapabilityId,
                    input.ProviderCode,
                    input.CapabilityCode,
                    taskId!), ct);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_POLL_RESPONSE", "info",
                    "Scene-video provider poll response received.",
                new
                {
                    jobId = job.Id,
                    input.ProjectId,
                    input.SceneId,
                    input.SceneIndex,
                    taskId,
                    idBase = providerVideoIdBase,
                    providerTaskId,
                    provider = input.ProviderCode,
                    normalizedStatus = status.Status
                }, ct);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_POLL_COMPLETED", "info",
                    "Scene-video provider poll completed.",
                    new
                    {
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        input.SceneIndex,
                        renderJobId = job.Id,
                        sceneVideoVersionId = version.Id,
                        providerCode = input.ProviderCode,
                        modelCode = status.ActualModel ?? policy.Model,
                        logicalRequestId = attemptLogicalRequestId,
                        providerTaskId,
                        providerVideoIdBase,
                        idBase = providerVideoIdBase,
                        errorCode = status.ErrorCode,
                        providerStatus = status.Status
                    }, ct);
                if (status.Status == VideoProviderTaskStatus.ResourceUnavailable)
                {
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE", "warning",
                        "79AI reported NOT_RESOURCES for the existing provider task; the same task will be reconciled without a new submit.",
                        new
                        {
                            jobId = job.Id,
                            input.ProjectId,
                            input.SceneId,
                            input.SceneIndex,
                            providerTaskId,
                            providerVideoIdBase,
                            idBase = providerVideoIdBase,
                            provider = input.ProviderCode,
                            actualModel = policy.Model,
                            providerStatus = "NOT_RESOURCES",
                            status.ErrorCode,
                            status.ErrorMessage,
                            providerRawResponse = SanitizeDiagnosticJson(status.SanitizedResponseJson)
                        }, ct);
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot,
                        "provider_resources_unavailable", "79AI reported NOT_RESOURCES for the existing video task.", ct,
                        providerTaskId, policy.Model, providerVideoIdBase);
                    await DeferProviderPollAsync(job, taskId!, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE", "79AI reported NOT_RESOURCES; the same task will be polled later.", ct);
                    return;
                }

                if (status.Status is VideoProviderTaskStatus.Queued or VideoProviderTaskStatus.Processing)
                {
                    await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_PROCESSING", "info",
                        $"Scene {input.SceneIndex} provider task is still processing.",
                        new
                        {
                            jobId = job.Id,
                            sceneId = input.SceneId,
                            sceneIndex = input.SceneIndex,
                            providerTaskId,
                            providerVideoIdBase,
                            idBase = providerVideoIdBase,
                            normalizedStatus = status.Status,
                            providerRawResponse = status.SanitizedResponseJson
                        }, ct);
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "provider_pending", "Video provider task remains pending.", ct, providerTaskId, policy.Model, providerVideoIdBase);
                    await DeferProviderPollAsync(job, taskId!, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_SCHEDULED", "Video task remains pending; the same provider task will be polled later.", ct);
                    return;
                }

                if (status.Status != VideoProviderTaskStatus.Success)
                {
                    var failure = status.ErrorMessage ?? $"Video provider task failed with status {status.Status}.";
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FAILED", "error",
                        "Scene-video provider reported a terminal failure.",
                        new
                        {
                            jobId = job.Id,
                            input.ProjectId,
                            input.SceneId,
                            input.SceneIndex,
                            candidateIndex = attemptIndex,
                            provider = input.ProviderCode,
                            requestedModel = policy.Model,
                            mode = policy.Mode,
                            taskId,
                            status = "failed",
                            errorCode = status.ErrorCode,
                            errorMessage = failure
                        }, ct);
                    if (reservation.BillingRecordId is not null)
                    {
                        await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                        {
                            LogicalRequestId = attemptLogicalRequestId,
                            Success = false,
                            ActualModel = status.ActualModel ?? policy.Model,
                            ProviderTaskId = providerTaskId ?? taskId,
                            ProviderUsageJson = status.SanitizedResponseJson,
                            TariffSnapshotJson = tariffSnapshot,
                            ErrorMessage = failure
                        }, ct);
                    }
                    await LogUsageAsync(input, job, attemptLogicalRequestId, reservation.ChargedPoints, status.SanitizedResponseJson, false, failure, providerTaskId ?? taskId, ct, policy.Model, candidate.ProviderDurationSeconds);
                    await _versions.FailSceneVideoVersionAsync(version.Id, status.ErrorCode ?? "provider_failure", failure, ct);
                    var failureClassification = ClassifyProviderFailure(status.ErrorCode, failure, null);
                    await AddFallbackFailedEventAsync(
                        project,
                        scene,
                        job,
                        input,
                        policy,
                        providerTaskId ?? taskId,
                        failureClassification,
                        status.ErrorCode,
                        status.SanitizedResponseJson,
                        failure,
                        ct);
                    if (attemptIndex + 1 < candidates.Count)
                    {
                        if (!ShouldFallback(failureClassification))
                        {
                            await FailAsync(project.Id, scene, version.Id, status.ErrorCode ?? "provider_failure", failure, ct);
                            throw new RenderJobTerminalFailureException(failure);
                        }

                        var nextCandidate = candidates[attemptIndex + 1];
                        await AddFallbackStartedEventAsync(
                            project,
                            scene,
                            job,
                            input,
                            policy,
                            nextCandidate.Policy,
                            providerTaskId ?? taskId,
                            failureClassification,
                            status.ErrorCode,
                            ct);
                        fallbackReason = failureClassification;
                        attemptIndex++;
                        continue;
                    }

                    await AddFallbackExhaustedEventAsync(
                        project,
                        scene,
                        job,
                        input,
                        policy,
                        providerTaskId ?? taskId,
                        failureClassification,
                        status.ErrorCode,
                        status.SanitizedResponseJson,
                        failure,
                        ct);
                    await FailAsync(project.Id, scene, version.Id, "provider_failure", failure, ct);
                    throw new RenderJobTerminalFailureException(failure);
                }

                try
                {
                    var outputUrl = status.OutputUrl;
                    if (string.IsNullOrWhiteSpace(outputUrl)
                        || !Uri.TryCreate(outputUrl, UriKind.Absolute, out var outputUri)
                        || outputUri.Scheme is not ("http" or "https"))
                    {
                        throw new VideoReconciliationException(
                            "PROVIDER_OUTPUT_URL_MISSING",
                            $"79AI returned SUCCESS without a usable output video URL. task_id={taskId}");
                    }

                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_DOWNLOAD_STARTED", "info",
                        "Scene-video provider output download started.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = status.ActualModel ?? policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId,
                            providerVideoIdBase
                        }, ct);
                    var saved = await _completion.CompleteProviderVideoAsync(new RVideoSceneVideoCompletionRequest(
                        project.Id,
                        scene.Id,
                        input.SceneIndex,
                        version.Id,
                        job.Id,
                        version.StorageKey,
                        attemptLogicalRequestId,
                        providerTaskId ?? taskId!,
                        outputUrl,
                        input.ProviderCode,
                        status.ActualModel ?? policy.Model,
                        input.ProviderCapabilityId,
                        status.SanitizedResponseJson,
                        tariffSnapshot,
                        input.CustomerPointRate,
                        input.EstimatedUsd,
                        input.CostSource,
                        input.AspectRatio,
                        sourceVersion.PublicUrl ?? input.SourceImageUrl,
                        input.DurationSeconds,
                        input.UserId,
                        input.CustomerId,
                        input.BillingIntent,
                        input.BillingOperationId,
                        IsRecovery: !string.IsNullOrWhiteSpace(existingTaskId),
                        BillableDurationSeconds: candidate.ProviderDurationSeconds), ct);
                    var actualVideoPoints = RVideoSceneVideoCompletionService.CalculateActualVideoPoints(
                        candidate.ProviderDurationSeconds,
                        input.CustomerPointRate);
                    await LogUsageAsync(input, job, attemptLogicalRequestId, actualVideoPoints, status.SanitizedResponseJson, true, null, providerTaskId ?? taskId, ct, status.ActualModel ?? policy.Model, candidate.ProviderDurationSeconds);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_DOWNLOAD_COMPLETED", "info",
                        "Scene-video provider output was downloaded and persisted locally.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = status.ActualModel ?? policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId,
                            providerVideoIdBase
                        }, ct);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_COMPLETED", "info",
                        "Scene-video completed after local media persistence.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerCode = input.ProviderCode,
                            modelCode = status.ActualModel ?? policy.Model,
                            logicalRequestId = attemptLogicalRequestId,
                            providerTaskId,
                            providerVideoIdBase
                        }, ct);
                    if (!string.IsNullOrWhiteSpace(existingTaskId))
                    {
                        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_RECONCILIATION_COMPLETED", "info",
                            "Scene-video reconciliation completed after local media persistence.",
                            new
                            {
                                projectId = input.ProjectId,
                                sceneId = input.SceneId,
                                input.SceneIndex,
                                renderJobId = job.Id,
                                sceneVideoVersionId = version.Id,
                                providerCode = input.ProviderCode,
                                modelCode = status.ActualModel ?? policy.Model,
                                logicalRequestId = attemptLogicalRequestId,
                                providerTaskId,
                                providerVideoIdBase
                            }, ct);
                    }
                    return;
                }
                catch (VideoReconciliationException ex)
                {
                    await HandleReconciliationFailureAsync(
                        job, project, scene, version.Id, input, attemptLogicalRequestId, tariffSnapshot, providerTaskId ?? taskId, providerVideoIdBase ?? taskId, ex.ErrorCode, ex.Message, ct);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await HandleReconciliationFailureAsync(
                        job, project, scene, version.Id, input, attemptLogicalRequestId, tariffSnapshot, providerTaskId ?? taskId, providerVideoIdBase ?? taskId,
                        "PROVIDER_SUCCESS_RECONCILIATION_FAILED", ex.Message, ct);
                    return;
                }
            }
            catch (VideoProviderTransientException ex)
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "SCENE_VIDEO_POLL_TRANSIENT", ex.Message, CancellationToken.None, providerTaskId, policy.Model, providerVideoIdBase);
                if (!string.IsNullOrWhiteSpace(taskId))
                {
                    await DeferProviderPollAsync(job, taskId!, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_TRANSIENT", "Temporary provider poll failure; the same task ID will be retried.", CancellationToken.None);
                }
                else
                {
                    await DeferPollAsync(job, attemptLogicalRequestId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_TRANSIENT", "Temporary provider poll failure before provider submission; application retry will resubmit.", CancellationToken.None);
                }
                return;
            }
        }

        const string exhaustedCode = "RVIDEO_VIDEO_FALLBACK_EXHAUSTED";
        await _repo.AddProjectEventAsync(project.Id, exhaustedCode, "error",
            "No valid scene-video fallback candidate remains.",
            new
            {
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                renderJobId = job.Id,
                provider = input.ProviderCode,
                model = input.ModelName,
                providerTaskId = (string?)null,
                failureClassification = fallbackReason ?? "SYSTEM_FAILURE"
            }, ct);
        await FailAsync(project.Id, scene, Guid.Empty, exhaustedCode,
            "No valid 79AI video fallback candidate remains.", ct);
        throw new RenderJobTerminalFailureException(exhaustedCode);
    }

    private async Task HandleReconciliationFailureAsync(
        RenderJobDto job,
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        Guid versionId,
        SceneVideoRenderWorkItemInput input,
        string logicalRequestId,
        string? tariffSnapshot,
        string providerTaskId,
        string providerVideoIdBase,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var retryLimit = Math.Max(1, _config.GetValue("VideoRender:MaxReconciliationRetries", DefaultMaxReconciliationRetries));
        var currentAttempt = await _jobs.GetProviderReconciliationAttemptCountAsync(job.Id, ct) + 1;
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_DOWNLOAD_FAILED", "warning",
            "Scene-video provider output could not be persisted locally.",
            new
            {
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                renderJobId = job.Id,
                sceneVideoVersionId = versionId,
                providerCode = input.ProviderCode,
                modelCode = input.ModelName,
                logicalRequestId,
                providerTaskId,
                errorCode,
                reconciliationAttempt = currentAttempt,
                maxReconciliationRetries = retryLimit
            }, ct);
        if (currentAttempt < retryLimit)
        {
            await MarkPendingReconciliationAsync(input, versionId, logicalRequestId, tariffSnapshot, errorCode, errorMessage, ct, providerTaskId, providerVideoIdBase: providerVideoIdBase);
            await _jobs.ScheduleProviderPollAsync(
                job.Id,
                TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                "SCENE_VIDEO_RECONCILIATION_RETRY",
                errorMessage,
                ct,
                enforceReconciliationLimit: true);
            throw new RenderJobDeferredException(errorMessage);
        }

        await MarkPendingReconciliationAsync(input, versionId, logicalRequestId, tariffSnapshot, errorCode, errorMessage, ct, providerTaskId, providerVideoIdBase: providerVideoIdBase);
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PENDING_RECONCILIATION", "warning",
            "Provider video succeeded but local persistence is pending reconciliation.",
            new
            {
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                renderJobId = job.Id,
                sceneVideoVersionId = versionId,
                providerCode = input.ProviderCode,
                modelCode = input.ModelName,
                logicalRequestId,
                providerTaskId,
                errorCode,
                errorMessage,
                reconciliationAttempt = currentAttempt,
                maxReconciliationRetries = retryLimit
            }, ct);
        throw new RenderJobPendingReconciliationException(errorMessage);
    }

#if false
    private async Task LegacyProviderPathDisabledAsync(
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        CancellationToken ct)
    {
        var version = await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
            input.ProjectId,
            input.SceneId,
            input.SelectedSourceImageVersionId,
            input.UserId,
            input.CustomerId,
            job.Id,
            input.LogicalRequestId,
            input.ImagePrompt,
            input.VideoPrompt,
            SceneSnapshot: new
            {
                scene.Id,
                scene.ProjectId,
                input.SceneIndex,
                scene.Title,
                input.DurationSeconds,
                input.SourceImageUrl,
                input.SourceImageObjectKey
            },
            RenderConfigSnapshot: input), ct);

        var validation = _promptValidator.Validate(input.VideoPrompt, input.ModelName, input.CapabilityConfigJson, input.SceneIndex);
        input.VideoPrompt = validation.TrimmedPrompt;
        input.MaxPromptCharacters = validation.MaxCharacterCount;
        if (!validation.IsValid)
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROMPT_VALIDATION_FAILED", "warning",
                validation.Message ?? $"Scene {input.SceneIndex:00}: prompt video không hợp lệ.",
                new
                {
                    jobId = job.Id,
                    input.SceneId,
                    input.SceneIndex,
                    model = validation.ModelName,
                    actualCharacters = validation.ActualCharacterCount,
                    maxCharacters = validation.MaxCharacterCount,
                    errorCode = validation.ErrorCode
                }, ct);
            await FailAsync(project.Id, scene, version.Id, validation.ErrorCode, validation.Message ?? "Scene video prompt invalid.", ct);
            throw new RenderJobTerminalFailureException(validation.Message ?? "Scene video prompt invalid.");
        }

        if (string.IsNullOrWhiteSpace(input.SourceImageUrl))
        {
            await FailAsync(project.Id, scene, version.Id, "missing_image", "Scene has no source image for video render.", ct);
            throw new RenderJobTerminalFailureException("Scene has no source image for video render.");
        }

        var billingCost = _billing.BuildConfiguredCost(input.EstimatedPoints, 1);
        var tariffSnapshot = string.IsNullOrWhiteSpace(input.TariffSnapshotJson)
            ? JsonSerializer.Serialize(new
            {
                model = input.ModelName,
                providerCapabilityId = input.ProviderCapabilityId,
                unitCostPoints = input.EstimatedPoints,
                providerEstimatedCostUsd = input.EstimatedUsd,
                costSource = input.CostSource ?? "configured_tariff",
                pricingMode = input.PricingMode,
                pricingRuleKey = input.PricingRuleKey,
                capturedAtUtc = DateTimeOffset.UtcNow
            }, JsonOptions)
            : input.TariffSnapshotJson;

        var reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
        {
            LogicalRequestId = input.LogicalRequestId,
            RenderJobId = job.Id.ToString("N"),
            CustomerId = input.CustomerId,
            UserId = input.UserId,
            ProviderId = input.ProviderId,
            ProviderCapabilityId = input.ProviderCapabilityId,
            ProviderCode = input.ProviderCode,
            CapabilityCode = input.CapabilityCode,
            FeatureCode = "render_job_scene_video",
            RequestedModel = input.ModelName,
            Cost = billingCost,
            TrustedPayerContext = input.TrustedPayerContext,
            TariffSnapshotJson = tariffSnapshot,
            Metadata = new
            {
                parentJobId = input.ParentJobId,
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                input.DurationSeconds,
                input.Resolution,
                input.AspectRatio
            },
        }, ct);

        if (!reservation.Ok)
        {
            await FailAsync(project.Id, scene, version.Id, reservation.Status, reservation.ErrorMessage ?? "Unable to reserve billing.", ct);
            throw new RenderJobTerminalFailureException(reservation.ErrorMessage ?? "Unable to reserve billing.");
        }

        if (!reservation.ShouldSubmitProvider)
        {
            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_REUSED", "info",
                $"Scene {input.SceneIndex} continues polling an existing logical request.",
                new { jobId = job.Id, input.SceneId, input.SceneIndex, input.LogicalRequestId }, ct);
        }

        string? taskId = null;
        try
        {
            if (reservation.ShouldSubmitProvider)
            {
                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.VideoRendering,
                    errorMessage: null, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);

                var payload = LegacyVideoMapper.BuildSubmitRequest(
                    input.ModelName ?? string.Empty,
                    input.VideoPrompt,
                    input.SourceImageUrl,
                    input.AspectRatio,
                    input.Resolution,
                    input.DurationSeconds,
                    providerConfigJson: input.ProviderConfigJson,
                    capabilityConfigJson: input.CapabilityConfigJson);

                var submit = await _tasks.SubmitAsync(payload, ct);
                taskId = string.IsNullOrWhiteSpace(submit.TaskId) ? null : submit.TaskId.Trim();
                if (string.IsNullOrWhiteSpace(taskId))
                {
                    throw new InvalidOperationException("Provider submit response is missing task_id.");
                }

                await _versions.MarkSceneVideoVersionSubmittedAsync(
                    version.Id,
                    input.ProviderCode,
                    input.ModelName,
                    input.ProviderCapabilityId,
                    taskId,
                    ct: ct);
                await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_SUBMITTED", "info",
                    $"Scene {input.SceneIndex} submitted to the provider.",
                    new { jobId = job.Id, input.SceneId, input.SceneIndex, taskId, input.ModelName }, ct);
                await DeferPollAsync(job, taskId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                    "SCENE_VIDEO_POLL_SCHEDULED", "Video task submitted; polling will continue in a later worker pass.", ct);
            }
            else
            {
                taskId = await _versions.GetSceneVideoProviderTaskIdAsync(version.Id, ct);
                if (string.IsNullOrWhiteSpace(taskId))
                {
                    await MarkPendingReconciliationAsync(input, version.Id, input.LogicalRequestId, tariffSnapshot, "missing_task_id", "Missing provider_task_id for scene video reconciliation.", ct);
                    throw new RenderJobPendingReconciliationException("Missing provider_task_id for scene video reconciliation.");
                }
            }

            var terminal = await _tasks.GetStatusAsync(taskId!, ct);
            var normalized = terminal.Status?.Trim().ToUpperInvariant();
            if (normalized is not ("SUCCESS" or "FAILURE" or "CANCELLED" or "EXPIRED"))
            {
                if (normalized is not ("QUEUED" or "PENDING" or "SUBMITTED" or "PROCESSING" or "RUNNING"))
                {
                    throw new InvalidOperationException($"Provider returned unsupported status: {terminal.Status}");
                }

                await MarkPendingReconciliationAsync(input, version.Id, input.LogicalRequestId, tariffSnapshot, "provider_pending",
                    $"Provider video task remains {terminal.Status}.", ct, taskId);
                await DeferPollAsync(job, taskId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                    "SCENE_VIDEO_POLL_SCHEDULED", "Video task remains pending; the same provider task will be polled later.", ct);
            }

            if (!terminal.IsSuccess)
            {
                var failure = ExtractFailureMessage(terminal);
                await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = input.LogicalRequestId,
                    Success = false,
                    ActualModel = input.ModelName,
                    ProviderTaskId = taskId,
                    ProviderUsageJson = JsonSerializer.Serialize(terminal, JsonOptions),
                    TariffSnapshotJson = tariffSnapshot,
                    ErrorMessage = failure
                }, ct);
                await LogUsageAsync(input, job, input.LogicalRequestId, reservation.ChargedPoints, JsonSerializer.Serialize(terminal, JsonOptions), false, failure, taskId, ct);
                await FailAsync(project.Id, scene, version.Id, "provider_failure", failure, ct);
                throw new RenderJobTerminalFailureException(failure);
            }

            var outputUrl = ExtractVideoUrl(terminal, input.SourceImageUrl)
                ?? throw new InvalidOperationException($"Provider returned SUCCESS but no output video URL. task_id={taskId}");

            await _tenant.EnsureLoadedAsync(ct);
            var objectKey = version.StorageKey ?? SceneMediaStorageKeys.SceneVideoOutput(_tenant.TenantId, project.Id, scene.Id, version.Id);
            var saved = await _media.DownloadAndSaveBinaryAtObjectKeyAsync(
                outputUrl,
                objectKey,
                "video_scene_video",
                "video/mp4",
                input.UserId,
                input.CustomerId,
                _tenant.TenantId,
                ct);

            await _billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = input.LogicalRequestId,
                Success = true,
                ActualModel = input.ModelName,
                ProviderTaskId = taskId,
                ProviderUsageJson = JsonSerializer.Serialize(terminal, JsonOptions),
                TariffSnapshotJson = tariffSnapshot
            }, ct);
            await LogUsageAsync(input, job, input.LogicalRequestId, reservation.ChargedPoints, JsonSerializer.Serialize(terminal, JsonOptions), true, null, taskId, ct);

            await _versions.CompleteSceneVideoVersionAsync(version.Id, new SceneVideoVersionCompleteRequest(
                saved.PublicUrl ?? saved.FileUrl,
                ResolvePhysicalPath(saved.ObjectKey),
                PosterUrl: input.SourceImageUrl,
                DurationSeconds: input.DurationSeconds,
                MimeType: "video/mp4",
                ProviderCode: input.ProviderCode,
                ModelName: input.ModelName,
                ProviderCapabilityId: input.ProviderCapabilityId,
                ProviderTaskId: taskId,
                BillingLogicalRequestId: input.LogicalRequestId,
                EstimatedUsd: input.EstimatedUsd,
                ActualUsd: null,
                ChargedPoints: reservation.ChargedPoints,
                RefundedPoints: 0,
                CostSource: input.CostSource ?? "configured_tariff",
                AspectRatio: input.AspectRatio,
                ResultMediaId: saved.Id), ct);

            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_READY", "info",
                $"Scene {input.SceneIndex} rendered successfully.",
                new { jobId = job.Id, input.SceneId, input.SceneIndex, taskId, videoUrl = saved.PublicUrl ?? saved.FileUrl }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (!string.IsNullOrWhiteSpace(taskId))
        {
            await MarkPendingReconciliationAsync(input, version.Id, input.LogicalRequestId, tariffSnapshot, ex.ErrorCode ?? ex.GetType().Name, ex.Message, CancellationToken.None, taskId);
            await DeferPollAsync(job, taskId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                "SCENE_VIDEO_POLL_TRANSIENT", "Temporary provider poll failure; the same task ID will be retried.", CancellationToken.None);
        }
    }
#endif

    private async Task<SceneImageVersionDto?> ResolveSourceImageVersionAsync(
        long sceneId,
        Guid? selectedVersionId,
        bool useSharedReferenceImage,
        string? sourceImageUrl,
        string? sourceImageObjectKey,
        Guid? sharedReferenceMediaId,
        CancellationToken ct)
    {
        if (useSharedReferenceImage)
        {
            if (string.IsNullOrWhiteSpace(sourceImageUrl) && string.IsNullOrWhiteSpace(sourceImageObjectKey))
            {
                return null;
            }

            return new SceneImageVersionDto
            {
                Id = sharedReferenceMediaId ?? Guid.Empty,
                PublicUrl = sourceImageUrl,
                StorageKey = sourceImageObjectKey,
                Status = "completed",
                IsSelected = true
            };
        }

        if (selectedVersionId is Guid explicitVersionId && explicitVersionId != Guid.Empty)
        {
            var versions = await _versions.ListImageVersionsAsync(sceneId, 0, 100, ct);
            var explicitVersion = versions.FirstOrDefault(version =>
                version.Id == explicitVersionId
                && version.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));

            if (explicitVersion is not null)
            {
                return explicitVersion;
            }

            if (!string.IsNullOrWhiteSpace(sourceImageUrl) || !string.IsNullOrWhiteSpace(sourceImageObjectKey))
            {
                return new SceneImageVersionDto
                {
                    Id = Guid.Empty,
                    PublicUrl = sourceImageUrl,
                    StorageKey = sourceImageObjectKey,
                    Status = "completed",
                    IsSelected = false
                };
            }

            return null;
        }

        var selected = await _versions.GetSelectedImageVersionAsync(sceneId, ct);
        if (IsCompletedSelectedImageVersion(selected))
        {
            return selected;
        }

        if (!string.IsNullOrWhiteSpace(sourceImageUrl) || !string.IsNullOrWhiteSpace(sourceImageObjectKey))
        {
            return new SceneImageVersionDto
            {
                Id = Guid.Empty,
                PublicUrl = sourceImageUrl,
                StorageKey = sourceImageObjectKey,
                Status = "completed",
                IsSelected = false
            };
        }

        return null;
    }

    private static Guid? ResolvePersistedSourceImageVersionId(
        bool useSharedReferenceImage,
        SceneImageVersionDto sourceVersion)
        => useSharedReferenceImage || sourceVersion.Id == Guid.Empty
            ? null
            : sourceVersion.Id;

    private static bool IsCompletedSelectedImageVersion(SceneImageVersionDto? version)
        => version is not null
           && version.Id != Guid.Empty
           && version.IsSelected
           && version.Status.Equals("completed", StringComparison.OrdinalIgnoreCase);

    private static VideoSceneImageInputMode ResolveImageInputMode(SceneVideoRenderWorkItemInput input)
    {
        if (input.ImageInputMode == VideoSceneImageInputMode.LegacySelectedSource)
        {
            return input.UseSharedReferenceImage
                ? VideoSceneImageInputMode.SharedBaseImage
                : VideoSceneImageInputMode.SceneSource;
        }

        return input.ImageInputMode == VideoSceneImageInputMode.ReferenceOnly
            ? VideoSceneImageInputMode.SharedBaseImage
            : input.ImageInputMode;
    }

    private async Task<MediaFileDto?> ResolveSourceImageMediaAsync(SceneImageVersionDto version, CancellationToken ct)
    {
        if (version.ResultMediaId is Guid mediaId && mediaId != Guid.Empty)
        {
            var media = await _media.GetAsync(mediaId, ct);
            if (media is not null)
            {
                return media;
            }
        }

        if (!string.IsNullOrWhiteSpace(version.StorageKey))
        {
            var media = await _media.GetByObjectKeyAsync(version.StorageKey!, ct);
            if (media is not null)
            {
                return media;
            }
        }

        if (!string.IsNullOrWhiteSpace(version.PublicUrl))
        {
            var media = await _media.GetByPublicUrlAsync(version.PublicUrl!, ct);
            if (media is not null)
            {
                return media;
            }
        }

        return null;
    }

    private static int ResolveNextAttemptIndex(string logicalRequestId, IReadOnlyList<SceneVideoVersionDto> versions)
    {
        SceneVideoVersionDto? activeVersion = null;
        var maxAttempt = -1;
        foreach (var version in versions)
        {
            if (!IsMatchingLogicalRequestId(version.LogicalRequestId, logicalRequestId))
            {
                continue;
            }

            var attempt = ParseAttemptIndex(version.LogicalRequestId, logicalRequestId);
            if (attempt < 0)
            {
                continue;
            }

            maxAttempt = Math.Max(maxAttempt, attempt);
            if (IsActiveSceneVideoStatus(version.Status)
                && (activeVersion is null || attempt > ParseAttemptIndex(activeVersion.LogicalRequestId, logicalRequestId)))
            {
                activeVersion = version;
            }
        }

        if (activeVersion is not null)
        {
            return ParseAttemptIndex(activeVersion.LogicalRequestId, logicalRequestId);
        }

        return Math.Max(0, maxAttempt + 1);
    }

    private sealed record ResolvedFallbackCandidate(
        RVideoVideoModelPolicyEntry Policy,
        int ProviderDurationSeconds,
        string ProviderResolution);

    private sealed record FallbackCandidateDiagnostic(
        int Index,
        string Provider,
        string Model,
        string? Mode,
        int RequestedDuration,
        int? ProviderDuration,
        string RequestedResolution,
        string? ProviderResolution,
        bool Valid,
        string? InvalidReason);

    private sealed record FallbackCandidateResolution(
        IReadOnlyList<ResolvedFallbackCandidate> Candidates,
        IReadOnlyList<FallbackCandidateDiagnostic> Diagnostics);

    private async Task EnrichCatalogDurationsAsync(
        long providerId,
        List<AiProviderModelListItemDto> catalogModels,
        CancellationToken ct)
    {
        foreach (var model in catalogModels.Where(x => x.MediaType.Equals("video", StringComparison.OrdinalIgnoreCase)))
        {
            if (model.SupportedDurations.Count > 0)
            {
                continue;
            }

            var detail = await _models.GetModelByCodeAsync(providerId, model.ProviderModelCode, ct);
            if (detail is null || detail.IsDeprecated || !detail.Enabled)
            {
                continue;
            }

            var durations = detail.SupportedDurations
                .Where(duration => duration > 0)
                .ToList();
            if (durations.Count == 0)
            {
                durations = detail.Prices
                    .Where(price => price.Active && price.DurationSeconds is not null && price.DurationSeconds > 0)
                    .Select(price => price.DurationSeconds!.Value)
                    .Distinct()
                    .OrderBy(duration => duration)
                    .ToList();
            }

            if (durations.Count > 0)
            {
                model.SupportedDurations = durations;
            }
        }
    }

    private static IReadOnlyList<ResolvedFallbackCandidate> ResolveFallbackCandidates(
        SceneVideoRenderWorkItemInput input,
        IReadOnlyList<AiProviderModelListItemDto> catalogModels)
        => ResolveFallbackCandidateResolution(input, catalogModels).Candidates;

    private static FallbackCandidateResolution ResolveFallbackCandidateResolution(
        SceneVideoRenderWorkItemInput input,
        IReadOnlyList<AiProviderModelListItemDto> catalogModels)
    {
        var resolved = new List<ResolvedFallbackCandidate>();
        var diagnostics = new List<FallbackCandidateDiagnostic>();
        foreach (var policy in RVideoVideoModelPolicy.Models)
        {
            var requestedResolution = NormalizeResolution(input.Resolution);
            if (!string.Equals(policy.ProviderCode, input.ProviderCode, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(policy.Model))
            {
                diagnostics.Add(new FallbackCandidateDiagnostic(
                    policy.AttemptIndex,
                    policy.ProviderCode,
                    policy.Model,
                    policy.Mode,
                    input.DurationSeconds,
                    null,
                    requestedResolution,
                    null,
                    false,
                    "policy_provider_or_model_mismatch"));
                continue;
            }

            var model = catalogModels.FirstOrDefault(x =>
                string.Equals(x.ProviderModelCode, policy.Model, StringComparison.OrdinalIgnoreCase));
            if (model is null
                || !string.Equals(model.ProviderCode, input.ProviderCode, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(model.MediaType, "video", StringComparison.OrdinalIgnoreCase)
                || !model.Enabled
                || model.IsDeprecated)
            {
                diagnostics.Add(new FallbackCandidateDiagnostic(
                    policy.AttemptIndex,
                    policy.ProviderCode,
                    policy.Model,
                    policy.Mode,
                    input.DurationSeconds,
                    null,
                    requestedResolution,
                    null,
                    false,
                    model is null ? "catalog_model_missing" : "catalog_model_disabled_or_deprecated"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(policy.Mode)
                || model.SupportedModes.Count == 0
                || !model.SupportedModes.Contains(policy.Mode, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(new FallbackCandidateDiagnostic(
                    policy.AttemptIndex,
                    policy.ProviderCode,
                    policy.Model,
                    policy.Mode,
                    input.DurationSeconds,
                    null,
                    requestedResolution,
                    null,
                    false,
                    "catalog_mode_not_supported"));
                continue;
            }

            var supportedDurations = model.SupportedDurations
                .Where(duration => duration > 0)
                .Distinct()
                .OrderBy(duration => duration)
                .ToHashSet();
            if (supportedDurations.Count == 0)
            {
                diagnostics.Add(new FallbackCandidateDiagnostic(
                    policy.AttemptIndex,
                    policy.ProviderCode,
                    policy.Model,
                    policy.Mode,
                    input.DurationSeconds,
                    null,
                    requestedResolution,
                    null,
                    false,
                    "catalog_duration_contract_missing"));
                continue;
            }

            var providerDuration = ResolveProviderDuration(input.DurationSeconds, supportedDurations);
            if (providerDuration is not int duration)
            {
                diagnostics.Add(new FallbackCandidateDiagnostic(
                    policy.AttemptIndex,
                    policy.ProviderCode,
                    policy.Model,
                    policy.Mode,
                    input.DurationSeconds,
                    null,
                    requestedResolution,
                    null,
                    false,
                    "requested_duration_not_supported"));
                continue;
            }

            var providerResolution = ResolveProviderResolution(input.Resolution, model.SupportedResolutions);
            resolved.Add(new ResolvedFallbackCandidate(policy, duration, providerResolution));
            diagnostics.Add(new FallbackCandidateDiagnostic(
                policy.AttemptIndex,
                policy.ProviderCode,
                policy.Model,
                policy.Mode,
                input.DurationSeconds,
                duration,
                requestedResolution,
                providerResolution,
                true,
                null));
        }

        return new FallbackCandidateResolution(resolved, diagnostics);
    }

    private static int? ResolveProviderDuration(int sceneDurationSeconds, HashSet<int>? safeDurations)
    {
        if (safeDurations is null || safeDurations.Count == 0)
        {
            return null;
        }

        var resolved = safeDurations
            .Where(duration => duration >= sceneDurationSeconds)
            .OrderBy(duration => duration)
            .FirstOrDefault();
        return resolved > 0 ? resolved : null;
    }

    private static string ResolveProviderResolution(string? requestedResolution, IReadOnlyList<string>? supportedResolutions)
    {
        var requested = NormalizeResolution(requestedResolution);
        var supported = (supportedResolutions ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeResolution)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (supported.Count == 0 || supported.Contains(requested, StringComparer.OrdinalIgnoreCase))
        {
            return requested;
        }

        return supported
            .OrderBy(value => Math.Abs(ResolutionRank(value) - ResolutionRank(requested)))
            .ThenBy(ResolutionRank)
            .ThenBy(value => value, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string NormalizeResolution(string? value)
        => (value ?? "720p").Trim().ToLowerInvariant() switch
        {
            "480p" => "480p",
            "720p" => "720p",
            "1080p" => "1080p",
            "2k" => "2k",
            "4k" => "4k",
            _ => "720p"
        };

    private static int ResolutionRank(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "480p" => 480,
            "720p" => 720,
            "1080p" => 1080,
            "2k" => 2000,
            "4k" => 4000,
            _ => 720
        };

    private static bool IsCompatibleVersion(
        SceneVideoVersionDto version,
        SceneVideoRenderWorkItemInput input,
        RVideoVideoModelPolicyEntry policy,
        int providerDurationSeconds,
        string providerResolution)
        => !string.IsNullOrWhiteSpace(version.ProviderCode)
           && string.Equals(version.ProviderCode, input.ProviderCode, StringComparison.OrdinalIgnoreCase)
           && version.ProviderCapabilityId == input.ProviderCapabilityId
           && !string.IsNullOrWhiteSpace(version.ModelName)
           && string.Equals(version.ModelName, policy.Model, StringComparison.OrdinalIgnoreCase)
            && IsCompatibleRenderConfig(version.RenderConfigJson, input, policy, providerDurationSeconds, providerResolution);

    private static bool IsCompatibleRenderConfig(
        string? renderConfigJson,
        SceneVideoRenderWorkItemInput input,
        RVideoVideoModelPolicyEntry policy,
        int providerDurationSeconds,
        string providerResolution)
    {
        if (string.IsNullOrWhiteSpace(renderConfigJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(renderConfigJson);
            var root = doc.RootElement;
            return string.Equals(ReadJsonString(root, "provider"), input.ProviderCode, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ReadJsonString(root, "capability"), input.CapabilityCode, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ReadJsonString(root, "model"), policy.Model, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(ReadJsonString(root, "mode"), policy.Mode, StringComparison.OrdinalIgnoreCase)
                    && ReadJsonInt(root, "providerDurationSeconds") == providerDurationSeconds
                    && string.Equals(ReadJsonString(root, "providerResolution"), providerResolution, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsMatchingLogicalRequestId(string value, string logicalRequestId)
        => string.Equals(value, logicalRequestId, StringComparison.OrdinalIgnoreCase)
           || value.StartsWith($"{logicalRequestId}-fallback-", StringComparison.OrdinalIgnoreCase);

    private static int ParseAttemptIndex(string value, string logicalRequestId)
    {
        if (string.Equals(value, logicalRequestId, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var prefix = $"{logicalRequestId}-fallback-";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return int.TryParse(value[prefix.Length..], out var attempt) ? attempt : -1;
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadRenderConfigString(string? renderConfigJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(renderConfigJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(renderConfigJson);
            return ReadJsonString(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadJsonInt(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool IsActiveSceneVideoStatus(string status)
        => status.Equals("queued", StringComparison.OrdinalIgnoreCase)
           || status.Equals("submitted", StringComparison.OrdinalIgnoreCase)
           || status.Equals("pending_reconciliation", StringComparison.OrdinalIgnoreCase)
           || status.Equals("video_rendering", StringComparison.OrdinalIgnoreCase)
           || status.Equals("rendering", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnknownSubmission(
        SceneVideoVersionDto version,
        string? providerTaskId,
        string? providerVideoIdBase)
        => string.IsNullOrWhiteSpace(providerVideoIdBase)
           && (!string.IsNullOrWhiteSpace(providerTaskId)
               || version.Status.Equals("pending_reconciliation", StringComparison.OrdinalIgnoreCase));

    private static string BuildAttemptLogicalRequestId(string logicalRequestId, int attemptIndex)
        => attemptIndex <= 0 ? logicalRequestId : $"{logicalRequestId}-fallback-{attemptIndex}";

    private static bool IsTransientSubmit(Ai79TaskSubmitException ex)
        => ex.HttpStatusCode is null || (int)ex.HttpStatusCode >= 500 || (int)ex.HttpStatusCode == 429;

    private static Ai79TaskSubmitException? ExtractAi79SubmitException(Exception exception)
        => exception switch
        {
            Ai79TaskSubmitException direct => direct,
            VideoProviderTransientException { InnerException: Ai79TaskSubmitException inner } => inner,
            _ => null
        };

    private static bool IsDefinitivelyRejectedSubmit(Ai79TaskSubmitException exception)
    {
        if (exception.HttpStatusCode is null
            || HasAcceptedTaskId(exception.SanitizedResponseJson))
        {
            return false;
        }

        var code = exception.ErrorCode ?? string.Empty;
        if (code.Equals("missing_task_id", StringComparison.OrdinalIgnoreCase)
            || code.Equals("empty_response", StringComparison.OrdinalIgnoreCase)
            || code.Equals("invalid_json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsKnownNoResourcesSubmit(exception))
        {
            return true;
        }

        var statusCode = (int)exception.HttpStatusCode.Value;
        if (statusCode == 429 || statusCode >= 500)
        {
            return HasExplicitProviderRejection(exception.ErrorCode, exception.ErrorMessage);
        }

        return statusCode is 400 or 401 or 403 or 404 or 409 or 422
            && HasExplicitProviderRejection(exception.ErrorCode, exception.ErrorMessage);
    }

    private static string ClassifyRVideoSubmitFailure(Ai79TaskSubmitException exception)
        => IsKnownNoResourcesSubmit(exception)
            ? KnownNoResourcesFailureClassification
            : ClassifyProviderFailure(exception.ErrorCode, exception.ErrorMessage, exception.HttpStatusCode);

    private static bool IsKnownNoResourcesSubmit(Ai79TaskSubmitException exception)
    {
        if (exception.HttpStatusCode is not { } statusCode
            || (int)statusCode < 200
            || (int)statusCode >= 300
            || HasAcceptedTaskId(exception.SanitizedResponseJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(exception.SanitizedResponseJson);
            return HasNoResourcesMarker(document.RootElement)
                   && HasZeroCountTasks(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasNoResourcesMarker(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.Name.Equals("error", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("error_code", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("errorCode", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("code", StringComparison.OrdinalIgnoreCase))
                    && property.Value.ValueKind == JsonValueKind.String
                    && string.Equals(property.Value.GetString()?.Trim(), "NOT_RESOURCES", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if ((property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    && HasNoResourcesMarker(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasNoResourcesMarker(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasZeroCountTasks(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.Name.Equals("countTasks", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("count_tasks", StringComparison.OrdinalIgnoreCase))
                    && IsZeroJsonValue(property.Value))
                {
                    return true;
                }

                if ((property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    && HasZeroCountTasks(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasZeroCountTasks(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsZeroJsonValue(JsonElement value)
        => value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt32(out var number) && number == 0
            : value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString()?.Trim(), "0", StringComparison.Ordinal);

    private static bool HasExplicitProviderRejection(string? errorCode, string? errorMessage)
    {
        var text = $"{errorCode} {errorMessage}".ToLowerInvariant();
        return text.Contains("model unavailable", StringComparison.Ordinal)
               || text.Contains("model not available", StringComparison.Ordinal)
               || text.Contains("model_provider_failure", StringComparison.Ordinal)
               || text.Contains("provider_failure", StringComparison.Ordinal)
               || text.Contains("rejected", StringComparison.Ordinal)
               || text.Contains("invalid", StringComparison.Ordinal)
               || text.Contains("bad_request", StringComparison.Ordinal)
               || text.Contains("unauthor", StringComparison.Ordinal)
               || text.Contains("forbidden", StringComparison.Ordinal)
               || text.Contains("access denied", StringComparison.Ordinal)
               || text.Contains("quota", StringComparison.Ordinal)
               || text.Contains("insufficient balance", StringComparison.Ordinal)
               || text.Contains("billing", StringComparison.Ordinal);
    }

    private static bool HasAcceptedTaskId(string? sanitizedResponseJson)
    {
        if (string.IsNullOrWhiteSpace(sanitizedResponseJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(sanitizedResponseJson);
            return HasAcceptedTaskId(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasAcceptedTaskId(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals("id_base")
                        || property.NameEquals("task_id")
                        || property.NameEquals("taskId")
                        || property.NameEquals("videoId")
                        || property.NameEquals("video_id"))
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return true;
                }

                if ((property.Value.ValueKind == JsonValueKind.Object
                        || property.Value.ValueKind == JsonValueKind.Array)
                    && HasAcceptedTaskId(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasAcceptedTaskId(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string ClassifyProviderFailure(
        string? errorCode,
        string? message,
        System.Net.HttpStatusCode? httpStatusCode)
    {
        var code = errorCode ?? string.Empty;
        var text = $"{code} {message}".ToLowerInvariant();
        if ((int?)httpStatusCode is 401 or 403
            || text.Contains("unauthor", StringComparison.Ordinal)
            || text.Contains("forbidden", StringComparison.Ordinal)
            || text.Contains("access denied", StringComparison.Ordinal))
        {
            return "AUTHENTICATION_FAILURE";
        }

        if (text.Contains("billing", StringComparison.Ordinal)
            || text.Contains("insufficient", StringComparison.Ordinal)
            || text.Contains("balance", StringComparison.Ordinal)
            || text.Contains("quota", StringComparison.Ordinal))
        {
            return "BILLING_FAILURE";
        }

        if (code.Equals("provider_failure", StringComparison.OrdinalIgnoreCase)
            || code.Equals("model_provider_failure", StringComparison.OrdinalIgnoreCase))
        {
            return "MODEL_PROVIDER_FAILURE";
        }

        if (text.Contains("invalid", StringComparison.Ordinal)
            || text.Contains("prompt", StringComparison.Ordinal)
            || text.Contains("parameter", StringComparison.Ordinal)
            || text.Contains("bad_request", StringComparison.Ordinal)
            || (int?)httpStatusCode == 400)
        {
            return "INVALID_INPUT";
        }

        if ((int?)httpStatusCode is >= 500 or 429
            || text.Contains("timeout", StringComparison.Ordinal)
            || text.Contains("tempor", StringComparison.Ordinal)
            || text.Contains("unavailable", StringComparison.Ordinal))
        {
            return "TRANSIENT_PROVIDER_FAILURE";
        }

        return "MODEL_PROVIDER_FAILURE";
    }

    private static bool ShouldFallback(string classification)
        => classification is "MODEL_PROVIDER_FAILURE"
            or "TRANSIENT_PROVIDER_FAILURE"
            or KnownNoResourcesFailureClassification;

    private async Task AddAi79SubmitDiagnosticsAsync(
        RenderJobDto job,
        RVideoVideoModelPolicyEntry policy,
        Ai79TaskSubmitException exception,
        CancellationToken ct)
    {
        await _jobs.AddEventAsync(
            job.Id,
            "RVIDEO_79AI_SUBMIT_DIAGNOSTICS",
            "RVideo 79AI submit diagnostics captured.",
            new
            {
                exceptionType = nameof(Ai79TaskSubmitException),
                provider = "79ai",
                model = policy.Model,
                httpStatusCode = (int?)exception.HttpStatusCode,
                providerErrorCode = exception.ErrorCode,
                sanitizedResponseJson = SanitizeDiagnosticJson(exception.SanitizedResponseJson),
                sanitizedRequestMetadataJson = SanitizeDiagnosticJson(exception.SanitizedRequestMetadataJson),
                attemptCount = job.AttemptCount,
                maxAttempts = job.MaxAttempts
            },
            "error",
            ct);
    }

    private static string SanitizeDiagnosticJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return SanitizeDiagnosticElement(document.RootElement)?.ToJsonString(JsonOptions) ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static JsonNode? SanitizeDiagnosticElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SanitizeDiagnosticObject(element),
            JsonValueKind.Array => SanitizeDiagnosticArray(element),
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.Number => JsonValue.Create(element.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            _ => null
        };
    }

    private static JsonObject SanitizeDiagnosticObject(JsonElement element)
    {
        var result = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            if (IsSensitiveDiagnosticProperty(property.Name))
            {
                continue;
            }

            result[property.Name] = SanitizeDiagnosticElement(property.Value);
        }

        return result;
    }

    private static JsonArray SanitizeDiagnosticArray(JsonElement element)
    {
        var result = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            result.Add(SanitizeDiagnosticElement(item));
        }

        return result;
    }

    private static bool IsSensitiveDiagnosticProperty(string name)
        => name.Contains("token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
           || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || name.Contains("password", StringComparison.OrdinalIgnoreCase)
           || name.Contains("api_key", StringComparison.OrdinalIgnoreCase)
           || name.Contains("apikey", StringComparison.OrdinalIgnoreCase);

    private async Task AddFallbackStartedEventAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        RVideoVideoModelPolicyEntry fromPolicy,
        RVideoVideoModelPolicyEntry toPolicy,
        string? providerTaskId,
        string failureClassification,
        string? providerErrorCode,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FALLBACK_STARTED", "warning",
            "Scene-video fallback candidate started.",
            new
            {
                projectId = input.ProjectId,
                sceneId = scene.Id,
                input.SceneIndex,
                renderJobId = job.Id,
                fromProvider = fromPolicy.ProviderCode,
                fromModel = fromPolicy.Model,
                fromMode = fromPolicy.Mode,
                fromCandidateIndex = fromPolicy.AttemptIndex,
                toProvider = toPolicy.ProviderCode,
                toModel = toPolicy.Model,
                toMode = toPolicy.Mode,
                candidateIndex = toPolicy.AttemptIndex,
                status = "selected",
                providerTaskId,
                failureClassification,
                providerErrorCode
            }, ct);
    }

    private async Task AddFallbackFailedEventAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        RVideoVideoModelPolicyEntry policy,
        string? providerTaskId,
        string failureClassification,
        string? providerErrorCode,
        string? providerResponseJson,
        string? errorMessage,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FALLBACK_FAILED", "warning",
            "Scene-video fallback candidate failed.",
            new
            {
                projectId = input.ProjectId,
                sceneId = scene.Id,
                input.SceneIndex,
                renderJobId = job.Id,
                provider = policy.ProviderCode,
                model = policy.Model,
                mode = policy.Mode,
                candidateIndex = policy.AttemptIndex,
                status = "failed",
                providerTaskId,
                failureClassification,
                providerErrorCode,
                providerResponseJson,
                errorMessage
            }, ct);
    }

    private async Task AddFallbackExhaustedEventAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        RVideoVideoModelPolicyEntry policy,
        string? providerTaskId,
        string failureClassification,
        string? providerErrorCode,
        string? providerResponseJson,
        string? errorMessage,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FALLBACK_EXHAUSTED", "error",
            "All configured scene-video fallback candidates failed.",
            new
            {
                projectId = input.ProjectId,
                sceneId = scene.Id,
                input.SceneIndex,
                renderJobId = job.Id,
                provider = policy.ProviderCode,
                model = policy.Model,
                mode = policy.Mode,
                candidateIndex = policy.AttemptIndex,
                status = "exhausted",
                providerTaskId,
                failureClassification,
                providerErrorCode,
                providerResponseJson,
                errorMessage
            }, ct);
    }

    private async Task DeferPollAsync(
        RenderJobDto job,
        string taskId,
        TimeSpan delay,
        string eventCode,
        string message,
        CancellationToken ct)
    {
        await _jobs.ScheduleRetryAsync(job.Id, delay, eventCode, message, ct);
        _logger.LogInformation("RVIDEO_VIDEO_POLL_DEFERRED jobId={JobId} providerTaskId={ProviderTaskId} delaySeconds={DelaySeconds}",
            job.Id, taskId, Math.Max(1, (int)delay.TotalSeconds));
        throw new RenderJobDeferredException(message);
    }

    private async Task DeferProviderPollAsync(
        RenderJobDto job,
        string taskId,
        TimeSpan delay,
        string reasonCode,
        string message,
        CancellationToken ct)
    {
        var scheduled = await _jobs.ScheduleProviderPollAsync(
            job.Id,
            delay,
            reasonCode,
            message,
            ct,
            enforceReconciliationLimit: false,
            enforceProviderPollTimeout: true);
        if (!scheduled)
        {
            var current = await _jobs.GetAsync(job.Id, ct);
            if (current?.Status is RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled)
            {
                return;
            }

            await _jobs.MarkStatusAsync(
                job.Id,
                RenderJobStatuses.PendingReconciliation,
                errorCode: "SCENE_VIDEO_PROVIDER_POLL_TIMEOUT",
                errorMessage: "Known provider task polling reached the configured elapsed-time timeout; the same provider identifiers were retained for recovery.",
                ct: ct);
            throw new RenderJobDeferredException("Known provider task polling reached the configured elapsed-time timeout.");
        }

        _logger.LogInformation("RVIDEO_VIDEO_PROVIDER_POLL_DEFERRED jobId={JobId} providerTaskId={ProviderTaskId} delaySeconds={DelaySeconds}",
            job.Id, taskId, Math.Max(1, (int)delay.TotalSeconds));
        throw new RenderJobDeferredException(message);
    }

    private async Task MarkPendingReconciliationAsync(
        SceneVideoRenderWorkItemInput input,
        Guid versionId,
        string logicalRequestId,
        string? tariffSnapshot,
        string? errorCode,
        string errorMessage,
        CancellationToken ct,
        string? providerTaskId = null,
        string? actualModel = null,
        string? providerVideoIdBase = null)
    {
        await _billing.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
        {
            LogicalRequestId = logicalRequestId,
            ActualModel = actualModel ?? input.ModelName,
            ProviderTaskId = providerTaskId,
            TariffSnapshotJson = tariffSnapshot,
            ErrorMessage = errorMessage
        }, ct);
        if (versionId != Guid.Empty)
        {
            await _versions.MarkSceneVideoPendingReconciliationAsync(versionId, errorCode, errorMessage, ct, providerVideoIdBase);
        }
    }

    private sealed class VideoReconciliationException : InvalidOperationException
    {
        public VideoReconciliationException(string errorCode, string message)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    private async Task LogUsageAsync(
        SceneVideoRenderWorkItemInput input,
        RenderJobDto job,
        string logicalRequestId,
        decimal chargedPoints,
        string? providerUsageJson,
        bool success,
        string? errorMessage,
        string? providerTaskId,
        CancellationToken ct,
        string? actualModel = null,
        int? providerDurationSeconds = null)
    {
        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = null,
            ProviderId = input.ProviderId,
            ProviderCapabilityId = input.ProviderCapabilityId,
            ProviderCode = input.ProviderCode,
            CapabilityCode = input.CapabilityCode,
            FeatureCode = "render_job_scene_video",
            ModelName = actualModel ?? input.ModelName,
            RequestId = logicalRequestId,
            JobId = job.Id.ToString("N"),
            Quantity = 1,
            UnitType = "request",
            UnitCostPoints = input.EstimatedPoints,
            TotalPoints = chargedPoints,
            ProviderRawCost = input.EstimatedUsd,
            Status = success ? "success" : "failed",
            ErrorMessage = errorMessage,
            MetadataJson = BuildUsageMetadata(input, logicalRequestId, providerTaskId, providerUsageJson, chargedPoints, actualModel, providerDurationSeconds),
        }, ct);
    }

    private static string BuildUsageMetadata(
        SceneVideoRenderWorkItemInput input,
        string logicalRequestId,
        string? providerTaskId,
        string? providerUsageJson,
        decimal chargedPoints,
        string? actualModel = null,
        int? providerDurationSeconds = null)
    {
        try
        {
            using var providerUsage = string.IsNullOrWhiteSpace(providerUsageJson) ? null : JsonDocument.Parse(providerUsageJson);
            return JsonSerializer.Serialize(new
            {
                customerGuid = input.CustomerId,
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                input.ParentJobId,
                logicalRequestId,
                providerTaskId,
                model = actualModel ?? input.ModelName,
                requestedDurationSeconds = input.DurationSeconds,
                providerDurationSeconds = providerDurationSeconds ?? input.DurationSeconds,
                input.AspectRatio,
                input.Resolution,
                chargedPoints,
                providerEstimatedCostUsd = input.EstimatedUsd,
                costSource = input.CostSource,
                pricingMode = input.PricingMode,
                pricingRuleKey = input.PricingRuleKey,
                providerUsage = providerUsage?.RootElement
            }, JsonOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new
            {
                customerGuid = input.CustomerId,
                projectId = input.ProjectId,
                sceneId = input.SceneId,
                input.SceneIndex,
                input.ParentJobId,
                logicalRequestId,
                providerTaskId,
                requestedDurationSeconds = input.DurationSeconds,
                providerDurationSeconds = providerDurationSeconds ?? input.DurationSeconds,
                input.AspectRatio,
                input.Resolution,
                chargedPoints,
                providerEstimatedCostUsd = input.EstimatedUsd,
                costSource = input.CostSource,
                pricingMode = input.PricingMode,
                pricingRuleKey = input.PricingRuleKey
            }, JsonOptions);
        }
    }

    private async Task FailAsync(long projectId, VideoProjectSceneDto scene, Guid versionId, string? errorCode, string errorMessage, CancellationToken ct)
    {
        if (versionId != Guid.Empty)
        {
            await _versions.FailSceneVideoVersionAsync(versionId, errorCode, errorMessage, ct);
        }
        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
            errorMessage: errorMessage, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
        await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_RENDER_FAILED", "error",
            $"Scene video render failed for scene {scene.SceneIndex}.",
            new { sceneId = scene.Id, scene.SceneIndex, errorCode, error = errorMessage }, ct);
    }

    private async Task AddHttpSubmitDiagnosticAsync(
        long projectId,
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        Guid sceneVideoVersionId,
        VideoProviderHttpSubmitDiagnostic diagnostic,
        CancellationToken ct)
    {
        var (eventType, level, message) = diagnostic.Stage switch
        {
            VideoProviderHttpSubmitDiagnosticStage.Request => (
                "RVIDEO_VIDEO_HTTP_SUBMIT_REQUEST",
                "info",
                "79AI create-video HTTP request is about to be sent."),
            VideoProviderHttpSubmitDiagnosticStage.ResponseParseFailed => (
                "RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE_PARSE_FAILED",
                "warning",
                "79AI create-video HTTP response could not be parsed."),
            _ => (
                "RVIDEO_VIDEO_HTTP_SUBMIT_RESPONSE",
                "info",
                "79AI create-video HTTP response was received.")
        };

        await _repo.AddProjectEventAsync(projectId, eventType, level, message, new
        {
            renderJobId = job.Id,
            projectId = input.ProjectId,
            sceneId = input.SceneId,
            input.SceneIndex,
            sceneVideoVersionId,
            providerCode = input.ProviderCode,
            actualModel = diagnostic.ActualModel,
            mode = diagnostic.Mode,
            duration = diagnostic.DurationSeconds,
            ratio = diagnostic.Ratio,
            resolution = diagnostic.Resolution,
            imageCount = diagnostic.ImageUrls.Count,
            imageUrls = diagnostic.ImageUrls,
            endpoint = diagnostic.Endpoint,
            promptPreview = diagnostic.PromptPreview,
            httpStatus = diagnostic.HttpStatus,
            providerTaskId = diagnostic.ProviderTaskId,
            providerVideoIdBase = diagnostic.ProviderVideoIdBase,
            providerStatus = diagnostic.ProviderStatus,
            countTasks = diagnostic.CountTasks,
            providerMessage = diagnostic.ProviderMessage,
            sanitizedResponseJson = SanitizeDiagnosticJson(diagnostic.SanitizedResponseJson)
        }, ct);
    }

    private static object? BuildSubmitFailureDiagnostics(Exception exception)
    {
        var ai79 = exception switch
        {
            Ai79TaskSubmitException direct => direct,
            VideoProviderTransientException { InnerException: Ai79TaskSubmitException inner } => inner,
            _ => null
        };

        if (ai79 is null)
        {
            return null;
        }

        return new
        {
            httpStatus = (int?)ai79.HttpStatusCode,
            providerErrorCode = ai79.ErrorCode,
            providerErrorMessage = ai79.ErrorMessage,
            sanitizedResponse = ai79.SanitizedResponseJson,
            sanitizedRequestMetadata = ai79.SanitizedRequestMetadataJson
        };
    }

#if false
    private static string? ExtractVideoUrl(object response, string? sourceImageUrl)
    {
        if (response.Extra is null)
        {
            return null;
        }

        foreach (var branchName in new[] { "task_result", "output", "result" })
        {
            if (response.Extra.TryGetValue(branchName, out var branch))
            {
                var value = ExtractVideoUrl(branch, sourceImageUrl);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? ExtractVideoUrl(JsonElement element, string? sourceImageUrl)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "video_url", "videoUrl", "url", "output_url", "outputUrl" })
            {
                if (element.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri))
                {
                    var candidate = uri.ToString();
                    if (!string.Equals(candidate, sourceImageUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = ExtractVideoUrl(property.Value, sourceImageUrl);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = ExtractVideoUrl(item, sourceImageUrl);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static string ExtractFailureMessage(object response)
    {
        if (response.Error is JsonElement error)
        {
            if (error.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(error.GetString()))
            {
                return error.GetString()!;
            }

            if (error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(message.GetString()))
            {
                return message.GetString()!;
            }
        }

        return "Provider video task failed.";
    }
#endif

    private string ResolvePhysicalPath(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return string.Empty;
        }

        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        return Path.Combine(AppContext.BaseDirectory, uploadRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }
}
