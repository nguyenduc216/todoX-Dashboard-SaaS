using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.DanceSell;

public sealed class DanceSellRenderHandler : IRenderJobHandler
{
    private const long MaxMotionControlVideoBytes = 50L * 1024 * 1024;
    private static readonly HashSet<string> AllowedMotionImageMime = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp" };
    private static readonly HashSet<string> AllowedMotionVideoMime = new(StringComparer.OrdinalIgnoreCase)
        { "video/mp4", "video/webm" };

    private readonly IDanceSellRepository _repo;
    private readonly IKiePayloadBuilder _payloadBuilder;
    private readonly IKieClient _client;
    private readonly IKieRateLimiter _rateLimiter;
    private readonly IRenderJobService _renderJobs;
    private readonly IDanceSellCompletionService _completion;
    private readonly IAiProviderService _providers;
    private readonly IDanceSellOperationRepository _operations;
    private readonly IDanceSellProviderCatalog _routes;
    private readonly AiProviderRepository _providerRepository;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderCredentialRepository _credentialRepository;
    private readonly IAi79TaskClient _ai79;
    private readonly IMediaFileService _media;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<KieOptions> _options;
    private readonly ILogger<DanceSellRenderHandler> _logger;

    public string JobType => RenderJobTypes.DanceSell;

    public DanceSellRenderHandler(
        IDanceSellRepository repo,
        IKiePayloadBuilder payloadBuilder,
        IKieClient client,
        IKieRateLimiter rateLimiter,
        IRenderJobService renderJobs,
        IDanceSellCompletionService completion,
        IAiProviderService providers,
        IDanceSellOperationRepository operations,
        IDanceSellProviderCatalog routes,
        AiProviderRepository providerRepository,
        IProviderCredentialResolver credentials,
        IProviderCredentialRepository credentialRepository,
        IAi79TaskClient ai79,
        IMediaFileService media,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<KieOptions> options,
        ILogger<DanceSellRenderHandler> logger)
    {
        _repo = repo;
        _payloadBuilder = payloadBuilder;
        _client = client;
        _rateLimiter = rateLimiter;
        _renderJobs = renderJobs;
        _completion = completion;
        _providers = providers;
        _operations = operations;
        _routes = routes;
        _providerRepository = providerRepository;
        _credentials = credentials;
        _credentialRepository = credentialRepository;
        _ai79 = ai79;
        _media = media;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<DanceSellRenderInput>(job.InputJson, KieJson.Options)
            ?? throw new RenderJobTerminalFailureException("Dance Sell render input invalid.");
        var danceJob = await _repo.GetByIdAsync(input.DanceSellJobId, ct)
            ?? throw new RenderJobTerminalFailureException("Dance Sell job not found.");

        if (danceJob.Status is DanceSellJobStatuses.Completed)
        {
            return;
        }

        if (danceJob.Status is DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout)
        {
            throw new RenderJobTerminalFailureException(danceJob.ErrorMessage ?? "Dance Sell job already failed.");
        }

        if (string.IsNullOrWhiteSpace(danceJob.ProviderTaskId))
        {
            if (Is79Ai(danceJob))
            {
                await Submit79AiAsync(job, danceJob, input.OperationId, ct);
                return;
            }

            await SubmitAsync(job, danceJob, input.OperationId, ct);
            return;
        }

        if (Is79Ai(danceJob))
        {
            await Poll79AiAsync(job, danceJob, input.OperationId, ct);
            return;
        }

        await PollAsync(job, danceJob, input.OperationId, ct);
    }

