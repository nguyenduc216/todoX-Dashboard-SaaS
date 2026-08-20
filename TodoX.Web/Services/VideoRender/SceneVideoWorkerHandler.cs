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
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CreatedBy { get; set; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public Guid? SelectedSourceImageVersionId { get; set; }
    public string? SourceImageUrl { get; set; }
    public string? SourceImageObjectKey { get; set; }
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
}

public sealed class SceneVideoWorkerHandler : IRenderJobHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IAiImageBillingService _billing;
    private readonly IAiProviderService _providers;
    private readonly IYEScaleTaskClient _tasks;
    private readonly IRVideo79AiVideoService _rvideo79Ai;
    private readonly IMediaFileService _media;
    private readonly IVideoPromptValidator _promptValidator;
    private readonly IRenderJobService _jobs;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneVideoWorkerHandler> _logger;
    private readonly VideoRenderOptions _options;

    public string JobType => RenderJobTypes.RenderSceneVideo;

    public SceneVideoWorkerHandler(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        IAiImageBillingService billing,
        IAiProviderService providers,
        IYEScaleTaskClient tasks,
        IRVideo79AiVideoService rvideo79Ai,
        IMediaFileService media,
        IVideoPromptValidator promptValidator,
        IRenderJobService jobs,
        TenantContext tenant,
        IConfiguration config,
        IOptionsMonitor<VideoRenderOptions> options,
        ILogger<SceneVideoWorkerHandler> logger)
    {
        _repo = repo;
        _versions = versions;
        _billing = billing;
        _providers = providers;
        _tasks = tasks;
        _rvideo79Ai = rvideo79Ai;
        _media = media;
        _promptValidator = promptValidator;
        _jobs = jobs;
        _tenant = tenant;
        _config = config;
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

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        var scene = project.Scenes.FirstOrDefault(x => x.Id == input.SceneId)
            ?? throw new InvalidOperationException("Video scene not found.");

        if (RVideoVideoModelPolicy.Is79AiProvider(job.ProviderCode) || string.Equals(input.CapabilityCode, RVideoVideoModelPolicy.CapabilityCode, StringComparison.OrdinalIgnoreCase))
        {
            await HandleRVideoAsync(job, input, project, scene, ct);
            return;
        }

        await HandleYescaleAsync(job, input, project, scene, ct);
    }

    private async Task HandleRVideoAsync(
        RenderJobDto job,
        SceneVideoRenderWorkItemInput input,
        VideoProjectDto project,
        VideoProjectSceneDto scene,
        CancellationToken ct)
    {
        var sourceVersion = await ResolveSourceImageVersionAsync(scene.Id, input.SelectedSourceImageVersionId, ct)
            ?? throw new InvalidOperationException("RVIDEO_SOURCE_IMAGE_UNAVAILABLE");

        var validation = _promptValidator.Validate(
            input.VideoPrompt,
            input.ModelName ?? RVideoVideoModelPolicy.GetInitial().Model,
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
            var policy = RVideoVideoModelPolicy.GetByAttemptIndex(attemptIndex)
                ?? throw new InvalidOperationException("RVIDEO_VIDEO_POLICY_MISSING");
            var attemptLogicalRequestId = BuildAttemptLogicalRequestId(input.LogicalRequestId, attemptIndex);
            var version = await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
                input.ProjectId,
                input.SceneId,
                sourceVersion.Id,
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
                    attemptIndex
                },
                RenderConfigSnapshot: new
                {
                    input,
                    attemptIndex,
                    policy.Model,
                    policy.Mode,
                    provider = RVideoVideoModelPolicy.ProviderCode,
                    capability = RVideoVideoModelPolicy.CapabilityCode
                }), ct);

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
                reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
                {
                    LogicalRequestId = attemptLogicalRequestId,
                    RenderJobId = job.Id.ToString("N"),
                    CustomerId = input.CustomerId,
                    UserId = input.UserId,
                    ProviderId = input.ProviderId,
                    ProviderCapabilityId = input.ProviderCapabilityId,
                    ProviderCode = RVideoVideoModelPolicy.ProviderCode,
                    CapabilityCode = RVideoVideoModelPolicy.CapabilityCode,
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
                    CreatedBy = input.CreatedBy
                }, ct);
            }
            else
            {
                reservation = await _billing.GetReservationAsync(attemptLogicalRequestId, ct)
                    ?? throw new InvalidOperationException($"RVIDEO billing reservation not found for {attemptLogicalRequestId}.");
            }

            if (!reservation.Ok)
            {
                await FailAsync(project.Id, scene, version.Id, reservation.Status, reservation.ErrorMessage ?? "Unable to reserve billing.", ct);
                throw new RenderJobTerminalFailureException(reservation.ErrorMessage ?? "Unable to reserve billing.");
            }

            if (!reservation.ShouldSubmitProvider)
            {
                attemptIndex = Math.Min(attemptIndex, RVideoVideoModelPolicy.Models.Count - 1);
            }

            if (string.IsNullOrWhiteSpace(taskId))
            {
                await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.VideoRendering,
                    errorMessage: null, title: scene.Title, scenePrompt: scene.ScenePrompt, imagePrompt: scene.ImagePrompt, videoPrompt: scene.VideoPrompt, ct: ct);

                try
                {
                    var runtime = await _rvideo79Ai.ResolveRuntimeAsync(ct);
                    var sourceMedia = await ResolveSourceImageMediaAsync(sourceVersion, ct);
                    var sourceAsset = await _rvideo79Ai.UploadSourceImageAsync(runtime, new RVideo79AiVideoSourceImage(
                        sourceVersion.Id,
                        sourceVersion.StorageKey,
                        sourceVersion.PublicUrl,
                        sourceMedia?.FileName,
                        sourceMedia?.MimeType), ct);
                    var submit = await _rvideo79Ai.SubmitAsync(new RVideo79AiVideoSubmitRequest(
                        runtime,
                        policy,
                        input.VideoPrompt ?? string.Empty,
                        input.AspectRatio,
                        input.Resolution,
                        input.DurationSeconds,
                        sourceAsset), ct);
                    taskId = string.IsNullOrWhiteSpace(submit.TaskId) ? null : submit.TaskId.Trim();
                    if (string.IsNullOrWhiteSpace(taskId))
                    {
                        throw new InvalidOperationException("RVIDEO submit response is missing task_id.");
                    }

                    await _versions.MarkSceneVideoVersionSubmittedAsync(version.Id, RVideoVideoModelPolicy.ProviderCode, policy.Model, input.ProviderCapabilityId, taskId, ct);
                    await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_SUBMITTED", "info",
                        $"Scene {input.SceneIndex} submitted to 79AI.",
                        new { jobId = job.Id, input.SceneId, input.SceneIndex, taskId, model = policy.Model, attemptIndex }, ct);
                }
                catch (Ai79TaskSubmitException ex) when (IsTransientSubmit(ex))
                {
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, ex.ErrorCode ?? "submit_transient", ex.Message, CancellationToken.None, null);
                    await DeferPollAsync(job, attemptLogicalRequestId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_SCHEDULED", "79AI submit transient; retry will reuse the same task flow.", CancellationToken.None);
                    return;
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
                var status = await _rvideo79Ai.PollAsync(await _rvideo79Ai.ResolveRuntimeAsync(ct), taskId!, ct);
                if (string.Equals(status.NormalizedStatus, Ai79TaskStatusNormalizer.Running, StringComparison.OrdinalIgnoreCase))
                {
                    await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "provider_pending", "79AI video task remains pending.", ct, taskId);
                    await DeferProviderPollAsync(job, taskId!, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                        "SCENE_VIDEO_POLL_SCHEDULED", "Video task remains pending; the same provider task will be polled later.", ct);
                    return;
                }

                if (!string.Equals(status.NormalizedStatus, Ai79TaskStatusNormalizer.Success, StringComparison.OrdinalIgnoreCase))
                {
                    var failure = status.ErrorMessage ?? $"79AI video task failed with status {status.NormalizedStatus}.";
                    await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                    {
                        LogicalRequestId = attemptLogicalRequestId,
                        Success = false,
                        ActualModel = policy.Model,
                        ProviderTaskId = taskId,
                        ProviderUsageJson = status.SanitizedResponseJson,
                        TariffSnapshotJson = tariffSnapshot,
                        ErrorMessage = failure
                    }, ct);
                    await LogUsageAsync(input, job, attemptLogicalRequestId, reservation.ChargedPoints, status.SanitizedResponseJson, false, failure, taskId, ct);
                    await _versions.FailSceneVideoVersionAsync(version.Id, status.ErrorCode ?? "provider_failure", status.ErrorMessage ?? $"79AI video task failed with status {status.NormalizedStatus}.", ct);
                    var next = RVideoVideoModelPolicy.GetNext(attemptIndex);
                    if (next is not null)
                    {
                        attemptIndex = next.AttemptIndex;
                        continue;
                    }

                    await FailAsync(project.Id, scene, version.Id, "provider_failure", status.ErrorMessage ?? $"79AI video task failed with status {status.NormalizedStatus}.", ct);
                    throw new RenderJobTerminalFailureException(status.ErrorMessage ?? $"79AI video task failed with status {status.NormalizedStatus}.");
                }

                var outputUrl = status.OutputUrl ?? throw new InvalidOperationException($"79AI returned SUCCESS but no output video URL. task_id={taskId}");
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
                    LogicalRequestId = attemptLogicalRequestId,
                    Success = true,
                    ActualModel = policy.Model,
                    ProviderTaskId = taskId,
                    ProviderUsageJson = status.SanitizedResponseJson,
                    TariffSnapshotJson = tariffSnapshot
                }, ct);
                await LogUsageAsync(input, job, attemptLogicalRequestId, reservation.ChargedPoints, status.SanitizedResponseJson, true, null, taskId, ct);

                await _versions.CompleteSceneVideoVersionAsync(version.Id, new SceneVideoVersionCompleteRequest(
                    saved.PublicUrl ?? saved.FileUrl,
                    ResolvePhysicalPath(saved.ObjectKey),
                    PosterUrl: sourceVersion.PublicUrl ?? input.SourceImageUrl,
                    DurationSeconds: input.DurationSeconds,
                    MimeType: "video/mp4",
                    ProviderCode: RVideoVideoModelPolicy.ProviderCode,
                    ModelName: policy.Model,
                    ProviderCapabilityId: input.ProviderCapabilityId,
                    ProviderTaskId: taskId,
                    BillingLogicalRequestId: attemptLogicalRequestId,
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
                return;
            }
            catch (Ai79TaskPollException ex)
            {
                await MarkPendingReconciliationAsync(input, version.Id, attemptLogicalRequestId, tariffSnapshot, "SCENE_VIDEO_POLL_TRANSIENT", ex.Message, CancellationToken.None, taskId);
                await DeferPollAsync(job, taskId!, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                    "SCENE_VIDEO_POLL_TRANSIENT", "Temporary 79AI poll failure; the same task ID will be retried.", CancellationToken.None);
                return;
            }
        }

        await FailAsync(project.Id, scene, Guid.Empty, "provider_failure", "79AI fallback attempts exhausted.", ct);
        throw new RenderJobTerminalFailureException("79AI fallback attempts exhausted.");
    }

    private async Task HandleYescaleAsync(
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
            CreatedBy = input.CreatedBy
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

                var payload = YEScaleVideoModelMapper.BuildSubmitRequest(
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
                    throw new InvalidOperationException("YEScale submit response is missing task_id.");
                }

                await _versions.MarkSceneVideoVersionSubmittedAsync(version.Id, input.ProviderCode, input.ModelName, input.ProviderCapabilityId, taskId, ct);
                await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_PROVIDER_SUBMITTED", "info",
                    $"Scene {input.SceneIndex} submitted to YEScale.",
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
                    throw new YEScaleTaskException($"YEScale returned unsupported status: {terminal.Status}", errorCode: "unknown_status", taskId: taskId);
                }

                await MarkPendingReconciliationAsync(input, version.Id, input.LogicalRequestId, tariffSnapshot, "provider_pending",
                    $"YEScale video task remains {terminal.Status}.", ct, taskId);
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
                ?? throw new InvalidOperationException($"YEScale returned SUCCESS but no output video URL. task_id={taskId}");

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
        catch (YEScaleTaskException ex) when (ex.IsTransient && !string.IsNullOrWhiteSpace(taskId))
        {
            await MarkPendingReconciliationAsync(input, version.Id, input.LogicalRequestId, tariffSnapshot, ex.ErrorCode ?? ex.GetType().Name, ex.Message, CancellationToken.None, taskId);
            await DeferPollAsync(job, taskId, TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)),
                "SCENE_VIDEO_POLL_TRANSIENT", "Temporary YEScale poll failure; the same task ID will be retried.", CancellationToken.None);
        }
    }

    private async Task<SceneImageVersionDto?> ResolveSourceImageVersionAsync(long sceneId, Guid? selectedVersionId, CancellationToken ct)
    {
        if (selectedVersionId is null || selectedVersionId == Guid.Empty)
        {
            return await _versions.GetSelectedImageVersionAsync(sceneId, ct);
        }

        var versions = await _versions.ListImageVersionsAsync(sceneId, 0, 100, ct);
        return versions.FirstOrDefault(x => x.Id == selectedVersionId.Value && x.Status.Equals("completed", StringComparison.OrdinalIgnoreCase));
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
            CreatedBy = input.CreatedBy
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

    private static string? ExtractVideoUrl(YEScaleTaskStatusResponse response, string? sourceImageUrl)
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

    private static string ExtractFailureMessage(YEScaleTaskStatusResponse response)
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

        return $"YEScale video task failed with status {response.Status ?? "unknown"}.";
    }

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
