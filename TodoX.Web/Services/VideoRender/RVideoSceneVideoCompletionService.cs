using TodoX.Web.Services.AiProviders;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;
using TodoX.Web.Services;

namespace TodoX.Web.Services.VideoRender;

public sealed record RVideoSceneVideoCompletionRequest(
    long ProjectId,
    long SceneId,
    int SceneIndex,
    Guid SceneVideoVersionId,
    Guid? RenderJobId,
    string? StorageKey,
    string LogicalRequestId,
    string ProviderTaskId,
    string OutputUrl,
    string? ProviderCode,
    string? ModelName,
    long? ProviderCapabilityId,
    string? ProviderUsageJson,
    string? TariffSnapshotJson,
    decimal CustomerPointRate,
    decimal? EstimatedUsd,
    string? CostSource,
    string? AspectRatio,
    string? PosterUrl,
    decimal? DurationSeconds,
    Guid? UserId,
    Guid? CustomerId,
    PointBillingIntent BillingIntent,
    Guid? BillingOperationId,
    bool IsRecovery);

public interface IRVideoSceneVideoCompletionService
{
    Task<MediaFileDto> CompleteProviderVideoAsync(RVideoSceneVideoCompletionRequest request, CancellationToken ct = default);
}

public sealed class RVideoSceneVideoCompletionService : IRVideoSceneVideoCompletionService
{
    private readonly IMediaFileService _media;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IRVideoSceneAudioAutoChainService _audioAutoChain;
    private readonly IRVideoSceneMediaFinalizerService _finalizer;
    private readonly IRVideoJobService _rvideoJobs;
    private readonly IRVideoProjectFinalizationService _finalization;
    private readonly IAiImageBillingService _billing;
    private readonly WalletService _wallets;
    private readonly IRenderJobService _jobs;
    private readonly VideoRenderRepository _projects;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<RVideoSceneVideoCompletionService> _logger;

    public RVideoSceneVideoCompletionService(
        IMediaFileService media,
        ISceneMediaVersioningService versions,
        IRVideoSceneAudioAutoChainService audioAutoChain,
        IRVideoSceneMediaFinalizerService finalizer,
        IRVideoJobService rvideoJobs,
        IRVideoProjectFinalizationService finalization,
        IAiImageBillingService billing,
        WalletService wallets,
        IRenderJobService jobs,
        VideoRenderRepository projects,
        TenantContext tenant,
        IConfiguration config,
        ILogger<RVideoSceneVideoCompletionService> logger)
    {
        _media = media;
        _versions = versions;
        _audioAutoChain = audioAutoChain;
        _finalizer = finalizer;
        _rvideoJobs = rvideoJobs;
        _finalization = finalization;
        _billing = billing;
        _wallets = wallets;
        _jobs = jobs;
        _projects = projects;
        _tenant = tenant;
        _config = config;
        _logger = logger;
    }

