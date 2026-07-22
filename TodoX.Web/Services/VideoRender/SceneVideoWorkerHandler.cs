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
    public Guid? ProviderAccountId { get; set; }
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
    private readonly IAiProviderAccountRepository _accounts;
    private readonly IAiProviderCredentialResolver _credentials;
    private readonly IYEScaleTaskClient _tasks;
    private readonly IMediaFileService _media;
    private readonly IVideoPromptValidator _promptValidator;
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
        IAiProviderAccountRepository accounts,
        IAiProviderCredentialResolver credentials,
        IYEScaleTaskClient tasks,
        IMediaFileService media,
        IVideoPromptValidator promptValidator,
        TenantContext tenant,
        IConfiguration config,
        IOptionsMonitor<VideoRenderOptions> options,
        ILogger<SceneVideoWorkerHandler> logger)
    {
        _repo = repo;
        _versions = versions;
        _billing = billing;
        _providers = providers;
        _accounts = accounts;
        _credentials = credentials;
        _tasks = tasks;
        _media = media;
        _promptValidator = promptValidator;
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

        var accountClaim = await ClaimProviderAccountAsync(job.Id, input, ct);
        input.ProviderAccountId = accountClaim.ProviderAccountId;
        var credential = await _credentials.ResolveAsync(accountClaim.ProviderAccountId!.Value, ct: ct);

        var reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
        {
            LogicalRequestId = input.LogicalRequestId,
            RenderJobId = job.Id.ToString("N"),
            CustomerId = input.CustomerId,
            UserId = input.UserId,
            ProviderId = input.ProviderId,
            ProviderCapabilityId = input.ProviderCapabilityId,
            ProviderAccountId = input.ProviderAccountId,
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
            await ReleaseProviderAccountAsync(accountClaim, "billing_blocked", CancellationToken.None);
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

                payload.ApiKey = credential.SecretValue;
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
            }
            else
            {
                taskId = await _versions.GetSceneVideoProviderTaskIdAsync(version.Id, ct);
                if (string.IsNullOrWhiteSpace(taskId))
                {
                    await MarkPendingReconciliationAsync(input, version.Id, tariffSnapshot, "missing_task_id", "Missing provider_task_id for scene video reconciliation.", ct);
                    throw new RenderJobPendingReconciliationException("Missing provider_task_id for scene video reconciliation.");
                }
            }

            var terminal = await PollTerminalStatusAsync(taskId!, input.ModelName, credential.SecretValue, ct);
            var responseJson = JsonSerializer.Serialize(terminal, JsonOptions);

            if (!terminal.IsSuccess)
            {
                var failure = ExtractFailureMessage(terminal);
                await _billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = input.LogicalRequestId,
                    Success = false,
                    ActualModel = input.ModelName,
                    ProviderTaskId = taskId,
                    ProviderUsageJson = responseJson,
                    TariffSnapshotJson = tariffSnapshot,
                    ErrorMessage = failure
                }, ct);
                await LogUsageAsync(input, job, reservation.ChargedPoints, responseJson, false, failure, taskId, ct);
                await FailAsync(project.Id, scene, version.Id, "provider_failure", failure, ct);
                await ReleaseProviderAccountAsync(accountClaim, "failed", CancellationToken.None);
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
                ProviderUsageJson = responseJson,
                TariffSnapshotJson = tariffSnapshot
            }, ct);
            await LogUsageAsync(input, job, reservation.ChargedPoints, responseJson, true, null, taskId, ct);

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
                AspectRatio: input.AspectRatio), ct);

            await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_READY", "info",
                $"Scene {input.SceneIndex} rendered successfully.",
                new { jobId = job.Id, input.SceneId, input.SceneIndex, taskId, videoUrl = saved.PublicUrl ?? saved.FileUrl }, ct);
            await ReleaseProviderAccountAsync(accountClaim, "completed", CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await ReleaseProviderAccountAsync(accountClaim, "cancelled", CancellationToken.None);
            throw;
        }
        catch (YEScaleTaskException ex) when (ex.IsTransient && !string.IsNullOrWhiteSpace(taskId))
        {
            await MarkPendingReconciliationAsync(input, version.Id, tariffSnapshot, ex.ErrorCode ?? ex.GetType().Name, ex.Message, CancellationToken.None, taskId);
            await ReleaseProviderAccountAsync(accountClaim, "pending_reconciliation", CancellationToken.None);
            throw new RenderJobPendingReconciliationException(ex.Message);
        }
        catch
        {
            await ReleaseProviderAccountAsync(accountClaim, "failed", CancellationToken.None);
            throw;
        }
    }

    private async Task<YEScaleTaskStatusResponse> PollTerminalStatusAsync(string taskId, string? modelName, string apiKey, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.MaxPollDurationMinutes));
        var consecutiveErrors = 0;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var status = await _tasks.GetStatusAsync(taskId, apiKey, ct);
                consecutiveErrors = 0;
                var normalized = status.Status?.Trim().ToUpperInvariant();
                if (normalized is "SUCCESS" or "FAILURE" or "CANCELLED" or "EXPIRED")
                {
                    return status;
                }

                if (normalized is not "QUEUED" and not "PENDING" and not "SUBMITTED" and not "PROCESSING" and not "RUNNING")
                {
                    throw new YEScaleTaskException($"YEScale returned unsupported status: {status.Status}", errorCode: "unknown_status", taskId: taskId);
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)), ct);
            }
            catch (YEScaleTaskException ex) when (ex.StatusCode is 429 or >= 500)
            {
                consecutiveErrors++;
                if (consecutiveErrors >= Math.Max(1, _options.MaxConsecutivePollErrors))
                {
                    throw;
                }

                _logger.LogWarning(ex, "YEScale transient poll error model={ModelName} taskId={TaskId} consecutiveErrors={Errors}", modelName, taskId, consecutiveErrors);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds)), ct);
            }
        }

        throw new YEScaleTaskException("YEScale poll timed out.", transient: true, taskId: taskId);
    }

    private async Task<AiProviderAccountSelectionResult> ClaimProviderAccountAsync(Guid renderJobId, SceneVideoRenderWorkItemInput input, CancellationToken ct)
    {
        var claim = await _accounts.ClaimAccountAsync(new AiProviderAccountSelectionRequest(
            renderJobId,
            input.ProviderCode,
            input.CapabilityCode,
            "scene_video",
            input.ModelName,
            "scene-video-worker",
            TimeSpan.FromMinutes(30)), ct);

        if (!claim.Claimed || claim.ProviderAccountId is null)
        {
            throw new InvalidOperationException(claim.Reason ?? "AI_PROVIDER_ACCOUNT_CLAIM_FAILED");
        }

        return claim;
    }

    private async Task ReleaseProviderAccountAsync(AiProviderAccountSelectionResult claim, string reason, CancellationToken ct)
    {
        if (claim.LeaseId is Guid leaseId)
        {
            await _accounts.ReleaseLeaseAsync(leaseId, "scene-video-worker", reason, ct);
        }
    }

    private async Task MarkPendingReconciliationAsync(
        SceneVideoRenderWorkItemInput input,
        Guid versionId,
        string? tariffSnapshot,
        string? errorCode,
        string errorMessage,
        CancellationToken ct,
        string? providerTaskId = null)
    {
        await _billing.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
        {
            LogicalRequestId = input.LogicalRequestId,
            ActualModel = input.ModelName,
            ProviderTaskId = providerTaskId,
            TariffSnapshotJson = tariffSnapshot,
            ErrorMessage = errorMessage
        }, ct);
        await _versions.MarkSceneVideoPendingReconciliationAsync(versionId, errorCode, errorMessage, ct);
    }

    private async Task LogUsageAsync(
        SceneVideoRenderWorkItemInput input,
        RenderJobDto job,
        decimal chargedPoints,
        string? providerUsageJson,
        bool success,
        string? errorMessage,
        string? providerTaskId,
        CancellationToken ct)
    {
        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerGuid = input.CustomerId,
            ProviderId = input.ProviderId,
            ProviderCapabilityId = input.ProviderCapabilityId,
            ProviderAccountId = input.ProviderAccountId,
            ProviderCode = input.ProviderCode,
            CapabilityCode = input.CapabilityCode,
            FeatureCode = "render_job_scene_video",
            ModelName = input.ModelName,
            RequestId = input.LogicalRequestId,
            JobId = job.Id.ToString("N"),
            Quantity = 1,
            UnitType = "request",
            UnitCostPoints = input.EstimatedPoints,
            TotalPoints = chargedPoints,
            ProviderRawCost = input.EstimatedUsd,
            Status = success ? "success" : "failed",
            ErrorMessage = errorMessage,
            MetadataJson = BuildUsageMetadata(input, providerTaskId, providerUsageJson, chargedPoints),
            CreatedBy = input.CreatedBy
        }, ct);
    }

    private static string BuildUsageMetadata(
        SceneVideoRenderWorkItemInput input,
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
        await _versions.FailSceneVideoVersionAsync(versionId, errorCode, errorMessage, ct);
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
