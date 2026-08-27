using TodoX.Web.Services.AiProviders;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

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
    decimal ChargedPoints,
    decimal? EstimatedUsd,
    string? CostSource,
    string? AspectRatio,
    string? PosterUrl,
    decimal? DurationSeconds,
    Guid? UserId,
    Guid? CustomerId,
    bool IsRecovery);

public interface IRVideoSceneVideoCompletionService
{
    Task<MediaFileDto> CompleteProviderVideoAsync(RVideoSceneVideoCompletionRequest request, CancellationToken ct = default);
}

public sealed class RVideoSceneVideoCompletionService : IRVideoSceneVideoCompletionService
{
    private readonly IMediaFileService _media;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IRVideoSceneMediaFinalizerService _finalizer;
    private readonly IRVideoJobService _rvideoJobs;
    private readonly IAiImageBillingService _billing;
    private readonly IRenderJobService _jobs;
    private readonly VideoRenderRepository _projects;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;

    public RVideoSceneVideoCompletionService(
        IMediaFileService media,
        ISceneMediaVersioningService versions,
        IRVideoSceneMediaFinalizerService finalizer,
        IRVideoJobService rvideoJobs,
        IAiImageBillingService billing,
        IRenderJobService jobs,
        VideoRenderRepository projects,
        TenantContext tenant,
        IConfiguration config)
    {
        _media = media;
        _versions = versions;
        _finalizer = finalizer;
        _rvideoJobs = rvideoJobs;
        _billing = billing;
        _jobs = jobs;
        _projects = projects;
        _tenant = tenant;
        _config = config;
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
                ChargedPoints: request.ChargedPoints,
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
        await _finalizer.TryFinalizeSceneMediaAsync(
            request.ProjectId,
            request.SceneId,
            request.IsRecovery ? "RVIDEO_VIDEO_RECOVERED" : "SCENE_VIDEO_READY",
            ct);
        await _rvideoJobs.SyncLifecycleAsync(request.ProjectId, RVideoStages.Video, VideoProjectStatuses.Rendering, ct);

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
            new { request.SceneId, request.SceneIndex, request.SceneVideoVersionId, request.ProviderTaskId, request.ChargedPoints },
            ct);

        if (request.IsRecovery && request.RenderJobId is Guid renderJobId)
        {
            await _jobs.MarkRecoveredCompletedAsync(
                renderJobId,
                request.ProjectId,
                request.SceneId,
                request.SceneVideoVersionId,
                request.LogicalRequestId,
                ct);
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