    public async Task<MediaFileDto> CompleteProviderVideoAsync(RVideoSceneVideoCompletionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.OutputUrl))
        {
            throw new InvalidOperationException("PROVIDER_OUTPUT_URL_MISSING");
        }

        await _tenant.EnsureLoadedAsync(ct);
        var objectKey = string.IsNullOrWhiteSpace(request.StorageKey)
            ? SceneMediaStorageKeys.SceneVideoOutput(_tenant.TenantId, request.ProjectId, request.SceneId, request.SceneVideoVersionId)
            : request.StorageKey;
        var existing = await _media.GetByObjectKeyAsync(_tenant.TenantId, objectKey, ct);

        await _projects.AddProjectEventAsync(
            request.ProjectId,
            request.IsRecovery ? "RVIDEO_VIDEO_RECOVERY_BEGIN" : "RVIDEO_VIDEO_PROVIDER_RESULT_SUCCESS",
            "info",
            request.IsRecovery ? "Recovering a successful provider video result." : "Scene-video provider returned a successful result.",
            new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId },
            ct);
        await _projects.AddProjectEventAsync(
            request.ProjectId,
            "RVIDEO_VIDEO_PERSIST_BEGIN",
            "info",
            "Persisting provider video output at the immutable scene-video key.",
            new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId, objectKey },
            ct);

        var saved = await _media.DownloadAndSaveBinaryAtObjectKeyAsync(
            request.OutputUrl,
            objectKey,
            "video_scene_video",
            "video/mp4",
            request.UserId,
            request.CustomerId,
            _tenant.TenantId,
            ct);

        if (saved.Id == Guid.Empty || string.IsNullOrWhiteSpace(saved.PublicUrl ?? saved.FileUrl))
        {
            throw new InvalidOperationException("MEDIA_STORAGE_FAILED");
        }

        await _projects.AddProjectEventAsync(
            request.ProjectId,
            existing?.Id == saved.Id ? "RVIDEO_VIDEO_PERSIST_REUSED" : "RVIDEO_VIDEO_PERSIST_SUCCESS",
            "info",
            existing?.Id == saved.Id ? "Reused the existing tenant media record for the immutable scene-video key." : "Provider video output was persisted.",
            new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId, mediaId = saved.Id, objectKey },
            ct);

        var chargedPoints = 0m;
        if (request.BillingIntent != PointBillingIntent.SystemRetry)
        {
            var referenceId = PointBillingReference.ForOperation(
                request.RenderJobId ?? request.SceneVideoVersionId,
                "rvideo_scene_video",
                request.SceneVideoVersionId.ToString("N"),
                request.BillingIntent,
                request.BillingOperationId);
            var charge = await _wallets.ChargeAsync(
                request.CustomerId, request.UserId, request.CustomerPointRate, (int)Math.Max(1, request.DurationSeconds ?? 1),
                request.BillingIntent == PointBillingIntent.UserRerender ? "rvideo_user_rerender_video" : "rvideo_initial_render_video",
                request.ProviderCode ?? "todox", request.ModelName ?? "video", "rvideo", "second",
                referenceId, "rvideo_scene_video_success");
            if (!charge.Ok)
            {
                await _projects.AddProjectEventAsync(request.ProjectId, "RVIDEO_VIDEO_BILLING_ANOMALY", "error",
                    "Video result was accepted but the success charge could not be completed.",
                    new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId, requiredPoints = request.CustomerPointRate, charge.Error }, ct);
                throw new InvalidOperationException(charge.Error ?? "Insufficient points after provider success.");
            }

            chargedPoints = charge.Charged == 0 ? request.CustomerPointRate : charge.Charged;
        }
        else
        {
            await _wallets.LogUsageOnlyAsync(request.CustomerId, request.UserId,
                request.ProviderCode ?? "todox", request.ModelName ?? "video",
                "rvideo_system_retry_video", (int)Math.Max(1, request.DurationSeconds ?? 1), request.CustomerPointRate,
                "rvideo", "second", request.SceneVideoVersionId, "rvideo_system_retry", "success");
        }

        await _versions.CompleteSceneVideoVersionAsync(
            request.SceneVideoVersionId,
            new SceneVideoVersionCompleteRequest(
                saved.PublicUrl ?? saved.FileUrl,
                ResolvePhysicalPath(saved.ObjectKey),
                PosterUrl: request.PosterUrl,
                DurationSeconds: request.DurationSeconds,
                MimeType: "video/mp4",
                ProviderCode: request.ProviderCode,
                ModelName: request.ModelName,
                ProviderCapabilityId: request.ProviderCapabilityId,
                ProviderTaskId: request.ProviderTaskId,
                BillingLogicalRequestId: request.LogicalRequestId,
                EstimatedUsd: request.EstimatedUsd,
                ActualUsd: null,
                ChargedPoints: chargedPoints,
                RefundedPoints: 0,
                CostSource: request.CostSource ?? "configured_tariff",
                AspectRatio: request.AspectRatio,
                ResultMediaId: saved.Id),
            ct);

        await _projects.AddProjectEventAsync(
            request.ProjectId,
            "SCENE_VIDEO_READY",
            "info",
            $"Scene {request.SceneIndex} rendered successfully.",
            new { request.SceneId, request.SceneIndex, request.ProviderTaskId, videoUrl = saved.PublicUrl ?? saved.FileUrl },
            ct);

        try
        {
            await _audioAutoChain.TryEnqueueSceneAudioAsync(
                request.ProjectId,
                request.SceneId,
                request.IsRecovery ? "RVIDEO_VIDEO_RECOVERED" : "SCENE_VIDEO_READY",
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordPostCompletionFailureAsync(
                request,
                "RVIDEO_SCENE_AUDIO_AUTO_CHAIN_FAILED",
                "Scene audio auto-chain failed after scene-video completion.",
                ex,
                ct);
        }

        await _billing.CompleteAsync(new AiImageBillingCompleteRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            Success = true,
            ActualModel = request.ModelName,
            ProviderTaskId = request.ProviderTaskId,
            ProviderUsageJson = request.ProviderUsageJson,
            TariffSnapshotJson = request.TariffSnapshotJson
        }, ct);
        await _projects.AddProjectEventAsync(
            request.ProjectId,
            "RVIDEO_VIDEO_BILLING_COMPLETED",
            "info",
            "Scene-video billing was completed after local provider output persistence.",
            new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId, chargedPoints },
            ct);

        try
        {
            await _finalizer.TryFinalizeSceneMediaAsync(
                request.ProjectId,
                request.SceneId,
                request.IsRecovery ? "RVIDEO_VIDEO_RECOVERED" : "SCENE_VIDEO_READY",
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordPostCompletionFailureAsync(
                request,
                "RVIDEO_VIDEO_FINALIZER_FAILED",
                "Scene-video finalization failed after billing completion.",
                ex,
                ct);
        }

        try
        {
            await _rvideoJobs.SyncLifecycleAsync(request.ProjectId, RVideoStages.Video, VideoProjectStatuses.Rendering, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordPostCompletionFailureAsync(
                request,
                "RVIDEO_VIDEO_LIFECYCLE_SYNC_FAILED",
                "RVIDEO lifecycle synchronization failed after billing completion.",
                ex,
                ct);
        }

        try
        {
            await _finalization.TryEnqueueFinalMergeAsync(
                request.ProjectId,
                request.IsRecovery ? RVideoProjectFinalizationContracts.TriggerVideoRecovered : RVideoProjectFinalizationContracts.TriggerSceneVideoReady,
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            await RecordPostCompletionFailureAsync(
                request,
                "RVIDEO_VIDEO_FINAL_MERGE_TRIGGER_FAILED",
                "RVIDEO final merge trigger failed after scene-video completion.",
                ex,
                ct);
        }

        if (request.IsRecovery && request.RenderJobId is Guid renderJobId)
        {
            try
            {
                await _jobs.MarkRecoveredCompletedAsync(
                    renderJobId,
                    request.ProjectId,
                    request.SceneId,
                    request.SceneVideoVersionId,
                    request.LogicalRequestId,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await RecordPostCompletionFailureAsync(
                    request,
                    "RVIDEO_VIDEO_RENDER_JOB_RECOVERY_MARK_FAILED",
                    "Recovered scene-video render-job bookkeeping failed after billing completion.",
                    ex,
                    ct);
            }
        }

        if (request.IsRecovery)
        {
            await _projects.AddProjectEventAsync(
                request.ProjectId,
                "RVIDEO_VIDEO_RECOVERY_COMPLETED",
                "info",
                "Recovered provider video output and continued the RVIDEO lifecycle.",
                new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId },
                ct);
        }

        return saved;
    }

    private async Task RecordPostCompletionFailureAsync(
        RVideoSceneVideoCompletionRequest request,
        string eventType,
        string message,
        Exception exception,
        CancellationToken ct)
    {
        var safeErrorMessage = ToSafeErrorMessage(exception);
        _logger.LogError(
            exception,
            "{EventType} projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} sceneVideoVersionId={SceneVideoVersionId} providerTaskId={ProviderTaskId} errorType={ErrorType} safeErrorMessage={SafeErrorMessage}",
            eventType,
            request.ProjectId,
            request.SceneId,
            request.SceneIndex,
            request.SceneVideoVersionId,
            request.ProviderTaskId,
            exception.GetType().Name,
            safeErrorMessage);

        try
        {
            await _projects.AddProjectEventAsync(
                request.ProjectId,
                eventType,
                "error",
                message,
                new
                {
                    request.ProjectId,
                    request.SceneId,
                    request.SceneIndex,
                    request.SceneVideoVersionId,
                    request.ProviderTaskId,
                    errorType = exception.GetType().Name,
                    safeErrorMessage
                },
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception eventException)
        {
            var safeEventErrorMessage = ToSafeErrorMessage(eventException);
            _logger.LogError(
                eventException,
                "RVIDEO_VIDEO_POST_COMPLETION_DIAGNOSTIC_EVENT_FAILED projectId={ProjectId} sceneId={SceneId} eventType={EventType} errorType={ErrorType} safeErrorMessage={SafeErrorMessage}",
                request.ProjectId,
                request.SceneId,
                eventType,
                eventException.GetType().Name,
                safeEventErrorMessage);
        }
    }

    private static string ToSafeErrorMessage(Exception ex)
    {
        var message = ex.Message.ReplaceLineEndings(" ").Trim();
        if (message.Length > 500)
        {
            message = message[..500];
        }

        return message;
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
