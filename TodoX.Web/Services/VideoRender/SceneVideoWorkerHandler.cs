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
    public string? RequestedModelCode { get; set; }
    public string? RequestedMode { get; set; }
    public int? RequestedDurationSeconds { get; set; }
    public bool ManualOverride { get; set; }
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

public sealed partial class SceneVideoWorkerHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int DefaultMaxReconciliationRetries = 3;
    private const string KnownNoResourcesFailureClassification = "KNOWN_NO_RESOURCES";
    private const string ProviderRejectedNoTaskFailureClassification = "PROVIDER_REJECTED_NO_TASK";
    private const string DurationRejectedFailureClassification = "PROVIDER_DURATION_REJECTED";
    private const string PollResourceUnavailableNoProgressFailureClassification = "POLL_RESOURCE_UNAVAILABLE_NO_PROGRESS";

    private readonly VideoRenderRepository _repo;
    private readonly SceneVideoSubmitCoordinator _submitCoordinator;
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
        SceneVideoSubmitCoordinator submitCoordinator,
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
        _submitCoordinator = submitCoordinator;
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
        try
        {
            await HandleCoreAsync(job, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            and not RenderJobPendingReconciliationException and not RenderJobDeferredException
            and not RenderJobTerminalFailureException)
        {
            // Parser/catalog/storage errors cannot turn an accepted task into an application retry/fallback.
            await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
            throw;
        }
    }

    private async Task HandleCoreAsync(RenderJobDto job, CancellationToken ct)
    {
        if (job.AttemptCount > job.MaxAttempts)
        {
            await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
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
            await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
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
        // Read all job-owned versions/intents, not the scene's latest page or just the worker snapshot.
        job = await _jobs.GetAsync(job.Id, ct)
            ?? throw new RenderJobPendingReconciliationException("Render job identity is missing.");
        var attemptVersions = await _versions.ListJobSceneVideoVersionsAsync(job.Id, ct);
        var intents = await _submitCoordinator.ListJobSubmitIntentsAsync(job.Id, ct);
        var taskIntents = intents.Where(i => i.SubmitState == "CONFIRMED_PROVIDER_TASK"
            || !string.IsNullOrWhiteSpace(i.ProviderTaskId) || !string.IsNullOrWhiteSpace(i.ProviderVideoIdBase)).ToArray();
        var taskVersions = attemptVersions.Where(v => !string.IsNullOrWhiteSpace(v.ProviderTaskId)
            || !string.IsNullOrWhiteSpace(v.ProviderVideoIdBase)).ToArray();
        var confirmedTask = job.ProviderTaskId?.Trim();
        var hasProviderIdentity = taskIntents.Length > 0 || taskVersions.Length > 0
            || !string.IsNullOrWhiteSpace(confirmedTask) || !string.IsNullOrWhiteSpace(job.ProviderVideoIdBase);
        var owners = Array.Empty<SceneVideoVersionDto>();
        if (intents.Any(i => i.SubmitState is "SUBMIT_IN_FLIGHT" or "AMBIGUOUS_SUBMIT" or "MANUAL_REVIEW_REQUIRED"))
            throw new RenderJobPendingReconciliationException("Unresolved provider submission must be reconciled before selecting another attempt.");
        if (hasProviderIdentity)
        {
            if (taskIntents.Length != 1 || taskVersions.Length != 1
                || string.IsNullOrWhiteSpace(confirmedTask) || string.IsNullOrWhiteSpace(job.ProviderVideoIdBase))
                throw new RenderJobPendingReconciliationException("Provider task ownership is missing or ambiguous; no new attempt is allowed.");
            var owner = taskVersions[0];
            var intent = taskIntents[0];
            if (owner.ProjectId != input.ProjectId || owner.SceneId != input.SceneId
                || !IsMatchingLogicalRequestId(owner.LogicalRequestId, input.LogicalRequestId)
                || intent.SceneId != input.SceneId || intent.LogicalRequestId != owner.LogicalRequestId
                || intent.SubmitState != "CONFIRMED_PROVIDER_TASK"
                || owner.ProviderTaskId != confirmedTask || intent.ProviderTaskId != confirmedTask
                || owner.ProviderVideoIdBase != job.ProviderVideoIdBase || intent.ProviderVideoIdBase != job.ProviderVideoIdBase)
                throw new RenderJobPendingReconciliationException("Job, owner version and confirmed intent identities conflict; submit is prohibited.");
            owners = [owner];
        }
        if (owners.Length == 0 && attemptVersions.Any(v => v.Status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            && IsMatchingLogicalRequestId(v.LogicalRequestId, input.LogicalRequestId)))
            return;

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
            if (hasProviderIdentity)
                throw new RenderJobPendingReconciliationException("Confirmed task source snapshot is unavailable; reconcile without creating another attempt.");
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
        if (!validation.IsValid && !hasProviderIdentity)
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

        var catalogModels = (await _models.GetModelsAsync(
            input.ProviderCode,
            mediaType: "video",
            enabled: true,
            ct: ct))
            .Where(x => !x.IsDeprecated)
            .ToList();
        await EnrichCatalogDurationsAsync(input.ProviderId, catalogModels, ct);
        var candidateResolution = ResolveFallbackCandidateResolution(input, catalogModels);
        var candidates = candidateResolution.Candidates.ToList();
        var historicalCandidates = HydrateHistoricalCandidates(candidates, attemptVersions, input);
        foreach (var historicalCandidate in historicalCandidates)
        {
            var historicalIndex = ParseAttemptIndex(historicalCandidate.Version.LogicalRequestId, input.LogicalRequestId);
            if (historicalIndex < 0)
            {
                continue;
            }

            if (historicalCandidate.Candidate is null)
            {
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_HISTORICAL_CANDIDATE_MISMATCH", "warning",
                    "A persisted scene-video candidate could not be matched to the current fallback catalog.",
                    new
                    {
                        projectId = input.ProjectId,
                        sceneId = input.SceneId,
                        renderJobId = job.Id,
                        logicalRequestId = historicalCandidate.Version.LogicalRequestId,
                        candidateIndex = historicalIndex,
                        provider = historicalCandidate.Version.ProviderCode,
                        model = historicalCandidate.Version.ModelName
                    }, ct);
                continue;
            }

            if (!candidates.Any(x => string.Equals(
                    BuildCandidateKey(x.Policy, x.ProviderDurationSeconds, x.ProviderResolution),
                    BuildCandidateKey(historicalCandidate.Candidate.Policy, historicalCandidate.Candidate.ProviderDurationSeconds, historicalCandidate.Candidate.ProviderResolution),
                    StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Insert(Math.Min(historicalIndex, candidates.Count), historicalCandidate.Candidate);
                await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_HISTORICAL_CANDIDATE_RESOLVED", "info",
                    "A persisted scene-video candidate was restored into the current fallback ordering.",
                    new
                    {
                        sceneId = input.SceneId,
                        renderJobId = job.Id,
                        logicalRequestId = historicalCandidate.Version.LogicalRequestId,
                        provider = historicalCandidate.Candidate.Policy.ProviderCode,
                        model = historicalCandidate.Candidate.Policy.Model,
                        mode = historicalCandidate.Candidate.Policy.Mode,
                        providerDurationSeconds = historicalCandidate.Candidate.ProviderDurationSeconds,
                        providerResolution = historicalCandidate.Candidate.ProviderResolution,
                        resolvedCandidateKey = BuildCandidateKey(
                            historicalCandidate.Candidate.Policy,
                            historicalCandidate.Candidate.ProviderDurationSeconds,
                            historicalCandidate.Candidate.ProviderResolution)
                    }, ct);
            }
        }
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
        var attemptIndex = owners.Length == 1
            ? ParseAttemptIndex(owners[0].LogicalRequestId, input.LogicalRequestId)
            : ResolveNextAttemptIndex(input.LogicalRequestId, attemptVersions, candidates);
        if (owners.Length == 1 && (attemptIndex < 0 || attemptIndex >= candidates.Count))
            throw new RenderJobPendingReconciliationException("Confirmed provider task owner has no matching fallback candidate.");
        string? fallbackReason = null;
        while (attemptIndex < candidates.Count)
        {
            var candidate = candidates[attemptIndex];
            var policy = candidate.Policy;
            var attemptLogicalRequestId = BuildAttemptLogicalRequestId(input.LogicalRequestId, attemptIndex);
            var version = owners.Length == 1
                ? owners[0]
                : input.ReuseExistingSceneVideoVersion
                ? await _versions.GetSceneVideoVersionByLogicalRequestIdAsync(attemptLogicalRequestId, ct)
                : await _versions.GetRecoverableSceneVideoVersionAsync(scene.Id, attemptLogicalRequestId, ct);
            if (owners.Length == 1 && (!string.Equals(version!.LogicalRequestId, attemptLogicalRequestId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(version.ProviderTaskId, confirmedTask, StringComparison.OrdinalIgnoreCase)))
                throw new RenderJobPendingReconciliationException("Confirmed provider task owner intent does not match the candidate.");
            if (input.ExistingSceneVideoVersionId is Guid expectedVersionId && version?.Id != expectedVersionId)
            {
                if (owners.Length == 1)
                    throw new RenderJobPendingReconciliationException("Confirmed task owner differs from the expected version; manual reconciliation required.");
                version = null;
            }
            if (version is not null && !IsCompatibleVersion(version, input, policy, candidate.ProviderDurationSeconds, candidate.ProviderResolution))
            {
                if (owners.Length == 1)
                    throw new RenderJobPendingReconciliationException("Confirmed provider task owner has a conflicting provider/model contract.");
                await _versions.FailSceneVideoVersionAsync(
                    version.Id,
                    "RVIDEO_VIDEO_INVALID_FALLBACK_VERSION",
                    "Existing scene-video version has no valid provider/model/task contract.",
                    ct);
                version = null;
            }

            // Keep the exact operation at the DB boundary: PostgreSQL may report a position in
            // a trigger/function rather than in the application command text.
            if (version is null)
            {
                if (owners.Length == 1)
                    throw new RenderJobPendingReconciliationException("Confirmed provider task owner could not be recovered.");
                try
                {
                    await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
                    version = await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
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
                        manualOverride = input.ManualOverride,
                        requestedModel = input.RequestedModelCode,
                        requestedMode = input.RequestedMode,
                        requestedDurationSeconds = input.RequestedDurationSeconds ?? input.DurationSeconds,
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
                        manualOverride = input.ManualOverride,
                        requestedModel = input.RequestedModelCode,
                        requestedMode = input.RequestedMode,
                        requestedDurationSeconds = input.RequestedDurationSeconds ?? input.DurationSeconds,
                        requestedResolution = input.Resolution,
                        providerResolution = candidate.ProviderResolution,
                        provider = input.ProviderCode,
                        capability = input.CapabilityCode
                    },
                    ProviderCode: input.ProviderCode,
                    RequestedModel: policy.Model,
                    ActualModel: policy.Model,
                    ProviderCapabilityId: input.ProviderCapabilityId), ct);
                }
                catch (Npgsql.PostgresException ex)
                {
                    _logger.LogError(ex,
                        "RVIDEO_SQL_DIAGNOSTIC operation=CreateQueuedSceneVideoVersion sqlState={SqlState} position={Position} internalPosition={InternalPosition} jobId={JobId} sceneId={SceneId} versionId=not_committed; command template: INSERT video_render.scene_video_versions (... render_job_id ...) VALUES (... @renderJobId ...); internal query intentionally omitted",
                        ex.SqlState, ex.Position, ex.InternalPosition, job.Id, input.SceneId);
                    throw;
                }
            }

            if (version.RenderJobId != job.Id || version.ProjectId != input.ProjectId || version.SceneId != input.SceneId
                || version.LogicalRequestId != attemptLogicalRequestId)
                throw new RenderJobPendingReconciliationException("Selected version does not belong to this job/scene/logical request; submit is prohibited.");

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
            if (owners.Length == 1 && (string.IsNullOrWhiteSpace(taskId)
                || !string.Equals(providerTaskId, confirmedTask, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(providerVideoIdBase, job.ProviderVideoIdBase, StringComparison.OrdinalIgnoreCase)))
                throw new RenderJobPendingReconciliationException("Confirmed provider task lost its owner identity before polling; submit is prohibited.");
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
                if (hasProviderIdentity)
                    throw new RenderJobPendingReconciliationException("Billing state requires reconciliation while the confirmed provider task is retained.");
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

            Guid? submitToken = null;
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
                    // A rejected claim (guard/duplicate intent) is decided entirely before any provider HTTP
                    // request. It must not be reported as an ambiguous in-flight submit: no request was sent,
                    // so no reconciliation is warranted and no manual review flag is set.
                    await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
                    var claim = await _submitCoordinator.ClaimSubmitAsync(input.ProjectId, input.SceneId, attemptLogicalRequestId, job.Id, ct);
                    if (claim.Outcome == SceneVideoSubmitClaimOutcome.Claimed
                        && claim.BlockReason == SceneVideoSubmitBlockReason.HistoricalManualReviewWithoutLiveAttempt)
                    {
                        // A settled historical attempt left a manual-review marker, but no live attempt holds
                        // the scene. The marker is reported here instead of being silently dropped; the new
                        // operation proceeds and the historical rows are never modified.
                        await _repo.AddProjectEventAsync(input.ProjectId, "RVIDEO_VIDEO_HISTORICAL_MANUAL_REVIEW_OVERRIDDEN", "info",
                            "A historical manual-review marker exists on a settled attempt; the new user-initiated operation proceeds with its own provider request.",
                            new
                            {
                                jobId = job.Id,
                                input.ProjectId,
                                input.SceneId,
                                input.SceneIndex,
                                sceneVideoVersionId = version.Id,
                                logicalRequestId = attemptLogicalRequestId,
                                historicalAttemptModified = false,
                                providerRequestSent = false
                            }, ct);
                    }
                    if (claim.Outcome != SceneVideoSubmitClaimOutcome.Claimed || claim.OwnerToken is null)
                    {
                        // A refused claim is decided entirely before the provider call, so it is a definite
                        // terminal failure and must never be parked in ambiguous reconciliation.
                        if (claim.Outcome == SceneVideoSubmitClaimOutcome.AlreadyClaimed)
                        {
                            // The intent already exists. If it proves a submit happened, re-link to the stored
                            // task instead of failing or resending; otherwise this attempt is a real duplicate.
                            var existingState = await _submitCoordinator.GetSubmitStateAsync(input.SceneId, attemptLogicalRequestId, ct);
                            if (existingState is not null && (!string.IsNullOrWhiteSpace(existingState.ProviderTaskId)
                                || !string.IsNullOrWhiteSpace(existingState.ProviderVideoIdBase)
                                || existingState.SubmitState is "CONFIRMED_PROVIDER_TASK" or "SUBMIT_IN_FLIGHT" or "AMBIGUOUS_SUBMIT" or "MANUAL_REVIEW_REQUIRED"))
                            {
                                throw new RenderJobPendingReconciliationException("Existing submit intent must be resolved through its unique owner before polling or submitting.");
                            }
                            else
                            {
                                await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, submitToken, ct);
                                await RecordPreHttpSubmitRefusalAsync(input, job, scene, version.Id, attemptLogicalRequestId,
                                    SceneVideoSubmitGuard.ErrorCodeDuplicate, SceneVideoSubmitGuard.MessageDuplicate,
                                    claim.BlockReason, intentAlreadyHadProviderTask: false, ct);
                                throw new RenderJobTerminalFailureException(SceneVideoSubmitGuard.MessageDuplicate);
                            }
                        }
                        else
                        {
                            await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, submitToken, ct);
                            await RecordPreHttpSubmitRefusalAsync(input, job, scene, version.Id, attemptLogicalRequestId,
                                SceneVideoSubmitGuard.ErrorCodeGuardRejected, SceneVideoSubmitGuard.MessageGuardRejected,
                                claim.BlockReason, intentAlreadyHadProviderTask: false, ct);
                            throw new RenderJobTerminalFailureException(SceneVideoSubmitGuard.MessageGuardRejected);
                        }
                    }
                    if (claim.Outcome == SceneVideoSubmitClaimOutcome.Claimed && claim.OwnerToken is { } claimedToken)
                        submitToken = claimedToken;
                    if (submitToken is { } activeToken)
                    {
                        await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, activeToken, ct);
                        if (!await _submitCoordinator.BeginSubmitAsync(activeToken, ct))
                        {
                            // State conflict only: no request was sent, and the existing intent protection is preserved.
                            await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, submitToken, ct);
                            await RecordPreHttpSubmitRefusalAsync(input, job, scene, version.Id, attemptLogicalRequestId,
                                SceneVideoSubmitGuard.ErrorCodeIntentTransitionFailed, SceneVideoSubmitGuard.MessageIntentTransitionFailed,
                                SceneVideoSubmitBlockReason.ExistingIntentUnresolved, intentAlreadyHadProviderTask: false, ct);
                            throw new RenderJobTerminalFailureException(SceneVideoSubmitGuard.MessageIntentTransitionFailed);
                        }
                        await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, activeToken, ct);
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
                        if (string.IsNullOrWhiteSpace(providerTaskId) || string.IsNullOrWhiteSpace(providerVideoIdBase))
                        {
                            await _submitCoordinator.MarkAmbiguousAsync(activeToken, CancellationToken.None);
                            throw new RenderJobPendingReconciliationException("Provider submit result requires manual reconciliation.");
                        }
                        if (!await _submitCoordinator.ConfirmSubmittedAsync(activeToken, providerTaskId, providerVideoIdBase, CancellationToken.None))
                            throw new RenderJobPendingReconciliationException("Provider submit intent could not be confirmed.");
                        hasProviderIdentity = true;
                        taskId = providerVideoIdBase;
                        await _jobs.SetProviderIdentifiersAsync(job.Id, providerTaskId, providerVideoIdBase, ct);
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
                    if (hasProviderIdentity)
                        throw new RenderJobPendingReconciliationException("An accepted provider task must be reconciled; submit fallback is prohibited.", submitException);
                    var canFallback = submitToken is not null && structuredException is not null
                        && IsDefinitivelyRejectedSubmit(structuredException);
                    if (canFallback)
                    {
                        // Only an explicitly rejected submit can release this attempt for fallback.
                        // Keep the durable intent even after rejection so a restart cannot resubmit it.
                        if (!await _submitCoordinator.ConfirmNoTaskAsync(submitToken.Value, CancellationToken.None))
                            throw new RenderJobPendingReconciliationException("Submit intent ownership changed; manual reconciliation required.");
                        var ai79Exception = structuredException!;
                        var failureClassification = ClassifyRVideoSubmitFailure(ai79Exception);
                        var nextCandidate = ResolveNextCandidateAfterFailure(
                            candidate,
                            failureClassification,
                            attemptIndex,
                            candidates,
                            catalogModels);
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
                                sanitizedRequestMetadataJson = SanitizeDiagnosticJson(ai79Exception.SanitizedRequestMetadataJson),
                                submitOutcome = "definitely_not_submitted",
                                countTasks = 0,
                                hasAcceptedTaskId = false,
                                fallbackAvailable = nextCandidate is not null,
                                nextProvider = nextCandidate?.Policy.ProviderCode,
                                nextModel = nextCandidate?.Policy.Model,
                                nextMode = nextCandidate?.Policy.Mode
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

                        if (nextCandidate is null)
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

                        var nextCandidateIndex = candidates.IndexOf(nextCandidate);
                        if (failureClassification == DurationRejectedFailureClassification
                            && IsSamePolicy(policy, nextCandidate.Policy)
                            && nextCandidate.ProviderDurationSeconds > candidate.ProviderDurationSeconds)
                        {
                            await AddDurationFallbackStartedEventAsync(
                                project,
                                scene,
                                job,
                                input,
                                policy,
                                candidate.ProviderDurationSeconds,
                                nextCandidate.ProviderDurationSeconds,
                                failureClassification,
                                ai79Exception.ErrorCode,
                                ai79Exception.ErrorMessage,
                                CancellationToken.None);
                        }
                        else
                        {
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
                        }
                        await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
                        fallbackReason = failureClassification;
                        attemptIndex = nextCandidateIndex;
                        continue;
                    }

                    var ex = submitException as VideoProviderTransientException
                        ?? new VideoProviderTransientException(
                            submitException.Message,
                            structuredException?.ErrorCode,
                            submitException);
                    if (submitToken is { } ambiguousToken)
                        await _submitCoordinator.MarkAmbiguousAsync(ambiguousToken, CancellationToken.None);
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
                            errorCode = ex.ErrorCode,
                            submitOutcome = "ambiguous"
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
                catch (OperationCanceledException) when (submitToken is not null)
                {
                    await _submitCoordinator.MarkAmbiguousAsync(submitToken.Value, CancellationToken.None);
                    throw;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (submitToken is not null)
                    {
                        await _submitCoordinator.MarkAmbiguousAsync(submitToken.Value, CancellationToken.None);
                        throw new RenderJobPendingReconciliationException(
                            "Provider submission outcome requires reconciliation before retry.", ex);
                    }
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
                await VerifyConfirmedPollOwnerAsync(job.Id, input.ProjectId, input.SceneId, version.Id,
                    attemptLogicalRequestId, providerTaskId, providerVideoIdBase, ct);
                var status = await adapter.PollAsync(new VideoProviderPollRequest(
                    input.ProviderId,
                    input.ProviderCapabilityId,
                    input.ProviderCode,
                    input.CapabilityCode,
                    taskId!), ct);
                if (status.ProviderTaskId != providerVideoIdBase)
                    throw new RenderJobPendingReconciliationException("Provider poll response identity conflicts with the confirmed owner.");
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
                    var resourceUnavailableState = ResolveResourceUnavailablePollState(
                        job,
                        version.Id,
                        providerTaskId,
                        providerVideoIdBase,
                        status,
                        await _jobs.GetEventsAsync(job.Id, ct),
                        DateTimeOffset.UtcNow,
                        GetResourceUnavailablePollThreshold(),
                        GetResourceUnavailableGracePeriod());
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
                            resourceUnavailableState.ResourceUnavailableCount,
                            resourceUnavailableState.FirstSeenAt,
                            resourceUnavailableState.ElapsedSeconds,
                            resourceUnavailableState.Percent,
                            resourceUnavailableState.HasProgressEvidence,
                            resourceUnavailableState.Threshold,
                            resourceUnavailableState.GraceSeconds,
                            status.ErrorCode,
                            status.ErrorMessage,
                            providerRawResponse = SanitizeDiagnosticJson(status.SanitizedResponseJson)
                        }, ct);
                    await _jobs.AddEventAsync(job.Id, "RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE",
                        "79AI reported NOT_RESOURCES for the existing provider task.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            sceneVideoVersionId = version.Id,
                            renderJobId = job.Id,
                            provider = input.ProviderCode,
                            model = policy.Model,
                            mode = policy.Mode,
                            providerTaskId,
                            providerVideoIdBase,
                            idBase = providerVideoIdBase,
                            providerStatus = "NOT_RESOURCES",
                            resourceUnavailableState.ResourceUnavailableCount,
                            resourceUnavailableState.Percent,
                            resourceUnavailableState.HasProgressEvidence,
                            resourceUnavailableState.Threshold,
                            resourceUnavailableState.GraceSeconds,
                            firstSeenAt = resourceUnavailableState.FirstSeenAt,
                            resourceUnavailableState.ElapsedSeconds,
                            providerRawResponse = SanitizeDiagnosticJson(status.SanitizedResponseJson)
                        }, "warning", ct);
                    if (resourceUnavailableState.ShouldTerminalize)
                    {
                        await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot,
                            "provider_resources_unavailable", "Resource-unavailable threshold is not a provider-confirmed terminal outcome.", ct,
                            providerTaskId, policy.Model, providerVideoIdBase);
                        throw new RenderJobPendingReconciliationException("Known task returned NOT_RESOURCES; operator reconciliation is required without fallback.");
                    }

                    await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_RESOURCE_UNAVAILABLE_RETRY", "warning",
                        "79AI reported NOT_RESOURCES for an existing provider task; keeping the same task until the no-progress threshold is reached.",
                        new
                        {
                            projectId = input.ProjectId,
                            sceneId = input.SceneId,
                            input.SceneIndex,
                            renderJobId = job.Id,
                            sceneVideoVersionId = version.Id,
                            providerTaskId,
                            providerVideoIdBase,
                            idBase = providerVideoIdBase,
                            provider = input.ProviderCode,
                            model = policy.Model,
                            mode = policy.Mode,
                            resourceUnavailableState.ResourceUnavailableCount,
                            resourceUnavailableState.FirstSeenAt,
                            resourceUnavailableState.ElapsedSeconds,
                            resourceUnavailableState.Percent,
                            resourceUnavailableState.HasProgressEvidence,
                            resourceUnavailableState.Threshold,
                            resourceUnavailableState.GraceSeconds
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
                    var providerTimeout = TimeSpan.FromMinutes(Math.Max(1,
                        _config.GetValue("RVideo:ProviderRenderHardTimeoutMinutes", _options.ProviderRenderHardTimeoutMinutes)));
                    var timeoutDecision = ProviderRenderHardTimeoutEvaluator.Evaluate(
                        version,
                        input,
                        job,
                        providerTimeout,
                        DateTimeOffset.UtcNow);
                    if (timeoutDecision.IsTimedOut)
                    {
                        await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot,
                            "provider_poll_timeout", "Local timeout is not a provider-confirmed terminal outcome.", ct,
                            providerTaskId, policy.Model, providerVideoIdBase);
                        throw new RenderJobPendingReconciliationException("Known provider task exceeded local polling timeout; operator reconciliation required.");
                    }

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
                    if (status.Status != VideoProviderTaskStatus.Failed)
                    {
                        await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot,
                            "provider_status_unrecognized", "Provider response did not establish a terminal failure.", ct,
                            providerTaskId, policy.Model, providerVideoIdBase);
                        throw new RenderJobPendingReconciliationException("Provider response did not establish a terminal outcome; fallback is prohibited.");
                    }
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
                    await FailAsync(project.Id, scene, version.Id, status.ErrorCode ?? "provider_failure", failure, ct);
                    throw new RenderJobTerminalFailureException(failure);
                }

                try
                {
                    // Poll identity is the video id base; the submit task id remains a separate owner identity.
                    if (version.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                        && (string.IsNullOrWhiteSpace(providerTaskId) || string.IsNullOrWhiteSpace(providerVideoIdBase)
                            || status.ProviderTaskId != providerVideoIdBase
                            || version.ProviderTaskId != providerTaskId || version.ProviderVideoIdBase != providerVideoIdBase))
                        throw new VideoReconciliationException("RVIDEO_RECOVERY_OWNER_IDENTITY_REQUIRED",
                            "Terminal success cannot be bound to the confirmed failed owner.");
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
                        BillableDurationSeconds: candidate.ProviderDurationSeconds,
                        TerminalSuccessEvidence: version.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
                            || (version.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) && !version.IsSelected) ? status : null,
                        RecoveryProviderVideoIdBase: providerVideoIdBase), ct);
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
                catch (Exception ex) when (ex is not OperationCanceledException and not RenderJobTerminalFailureException)
                {
                    await HandleReconciliationFailureAsync(
                        job, project, scene, version.Id, input, attemptLogicalRequestId, tariffSnapshot, providerTaskId ?? taskId, providerVideoIdBase ?? taskId,
                        "PROVIDER_SUCCESS_RECONCILIATION_FAILED", ex.Message, ct);
                    return;
                }
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new RenderJobPendingReconciliationException("Provider poll timed out locally; retain the confirmed task for reconciliation.", ex);
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
                    throw new RenderJobPendingReconciliationException(
                        "Provider task identity is missing; manual reconciliation required before retry.", ex);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException
                and not RenderJobPendingReconciliationException and not RenderJobDeferredException
                and not RenderJobTerminalFailureException)
            {
                throw new RenderJobPendingReconciliationException("Provider polling could not establish an outcome; retain the same task for reconciliation.", ex);
            }
        }

        await _submitCoordinator.AssertJobMaySubmitAsync(job.Id, null, ct);
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

    private async Task VerifyConfirmedPollOwnerAsync(Guid jobId, long projectId, long sceneId, Guid versionId,
        string logicalRequestId, string? providerTaskId, string? providerVideoIdBase, CancellationToken ct)
    {
        var current = await _jobs.GetAsync(jobId, ct);
        var versions = await _versions.ListJobSceneVideoVersionsAsync(jobId, ct);
        var intents = await _submitCoordinator.ListJobSubmitIntentsAsync(jobId, ct);
        var owners = versions.Where(v => !string.IsNullOrWhiteSpace(v.ProviderTaskId)
            || !string.IsNullOrWhiteSpace(v.ProviderVideoIdBase)).ToArray();
        var confirmed = intents.Where(i => i.SubmitState == "CONFIRMED_PROVIDER_TASK"
            || !string.IsNullOrWhiteSpace(i.ProviderTaskId) || !string.IsNullOrWhiteSpace(i.ProviderVideoIdBase)).ToArray();
        if (current is null || string.IsNullOrWhiteSpace(providerTaskId) || string.IsNullOrWhiteSpace(providerVideoIdBase)
            || current.ProviderTaskId != providerTaskId || current.ProviderVideoIdBase != providerVideoIdBase
            || owners.Length != 1 || confirmed.Length != 1
            || owners[0].Id != versionId || owners[0].ProjectId != projectId || owners[0].SceneId != sceneId
            || owners[0].LogicalRequestId != logicalRequestId || owners[0].ProviderTaskId != providerTaskId
            || owners[0].ProviderVideoIdBase != providerVideoIdBase
            || confirmed[0].SceneId != sceneId || confirmed[0].LogicalRequestId != logicalRequestId
            || confirmed[0].SubmitState != "CONFIRMED_PROVIDER_TASK" || confirmed[0].ProviderTaskId != providerTaskId
            || confirmed[0].ProviderVideoIdBase != providerVideoIdBase
            || intents.Any(i => i.SubmitState is "SUBMIT_IN_FLIGHT" or "AMBIGUOUS_SUBMIT" or "MANUAL_REVIEW_REQUIRED"))
            throw new RenderJobPendingReconciliationException("Polling requires one matching job/version/confirmed-intent identity; no new submit is allowed.");
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
            var scheduled = await _jobs.ScheduleProviderPollAsync(
                job.Id,
                TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                "SCENE_VIDEO_RECONCILIATION_RETRY",
                errorMessage,
                ct,
                enforceReconciliationLimit: true);
            if (!scheduled)
                throw new RenderJobPendingReconciliationException("Provider output retry could not be scheduled; retain owner identifiers for reconciliation.");
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

                var submitToken = await _submitCoordinator.ClaimSubmitAsync(input.ProjectId, input.SceneId, input.LogicalRequestId, job.Id, ct);
                if (submitToken is null || !await _submitCoordinator.BeginSubmitAsync(submitToken.Value, ct))
                    throw new RenderJobPendingReconciliationException(SceneVideoManualReview.Message);
                var submit = await _tasks.SubmitAsync(payload, ct);
                taskId = string.IsNullOrWhiteSpace(submit.TaskId) ? null : submit.TaskId.Trim();
                if (taskId is not null && !await _submitCoordinator.ConfirmSubmittedAsync(submitToken.Value, taskId, taskId, CancellationToken.None))
                    throw new RenderJobPendingReconciliationException("Provider submit result requires manual reconciliation.");
                if (string.IsNullOrWhiteSpace(taskId))
                {
                    await _submitCoordinator.MarkAmbiguousAsync(submitToken.Value, CancellationToken.None);
                    throw new RenderJobPendingReconciliationException("Provider submit response is missing task_id; manual reconciliation required.");
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

    private static int ResolveNextAttemptIndex(
        string logicalRequestId,
        IReadOnlyList<SceneVideoVersionDto> versions,
        IReadOnlyList<ResolvedFallbackCandidate> candidates)
    {
        var providerOwners = versions.Where(v => IsMatchingLogicalRequestId(v.LogicalRequestId, logicalRequestId)
            && (!string.IsNullOrWhiteSpace(v.ProviderTaskId) || !string.IsNullOrWhiteSpace(v.ProviderVideoIdBase))).ToArray();
        if (providerOwners.Length > 1)
            throw new RenderJobPendingReconciliationException("Multiple task-owning attempts require reconciliation; selecting a fallback is prohibited.");
        if (providerOwners.Length == 1)
        {
            var ownerIndex = ParseAttemptIndex(providerOwners[0].LogicalRequestId, logicalRequestId);
            if (ownerIndex < 0 || ownerIndex >= candidates.Count)
                throw new RenderJobPendingReconciliationException("Task owner cannot be mapped to its persisted attempt.");
            return ownerIndex;
        }
        SceneVideoVersionDto? activeVersion = null;
        var maxAttempt = -1;
        var maxMappedAttempt = -1;
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
            var mappedIndex = ResolvePersistedCandidateIndex(version, candidates);
            if (mappedIndex >= 0)
            {
                maxMappedAttempt = Math.Max(maxMappedAttempt, mappedIndex);
            }

            if (IsActiveSceneVideoStatus(version.Status)
                && (activeVersion is null || attempt > ParseAttemptIndex(activeVersion.LogicalRequestId, logicalRequestId)))
            {
                activeVersion = version;
            }
        }

        if (activeVersion is not null)
        {
            var activeCandidateIndex = ResolvePersistedCandidateIndex(activeVersion, candidates);
            return activeCandidateIndex >= 0
                ? activeCandidateIndex
                : ParseAttemptIndex(activeVersion.LogicalRequestId, logicalRequestId);
        }

        return Math.Max(0, (maxMappedAttempt >= 0 ? maxMappedAttempt : maxAttempt) + 1);
    }

    private sealed record ResolvedFallbackCandidate(
        RVideoVideoModelPolicyEntry Policy,
        int ProviderDurationSeconds,
        string ProviderResolution,
        bool IsDurationFallback = false);

    private sealed record HistoricalFallbackCandidate(
        SceneVideoVersionDto Version,
        ResolvedFallbackCandidate? Candidate);

    private sealed record ResourceUnavailablePollState(
        int ResourceUnavailableCount,
        DateTimeOffset FirstSeenAt,
        int ElapsedSeconds,
        int? Percent,
        bool HasProgressEvidence,
        int Threshold,
        int GraceSeconds,
        bool ShouldTerminalize);

    private sealed record FallbackCandidateDiagnostic(
        int Index,
        string Provider,
        string Model,
        string? Mode,
        int RequestedDuration,
        int? ProviderDuration,
        int? MaxSupportedDuration,
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
        var policies = ResolveOrderedPolicies(input);
        var candidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var policy in policies)
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
                    null,
                    requestedResolution,
                    null,
                    false,
                    model is null ? "catalog_model_missing" : "catalog_model_disabled_or_deprecated"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(policy.Mode)
                && model.SupportedModes.Count > 0
                && !model.SupportedModes.Contains(policy.Mode, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(new FallbackCandidateDiagnostic(
                    policy.AttemptIndex,
                    policy.ProviderCode,
                    policy.Model,
                    policy.Mode,
                    input.DurationSeconds,
                    null,
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
                    supportedDurations.Max(),
                    requestedResolution,
                    null,
                    false,
                    "duration_not_supported"));
                continue;
            }

            var providerResolution = ResolveProviderResolution(input.Resolution, model.SupportedResolutions);
            AddResolvedCandidate(resolved, candidateKeys, policy, duration, providerResolution);
            diagnostics.Add(new FallbackCandidateDiagnostic(
                policy.AttemptIndex,
                policy.ProviderCode,
                policy.Model,
                policy.Mode,
                input.DurationSeconds,
                duration,
                supportedDurations.Max(),
                requestedResolution,
                providerResolution,
                true,
                null));
        }

        return new FallbackCandidateResolution(resolved, diagnostics);
    }

    private static IReadOnlyList<RVideoVideoModelPolicyEntry> ResolveOrderedPolicies(SceneVideoRenderWorkItemInput input)
    {
        var policies = RVideoVideoModelPolicy.Models.ToList();
        if (!input.ManualOverride || string.IsNullOrWhiteSpace(input.RequestedModelCode))
        {
            return policies;
        }

        var requestedModel = input.RequestedModelCode.Trim();
        var requestedMode = input.RequestedMode?.Trim();
        var selected = policies.FirstOrDefault(policy =>
            string.Equals(policy.ProviderCode, input.ProviderCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(policy.Model, requestedModel, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(requestedMode)
                || string.Equals(policy.Mode, requestedMode, StringComparison.OrdinalIgnoreCase)));
        if (selected is null)
        {
            selected = new RVideoVideoModelPolicyEntry(
                -1,
                input.ProviderCode,
                requestedModel,
                string.IsNullOrWhiteSpace(requestedMode) ? null : requestedMode);
        }

        return new[] { selected }
            .Concat(policies.Where(policy => !IsSamePolicy(policy, selected)))
            .ToList();
    }

    private static void AddResolvedCandidate(
        List<ResolvedFallbackCandidate> resolved,
        HashSet<string> candidateKeys,
        RVideoVideoModelPolicyEntry policy,
        int duration,
        string providerResolution,
        bool isDurationFallback = false)
    {
        if (!candidateKeys.Add(BuildCandidateKey(policy, duration, providerResolution)))
        {
            return;
        }

        resolved.Add(new ResolvedFallbackCandidate(policy, duration, providerResolution, isDurationFallback));
    }

    private static string BuildCandidateKey(
        RVideoVideoModelPolicyEntry policy,
        int duration,
        string providerResolution)
        => $"{policy.ProviderCode}|{policy.Model}|{policy.Mode}|{duration}|{providerResolution}";

    private static ResourceUnavailablePollState ResolveResourceUnavailablePollState(
        RenderJobDto job,
        Guid versionId,
        string? providerTaskId,
        string? providerVideoIdBase,
        VideoProviderPollResult currentStatus,
        IReadOnlyList<RenderJobEventDto> events,
        DateTimeOffset now,
        int threshold,
        TimeSpan gracePeriod)
    {
        var normalizedTaskId = providerTaskId?.Trim();
        var normalizedIdBase = providerVideoIdBase?.Trim();
        var matchingEvents = events
            .Where(evt => EventMatchesProviderTask(evt, versionId, normalizedTaskId, normalizedIdBase))
            .ToList();
        var previousRetryEvents = matchingEvents
            .Where(evt => string.Equals(evt.EventType, "RVIDEO_VIDEO_PROVIDER_RESOURCES_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var previousFirstSeen = previousRetryEvents
            .Select(ReadFirstSeenAt)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .OrderBy(value => value)
            .FirstOrDefault();
        var firstSeenAt = previousFirstSeen == default ? now : previousFirstSeen;
        var resourceUnavailableCount = previousRetryEvents.Count + 1;
        var elapsedSeconds = Math.Max(0, (int)Math.Floor((now - firstSeenAt).TotalSeconds));
        var hasProgressEvidence = HasProviderProgressEvidence(currentStatus)
                                  || matchingEvents.Any(HasProviderProgressEvidence);
        var percent = ReadProviderPercent(currentStatus.SanitizedResponseJson);
        var hasIdentifiers = !string.IsNullOrWhiteSpace(normalizedTaskId) && !string.IsNullOrWhiteSpace(normalizedIdBase);
        var isNoProgressNow = currentStatus.Status == VideoProviderTaskStatus.ResourceUnavailable
                              && !HasProviderProgressEvidence(currentStatus)
                              && (percent is null or <= 0);
        var shouldTerminalize = hasIdentifiers
                                && isNoProgressNow
                                && !hasProgressEvidence
                                && resourceUnavailableCount >= Math.Max(1, threshold)
                                && elapsedSeconds >= Math.Max(0, (int)gracePeriod.TotalSeconds);

        return new ResourceUnavailablePollState(
            resourceUnavailableCount,
            firstSeenAt,
            elapsedSeconds,
            percent,
            hasProgressEvidence,
            Math.Max(1, threshold),
            Math.Max(0, (int)gracePeriod.TotalSeconds),
            shouldTerminalize);
    }

    private static bool EventMatchesProviderTask(
        RenderJobEventDto evt,
        Guid versionId,
        string? providerTaskId,
        string? providerVideoIdBase)
    {
        if (string.IsNullOrWhiteSpace(evt.DataJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(evt.DataJson);
            var root = document.RootElement;
            var eventVersionId = ReadJsonString(root, "sceneVideoVersionId");
            var eventTaskId = ReadJsonString(root, "providerTaskId") ?? ReadJsonString(root, "taskId");
            var eventIdBase = ReadJsonString(root, "providerVideoIdBase") ?? ReadJsonString(root, "idBase");
            var versionMatches = Guid.TryParse(eventVersionId, out var parsedVersionId) && parsedVersionId == versionId;
            var taskMatches = !string.IsNullOrWhiteSpace(providerTaskId)
                              && string.Equals(eventTaskId, providerTaskId, StringComparison.OrdinalIgnoreCase);
            var idBaseMatches = !string.IsNullOrWhiteSpace(providerVideoIdBase)
                                && string.Equals(eventIdBase, providerVideoIdBase, StringComparison.OrdinalIgnoreCase);
            return versionMatches || taskMatches || idBaseMatches;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DateTimeOffset? ReadFirstSeenAt(RenderJobEventDto evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.DataJson))
        {
            try
            {
                using var document = JsonDocument.Parse(evt.DataJson);
                var root = document.RootElement;
                var firstSeen = ReadJsonString(root, "firstSeenAt");
                if (DateTimeOffset.TryParse(firstSeen, out var parsedFirstSeen))
                {
                    return parsedFirstSeen;
                }
            }
            catch (JsonException)
            {
                // Ignore malformed event data and fall back to the event timestamp.
            }
        }

        return evt.CreatedAt == default
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(evt.CreatedAt, DateTimeKind.Utc));
    }

    private static bool HasProviderProgressEvidence(VideoProviderPollResult status)
        => !string.IsNullOrWhiteSpace(status.OutputUrl)
           || HasProviderProgressEvidence(status.SanitizedResponseJson)
           || (status.Status == VideoProviderTaskStatus.Processing
               && ReadProviderPercent(status.SanitizedResponseJson) is > 0);

    private static bool HasProviderProgressEvidence(RenderJobEventDto evt)
    {
        if (string.IsNullOrWhiteSpace(evt.DataJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(evt.DataJson);
            var root = document.RootElement;
            var normalizedStatus = ReadJsonString(root, "normalizedStatus") ?? ReadJsonString(root, "providerStatus");
            if (string.Equals(normalizedStatus, nameof(VideoProviderTaskStatus.Success), StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedStatus, "SUCCESSFUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var providerRawResponse = ReadJsonString(root, "providerRawResponse");
            if (HasProviderProgressEvidence(providerRawResponse))
            {
                return true;
            }

            return HasProviderProgressEvidence(root.GetRawText());
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasProviderProgressEvidence(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return HasProviderProgressEvidence(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasProviderProgressEvidence(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name;
                var value = property.Value;
                if (name.Equals("percent", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("progress", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("progressPercent", StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadJsonNumber(value) is > 0)
                    {
                        return true;
                    }
                }

                if (name.Equals("video_id", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("videoId", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("video_id_default", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("videoIdDefault", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("download_url", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("downloadUrl", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("output_url", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("outputUrl", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("work_id", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("workId", StringComparison.OrdinalIgnoreCase))
                {
                    if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        return true;
                    }
                }

                if (name.Equals("status", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("providerStatus", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("normalizedStatus", StringComparison.OrdinalIgnoreCase))
                {
                    var status = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    if (status is not null
                        && (status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)
                            || status.Equals("PROCESSING", StringComparison.OrdinalIgnoreCase)
                            || status.Equals("SUCCESSFUL", StringComparison.OrdinalIgnoreCase)
                            || status.Equals(nameof(VideoProviderTaskStatus.Success), StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }

                if ((value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
                    && HasProviderProgressEvidence(value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasProviderProgressEvidence(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int? ReadProviderPercent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadProviderPercent(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadProviderPercent(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals("percent", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("progress", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("progressPercent", StringComparison.OrdinalIgnoreCase))
                {
                    return ReadJsonNumber(property.Value);
                }

                if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                    && ReadProviderPercent(property.Value) is int nested)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ReadProviderPercent(item) is int nested)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static int? ReadJsonNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
            ? number
            : null;
    }

    private static IReadOnlyList<HistoricalFallbackCandidate> HydrateHistoricalCandidates(
        IReadOnlyList<ResolvedFallbackCandidate> candidates,
        IReadOnlyList<SceneVideoVersionDto> versions,
        SceneVideoRenderWorkItemInput input)
    {
        var result = new List<HistoricalFallbackCandidate>();
        foreach (var version in versions.Where(version => IsMatchingLogicalRequestId(version.LogicalRequestId, input.LogicalRequestId)))
        {
            var persisted = ResolvePersistedCandidate(version);
            if (persisted is null)
            {
                continue;
            }

            var existing = candidates.FirstOrDefault(candidate =>
                string.Equals(
                    BuildCandidateKey(candidate.Policy, candidate.ProviderDurationSeconds, candidate.ProviderResolution),
                    BuildCandidateKey(persisted.Policy, persisted.ProviderDurationSeconds, persisted.ProviderResolution),
                    StringComparison.OrdinalIgnoreCase));
            result.Add(new HistoricalFallbackCandidate(version, existing ?? persisted));
        }

        return result;
    }

    private static int ResolvePersistedCandidateIndex(
        SceneVideoVersionDto version,
        IReadOnlyList<ResolvedFallbackCandidate> candidates)
    {
        var persisted = ResolvePersistedCandidate(version);
        if (persisted is null)
        {
            return -1;
        }

        var key = BuildCandidateKey(persisted.Policy, persisted.ProviderDurationSeconds, persisted.ProviderResolution);
        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (string.Equals(
                    BuildCandidateKey(candidate.Policy, candidate.ProviderDurationSeconds, candidate.ProviderResolution),
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static ResolvedFallbackCandidate? ResolvePersistedCandidate(SceneVideoVersionDto version)
    {
        if (string.IsNullOrWhiteSpace(version.RenderConfigJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(version.RenderConfigJson);
            var root = document.RootElement;
            var provider = ReadJsonString(root, "provider") ?? version.ProviderCode;
            var model = ReadJsonString(root, "actualModel")
                        ?? ReadJsonString(root, "model")
                        ?? ReadJsonString(root, "requestedModel")
                        ?? version.ModelName;
            var mode = ReadJsonString(root, "mode") ?? ReadJsonString(root, "requestedMode");
            var duration = ReadJsonInt(root, "providerDurationSeconds")
                           ?? ReadJsonInt(root, "requestedDurationSeconds")
                           ?? (version.DurationSeconds is > 0 ? (int)version.DurationSeconds.Value : null);
            var resolution = ReadJsonString(root, "providerResolution")
                             ?? ReadJsonString(root, "requestedResolution")
                             ?? "720p";
            if (string.IsNullOrWhiteSpace(provider)
                || string.IsNullOrWhiteSpace(model)
                || duration is not > 0)
            {
                return null;
            }

            return new ResolvedFallbackCandidate(
                new RVideoVideoModelPolicyEntry(
                    ResolvePolicyAttemptIndex(provider, model, mode),
                    provider.Trim(),
                    model.Trim(),
                    string.IsNullOrWhiteSpace(mode) ? null : mode.Trim()),
                duration.Value,
                NormalizeResolution(resolution),
                IsDurationFallback: false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ResolvePolicyAttemptIndex(string provider, string model, string? mode)
        => RVideoVideoModelPolicy.Models.FirstOrDefault(policy =>
               string.Equals(policy.ProviderCode, provider, StringComparison.OrdinalIgnoreCase)
               && string.Equals(policy.Model, model, StringComparison.OrdinalIgnoreCase)
               && string.Equals(policy.Mode, mode, StringComparison.OrdinalIgnoreCase))
           ?.AttemptIndex ?? -1;

    private static int? ResolveSameModelDurationFallback(int currentDuration, HashSet<int> supportedDurations)
        => currentDuration is 4 or 6 && supportedDurations.Contains(8) && currentDuration < 8
            ? 8
            : null;

    private static ResolvedFallbackCandidate? ResolveNextCandidateAfterFailure(
        ResolvedFallbackCandidate current,
        string failureClassification,
        int currentIndex,
        List<ResolvedFallbackCandidate> candidates,
        IReadOnlyList<AiProviderModelListItemDto> catalogModels)
    {
        if (failureClassification == DurationRejectedFailureClassification)
        {
            var durationFallback = ResolveSameModelDurationCandidate(current, candidates, catalogModels);
            if (durationFallback is not null)
            {
                return durationFallback;
            }
        }

        return candidates
            .Skip(currentIndex + 1)
            .FirstOrDefault(candidate => !candidate.IsDurationFallback && !IsSamePolicy(candidate.Policy, current.Policy));
    }

    private static ResolvedFallbackCandidate? ResolveSameModelDurationCandidate(
        ResolvedFallbackCandidate current,
        List<ResolvedFallbackCandidate> candidates,
        IReadOnlyList<AiProviderModelListItemDto> catalogModels)
    {
        var model = catalogModels.FirstOrDefault(x =>
            string.Equals(x.ProviderCode, current.Policy.ProviderCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.ProviderModelCode, current.Policy.Model, StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            return null;
        }

        var supportedDurations = model.SupportedDurations
            .Where(duration => duration > 0)
            .Distinct()
            .OrderBy(duration => duration)
            .ToHashSet();
        var durationFallback = ResolveSameModelDurationFallback(current.ProviderDurationSeconds, supportedDurations);
        if (durationFallback is not int nextDuration)
        {
            return null;
        }

        var existing = candidates.FirstOrDefault(candidate =>
            candidate.IsDurationFallback
            && IsSamePolicy(candidate.Policy, current.Policy)
            && candidate.ProviderDurationSeconds == nextDuration
            && string.Equals(candidate.ProviderResolution, current.ProviderResolution, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var next = new ResolvedFallbackCandidate(current.Policy, nextDuration, current.ProviderResolution, IsDurationFallback: true);
        var insertAt = candidates.IndexOf(current);
        candidates.Insert(insertAt < 0 ? candidates.Count : insertAt + 1, next);
        return next;
    }

    private static bool IsSamePolicy(RVideoVideoModelPolicyEntry left, RVideoVideoModelPolicyEntry right)
        => string.Equals(left.ProviderCode, right.ProviderCode, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Model, right.Model, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Mode, right.Mode, StringComparison.OrdinalIgnoreCase);

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

        if (IsDurationRejectedError(exception))
        {
            return true;
        }

        if (IsDefinitelyNotSubmitted(exception))
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
            : IsDurationRejectedError(exception)
                ? DurationRejectedFailureClassification
                : IsDefinitelyNotSubmitted(exception)
                    ? ProviderRejectedNoTaskFailureClassification
                    : ClassifyProviderFailure(exception.ErrorCode, exception.ErrorMessage, exception.HttpStatusCode);

    private static bool IsDefinitelyNotSubmitted(Ai79TaskSubmitException exception)
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
            return HasZeroCountTasks(document.RootElement)
                   && HasExplicitProviderRejection(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsDurationRejectedError(Ai79TaskSubmitException exception)
    {
        if (IsKnownNoResourcesSubmit(exception)
            || IsTransientSubmit(exception)
            || HasAcceptedTaskId(exception.SanitizedResponseJson))
        {
            return false;
        }

        var text = $"{exception.ErrorCode} {exception.ErrorMessage} {exception.SanitizedResponseJson}".ToLowerInvariant();
        var hasDurationSignal = text.Contains("duration", StringComparison.Ordinal)
                                || text.Contains("seconds", StringComparison.Ordinal)
                                || text.Contains("second", StringComparison.Ordinal)
                                || text.Contains("4s", StringComparison.Ordinal)
                                || text.Contains("6s", StringComparison.Ordinal)
                                || text.Contains("8s", StringComparison.Ordinal);
        if (!hasDurationSignal)
        {
            return false;
        }

        return text.Contains("unsupported", StringComparison.Ordinal)
               || text.Contains("not supported", StringComparison.Ordinal)
               || text.Contains("invalid", StringComparison.Ordinal)
               || text.Contains("unavailable", StringComparison.Ordinal)
               || text.Contains("not available", StringComparison.Ordinal)
               || text.Contains("only 8", StringComparison.Ordinal)
               || text.Contains("8 seconds", StringComparison.Ordinal)
               || text.Contains("8s", StringComparison.Ordinal);
    }

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

    private static bool HasExplicitProviderRejection(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.Name.Equals("error", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("errors", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("error_code", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("errorCode", StringComparison.OrdinalIgnoreCase))
                    && IsExplicitProviderRejectionValue(property.Value))
                {
                    return true;
                }

                if (property.Name.Equals("code", StringComparison.OrdinalIgnoreCase)
                    && IsExplicitProviderRejectionCode(property.Value))
                {
                    return true;
                }

                if ((property.Name.Equals("message", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("msg", StringComparison.OrdinalIgnoreCase))
                    && property.Value.ValueKind == JsonValueKind.String
                    && HasExplicitProviderRejectionText(property.Value.GetString()))
                {
                    return true;
                }

                if ((property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    && HasExplicitProviderRejection(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasExplicitProviderRejection(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsExplicitProviderRejectionValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(value.GetString())
                                    && !string.Equals(value.GetString()?.Trim(), "0", StringComparison.Ordinal),
            JsonValueKind.Number => !value.TryGetInt64(out var number) || number != 0,
            JsonValueKind.True => true,
            JsonValueKind.Array => value.GetArrayLength() > 0,
            JsonValueKind.Object => value.EnumerateObject().Any(),
            _ => false
        };

    private static bool IsExplicitProviderRejectionCode(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericCode))
        {
            return numericCode is not 0 and (< 200 or >= 300);
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return IsExplicitProviderRejectionValue(value);
        }

        var code = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        return int.TryParse(code, out numericCode)
            ? numericCode is not 0 and (< 200 or >= 300)
            : HasExplicitProviderRejectionText(code)
              || code.Contains("error", StringComparison.OrdinalIgnoreCase)
              || code.Contains("fail", StringComparison.OrdinalIgnoreCase)
              || code.Contains("reject", StringComparison.OrdinalIgnoreCase)
              || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
              || code.Contains("not_resources", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitProviderRejectionText(string? value)
    {
        var text = (value ?? string.Empty).ToLowerInvariant();
        return text.Contains("unavailable", StringComparison.Ordinal)
               || text.Contains("not available", StringComparison.Ordinal)
               || text.Contains("rejected", StringComparison.Ordinal)
               || text.Contains("provider error", StringComparison.Ordinal)
               || text.Contains("provider_error", StringComparison.Ordinal)
               || text.Contains("not_resources", StringComparison.Ordinal)
               || text.Contains("no resources", StringComparison.Ordinal);
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
                    && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    && !string.IsNullOrWhiteSpace(property.Value.ToString()))
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
            or DurationRejectedFailureClassification
            or KnownNoResourcesFailureClassification
            or ProviderRejectedNoTaskFailureClassification
            or PollResourceUnavailableNoProgressFailureClassification;

    private int GetResourceUnavailablePollThreshold()
        => Math.Max(1, _config.GetValue("VideoRender:ProviderResourceUnavailablePollThreshold", 3));

    private TimeSpan GetResourceUnavailableGracePeriod()
        => TimeSpan.FromSeconds(Math.Max(0, _config.GetValue("VideoRender:ProviderResourceUnavailableGraceSeconds", 30)));

    private async Task AddDurationFallbackStartedEventAsync(
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        RVideoVideoModelPolicyEntry policy,
        int fromDuration,
        int toDuration,
        string reason,
        string? providerErrorCode,
        string? providerMessage,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(project.Id, "RVIDEO_VIDEO_DURATION_FALLBACK_STARTED", "warning",
            "Scene-video provider rejected the current duration; retrying the same model with a higher supported duration.",
            new
            {
                projectId = project.Id,
                sceneId = scene.Id,
                input.SceneIndex,
                renderJobId = job.Id,
                providerCode = input.ProviderCode,
                modelCode = policy.Model,
                mode = policy.Mode,
                fromDuration,
                toDuration,
                reason,
                providerErrorCode,
                providerMessage
            }, ct);
    }

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
            if (current?.Status is RenderJobStatuses.Completed or RenderJobStatuses.Cancelled)
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

}

internal readonly record struct ProviderRenderHardTimeoutDecision(
    bool IsTimedOut,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed);

internal static class ProviderRenderHardTimeoutEvaluator
{
    internal static ProviderRenderHardTimeoutDecision Evaluate(
        SceneVideoVersionDto version,
        SceneVideoRenderWorkItemInput input,
        RenderJobDto job,
        TimeSpan timeout,
        DateTimeOffset now)
    {
        var startedAt = version.SubmittedAt
                        ?? (input.CreatedAtUtc == default ? (DateTimeOffset?)null : input.CreatedAtUtc)
                        ?? (job.StartedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(job.StartedAt.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null)
                        ?? new DateTimeOffset(DateTime.SpecifyKind(job.CreatedAt, DateTimeKind.Utc));
        var elapsed = now - startedAt;
        return new ProviderRenderHardTimeoutDecision(elapsed >= timeout, startedAt, elapsed);
    }

}

public sealed partial class SceneVideoWorkerHandler
{

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

    /// <summary>
    /// Records a scene-video submit that was refused by the pre-HTTP guard. No provider request
    /// left the process, so this is a definite terminal failure: the version and job are marked
    /// failed (never pending_reconciliation), the scene is synchronised, and the billing
    /// reservation is released instead of being parked in ambiguous reconciliation. V2 selected
    /// completed output is never touched because only this attempt's version row is updated.
    /// </summary>
    private async Task RecordPreHttpSubmitRefusalAsync(
        SceneVideoRenderWorkItemInput input,
        RenderJobDto job,
        VideoProjectSceneDto scene,
        Guid versionId,
        string logicalRequestId,
        string errorCode,
        string errorMessage,
        SceneVideoSubmitBlockReason blockReason,
        bool intentAlreadyHadProviderTask,
        CancellationToken ct)
    {
        await _repo.AddProjectEventAsync(input.ProjectId, "RVIDEO_VIDEO_NOT_SUBMITTED", "warning",
            "Scene-video submit was refused before any provider request; recording a terminal failure.",
            new
            {
                jobId = job.Id,
                input.ProjectId,
                input.SceneId,
                input.SceneIndex,
                sceneVideoVersionId = versionId,
                logicalRequestId,
                errorCode,
                blockReason = blockReason.ToString(),
                blockerCategory = blockReason switch
                {
                    SceneVideoSubmitBlockReason.ExistingIntentUnresolved => "intent",
                    SceneVideoSubmitBlockReason.SceneJobInFlightOrManualReview => "job",
                    SceneVideoSubmitBlockReason.SceneVersionInFlightOrReconciliation => "video version",
                    _ => "none"
                },
                intentAlreadyHadProviderTask,
                providerRequestSent = false
            }, ct);

        if (versionId != Guid.Empty)
        {
            await _versions.FailSceneVideoVersionAsync(versionId, errorCode, errorMessage, ct);
        }

        // Settle the reservation as a definite failure instead of parking it in reconciliation:
        // no provider request was sent, so the reserved points must return to the wallet.
        await _billing.CompleteAsync(new AiImageBillingCompleteRequest
        {
            LogicalRequestId = logicalRequestId,
            Success = false,
            ErrorMessage = errorMessage
        }, ct);

        await FailAsync(input.ProjectId, scene, versionId, errorCode, errorMessage, ct);
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
