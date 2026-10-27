using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoProjectFinalizationService
{
    Task<bool> IsCurrentFinalVideoAsync(long projectId, CancellationToken ct = default) => Task.FromResult(false);
    Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync(long projectId, string triggerSource, CancellationToken ct = default);
}

public sealed record RVideoProjectFinalizationResult(
    bool Enqueued,
    bool AlreadyActive,
    string Reason,
    Guid? RenderJobId,
    string LogicalRequestId);

public static class RVideoProjectFinalizationContracts
{
    public const string TriggerAuto = "rvideo_auto_lifecycle";
    public const string TriggerManual = "rvideo_manual_ui";
    public const string TriggerSceneVideoReady = "SCENE_VIDEO_READY";
    public const string TriggerVideoRecovered = "RVIDEO_VIDEO_RECOVERED";
    public const string TriggerSceneAudioReady = "SCENE_AUDIO_READY";
}

public sealed class RVideoProjectFinalizationService : IRVideoProjectFinalizationService
{
    private readonly Func<long, CancellationToken, Task<VideoProjectDto?>> _getProject;
    private readonly Func<long, CancellationToken, Task<RVideoJobSettingsDto?>> _getSettings;
    private readonly Func<long, string, string, string, object?, CancellationToken, Task> _event;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IRenderJobService _jobs;
    private readonly Func<long, string, string, CancellationToken, Task> _sync;
    private readonly Func<CurrentUserSession?> _currentUser;
    private readonly Func<long, CancellationToken, Task<bool>> _hasOpenVideoGeneration;
    private readonly Func<long, CancellationToken, Task<string>> _getMergeSnapshot;

    public RVideoProjectFinalizationService(
        VideoRenderRepository repo,
        RVideoJobSettingsRepository settings,
        ISceneMediaVersioningService versions,
        IRenderJobService jobs,
        IRVideoJobService rvideoJobs,
        TodoX.Web.Services.AuthStateService auth)
        : this(repo.GetProjectAsync, settings.GetAsync, repo.AddProjectEventAsync,
            versions, jobs, rvideoJobs.SyncLifecycleAsync, () => auth.CurrentUser,
            repo.HasOpenVideoGenerationAsync, repo.GetVideoMergeSnapshotAsync)
    {
    }

    internal RVideoProjectFinalizationService(
        Func<long, CancellationToken, Task<VideoProjectDto?>> getProject,
        Func<long, CancellationToken, Task<RVideoJobSettingsDto?>> getSettings,
        Func<long, string, string, string, object?, CancellationToken, Task> addEvent,
        ISceneMediaVersioningService versions, IRenderJobService jobs,
        Func<long, string, string, CancellationToken, Task> sync,
        Func<CurrentUserSession?> currentUser,
        Func<long, CancellationToken, Task<bool>>? hasOpenVideoGeneration = null,
        Func<long, CancellationToken, Task<string>>? getMergeSnapshot = null)
    {
        _getProject = getProject;
        _getSettings = getSettings;
        _event = addEvent;
        _versions = versions;
        _jobs = jobs;
        _sync = sync;
        _currentUser = currentUser;
        _hasOpenVideoGeneration = hasOpenVideoGeneration ?? ((_, _) => Task.FromResult(false));
        _getMergeSnapshot = getMergeSnapshot ?? ((_, _) => Task.FromResult(string.Empty));
    }

