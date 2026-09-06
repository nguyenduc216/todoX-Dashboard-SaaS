using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneVideoRecoveryService
{
    bool IsRecoverableStuck(SceneVideoVersionDto version, RenderJobDto job);
    Task<bool> RecoverStuckAsync(long projectId, VideoProjectSceneDto scene, SceneVideoVersionDto version, RenderJobDto job, string? reason = null, CancellationToken ct = default);
}

public sealed class RVideoSceneVideoRecoveryService : IRVideoSceneVideoRecoveryService
{
    private readonly ISceneMediaVersioningService _versions;
    private readonly VideoRenderRepository _repo;

    public RVideoSceneVideoRecoveryService(ISceneMediaVersioningService versions, VideoRenderRepository repo)
    {
        _versions = versions;
        _repo = repo;
    }

    public bool IsRecoverableStuck(SceneVideoVersionDto version, RenderJobDto job)
        => string.Equals(job.JobType, RenderJobTypes.RenderSceneVideo, StringComparison.OrdinalIgnoreCase)
           && string.Equals(job.Status, RenderJobStatuses.Failed, StringComparison.OrdinalIgnoreCase)
           && string.IsNullOrWhiteSpace(version.ProviderTaskId)
           && version.Status.Trim().ToLowerInvariant() is "queued" or "submitted" or "processing" or "pending_reconciliation" or "rendering" or "video_rendering";

    public async Task<bool> RecoverStuckAsync(long projectId, VideoProjectSceneDto scene, SceneVideoVersionDto version, RenderJobDto job, string? reason = null, CancellationToken ct = default)
    {
        if (!IsRecoverableStuck(version, job))
        {
            return false;
        }

        var message = reason ?? job.ErrorMessage ?? "Scene video render job failed before a provider task was created.";
        await _versions.FailSceneVideoVersionAsync(version.Id, "RVIDEO_STUCK_VIDEO_RECOVERED", message, ct);
        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.Failed,
            errorMessage: message,
            title: scene.Title,
            scenePrompt: scene.ScenePrompt,
            imagePrompt: scene.ImagePrompt,
            videoPrompt: scene.VideoPrompt,
            ct: ct);
        await _repo.AddProjectEventAsync(projectId, "RVIDEO_STUCK_VIDEO_RECOVERED", "warning",
            "Recovered a stuck scene-video record without retrying it.",
            new
            {
                sceneId = scene.Id,
                scene.SceneIndex,
                sceneVideoVersionId = version.Id,
                failedRenderJobId = job.Id,
                version.LogicalRequestId,
                providerTaskId = version.ProviderTaskId
            }, ct);
        return true;
    }
}