    private async Task Submit79AiAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, Guid? operationId, CancellationToken ct)
    {
        var runtime = await Resolve79AiRuntimeAsync(danceJob, ct);
        if (danceJob.PreparedReferenceStatus != DanceSellReferenceStatuses.Approved)
        {
            await FailAsync(renderJob, danceJob, "DANCE_SELL_REFERENCE_NOT_APPROVED", "Approved reference image is required before creating the motion video.", "{}", permanent: true, ct);
            throw new RenderJobTerminalFailureException("DANCE_SELL_REFERENCE_NOT_APPROVED");
        }

        ResolvedMotionFile? referenceImage = null;
        ResolvedMotionFile? motionVideo;
        string referenceUrlUsed;
        string referenceSource;
        Ai79MediaUploadResult? referenceUpload = null;
        try
        {
            if (TryUseApprovedReferenceUrl(danceJob.PreparedReferenceStatus, danceJob.PreparedReferenceUrl, out var approvedReferenceUrl))
            {
                referenceUrlUsed = approvedReferenceUrl;
                referenceSource = "approved_url";
                await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_REFERENCE_USING_APPROVED_URL", "79AI motion reference uses approved public URL directly.",
                    new { danceSellJobId = danceJob.Id, referenceUrl = referenceUrlUsed, source = referenceSource }, ct: ct);
                await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_REFERENCE_UPLOAD_SKIPPED", "79AI motion reference image upload skipped because an approved public URL is available.",
                    new { danceSellJobId = danceJob.Id, referenceUrlUsed, reason = "approved_reference_url_present" }, ct: ct);
            }
            else
            {
                referenceSource = "uploaded_binary";
                referenceUrlUsed = string.Empty;
                referenceImage = await ResolveMotionFileAsync(
                    danceJob.PreparedReferenceMediaId,
                    danceJob.PreparedReferenceObjectKey,
                    danceJob.PreparedReferenceUrl,
                    runtime.ReferenceImageField,
                    "reference.jpg",
                    AllowedMotionImageMime,
                    maxBytes: null,
                    "DANCE_SELL_REFERENCE_FILE_REQUIRED",
                    "DANCE_SELL_REFERENCE_UNSUPPORTED_MIME",
                    "DANCE_SELL_REFERENCE_FILE_REQUIRED",
                    ct);
                if (referenceImage is null)
                {
                    await FailAsync(renderJob, danceJob, "DANCE_SELL_REFERENCE_FILE_REQUIRED", "Approved reference image file is required before creating the motion video.", "{}", permanent: true, ct);
                    throw new RenderJobTerminalFailureException("DANCE_SELL_REFERENCE_FILE_REQUIRED");
                }
            }
            motionVideo = await ResolveMotionFileAsync(
                danceJob.MotionVideoMediaId,
                danceJob.MotionVideoObjectKey,
                danceJob.MotionVideoUrl,
                runtime.MotionVideoField,
                "motion.mp4",
                AllowedMotionVideoMime,
                MaxMotionControlVideoBytes,
                "DANCE_SELL_MOTION_FILE_REQUIRED",
                "DANCE_SELL_MOTION_UNSUPPORTED_MIME",
                "DANCE_SELL_MOTION_FILE_TOO_LARGE",
                ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("DANCE_SELL_", StringComparison.Ordinal))
        {
            await FailAsync(renderJob, danceJob, ex.Message, ex.Message, "{}", permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }

        if (motionVideo is null)
        {
            await FailAsync(renderJob, danceJob, "DANCE_SELL_MOTION_FILE_REQUIRED", "Motion video file is required before creating the motion video.", "{}", permanent: true, ct);
            throw new RenderJobTerminalFailureException("DANCE_SELL_MOTION_FILE_REQUIRED");
        }

        var submittedAt = DateTime.UtcNow;
        var motionOperationId = await EnsureMotionOperationIdAsync(renderJob, danceJob, operationId, ct);
        var motionSource = !string.IsNullOrWhiteSpace(danceJob.MotionVideoUrl) ? "motion_video_url" : "uploaded_binary";
        string motionProviderUrl = string.Empty;
        string? motionProviderIdBase = null;
        string? motionProviderProjectId = runtime.ProjectId;
        string? motionProviderFileName = motionVideo.FileName;
        string motionUploadState;
        AiOperationAssetDto? reusableMotionUpload = null;
        try
        {
            if (referenceSource == "uploaded_binary")
            {
                referenceUpload = await _ai79.UploadMediaAsync(new Ai79MediaUploadRequest(
                    runtime.BaseUrl,
                    runtime.UploadImagePath,
                    runtime.Credential.Secret,
                    runtime.Domain,
                    runtime.ProjectId,
                    runtime.UploadImageField,
                    referenceImage!.ToMultipartPart(runtime.UploadImageField)),
                    ct);
                referenceUrlUsed = referenceUpload.Url;
                await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_REFERENCE_UPLOADED", "79AI motion reference image uploaded.",
                    new { danceSellJobId = danceJob.Id, providerUrl = referenceUpload.Url, runtime.UploadImagePath, referenceSource }, ct: ct);
            }

            reusableMotionUpload = await _operations.GetLatestAssetAsync(
                danceJob.Id,
                DanceSellOperationTypes.MotionVideo,
                DanceSellAssetRoles.MotionProviderUpload,
                danceJob.MotionVideoMediaId,
                danceJob.MotionVideoObjectKey,
                ct);
            if (!string.IsNullOrWhiteSpace(reusableMotionUpload?.ProviderUrl))
            {
                motionProviderUrl = reusableMotionUpload.ProviderUrl!;
                motionProviderIdBase = ReadConfigString(reusableMotionUpload.MetadataJson, "idBase");
                motionProviderProjectId = ReadConfigString(reusableMotionUpload.MetadataJson, "projectId") ?? runtime.ProjectId;
                motionProviderFileName = ReadConfigString(reusableMotionUpload.MetadataJson, "fileName") ?? motionVideo.FileName;
                motionUploadState = "reused";
                await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_SOURCE_UPLOAD_REUSED", "79AI source motion video upload reused.",
                    new
                    {
                        danceSellJobId = danceJob.Id,
                        mediaId = danceJob.MotionVideoMediaId,
                        objectKey = danceJob.MotionVideoObjectKey,
                        providerUrl = motionProviderUrl,
                        previousUploadAt = reusableMotionUpload.CreatedAt
                    }, ct: ct);
            }
            else
            {
                var motionUpload = await _ai79.UploadMediaAsync(new Ai79MediaUploadRequest(
                    runtime.BaseUrl,
                    runtime.UploadVideoPath,
                    runtime.Credential.Secret,
                    runtime.Domain,
                    runtime.ProjectId,
                    runtime.UploadVideoField,
                    motionVideo.ToMultipartPart(runtime.UploadVideoField)),
                    ct);
                motionProviderUrl = motionUpload.Url;
                motionProviderIdBase = motionUpload.IdBase;
                motionProviderProjectId = motionUpload.ProjectId;
                motionProviderFileName = motionUpload.FileName;
                motionUploadState = "uploaded";
                await _operations.UpsertAssetAsync(new AiOperationAssetDto
                {
                    OperationId = motionOperationId,
                    AssetRole = DanceSellAssetRoles.MotionProviderUpload,
                    MediaId = danceJob.MotionVideoMediaId,
                    ObjectKey = danceJob.MotionVideoObjectKey,
                    PublicUrl = danceJob.MotionVideoUrl,
                    ProviderUrl = motionProviderUrl,
                    MimeType = motionVideo.MimeType,
                    MetadataJson = DanceSellRepository.ToJson(new
                    {
                        danceSellJobId = danceJob.Id,
                        mediaId = danceJob.MotionVideoMediaId,
                        objectKey = danceJob.MotionVideoObjectKey,
                        providerUrl = motionProviderUrl,
                        idBase = motionProviderIdBase,
                        projectId = motionProviderProjectId,
                        fileName = motionProviderFileName,
                        uploadedAt = DateTime.UtcNow,
                        source = motionSource,
                        mime = motionVideo.MimeType,
                        bytes = motionVideo.SizeBytes >= 0 ? motionVideo.SizeBytes : (long?)null,
                        endpointPath = runtime.UploadVideoPath,
                        field = runtime.UploadVideoField
                    })
                }, ct);
                await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_SOURCE_UPLOADED", "79AI source motion video uploaded.",
                    new { danceSellJobId = danceJob.Id, providerUrl = motionProviderUrl, runtime.UploadVideoPath }, ct: ct);
            }

            var motionPrompt = ReadConfigString(runtime.RouteConfigJson, "motion_prompt") ?? string.Empty;
            var request = new Ai79MotionControlSubmitRequest(
                runtime.BaseUrl,
                runtime.MotionSubmitPath,
                runtime.Credential.Secret,
                runtime.Domain,
                runtime.ProjectId,
                runtime.Model,
                motionPrompt,
                referenceUrlUsed,
                motionProviderUrl,
                runtime.ProviderMode,
                runtime.ProviderRatio,
                runtime.SubType,
                runtime.BackgroundSource,
                runtime.IncludeImagesZeroUrl);
            var requestJson = JsonSerializer.Serialize(new
            {
                providerCode = runtime.ProviderCode,
                providerModel = runtime.Model,
                submitEndpointPath = runtime.MotionSubmitPath,
                contentType = "application/x-www-form-urlencoded",
                referenceSource,
                referenceUrlUsed,
                motionSource,
                reference = new
                {
                    source = referenceSource,
                    url = referenceUrlUsed
                },
                motionUpload = new
                {
                    status = "completed",
                    uploadState = motionUploadState,
                    endpointPath = runtime.UploadVideoPath,
                    field = runtime.UploadVideoField,
                    source = motionSource,
                    resolvedSource = motionVideo.Source,
                    mime = motionVideo.MimeType,
                    bytes = motionVideo.SizeBytes >= 0 ? motionVideo.SizeBytes : (long?)null,
                    duration = (decimal?)null,
                    providerUrl = motionProviderUrl,
                    idBase = motionProviderIdBase,
                    projectId = motionProviderProjectId,
                    fileName = motionProviderFileName,
                    reusedAssetId = reusableMotionUpload?.Id
                },
                submit = new
                {
                    providerModel = runtime.Model,
                    submitEndpointPath = runtime.MotionSubmitPath,
                    projectId = runtime.ProjectId,
                    mode = runtime.ProviderMode,
                    ratio = runtime.ProviderRatio,
                    imageUrl = referenceUrlUsed,
                    images0Url = runtime.IncludeImagesZeroUrl ? referenceUrlUsed : null,
                    videoUrl = motionProviderUrl,
                    subType = runtime.SubType,
                    backgroundSource = runtime.BackgroundSource,
                    prompt = motionPrompt
                },
                auditPrompt = danceJob.Prompt,
                submittedAt
            }, KieJson.Options);

            var submitStartedAt = Stopwatch.StartNew();
            await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_SUBMIT_STARTED", "79AI Kling Motion Control submit started.",
                new
                {
                    danceSellJobId = danceJob.Id,
                    endpointPath = runtime.MotionSubmitPath,
                    model = runtime.Model,
                    mode = runtime.ProviderMode,
                    ratio = runtime.ProviderRatio,
                    imageUrl = referenceUrlUsed,
                    videoUrl = motionProviderUrl,
                    projectId = motionProviderProjectId
                }, ct: ct);
            var submitted = await _ai79.SubmitMotionControlAsync(request, ct);
            submitStartedAt.Stop();
            await _repo.UpdateSubmittedAsync(danceJob.Id, requestJson, submitted.TaskId, submitted.SanitizedResponseJson, ct);
            await _operations.MarkSubmittedAsync(motionOperationId, submitted.TaskId, submitted.SanitizedResponseJson, ct);

            await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_TASK_SUBMITTED", "79AI Kling Motion Control task submitted.",
                new { danceSellJobId = danceJob.Id, taskId = submitted.TaskId, runtime.Model, runtime.ProviderMode, runtime.ProviderRatio, runtime.MotionSubmitPath, durationMs = submitStartedAt.ElapsedMilliseconds }, ct: ct);
            await LogUsageAsync(danceJob, renderJob, "submitted", submitted.TaskId, "submitted", null, null, ct);
            await ScheduleNextPollAsync(renderJob, "79AI motion task submitted; polling scheduled.", ct);
        }
        catch (Ai79TaskSubmitException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? "ai79_submit_failed", ex.ErrorMessage, ex.SanitizedResponseJson, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            await Record79AiSubmitTimeoutAsync(renderJob, danceJob, runtime.MotionSubmitPath, motionProviderUrl, submittedAt, ex, ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, TimeSpan.FromSeconds(30), "AI79_MOTION_SUBMIT_TIMEOUT", "79AI motion submit timed out after source upload; retry will reuse the persisted provider video URL.", ct);
            throw new RenderJobDeferredException("79AI motion submit timeout scheduled for retry.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            await Record79AiSubmitTimeoutAsync(renderJob, danceJob, runtime.MotionSubmitPath, motionProviderUrl, submittedAt, ex, ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, TimeSpan.FromSeconds(30), "AI79_MOTION_SUBMIT_TIMEOUT", "79AI motion submit was cancelled by HTTP timeout after source upload; retry will reuse the persisted provider video URL.", ct);
            throw new RenderJobDeferredException("79AI motion submit cancellation scheduled for retry.");
        }
        catch (HttpRequestException ex)
        {
            await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_SUBMIT_HTTP_ERROR", "79AI Kling Motion Control submit HTTP error.",
                new
                {
                    danceSellJobId = danceJob.Id,
                    endpoint = runtime.MotionSubmitPath,
                    elapsedMs = (long)(DateTime.UtcNow - submittedAt).TotalMilliseconds,
                    providerUploadUrl = motionProviderUrl,
                    errorType = ex.GetType().Name,
                    errorMessage = ex.Message
                }, level: "warning", ct: ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, TimeSpan.FromSeconds(30), "AI79_MOTION_SUBMIT_HTTP_ERROR", "79AI motion submit HTTP error after source upload; retry will reuse the persisted provider video URL.", ct);
            throw new RenderJobDeferredException("79AI motion submit HTTP error scheduled for retry.");
        }
    }

    private async Task<Guid> EnsureMotionOperationIdAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, Guid? operationId, CancellationToken ct)
    {
        if (operationId is Guid existingOperationId)
        {
            return existingOperationId;
        }

        var attemptNo = await _operations.GetNextAttemptNoAsync(danceJob.Id, DanceSellOperationTypes.MotionVideo, ct);
        var operation = await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
        {
            Id = Guid.NewGuid(),
            DanceSellJobId = danceJob.Id,
            RenderJobId = renderJob.Id,
            OperationType = DanceSellOperationTypes.MotionVideo,
            AttemptNo = attemptNo,
            ReferenceMode = danceJob.ReferenceMode,
            ProviderCode = danceJob.MotionProviderCode ?? danceJob.ProviderCode,
            ProviderCapabilityId = danceJob.MotionProviderCapabilityId,
            ProviderAccountId = danceJob.MotionProviderAccountId,
            ProviderModel = danceJob.MotionProviderModel ?? danceJob.ProviderModel,
            Status = DanceSellOperationStatuses.Queued,
            BillingStatus = danceJob.BillingStatus,
            RefundStatus = danceJob.RefundStatus,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        }, ct);

        return operation?.Id ?? throw new InvalidOperationException("DANCE_SELL_MOTION_OPERATION_REQUIRED");
    }

    private async Task Record79AiSubmitTimeoutAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, string endpointPath, string providerUploadUrl, DateTime startedAt, Exception ex, CancellationToken ct)
    {
        await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_SUBMIT_TIMEOUT", "79AI Kling Motion Control submit timed out after source upload.",
            new
            {
                danceSellJobId = danceJob.Id,
                endpoint = endpointPath,
                elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                providerUploadUrl,
                errorType = ex.GetType().Name,
                errorMessage = ex.Message
            }, level: "warning", ct: ct);
    }

    private async Task Poll79AiAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, Guid? operationId, CancellationToken ct)
    {
        if (danceJob.PollCount >= Math.Max(1, _options.CurrentValue.MaxPollCount))
        {
            if (string.Equals(danceJob.ProviderStatus, Ai79TaskStatusNormalizer.Success, StringComparison.OrdinalIgnoreCase))
            {
                await FailAsync(renderJob, danceJob, "DANCE_SELL_OUTPUT_URL_TIMEOUT", "79AI motion task succeeded but no output URL became available before the poll grace window expired.", danceJob.PollResponseJson, permanent: true, ct, DanceSellJobStatuses.Timeout);
                throw new RenderJobTerminalFailureException("79AI motion output URL timeout.");
            }

            await FailAsync(renderJob, danceJob, "ai79_poll_timeout", "79AI motion poll max count reached.", danceJob.PollResponseJson, permanent: true, ct, DanceSellJobStatuses.Timeout);
            throw new RenderJobTerminalFailureException("79AI motion poll max count reached.");
        }

        var runtime = await Resolve79AiRuntimeAsync(danceJob, ct);
        try
        {
            var status = await _ai79.GetStatusAsync(new Ai79TaskStatusRequest(
                runtime.BaseUrl,
                runtime.PollPath,
                runtime.Credential.Secret,
                runtime.Domain,
                danceJob.ProviderTaskId!,
                Ai79TaskOperation.Video,
                runtime.PollIdField,
                UseBearerAuth: true,
                ProjectId: runtime.ProjectId), ct);
            if (status.NormalizedStatus == Ai79TaskStatusNormalizer.Success)
            {
                if (string.IsNullOrWhiteSpace(status.OutputUrl))
                {
                    var outputPendingNextPoll = DateTime.UtcNow.Add(_options.CurrentValue.PollInterval);
                    await _repo.UpdatePollingAsync(danceJob.Id, status.NormalizedStatus, status.SanitizedResponseJson, danceJob.PollCount + 1, outputPendingNextPoll, ct);
                    await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_OUTPUT_PENDING", "79AI Kling Motion Control task succeeded; output URL is not available yet.",
                        new { danceSellJobId = danceJob.Id, danceJob.ProviderTaskId, status = status.NormalizedStatus, pollCount = danceJob.PollCount + 1 }, ct: ct);
                    await ScheduleNextPollAsync(renderJob, "79AI motion output URL pending; next poll scheduled.", ct);
                    return;
                }

                await _completion.CompleteAsync(new DanceSellCompletionRequest
                {
                    DanceJob = danceJob,
                    ProviderTaskId = danceJob.ProviderTaskId,
                    ProviderStatus = status.NormalizedStatus,
                    ResponseJson = status.SanitizedResponseJson,
                    ResultVideoUrl = status.OutputUrl,
                    ResultUrlCount = 1,
                    Source = "poll"
                }, ct);
                if (operationId is Guid existingOperationId)
                {
                    await _operations.MarkCompletedAsync(existingOperationId, status.NormalizedStatus, status.SanitizedResponseJson, null, status.OutputUrl, ct);
                    await _operations.UpsertAssetAsync(new AiOperationAssetDto
                    {
                        OperationId = existingOperationId,
                        AssetRole = DanceSellAssetRoles.VideoOutput,
                        PublicUrl = status.OutputUrl,
                        ProviderUrl = status.OutputUrl,
                        MimeType = "video/mp4",
                        MetadataJson = DanceSellRepository.ToJson(new { source = "poll" })
                    }, ct);
                }

                return;
            }

            if (status.NormalizedStatus == Ai79TaskStatusNormalizer.Failed)
            {
                var error = status.ErrorMessage ?? "79AI motion task failed.";
                await FailAsync(renderJob, danceJob, status.ErrorCode ?? "ai79_task_failed", error, status.SanitizedResponseJson, permanent: true, ct);
                throw new RenderJobTerminalFailureException(error);
            }

            var nextPoll = DateTime.UtcNow.Add(_options.CurrentValue.PollInterval);
            await _repo.UpdatePollingAsync(danceJob.Id, status.NormalizedStatus, status.SanitizedResponseJson, danceJob.PollCount + 1, nextPoll, ct);
            await _renderJobs.AddEventAsync(renderJob.Id, "AI79_MOTION_TASK_POLLING", "79AI Kling Motion Control task is still running.",
                new { danceSellJobId = danceJob.Id, danceJob.ProviderTaskId, status = status.NormalizedStatus, pollCount = danceJob.PollCount + 1 }, ct: ct);
            await ScheduleNextPollAsync(renderJob, "79AI motion task not terminal; next poll scheduled.", ct);
        }
        catch (RenderJobDeferredException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await FailAsync(renderJob, danceJob, ex.GetType().Name, ex.Message, "{}", permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }
    }

    private async Task<Ai79MotionRuntime> Resolve79AiRuntimeAsync(DanceSellJobDto job, CancellationToken ct)
    {
        var route = await _routes.ResolveAsync(DanceSellOperationTypes.MotionVideo, job.MotionProviderCode, job.MotionProviderModel, ct);
        var provider = await _providerRepository.GetProviderByCodeAsync(route.ProviderCode, ct)
            ?? throw new InvalidOperationException("DANCE_SELL_79AI_PROVIDER_NOT_CONFIGURED");
        var credential = await _credentials.ResolveAsync(route.ProviderCode, "access_token", ct);
        var account = await _credentialRepository.GetAccountByIdAsync(credential.ProviderAccountId, ct);
        var providerMode = DanceSellMotionProviderContract.ResolveProviderMode(route, job.Mode);
        var providerRatio = DanceSellMotionProviderContract.ResolveProviderRatio(route);
        return new Ai79MotionRuntime(
            route.ProviderCode,
            route.ModelName,
            FirstNonBlank(ReadConfigString(route.ConfigJson, "base_url"), ReadConfigString(account?.ConfigJson, "base_url"), provider.BaseUrl, ReadConfigString(provider.ConfigJson, "base_url"), "https://api.gommo.net/ai")!,
            FirstNonBlank(ReadConfigString(account?.ConfigJson, "domain"), ReadConfigString(provider.ConfigJson, "domain"), "79ai.net")!,
            route.ConfigJson,
            ReadConfigString(route.ConfigJson, "upload_image_path") ?? "/ai/upload/image",
            ReadConfigString(route.ConfigJson, "upload_video_path") ?? "/ai/upload/video",
            ReadConfigString(route.ConfigJson, "motion_submit_path") ?? $"/ai/jobs/video/{route.ModelName}",
            ReadConfigString(route.ConfigJson, "poll_path") ?? "/ai/jobs/{task_id}?media=video",
            ReadConfigString(route.ConfigJson, "poll_id_field") ?? "id_base",
            DanceSellMotionProviderContract.ResolveReferenceImageField(route),
            DanceSellMotionProviderContract.ResolveMotionVideoField(route),
            ReadConfigString(route.ConfigJson, "upload_image_field") ?? "file",
            ReadConfigString(route.ConfigJson, "upload_video_field") ?? "video_file",
            ReadConfigString(route.ConfigJson, "project_id") ?? "default",
            ReadConfigString(route.ConfigJson, "subType") ?? ReadConfigString(route.ConfigJson, "sub_type") ?? "motion",
            ReadConfigString(route.ConfigJson, "background_source") ?? "input_video",
            !string.Equals(ReadConfigString(route.ConfigJson, "include_images_zero_url"), "false", StringComparison.OrdinalIgnoreCase),
            providerMode,
            providerRatio,
            credential);
    }

    private static bool Is79Ai(DanceSellJobDto job)
        => string.Equals(job.MotionProviderCode ?? job.ProviderCode, DanceSellConstants.ProviderCode, StringComparison.OrdinalIgnoreCase);

    private sealed record Ai79MotionRuntime(
        string ProviderCode,
        string Model,
        string BaseUrl,
        string Domain,
        string RouteConfigJson,
        string UploadImagePath,
        string UploadVideoPath,
        string MotionSubmitPath,
        string PollPath,
        string PollIdField,
        string ReferenceImageField,
        string MotionVideoField,
        string UploadImageField,
        string UploadVideoField,
        string ProjectId,
        string SubType,
        string BackgroundSource,
        bool IncludeImagesZeroUrl,
        string ProviderMode,
        string ProviderRatio,
        ResolvedProviderCredential Credential);

    private static string? ReadConfigString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(propertyName, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static bool TryUseApprovedReferenceUrl(string status, string? preparedReferenceUrl, out string approvedReferenceUrl)
    {
        approvedReferenceUrl = string.Empty;
        if (!string.Equals(status, DanceSellReferenceStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(preparedReferenceUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(preparedReferenceUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        approvedReferenceUrl = preparedReferenceUrl.Trim();
        return true;
    }

    private async Task<ResolvedMotionFile?> ResolveMotionFileAsync(
        Guid? mediaId,
        string? objectKey,
        string? publicUrl,
        string fieldName,
        string fallbackFileName,
        IReadOnlySet<string> allowedMime,
        long? maxBytes,
        string requiredErrorCode,
        string unsupportedMimeErrorCode,
        string tooLargeErrorCode,
        CancellationToken ct)
    {
        MediaFileDto? media = null;
        if (mediaId is Guid id && id != Guid.Empty)
        {
            media = await _media.GetAsync(id, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(objectKey))
        {
            media = await _media.GetByObjectKeyAsync(objectKey, ct);
        }

        if (media is null && !string.IsNullOrWhiteSpace(publicUrl))
        {
            media = await _media.GetByPublicUrlAsync(publicUrl, ct);
        }

        if (media is not null && media.IsActive)
        {
            var mime = NormalizeMotionMime(media.MimeType, media.FileName, allowedMime);
            ValidateMotionFile(mime, media.FileSizeBytes, allowedMime, maxBytes, requiredErrorCode, unsupportedMimeErrorCode, tooLargeErrorCode);
            var mediaIdValue = media.Id;
            return new ResolvedMotionFile(
                fieldName,
                FirstNonBlank(media.FileName, Path.GetFileName(media.ObjectKey), fallbackFileName)!,
                mime,
                media.FileSizeBytes ?? -1,
                media.ObjectKey is null ? "media" : "media_storage",
                async token => await _media.OpenReadAsync(mediaIdValue, token));
        }

        var fallbackUrl = FirstNonBlank(publicUrl);
        if (fallbackUrl is null)
        {
            return null;
        }

        if (!Uri.TryCreate(fallbackUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(requiredErrorCode);
        }

        var fileName = FirstNonBlank(Path.GetFileName(uri.LocalPath), fallbackFileName)!;
        var mimeType = NormalizeMotionMime(null, fileName, allowedMime);
        ValidateMotionFile(mimeType, null, allowedMime, maxBytes, requiredErrorCode, unsupportedMimeErrorCode, tooLargeErrorCode);
        return new ResolvedMotionFile(
            fieldName,
            fileName,
            mimeType,
            -1,
            "https_url_fallback",
            async token =>
            {
                var client = _httpClientFactory.CreateClient();
                var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStreamAsync(token);
            });
    }

    private static void ValidateMotionFile(
        string mimeType,
        long? sizeBytes,
        IReadOnlySet<string> allowedMime,
        long? maxBytes,
        string requiredErrorCode,
        string unsupportedMimeErrorCode,
        string tooLargeErrorCode)
    {
        if (!allowedMime.Contains(mimeType))
        {
            throw new InvalidOperationException(unsupportedMimeErrorCode);
        }

        if (sizeBytes is <= 0)
        {
            throw new InvalidOperationException(requiredErrorCode);
        }

        if (maxBytes is long limit && sizeBytes is long size && size > limit)
        {
            throw new InvalidOperationException(tooLargeErrorCode);
        }
    }

    private static string NormalizeMotionMime(string? mimeType, string? fileName, IReadOnlySet<string> allowedMime)
    {
        var normalized = mimeType?.Trim().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(normalized) && allowedMime.Contains(normalized))
        {
            return normalized;
        }

        return Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            _ => normalized ?? string.Empty
        };
    }

    private sealed record ResolvedMotionFile(
        string FieldName,
        string FileName,
        string MimeType,
        long SizeBytes,
        string Source,
        Func<CancellationToken, Task<Stream?>> OpenReadAsync)
    {
        public Ai79MultipartFilePart ToMultipartPart()
            => new(FieldName, FileName, MimeType, SizeBytes, OpenReadAsync);

        public Ai79MultipartFilePart ToMultipartPart(string fieldName)
            => new(fieldName, FileName, MimeType, SizeBytes, OpenReadAsync);
    }

    private async Task SubmitAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, Guid? operationId, CancellationToken ct)
    {
        var permit = await _rateLimiter.AcquireSubmitPermitAsync(DanceSellConstants.KieProviderCode, ct);
        if (!permit.Allowed)
        {
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, permit.RetryAfter, KieErrorCodes.RateLimited, "KIE submit rate limit reached.", ct);
            throw new RenderJobDeferredException("KIE submit deferred by local rate limiter.");
        }

        KieMotionControlRequest payload;
        try
        {
            payload = _payloadBuilder.BuildMotionControlRequest(new KieMotionControlBuildRequest
            {
                Prompt = danceJob.Prompt,
                CharacterImageUrl = danceJob.CharacterImageUrl,
                MotionVideoUrl = danceJob.MotionVideoUrl,
                Mode = danceJob.Mode,
                CharacterOrientation = danceJob.CharacterOrientation,
                ModelName = danceJob.MotionProviderModel ?? danceJob.ProviderModel
            });
        }
        catch (KieProviderException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? KieErrorCodes.Unknown, ex.Message, ex.RawResponse, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }

        var requestJson = KieJsonRedactor.Redact(JsonSerializer.Serialize(payload, KieJson.Options)) ?? "{}";
        var sw = Stopwatch.StartNew();
        try
        {
            var submitted = await _client.CreateTaskAsync(payload, ct);
            sw.Stop();
            var responseJson = KieJsonRedactor.Redact(submitted.RawResponse) ?? "{}";
            await _repo.UpdateSubmittedAsync(danceJob.Id, requestJson, submitted.TaskId!, responseJson, ct);
            if (operationId is Guid existingOperationId)
            {
                await _operations.MarkSubmittedAsync(existingOperationId, submitted.TaskId!, responseJson, ct);
            }
            else
            {
                var attemptNo = await _operations.GetNextAttemptNoAsync(danceJob.Id, DanceSellOperationTypes.MotionVideo, ct);
                await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
                {
                    Id = Guid.NewGuid(),
                    DanceSellJobId = danceJob.Id,
                    RenderJobId = renderJob.Id,
                    OperationType = DanceSellOperationTypes.MotionVideo,
                    AttemptNo = attemptNo,
                    ReferenceMode = danceJob.ReferenceMode,
                    ProviderCode = danceJob.MotionProviderCode ?? danceJob.ProviderCode,
                    ProviderCapabilityId = danceJob.MotionProviderCapabilityId,
                    ProviderAccountId = danceJob.MotionProviderAccountId,
                    ProviderModel = danceJob.MotionProviderModel ?? danceJob.ProviderModel,
                    ProviderTaskId = submitted.TaskId,
                    Status = DanceSellOperationStatuses.Submitted,
                    BillingStatus = danceJob.BillingStatus,
                    RefundStatus = danceJob.RefundStatus,
                    RequestJson = requestJson,
                    ResponseJson = responseJson,
                    CreatedAt = DateTime.UtcNow,
                    StartedAt = DateTime.UtcNow,
                    SubmittedAt = DateTime.UtcNow
                }, ct);
            }
            await _renderJobs.AddEventAsync(renderJob.Id, "KIE_TASK_SUBMITTED", "KIE Motion Control task submitted.",
                new { danceSellJobId = danceJob.Id, taskId = submitted.TaskId, durationMs = sw.ElapsedMilliseconds },
                ct: ct);
            await LogUsageAsync(danceJob, renderJob, "submitted", submitted.TaskId, "submitted", null, null, ct);
            await ScheduleNextPollAsync(renderJob, "KIE task submitted; polling scheduled.", ct);
        }
        catch (KieProviderException ex) when (ex.ErrorCode == KieErrorCodes.RateLimited)
        {
            await _repo.UpdateFailedAsync(danceJob.Id, DanceSellJobStatuses.Queued, null, BuildErrorJson(ex), KieErrorCodes.RateLimited, ex.Message, ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, ex.RetryAfter ?? _options.CurrentValue.PollInterval, KieErrorCodes.RateLimited, ex.Message, ct);
            throw new RenderJobDeferredException("KIE submit deferred after HTTP 429.");
        }
        catch (KieProviderException ex) when (ex.IsTransient)
        {
            await _repo.UpdateFailedAsync(danceJob.Id, DanceSellJobStatuses.Queued, null, BuildErrorJson(ex), ex.ErrorCode ?? KieErrorCodes.ProviderUnavailable, ex.Message, ct);
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, ex.RetryAfter ?? TimeSpan.FromSeconds(30), ex.ErrorCode ?? KieErrorCodes.ProviderUnavailable, ex.Message, ct);
            throw new RenderJobDeferredException("KIE transient submit failure scheduled for retry.");
        }
        catch (KieProviderException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? KieErrorCodes.Unknown, ex.Message, ex.RawResponse, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }
    }

    private async Task PollAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, Guid? operationId, CancellationToken ct)
    {
        if (danceJob.PollCount >= Math.Max(1, _options.CurrentValue.MaxPollCount))
        {
            await FailAsync(renderJob, danceJob, KieErrorCodes.PollTimeout, "KIE poll max count reached.", danceJob.PollResponseJson, permanent: true, ct, DanceSellJobStatuses.Timeout);
            throw new RenderJobTerminalFailureException("KIE poll max count reached.");
        }

        try
        {
            var detail = await _client.GetTaskDetailAsync(danceJob.ProviderTaskId!, ct);
            var responseJson = KieJsonRedactor.Redact(detail.RawResponse) ?? "{}";
            if (detail.Status == KieTaskStatuses.Completed)
            {
                if (!string.IsNullOrWhiteSpace(detail.ResultParseError))
                {
                    await FailAsync(renderJob, danceJob, KieErrorCodes.ResultJsonInvalid, detail.ResultParseError, responseJson, permanent: true, ct);
                    throw new RenderJobTerminalFailureException(detail.ResultParseError);
                }

                var resultUrl = detail.ResultUrls.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(resultUrl))
                {
                    await FailAsync(renderJob, danceJob, KieErrorCodes.ResultUrlMissing, "KIE resultUrls is empty.", responseJson, permanent: true, ct);
                    throw new RenderJobTerminalFailureException("KIE resultUrls is empty.");
                }

                await _completion.CompleteAsync(new DanceSellCompletionRequest
                {
                    DanceJob = danceJob,
                    ProviderTaskId = danceJob.ProviderTaskId,
                    ProviderStatus = detail.ProviderState ?? detail.Status,
                    ResponseJson = responseJson,
                    ResultVideoUrl = resultUrl,
                    ResultUrlCount = detail.ResultUrls.Count,
                    CreditsConsumed = detail.CreditsConsumed,
                    Source = "poll"
                }, ct);
                if (operationId is Guid existingOperationId)
                {
                    await _operations.MarkCompletedAsync(existingOperationId, detail.ProviderState ?? detail.Status, responseJson, detail.CreditsConsumed, resultUrl, ct);
                    await _operations.UpsertAssetAsync(new AiOperationAssetDto
                    {
                        OperationId = existingOperationId,
                        AssetRole = DanceSellAssetRoles.VideoOutput,
                        PublicUrl = resultUrl,
                        ProviderUrl = resultUrl,
                        MimeType = "video/mp4",
                        MetadataJson = DanceSellRepository.ToJson(new { source = "poll", resultUrlCount = detail.ResultUrls.Count })
                    }, ct);
                }
                else
                {
                    var attemptNo = await _operations.GetNextAttemptNoAsync(danceJob.Id, DanceSellOperationTypes.MotionVideo, ct);
                    var operation = await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
                    {
                        Id = Guid.NewGuid(),
                        DanceSellJobId = danceJob.Id,
                        RenderJobId = renderJob.Id,
                        OperationType = DanceSellOperationTypes.MotionVideo,
                        AttemptNo = attemptNo,
                        ReferenceMode = danceJob.ReferenceMode,
                        ProviderCode = danceJob.MotionProviderCode ?? danceJob.ProviderCode,
                        ProviderCapabilityId = danceJob.MotionProviderCapabilityId,
                        ProviderAccountId = danceJob.MotionProviderAccountId,
                        ProviderModel = danceJob.MotionProviderModel ?? danceJob.ProviderModel,
                        ProviderTaskId = danceJob.ProviderTaskId,
                        Status = DanceSellOperationStatuses.Completed,
                        BillingStatus = danceJob.BillingStatus,
                        RefundStatus = danceJob.RefundStatus,
                        ResponseJson = responseJson,
                        CreditsConsumed = detail.CreditsConsumed,
                        UsageQuantity = detail.CreditsConsumed,
                        UsageUnit = detail.CreditsConsumed is null ? null : "credits",
                        CostSource = detail.CreditsConsumed is null ? "estimated" : "provider_response",
                        CreatedAt = DateTime.UtcNow,
                        CompletedAt = DateTime.UtcNow
                    }, ct);
                    if (operation is not null)
                    {
                        await _operations.MarkCompletedAsync(operation.Id, detail.ProviderState ?? detail.Status, responseJson, detail.CreditsConsumed, resultUrl, ct);
                        await _operations.UpsertAssetAsync(new AiOperationAssetDto
                        {
                            OperationId = operation.Id,
                            AssetRole = DanceSellAssetRoles.VideoOutput,
                            PublicUrl = resultUrl,
                            ProviderUrl = resultUrl,
                            MimeType = "video/mp4",
                            MetadataJson = DanceSellRepository.ToJson(new { source = "poll", resultUrlCount = detail.ResultUrls.Count })
                        }, ct);
                    }
                }
                return;
            }

            if (detail.Status == KieTaskStatuses.Failed)
            {
                var error = string.IsNullOrWhiteSpace(detail.FailMsg) ? "KIE task failed." : detail.FailMsg;
                await FailAsync(renderJob, danceJob, KieErrorCodes.TaskFailed, error, responseJson, permanent: true, ct);
                throw new RenderJobTerminalFailureException(error);
            }

            var nextPoll = DateTime.UtcNow.Add(_options.CurrentValue.PollInterval);
            await _repo.UpdatePollingAsync(danceJob.Id, detail.ProviderState ?? detail.Status, responseJson, danceJob.PollCount + 1, nextPoll, ct);
            await _renderJobs.AddEventAsync(renderJob.Id, "KIE_TASK_POLLING", "KIE task is still running.",
                new { danceSellJobId = danceJob.Id, danceJob.ProviderTaskId, detail.ProviderState, detail.Status, pollCount = danceJob.PollCount + 1 }, ct: ct);
            await LogUsageAsync(danceJob, renderJob, "processing", danceJob.ProviderTaskId, detail.ProviderState, null, null, ct);
            await ScheduleNextPollAsync(renderJob, "KIE task not terminal; next poll scheduled.", ct);
        }
        catch (KieProviderException ex) when (ex.IsTransient)
        {
            await _renderJobs.ScheduleRetryAsync(renderJob.Id, ex.RetryAfter ?? _options.CurrentValue.PollInterval, ex.ErrorCode ?? KieErrorCodes.PollFailed, ex.Message, ct);
            throw new RenderJobDeferredException("KIE transient poll failure scheduled for retry.");
        }
        catch (KieProviderException ex)
        {
            await FailAsync(renderJob, danceJob, ex.ErrorCode ?? KieErrorCodes.PollFailed, ex.Message, ex.RawResponse, permanent: true, ct);
            throw new RenderJobTerminalFailureException(ex.Message, ex);
        }
    }

    private async Task ScheduleNextPollAsync(RenderJobDto renderJob, string message, CancellationToken ct)
    {
        await _renderJobs.ScheduleRetryAsync(renderJob.Id, _options.CurrentValue.PollInterval, "KIE_POLL_SCHEDULED", message, ct);
        throw new RenderJobDeferredException(message);
    }

    private async Task FailAsync(RenderJobDto renderJob, DanceSellJobDto danceJob, string errorCode, string errorMessage, string? rawResponse, bool permanent, CancellationToken ct, string status = DanceSellJobStatuses.Failed)
    {
        await _completion.FailAsync(new DanceSellFailureRequest
        {
            DanceJob = danceJob,
            ProviderTaskId = danceJob.ProviderTaskId,
            ProviderStatus = danceJob.ProviderStatus,
            ResponseJson = BuildErrorJson(errorCode, errorMessage, rawResponse),
            Status = status,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Permanent = permanent,
            Source = "poll"
        }, ct);
    }

    private async Task LogUsageAsync(DanceSellJobDto danceJob, RenderJobDto renderJob, string status, string? taskId, string? providerStatus, int? resultUrlCount, string? errorMessage, CancellationToken ct)
    {
        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = DanceSellCompletionService.ToBigIntCustomerId(danceJob.CustomerId),
            ProviderCode = danceJob.MotionProviderCode ?? danceJob.ProviderCode,
            CapabilityCode = string.Equals(danceJob.MotionProviderCode ?? danceJob.ProviderCode, DanceSellConstants.KieProviderCode, StringComparison.OrdinalIgnoreCase)
                ? DanceSellConstants.KieCapabilityCode
                : DanceSellConstants.CapabilityCode,
            FeatureCode = DanceSellConstants.FeatureCode,
            ModelName = danceJob.MotionProviderModel ?? danceJob.ProviderModel,
            RequestId = danceJob.LogicalRequestId,
            JobId = renderJob.Id.ToString("N"),
            Quantity = 1,
            UnitType = "request",
            UnitCostPoints = 0,
            TotalPoints = 0,
            ProviderRawCost = null,
            Status = status,
            ErrorMessage = errorMessage,
            MetadataJson = JsonSerializer.Serialize(new
            {
                danceSellJobId = danceJob.Id,
                providerTaskId = taskId,
                providerStatus,
                resultUrlCount,
                phase = "phase1_no_billing"
            }, KieJson.Options)
        }, ct);
    }

    private static string BuildErrorJson(KieProviderException ex)
        => BuildErrorJson(ex.ErrorCode, ex.Message, ex.RawResponse, ex.StatusCode);

    private static string BuildErrorJson(string? errorCode, string errorMessage, string? rawResponse, int? statusCode = null)
        => JsonSerializer.Serialize(new KieProviderError
        {
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            HttpStatus = statusCode,
            RawResponse = KieJsonRedactor.Redact(rawResponse)
        }, KieJson.Options);
}

public static class KieJsonRedactor
{
    private static readonly string[] SecretKeys = new[]
    {
        "authorization", "apiKey", "api_key", "token", "accessToken", "secret", "password", "KIE_API_KEY"
    };

    public static string? Redact(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var redacted = RedactElement(doc.RootElement);
            return JsonSerializer.Serialize(redacted, KieJson.Options);
        }
        catch (JsonException)
        {
            var text = raw;
            foreach (var key in SecretKeys)
            {
                text = text.Replace(key, "[redacted-key]", StringComparison.OrdinalIgnoreCase);
            }

            return text;
        }
    }

    private static object? RedactElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => IsSecretKey(p.Name) ? (object?)"[redacted]" : RedactElement(p.Value),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(RedactElement).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDecimal(out var d) => d,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool IsSecretKey(string key)
        => SecretKeys.Any(x => key.Equals(x, StringComparison.OrdinalIgnoreCase));
}