    public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync(long projectId, string triggerSource, CancellationToken ct = default)
    {
        var logicalRequestId = BuildMergeLogicalRequestKey(projectId);
        var manual = triggerSource == RVideoProjectFinalizationContracts.TriggerManual;
        if (manual && _currentUser()?.IsAuthenticated != true)
            return NotEnqueued("unauthorized", logicalRequestId);
        var project = await _getProject(projectId, ct);
        if (project is null)
        {
            return NotEnqueued("project_missing", logicalRequestId);
        }
        if (manual && project.UserId != _currentUser()!.UserId)
            return NotEnqueued("unauthorized", logicalRequestId);
        if (manual)
        {
            var versioning = await _versions.GetFinalVideoVersioningStateAsync(ct);
            if (!versioning.Ready) return NotEnqueued(versioning.Reason, logicalRequestId);
        }

        if (await _hasOpenVideoGeneration(projectId, ct))
        {
            if (triggerSource != RVideoProjectFinalizationContracts.TriggerAuto)
                await _event(projectId, "PROJECT_FINAL_MERGE_NOT_READY", "info",
                    "Final merge is waiting for current video generations to complete.",
                    new { projectId, triggerSource, reason = "current_generation_pending" }, ct);
            return NotEnqueued("current_generation_pending", logicalRequestId);
        }
        var settings = await _getSettings(projectId, ct);
        if (settings is null)
        {
            return NotEnqueued("settings_missing", logicalRequestId);
        }

        if (project.Scenes.Count == 0)
        {
            return NotEnqueued("no_scenes", logicalRequestId);
        }

        if (!manual && !string.IsNullOrWhiteSpace(project.FinalVideoUrl)
            && await IsCurrentFinalVideoAsync(project.Id, project.Scenes, ct))
        {
            await _sync(project.Id, RVideoStages.Result, project.Status, ct);
            return NotEnqueued("already_final", logicalRequestId);
        }

        var readiness = new List<RVideoSceneReadiness>(project.Scenes.Count);
        foreach (var scene in project.Scenes)
        {
            var selectedVideo = await _versions.GetSelectedVideoVersionAsync(scene.Id, ct);
            var selectedAudio = await _versions.GetSelectedAudioVersionAsync(scene.Id, ct);
            readiness.Add(RVideoRules.GetSceneReadiness(scene, settings, selectedVideo, selectedAudio));
        }
        var missing = readiness.Where(x => !x.VideoReady || !x.MuxReady).ToList();
        if (missing.Count > 0)
        {
            await _event(project.Id, "PROJECT_FINAL_MERGE_NOT_READY", "info",
                "Final merge is waiting for scene readiness.",
                new
                {
                    projectId = project.Id,
                    voiceMode = RVideoRules.ResolveVoiceMode(settings),
                    missingSceneIds = missing.Select(x => x.SceneId).ToArray(),
                    reasons = missing
                }, ct);
            return NotEnqueued("not_ready", logicalRequestId);
        }

        var mergeSnapshot = await _getMergeSnapshot(project.Id, ct);
        var sceneOrder = project.Scenes.OrderBy(s => s.SceneIndex).Select(s => s.Id).ToArray();
        var mergeSelectionKey = BuildMergeSelectionKey(mergeSnapshot, sceneOrder);
        var (job, alreadyActive) = await _jobs.EnqueueForLogCodeIfNoneActiveAsync(new RenderJobCreateModel
        {
            JobType = RenderJobTypes.MergeProjectVideo,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
            // Manual recompose must not be deduplicated against the completed selected merge of the same selection.
            Input = new { projectId = project.Id, logicalRequestId = $"{logicalRequestId}:{Guid.NewGuid():N}", source = triggerSource, mergeSnapshot, sceneOrder, mergeSelectionKey = manual ? null : mergeSelectionKey },
            Prompt = new { projectId = project.Id, source = triggerSource, stage = "merge", voiceMode = RVideoRules.ResolveVoiceMode(settings) },
            References = Array.Empty<object>(),
            LogCode = logicalRequestId,
            ProviderCode = "ffmpeg",
            ModelCode = "copy_concat",
            MaxAttempts = 3,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        }, logicalRequestId, ct);

        if (alreadyActive && job.Status == RenderJobStatuses.Completed)
        {
            await _sync(project.Id, RVideoStages.Result, VideoProjectStatuses.Completed, ct);
            return NotEnqueued("already_final", logicalRequestId);
        }
        if (alreadyActive)
        {
            await _sync(project.Id, RVideoStages.Result, VideoProjectStatuses.Merging, ct);
            return new(false, true, "already_active", job.Id, logicalRequestId);
        }

        await _event(project.Id, "PROJECT_MERGE_AUTO_ENQUEUED", "info",
            "Project final merge auto-enqueued.",
            new { projectId = project.Id, renderJobId = job.Id, logicalRequestId, voiceMode = RVideoRules.ResolveVoiceMode(settings), triggerSource, mergeSnapshot, mergeSelectionKey }, ct);
        await _sync(project.Id, RVideoStages.Result, VideoProjectStatuses.Merging, ct);
        return new(true, false, "enqueued", job.Id, logicalRequestId);
    }

    internal static string BuildMergeSelectionKey(string mergeSnapshot, IEnumerable<long> sceneOrder)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { mergeSnapshot, sceneOrder = sceneOrder.ToArray() }))));

    public static string BuildMergeLogicalRequestKey(long projectId)
        => $"rvideo-final-merge:{projectId}";

    public async Task<bool> IsCurrentFinalVideoAsync(long projectId, CancellationToken ct = default)
    {
        var project = await _getProject(projectId, ct);
        return project is not null && !string.IsNullOrWhiteSpace(project.FinalVideoUrl)
            && await IsCurrentFinalVideoAsync(projectId, project.Scenes, ct);
    }

    private async Task<bool> IsCurrentFinalVideoAsync(long projectId, IReadOnlyCollection<VideoProjectSceneDto> scenes, CancellationToken ct)
    {
        var versions = await _versions.ListFinalVideoVersionsAsync(projectId, ct: ct);
        var current = versions.FirstOrDefault(x => x.IsSelected && string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            return false;
        }

        var items = await _versions.ListFinalVideoVersionItemsAsync(current.Id, ct);
        var selectedVersions = new List<Guid>(scenes.Count);
        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            var selected = await _versions.GetSelectedVideoVersionAsync(scene.Id, ct);
            if (selected is null)
            {
                return false;
            }
            selectedVersions.Add(selected.Id);
        }

        return items.Count == selectedVersions.Count
               && items.OrderBy(x => x.ItemOrder).Select(x => x.SceneVideoVersionId).SequenceEqual(selectedVersions);
    }

    private static RVideoProjectFinalizationResult NotEnqueued(string reason, string logicalRequestId)
        => new(false, false, reason, null, logicalRequestId);
}
