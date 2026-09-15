using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;
using Microsoft.Extensions.Options;

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
    private readonly IWebHostEnvironment _env;
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    private readonly LocalMediaPathResolver _localMediaPaths;
    private readonly ILogger<RVideoSceneMediaFinalizerService> _logger;

    public RVideoSceneMediaFinalizerService(
        VideoRenderRepository repo,
        ISceneMediaVersioningService versions,
        RVideoJobSettingsRepository settings,
        IRenderJobService jobs,
        IWebHostEnvironment env,
        IOptionsMonitor<VideoRenderOptions> options,
        LocalMediaPathResolver localMediaPaths,
        ILogger<RVideoSceneMediaFinalizerService> logger)
    {
        _repo = repo;
        _versions = versions;
        _settings = settings;
        _jobs = jobs;
        _env = env;
        _options = options;
        _localMediaPaths = localMediaPaths;
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
            && FinalMuxMediaExists(selectedVideo))
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

    internal static bool ShouldSkipMux(SceneVideoVersionDto selectedVideo, SceneAudioVersionDto selectedAudio, string contentRootPath, string storageRoot)
        => selectedVideo.VoiceAudioVersionId == selectedAudio.Id
           && IsMuxOutputPath(selectedVideo.SourceFilePath)
           && !string.IsNullOrWhiteSpace(selectedVideo.PublicUrl)
           && !string.IsNullOrWhiteSpace(selectedVideo.SourceFilePath)
           && File.Exists(ResolveLocalPath(selectedVideo.SourceFilePath, contentRootPath, storageRoot));

    private bool FinalMuxMediaExists(SceneVideoVersionDto selectedVideo)
        => !string.IsNullOrWhiteSpace(selectedVideo.PublicUrl)
           && !string.IsNullOrWhiteSpace(selectedVideo.SourceFilePath)
           && IsMuxOutputPath(selectedVideo.SourceFilePath)
           && _localMediaPaths.TryResolveExistingFile(
               selectedVideo.SourceFilePath,
               LocalMediaPathSource.SourceFilePath,
               out _);

    internal static bool IsMuxOutputPath(string? path)
        => !string.IsNullOrWhiteSpace(path)
           && path.Replace('\\', '/').Contains("/final-scenes/", StringComparison.OrdinalIgnoreCase);

    private string ResolveLocalPath(string objectKeyOrPath)
    {
        if (Path.IsPathRooted(objectKeyOrPath))
        {
            return objectKeyOrPath;
        }

        var storageRoot = _options.CurrentValue.StorageRoot;
        var root = Path.IsPathRooted(storageRoot)
            ? storageRoot
            : Path.Combine(_env.ContentRootPath, storageRoot ?? string.Empty);
        return Path.Combine(root, objectKeyOrPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ResolveLocalPath(string objectKeyOrPath, string contentRootPath, string storageRoot)
    {
        if (Path.IsPathRooted(objectKeyOrPath))
        {
            return objectKeyOrPath;
        }

        var root = Path.IsPathRooted(storageRoot)
            ? storageRoot
            : Path.Combine(contentRootPath, storageRoot ?? string.Empty);
        return Path.Combine(root, objectKeyOrPath.Replace('/', Path.DirectorySeparatorChar));
    }
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
