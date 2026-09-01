using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
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
    public decimal? EstimatedUsd { get; set; }
    public decimal EstimatedPoints { get; set; }
    public string? PricingMode { get; set; }
    public string? PricingRuleKey { get; set; }
    public string? TariffSnapshotJson { get; set; }
    public string? CostSource { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public VideoSceneImageInputMode ImageInputMode { get; set; } = VideoSceneImageInputMode.LegacySelectedSource;
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

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IAiImageBillingService _billing;
    private readonly IAiProviderService _providers;
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
        var sourceImageVersionId = input.UseSharedReferenceImage
            ? null
            : requestedSourceImageVersionId ?? (sourceVersion.Id == Guid.Empty ? null : sourceVersion.Id);

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

        var attemptIndex = ResolveNextAttemptIndex(input.LogicalRequestId, attemptVersions);
        while (attemptIndex < RVideoVideoModelPolicy.Models.Count)
        {
            var policy = GetAttemptPolicy(input, attemptIndex)
                ?? throw new InvalidOperationException("SCENE_VIDEO_PROVIDER_POLICY_MISSING");
            var attemptLogicalRequestId = BuildAttemptLogicalRequestId(input.LogicalRequestId, attemptIndex);
            var version = await _versions.GetRecoverableSceneVideoVersionAsync(
                scene.Id,
                attemptLogicalRequestId,
                ct)
                ?? await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
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
                        attemptIndex
                    },
                    RenderConfigSnapshot: new
                    {
                        input,
                        attemptIndex,
                        policy.Model,
                        policy.Mode,
                        provider = input.ProviderCode,
                        capability = input.CapabilityCode
                    }), ct);

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
            string? taskId = string.IsNullOrWhiteSpace(existingTaskId) ? null : existingTaskId.Trim();
            AiImageBillingReservation reservation;
            if (string.IsNullOrWhiteSpace(taskId))
            {
                var billingCost = _billing.BuildConfiguredCost(input.EstimatedPoints, 1);
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
                        input.DurationSeconds,
                        input.Resolution,
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
                new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, input.CustomerId, input.ProviderCode, input.ModelName, input.CapabilityCode, requiredPoints = reservation.ChargedPoints }, ct);

            if (!reservation.ShouldSubmitProvider && string.IsNullOrWhiteSpace(taskId))
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot,
                    "missing_task_id", "Existing billing reservation has no provider_task_id.", ct);
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
                    var submit = await adapter.SubmitAsync(new VideoProviderSubmitRequest(
                        input.ProviderId,
                        input.ProviderCapabilityId,
                        input.ProviderCode,
                        input.CapabilityCode,
                        policy.Model,
                        policy.Mode,
                        providerPrompt,
                        input.AspectRatio,
                        input.Resolution,
                        input.DurationSeconds,
                        sourceImageAsset,
                        referenceImages), ct);
                    taskId = string.IsNullOrWhiteSpace(submit.ProviderTaskId) ? null : submit.ProviderTaskId.Trim();
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        throw new InvalidOperationException("Video provider submit response is missing task_id.");
                    }

                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SOURCE_UPLOAD_SUCCESS", "info",
                        "Scene-video source image handoff completed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, sourceImageVersionId = sourceVersion.Id, imageInputMode = imageInputMode.ToString() }, ct);
                    await _versions.MarkSceneVideoVersionSubmittedAsync(version.Id, input.ProviderCode, policy.Model, input.ProviderCapabilityId, taskId, ct);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMITTED", "info",
                        "Scene-video provider submit completed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, taskId, input.ProviderCode, model = policy.Model, input.CapabilityCode }, ct);
                    await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_SUBMITTED", "info",
                        $"Scene {input.SceneIndex} submitted to its configured video provider.",
                        new { jobId = job.Id, input.SceneId, input.SceneIndex, taskId, model = policy.Model, input.ProviderCode, attemptIndex }, ct);
                }
                catch (VideoProviderTransientException ex)
                {
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_FAILED", "warning",
                        "Scene-video provider submit did not complete.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, errorCode = ex.ErrorCode }, CancellationToken.None);
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, ex.ErrorCode ?? "submit_transient", ex.Message, CancellationToken.None, null);
                    await DeferPollAsync(job, attemptLogicalRequestId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_SCHEDULED", "Video provider submit transient; retry will reuse the same task flow.", CancellationToken.None);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SOURCE_UPLOAD_FAILED", "error",
                        "Scene-video source image handoff or provider submit failed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, errorCode = ex.GetType().Name, imageInputMode = ResolveImageInputMode(input).ToString() }, CancellationToken.None);
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_SUBMIT_FAILED", "error",
                        "Scene-video provider submit failed.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, errorCode = ex.GetType().Name }, CancellationToken.None);
                    throw;
                }
            }

            if (string.IsNullOrWhiteSpace(taskId))
            {
                taskId = await _versions.GetSceneVideoProviderTaskIdAsync(version.Id, ct);
            }

            if (string.IsNullOrWhiteSpace(taskId))
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "missing_task_id", "Missing provider_task_id for scene video reconciliation.", ct);
                throw new RenderJobPendingReconciliationException("Missing provider_task_id for scene video reconciliation.");
            }

            try
            {
                var adapter = _providerAdapters.Resolve(input.ProviderCode, input.CapabilityCode);
                var status = await adapter.PollAsync(new VideoProviderPollRequest(
                    input.ProviderId,
                    input.ProviderCapabilityId,
                    input.ProviderCode,
                    input.CapabilityCode,
                    taskId!), ct);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_POLL_RESPONSE", "info",
                    "Scene-video provider poll response received.",
                    new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, taskId, provider = input.ProviderCode, normalizedStatus = status.Status }, ct);
                if (status.Status is VideoProviderTaskStatus.Queued or VideoProviderTaskStatus.Processing)
                {
                    await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_PROCESSING", "info",
                        $"Scene {input.SceneIndex} provider task is still processing.",
                        new
                        {
                            jobId = job.Id,
                            sceneId = input.SceneId,
                            sceneIndex = input.SceneIndex,
                            providerTaskId = taskId,
                            normalizedStatus = status.Status,
                            providerRawResponse = status.SanitizedResponseJson
                        }, ct);
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "provider_pending", "Video provider task remains pending.", ct, taskId);
                    await DeferProviderPollAsync(job, taskId!, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_SCHEDULED", "Video task remains pending; the same provider task will be polled later.", ct);
                    return;
                }

                if (status.Status != VideoProviderTaskStatus.Success)
                {
                    var failure = status.ErrorMessage ?? $"Video provider task failed with status {status.Status}.";
                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_FAILED", "error",
                        "Scene-video provider reported a terminal failure.",
                        new { jobId = job.Id, input.ProjectId, input.SceneId, input.SceneIndex, taskId, errorCode = status.ErrorCode }, ct);
                    if (reservation.BillingRecordId is not null)
                    {
                        await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                        {
                            LogicalRequestId = attemptLogicalRequestId,
                            Success = false,
                            ActualModel = status.ActualModel ?? policy.Model,
                            ProviderTaskId = taskId,
                            ProviderUsageJson = status.SanitizedResponseJson,
                            TariffSnapshotJson = tariffSnapshot,
                            ErrorMessage = failure
                        }, ct);
                    }
                    await LogUsageAsync(input, job, attemptLogicalRequestId, reservation.ChargedPoints, status.SanitizedResponseJson, false, failure, taskId, ct);
                    await _versions.FailSceneVideoVersionAsync(version.Id, status.ErrorCode ?? "provider_failure", failure, ct);
                    if (GetAttemptPolicy(input, attemptIndex + 1) is not null)
                    {
                        attemptIndex++;
                        continue;
                    }

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

                    var saved = await _completion.CompleteProviderVideoAsync(new RVideoSceneVideoCompletionRequest(
                        project.Id,
                        scene.Id,
                        input.SceneIndex,
                        version.Id,
                        job.Id,
                        version.StorageKey,
                        attemptLogicalRequestId,
                        taskId!,
                        outputUrl,
                        input.ProviderCode,
                        status.ActualModel ?? policy.Model,
                        input.ProviderCapabilityId,
                        status.SanitizedResponseJson,
                        tariffSnapshot,
                        reservation.ChargedPoints,
                        input.EstimatedUsd,
                        input.CostSource,
                        input.AspectRatio,
                        sourceVersion.PublicUrl ?? input.SourceImageUrl,
                        input.DurationSeconds,
                        input.UserId,
                        input.CustomerId,
                        IsRecovery: !string.IsNullOrWhiteSpace(existingTaskId)), ct);
                    await LogUsageAsync(input, job, attemptLogicalRequestId, reservation.ChargedPoints, status.SanitizedResponseJson, true, null, taskId, ct);
                    return;
                }
                catch (VideoReconciliationException ex)
                {
                    await HandleReconciliationFailureAsync(
                        job, project, scene, version.Id, input, attemptLogicalRequestId, tariffSnapshot, taskId, ex.ErrorCode, ex.Message, ct);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await HandleReconciliationFailureAsync(
                        job, project, scene, version.Id, input, attemptLogicalRequestId, tariffSnapshot, taskId,
                        "PROVIDER_SUCCESS_RECONCILIATION_FAILED", ex.Message, ct);
                    return;
                }
            }
            catch (VideoProviderTransientException ex)
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "SCENE_VIDEO_POLL_TRANSIENT", ex.Message, CancellationToken.None, taskId);
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

        await FailAsync(project.Id, scene, Guid.Empty, "provider_failure", "Configured provider fallback attempts exhausted.", ct);
        throw new RenderJobTerminalFailureException("Configured provider fallback attempts exhausted.");
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
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var retryLimit = Math.Max(1, _config.GetValue("VideoRender:MaxReconciliationRetries", DefaultMaxReconciliationRetries));
        var currentAttempt = await _jobs.GetProviderReconciliationAttemptCountAsync(job.Id, ct) + 1;
        if (currentAttempt < retryLimit)
        {
            await MarkPendingReconciliationAsync(input, versionId, logicalRequestId, tariffSnapshot, errorCode, errorMessage, ct, providerTaskId);
            await _jobs.ScheduleProviderPollAsync(
                job.Id,
                TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                "SCENE_VIDEO_RECONCILIATION_RETRY",
                errorMessage,
                ct);
            throw new RenderJobDeferredException(errorMessage);
        }

        var finalCode = errorCode is "PROVIDER_OUTPUT_URL_MISSING"
            ? errorCode
            : "RVIDEO_VIDEO_PERSIST_FAILED";
        await _versions.FailSceneVideoVersionAsync(versionId, finalCode, errorMessage, ct);
        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
            errorMessage: errorMessage, title: scene.Title, scenePrompt: scene.ScenePrompt,
            imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_PERSIST_FAILED", "error",
            "Provider video succeeded but local persistence did not complete.",
            new { jobId = job.Id, input.SceneId, input.SceneIndex, versionId, providerTaskId, errorCode = finalCode },
            ct);
        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_FAILED", "error",
            $"Scene {input.SceneIndex} result reconciliation failed.",
            new
            {
                jobId = job.Id,
                sceneId = input.SceneId,
                sceneIndex = input.SceneIndex,
                providerTaskId,
                errorCode = finalCode,
                errorMessage,
                reconciliationAttempt = currentAttempt,
                maxReconciliationRetries = retryLimit
            }, ct);
        throw new RenderJobTerminalFailureException(errorMessage);
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

                await _versions.MarkSceneVideoVersionSubmittedAsync(version.Id, input.ProviderCode, input.ModelName, input.ProviderCapabilityId, taskId, ct);
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

            return explicitVersion;
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

    private static RVideoVideoModelPolicyEntry? GetAttemptPolicy(SceneVideoRenderWorkItemInput input, int attemptIndex)
    {
        if (RVideoVideoModelPolicy.Is79AiProvider(input.ProviderCode)
            && string.Equals(input.CapabilityCode, RVideoVideoModelPolicy.CapabilityCode, StringComparison.OrdinalIgnoreCase))
        {
            return RVideoVideoModelPolicy.GetByAttemptIndex(attemptIndex);
        }

        return attemptIndex == 0
            ? new RVideoVideoModelPolicyEntry(0, input.ProviderCode, input.ModelName ?? string.Empty, null)
            : null;
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

    private static bool IsActiveSceneVideoStatus(string status)
        => status.Equals("queued", StringComparison.OrdinalIgnoreCase)
           || status.Equals("submitted", StringComparison.OrdinalIgnoreCase)
           || status.Equals("pending_reconciliation", StringComparison.OrdinalIgnoreCase)
           || status.Equals("video_rendering", StringComparison.OrdinalIgnoreCase)
           || status.Equals("rendering", StringComparison.OrdinalIgnoreCase);

    private static string BuildAttemptLogicalRequestId(string logicalRequestId, int attemptIndex)
        => attemptIndex <= 0 ? logicalRequestId : $"{logicalRequestId}-fallback-{attemptIndex}";

    private static bool IsTransientSubmit(Ai79TaskSubmitException ex)
        => ex.HttpStatusCode is null || (int)ex.HttpStatusCode >= 500 || (int)ex.HttpStatusCode == 429;

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
        var scheduled = await _jobs.ScheduleProviderPollAsync(job.Id, delay, reasonCode, message, ct);
        if (!scheduled)
        {
            var current = await _jobs.GetAsync(job.Id, ct);
            if (current?.Status is RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled)
            {
                return;
            }

            throw new InvalidOperationException($"RVIDEO provider poll could not be re-queued for job {job.Id}.");
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
        string? providerTaskId = null)
    {
        await _billing.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
        {
            LogicalRequestId = logicalRequestId,
            ActualModel = input.ModelName,
            ProviderTaskId = providerTaskId,
            TariffSnapshotJson = tariffSnapshot,
            ErrorMessage = errorMessage
        }, ct);
        if (versionId != Guid.Empty)
        {
            await _versions.MarkSceneVideoPendingReconciliationAsync(versionId, errorCode, errorMessage, ct);
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
        CancellationToken ct)
    {
        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = null,
            ProviderId = input.ProviderId,
            ProviderCapabilityId = input.ProviderCapabilityId,
            ProviderCode = input.ProviderCode,
            CapabilityCode = input.CapabilityCode,
            FeatureCode = "render_job_scene_video",
            ModelName = input.ModelName,
            RequestId = logicalRequestId,
            JobId = job.Id.ToString("N"),
            Quantity = 1,
            UnitType = "request",
            UnitCostPoints = input.EstimatedPoints,
            TotalPoints = chargedPoints,
            ProviderRawCost = input.EstimatedUsd,
            Status = success ? "success" : "failed",
            ErrorMessage = errorMessage,
            MetadataJson = BuildUsageMetadata(input, logicalRequestId, providerTaskId, providerUsageJson, chargedPoints),
        }, ct);
    }

    private static string BuildUsageMetadata(
        SceneVideoRenderWorkItemInput input,
        string logicalRequestId,
        string? providerTaskId,
        string? providerUsageJson,
        decimal chargedPoints)
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
                input.DurationSeconds,
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
                input.DurationSeconds,
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
