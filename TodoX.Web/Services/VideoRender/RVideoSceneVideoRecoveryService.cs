using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneVideoRecoveryService
{
    bool IsRecoverableStuck(SceneVideoVersionDto version, RenderJobDto job);
    Task<bool> RecoverStuckAsync(long projectId, VideoProjectSceneDto scene, SceneVideoVersionDto version, RenderJobDto job, string? reason = null, CancellationToken ct = default);
    Task<IReadOnlyList<RVideoSceneVideoRecoveryCandidate>> ListRecoverableStuckAsync(long projectId, int take = 100, CancellationToken ct = default);
    Task<int> RecoverRecoverableStuckAsync(long projectId, int take = 100, CancellationToken ct = default);
}

public sealed record RVideoSceneVideoRecoveryCandidate(
    long ProjectId,
    long SceneId,
    int SceneIndex,
    Guid SceneVideoVersionId,
    Guid RenderJobId,
    string LogicalRequestId,
    string VersionStatus,
    string JobStatus,
    string? ProviderTaskId,
    string? ErrorMessage);

public sealed class RVideoSceneVideoRecoveryService : IRVideoSceneVideoRecoveryService
{
    private readonly ISceneMediaVersioningService _versions;
    private readonly VideoRenderRepository _repo;
    private readonly IRenderJobService _jobs;

    public RVideoSceneVideoRecoveryService(ISceneMediaVersioningService versions, VideoRenderRepository repo, IRenderJobService jobs)
    {
        _versions = versions;
        _repo = repo;
        _jobs = jobs;
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

    public async Task<IReadOnlyList<RVideoSceneVideoRecoveryCandidate>> ListRecoverableStuckAsync(long projectId, int take = 100, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            return Array.Empty<RVideoSceneVideoRecoveryCandidate>();
        }

        var results = new List<RVideoSceneVideoRecoveryCandidate>();
        foreach (var scene in project.Scenes.OrderBy(x => x.SceneIndex))
        {
            var versions = await _versions.ListSceneVideoVersionsAsync(scene.Id, 0, Math.Clamp(take, 1, 100), ct);
            foreach (var version in versions.OrderByDescending(x => x.VersionNumber))
            {
                if (version.RenderJobId is not Guid renderJobId)
                {
                    continue;
                }

                var job = await _jobs.GetAsync(renderJobId, ct);
                if (job is null || !IsRecoverableStuck(version, job))
                {
                    continue;
                }

                if (!string.Equals(scene.Status, VideoSceneStatuses.Failed, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(scene.Status, VideoSceneStatuses.VideoReady, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new RVideoSceneVideoRecoveryCandidate(
                        projectId,
                        scene.Id,
                        scene.SceneIndex,
                        version.Id,
                        job.Id,
                        version.LogicalRequestId,
                        version.Status,
                        job.Status,
                        version.ProviderTaskId,
                        job.ErrorMessage));
                }

                break;
            }

            if (results.Count >= take)
            {
                break;
            }
        }

        return results;
    }

    public async Task<int> RecoverRecoverableStuckAsync(long projectId, int take = 100, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            return 0;
        }

        var candidates = await ListRecoverableStuckAsync(projectId, take, ct);
        var recovered = 0;
        foreach (var candidate in candidates)
        {
            var scene = project.Scenes.FirstOrDefault(x => x.Id == candidate.SceneId);
            if (scene is null)
            {
                continue;
            }

            var versions = await _versions.ListSceneVideoVersionsAsync(scene.Id, 0, 100, ct);
            var matchedVersion = versions.FirstOrDefault(x => x.Id == candidate.SceneVideoVersionId);
            var job = await _jobs.GetAsync(candidate.RenderJobId, ct);
            if (matchedVersion is null || job is null)
            {
                continue;
            }

            if (await RecoverStuckAsync(projectId, scene, matchedVersion, job, job.ErrorMessage, ct))
            {
                recovered++;
            }
        }

        return recovered;
    }
}
