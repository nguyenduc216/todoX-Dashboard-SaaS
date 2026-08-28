using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoProjectFinalizationService
{
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
    private readonly VideoRenderRepository _repo;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IRenderJobService _jobs;
    private readonly IRVideoJobService _rvideoJobs;

    public RVideoProjectFinalizationService(
        VideoRenderRepository repo,
        RVideoJobSettingsRepository settings,
        ISceneMediaVersioningService versions,
        IRenderJobService jobs,
        IRVideoJobService rvideoJobs)
    {
        _repo = repo;
        _settings = settings;
        _versions = versions;
        _jobs = jobs;
        _rvideoJobs = rvideoJobs;
    }

    public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync(long projectId, string triggerSource, CancellationToken ct = default)
    {
        var logicalRequestId = BuildMergeLogicalRequestKey(projectId);
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            return NotEnqueued("project_missing", logicalRequestId);
        }

        var settings = await _settings.GetAsync(projectId, ct);
        if (settings is null)
        {
            return NotEnqueued("settings_missing", logicalRequestId);
        }

        if (project.Scenes.Count == 0)
        {
            return NotEnqueued("no_scenes", logicalRequestId);
        }

        if (!string.IsNullOrWhiteSpace(project.FinalVideoUrl))
        {
            await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, project.Status, ct);
            return NotEnqueued("already_final", logicalRequestId);
        }

        foreach (var scene in project.Scenes)
        {
            var selectedVideo = await _versions.GetSelectedVideoVersionAsync(scene.Id, ct);
            var selectedAudio = await _versions.GetSelectedAudioVersionAsync(scene.Id, ct);
            if (!RVideoRules.IsSceneFinalReady(scene, settings, selectedVideo, selectedAudio))
            {
                return NotEnqueued("not_ready", logicalRequestId);
            }
        }

        var (job, alreadyActive) = await _jobs.EnqueueForLogCodeIfNoneActiveAsync(new RenderJobCreateModel
        {
            JobType = RenderJobTypes.MergeProjectVideo,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
            Input = new { projectId = project.Id, logicalRequestId, source = triggerSource },
            Prompt = new { projectId = project.Id, source = triggerSource, stage = "merge", voiceMode = RVideoRules.ResolveVoiceMode(settings) },
            References = Array.Empty<object>(),
            LogCode = logicalRequestId,
            ProviderCode = "ffmpeg",
            ModelCode = "copy_concat",
            MaxAttempts = 3,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        }, logicalRequestId, ct);

        if (alreadyActive)
        {
            await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Merging, ct);
            return new(false, true, "already_active", job.Id, logicalRequestId);
        }

        await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGE_AUTO_ENQUEUED", "info",
            "Project final merge auto-enqueued.",
            new { projectId = project.Id, renderJobId = job.Id, logicalRequestId, voiceMode = RVideoRules.ResolveVoiceMode(settings), triggerSource }, ct);
        await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Merging, ct);
        return new(true, false, "enqueued", job.Id, logicalRequestId);
    }

    public static string BuildMergeLogicalRequestKey(long projectId)
        => $"rvideo-final-merge:{projectId}";

    private static RVideoProjectFinalizationResult NotEnqueued(string reason, string logicalRequestId)
        => new(false, false, reason, null, logicalRequestId);
}
