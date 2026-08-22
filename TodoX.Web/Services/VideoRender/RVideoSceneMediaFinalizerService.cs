using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public interface IRVideoSceneMediaFinalizerService
{
    Task<bool> TryFinalizeSceneMediaAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default);
}

public sealed class RVideoSceneMediaFinalizerService : IRVideoSceneMediaFinalizerService
{
    private readonly VideoRenderRepository _repo;
    private readonly ISceneMediaVersioningService _versions;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRenderJobService _jobs;
    private readonly ILogger<RVideoSceneMediaFinalizerService> _logger;

    public RVideoSceneMediaFinalizerService(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        RVideoJobSettingsRepository settings,
        IRenderJobService jobs,
        ILogger<RVideoSceneMediaFinalizerService> logger)
    {
        _repo = repo;
        _versions = versions;
        _settings = settings;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<bool> TryFinalizeSceneMediaAsync(long projectId, long sceneId, string triggerSource, CancellationToken ct = default)
    {
        var project = await _repo.GetProjectAsync(projectId, ct);
        if (project is null)
        {
            _logger.LogWarning("RVIDEO_FINALIZER_PROJECT_MISSING projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        var scene = project.Scenes.FirstOrDefault(x => x.Id == sceneId);
        if (scene is null)
        {
            _logger.LogWarning("RVIDEO_FINALIZER_SCENE_MISSING projectId={ProjectId} sceneId={SceneId}", projectId, sceneId);
            return false;
        }

        var settings = await _settings.GetAsync(projectId, ct);
        if (!RVideoRules.RequiresExternalVoice(scene, settings))
        {
            return false;
        }

        var selectedVideo = await _versions.GetSelectedVideoVersionAsync(sceneId, ct);
        var selectedAudio = await _versions.GetSelectedAudioVersionAsync(sceneId, ct);
        if (selectedVideo is null || selectedAudio is null)
        {
            return false;
        }

        if (!string.Equals(selectedVideo.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(selectedAudio.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (selectedVideo.VoiceAudioVersionId == selectedAudio.Id
            && string.Equals(selectedVideo.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(selectedVideo.PublicUrl)
            && selectedVideo.PublicUrl.Contains("/audio/", StringComparison.OrdinalIgnoreCase) == false)
        {
            return false;
        }

        var logicalRequestId = BuildLogicalRequestKey(projectId, sceneId);
        var model = new RenderJobCreateModel
        {
            JobType = RenderJobTypes.RenderSceneAudioMux,
            UserId = project.UserId,
            CustomerId = project.CustomerId,
            Input = new SceneAudioMuxWorkItemInput
            {
                ProjectId = project.Id,
                SceneId = scene.Id,
                SceneIndex = scene.SceneIndex,
                SceneVideoVersionId = selectedVideo.Id,
                AudioVersionId = selectedAudio.Id,
                LogicalRequestId = logicalRequestId,
                UserId = project.UserId,
                CustomerId = project.CustomerId,
                TriggerSource = triggerSource
            },
            Prompt = new
            {
                projectId,
                sceneId,
                source = triggerSource,
                stage = "mux"
            },
            References = Array.Empty<object>(),
            LogCode = logicalRequestId,
            ProviderCode = "ffmpeg",
            ModelCode = "copy_concat",
            MaxAttempts = 3,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired
        };

        var (job, alreadyActive) = await _jobs.EnqueueForLogCodeIfNoneActiveAsync(model, logicalRequestId, ct);
        if (alreadyActive)
        {
            return false;
        }

        await _repo.AddProjectEventAsync(projectId, "SCENE_AUDIO_MUX_QUEUED", "info",
            $"Scene {scene.SceneIndex} audio mux queued.",
            new
            {
                projectId,
                sceneId,
                scene.SceneIndex,
                triggerSource,
                logicalRequestId,
                jobId = job.Id,
                sceneVideoVersionId = selectedVideo.Id,
                audioVersionId = selectedAudio.Id
            }, ct);

        return true;
    }

    public static string BuildLogicalRequestKey(long projectId, long sceneId)
        => $"rvideo-auto-mux:{projectId}:{sceneId}";
}

public sealed class SceneAudioMuxWorkItemInput
{
    public long ProjectId { get; set; }
    public long SceneId { get; set; }
    public int SceneIndex { get; set; }
    public Guid SceneVideoVersionId { get; set; }
    public Guid AudioVersionId { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? TriggerSource { get; set; }
}

