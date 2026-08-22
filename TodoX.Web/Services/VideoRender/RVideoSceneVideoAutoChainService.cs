using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneVideoAutoChainService
{
    Task<bool> TryEnqueueSceneVideoAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default);
}

public sealed class RVideoSceneVideoAutoChainService : IRVideoSceneVideoAutoChainService
{
    private readonly VideoRenderRepository _repo;
    private readonly IVideoRenderEligibilityService _eligibility;
    private readonly IRenderJobService _jobs;
    private readonly ILogger<RVideoSceneVideoAutoChainService> _logger;

    public RVideoSceneVideoAutoChainService(
        VideoRenderRepository repo,
        IVideoRenderEligibilityService eligibility,
        IRenderJobService jobs,
        ILogger<RVideoSceneVideoAutoChainService> logger)
    {
        _repo = repo;
        _eligibility = eligibility;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<bool> TryEnqueueSceneVideoAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            _logger.LogWarning("RVIDEO_AUTO_CHAIN_PROJECT_MISSING projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        var scene = project.Scenes.FirstOrDefault(x => x.Id == sceneId);
        if (scene is null)
        {
            _logger.LogWarning("RVIDEO_AUTO_CHAIN_SCENE_MISSING projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        var eligibility = await _eligibility.GetVideoRenderEligibilityAsync(projectId, new[] { sceneId }, ct);
        var result = eligibility.Results.FirstOrDefault();
        if (result is null)
        {
            _logger.LogWarning("RVIDEO_AUTO_CHAIN_ELIGIBILITY_EMPTY projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        if (result.Status != VideoRenderEligibilityStatus.Eligible)
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED", result.Status == VideoRenderEligibilityStatus.AlreadyActive ? "info" : "warning",
                result.Message,
                new
                {
                    projectId,
                    sceneId,
                    scene.SceneIndex,
                    status = result.Status.ToString(),
                    errorCode = result.ErrorCode,
                    triggerSource
                }, ct);
            return false;
        }

        var renderSettings = RVideoRules.ResolveRenderSettings(project.OriginalPrompt);
        var logicalRequestKey = BuildLogicalRequestKey(projectId, sceneId);

        await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_AUTO_ENQUEUE_REQUESTED", "info",
            $"Scene {scene.SceneIndex} video auto enqueue requested.",
            new
            {
                projectId,
                sceneId,
                scene.SceneIndex,
                triggerSource,
                aspectRatio = renderSettings.AspectRatio,
                resolution = renderSettings.Resolution,
                logicalRequestKey
            }, ct);

        var enqueueInput = new SceneVideoRenderInput
        {
            ProjectId = projectId,
            SceneIds = new[] { sceneId },
            AspectRatio = renderSettings.AspectRatio,
            Resolution = renderSettings.Resolution,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
        };

        var model = new RenderJobCreateModel
        {
            JobType = SceneVideoRenderHandler.JobTypeName,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
            Input = enqueueInput,
            Prompt = new
            {
                projectId,
                sceneId,
                source = triggerSource,
                stage = "video",
                autoChain = true
            },
            References = Array.Empty<object>(),
            LogCode = logicalRequestKey,
            MaxAttempts = 1,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.Pending,
            ProviderCode = RVideoVideoModelPolicy.ProviderCode,
            ModelCode = RVideoVideoModelPolicy.GetInitial().Model
        };

        var (job, alreadyActive) = await _jobs.EnqueueForLogCodeIfNoneActiveAsync(model, logicalRequestKey, ct);
        if (alreadyActive)
        {
            await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED", "info",
                $"Scene {scene.SceneIndex} video auto enqueue skipped because the same request is already active.",
                new
                {
                    projectId,
                    sceneId,
                    scene.SceneIndex,
                    triggerSource,
                    logicalRequestKey,
                    activeJobId = job.Id
                }, ct);
            return false;
        }

        await _repo.AddProjectEventAsync(projectId, "SCENE_VIDEO_AUTO_ENQUEUED", "info",
            $"Scene {scene.SceneIndex} video auto enqueue submitted.",
            new
            {
                projectId,
                sceneId,
                scene.SceneIndex,
                triggerSource,
                logicalRequestKey,
                jobId = job.Id,
                sceneIds = enqueueInput.SceneIds
            }, ct);

        return true;
    }

    public static string BuildLogicalRequestKey(long projectId, long sceneId)
        => $"rvideo-auto-video:{projectId}:{sceneId}";
}
