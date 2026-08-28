using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneAudioMuxHandler : IRenderJobHandler
{
    public const string JobTypeName = RenderJobTypes.RenderSceneAudioMux;
    public string JobType => JobTypeName;

    private readonly ILogger<SceneAudioMuxHandler> _logger;
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    private readonly VideoRenderRepository _repo;
    private readonly IWebHostEnvironment _env;
    private readonly ISceneMediaVersioningService _versions;
    private readonly IMediaFileService _media;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _configuration;

    public SceneAudioMuxHandler(
        ILogger<SceneAudioMuxHandler> logger,
        IOptionsMonitor<VideoRenderOptions> options,
        VideoRenderRepository repo,
        IWebHostEnvironment env,
        ISceneMediaVersioningService versions,
        IMediaFileService media,
        TenantContext tenant,
        IConfiguration configuration)
    {
        _logger = logger;
        _options = options;
        _repo = repo;
        _env = env;
        _versions = versions;
        _media = media;
        _tenant = tenant;
        _configuration = configuration;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<SceneAudioMuxWorkItemInput>(job.InputJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Scene audio mux input invalid.");
        if (input.ProjectId <= 0 || input.SceneId <= 0 || input.SceneVideoVersionId == Guid.Empty || input.AudioVersionId == Guid.Empty)
        {
            throw new InvalidOperationException("Missing scene audio mux snapshot.");
        }

        var project = await _repo.GetProjectAsync(input.ProjectId, ct)
            ?? throw new InvalidOperationException("Video project not found.");
        var scene = project.Scenes.FirstOrDefault(x => x.Id == input.SceneId)
            ?? throw new InvalidOperationException("Scene not found.");
        var sceneVideo = (await _versions.ListSceneVideoVersionsAsync(scene.Id, 0, 100, ct))
            .FirstOrDefault(x => x.Id == input.SceneVideoVersionId)
            ?? throw new InvalidOperationException("Scene video version not found.");
        var sceneAudio = (await _versions.ListSceneAudioVersionsAsync(scene.Id, 0, 100, ct))
            .FirstOrDefault(x => x.Id == input.AudioVersionId)
            ?? throw new InvalidOperationException("Scene audio version not found.");
        ValidateSceneOwnership(scene.Id, sceneVideo, sceneAudio);

        if (!string.Equals(sceneVideo.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(sceneAudio.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new RenderJobDeferredException("Scene video/audio is not ready for mux yet.");
        }

        var root = ResolveRoot(_options.CurrentValue.StorageRoot);
        var projectRoot = Path.Combine(root, project.JobFolder);
        var finalDir = Path.Combine(projectRoot, "final-scenes", scene.SceneIndex.ToString("00"));
        Directory.CreateDirectory(finalDir);
        var finalPath = Path.Combine(finalDir, "final.mp4");
        var sourceVideoPath = ResolveLocalPath(sceneVideo.SourceFilePath);
        var audioPath = ResolveLocalPath(sceneAudio.SourceFilePath);
        if (string.IsNullOrWhiteSpace(sourceVideoPath) || !File.Exists(sourceVideoPath))
        {
            throw new InvalidOperationException("Source scene video is missing.");
        }
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            throw new InvalidOperationException("Source scene audio is missing.");
        }

        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_MUX_STARTED", "info",
            $"Scene {scene.SceneIndex} external voice mux started.",
            new
            {
                projectId = project.Id,
                sceneId = scene.Id,
                scene.SceneIndex,
                input.SceneVideoVersionId,
                input.AudioVersionId,
                input.LogicalRequestId,
                sourceVideoPath,
                audioPath
            }, ct);

        var ffmpegPath = _options.CurrentValue.FfmpegPath;
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = finalDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(sourceVideoPath);
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(audioPath);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("0:v:0");
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("1:a:0");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("copy");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-b:a");
        psi.ArgumentList.Add("192k");
        psi.ArgumentList.Add("-shortest");
        if (scene.DurationSeconds > 0)
        {
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(scene.DurationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        psi.ArgumentList.Add(finalPath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Khong khoi dong duoc FFmpeg.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.MergeTimeoutMinutes)));
        await process.WaitForExitAsync(timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "ffmpeg.log"), stdout + Environment.NewLine + stderr, ct);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg mux failed. ExitCode={process.ExitCode}");
        }

        var relative = Path.GetRelativePath(root, finalPath).Replace(Path.DirectorySeparatorChar, '/');
        var url = $"{_options.CurrentValue.PublicBase.TrimEnd('/')}/{relative}";
        await _tenant.EnsureLoadedAsync(ct);
        var mediaObjectKey = BuildMuxMediaObjectKey(relative);
        var muxedMedia = await _media.SaveBinaryAtObjectKeyAsync(
            await File.ReadAllBytesAsync(finalPath, ct),
            mediaObjectKey,
            Path.GetFileName(finalPath),
            "video/mp4",
            "video_scene_mux",
            project.UserId,
            project.CustomerId,
            _tenant.TenantId,
            ct);
        var muxedUrl = muxedMedia.PublicUrl ?? muxedMedia.FileUrl ?? url;
        var muxedPath = ResolveMediaPhysicalPath(muxedMedia.ObjectKey) ?? finalPath;
        var completion = BuildCompletionRequest(sceneVideo, input.AudioVersionId, muxedUrl, muxedPath, scene.DurationSeconds, muxedMedia.Id);
        await _versions.CompleteSceneVideoVersionAsync(sceneVideo.Id, completion, ct);

        await _repo.AddProjectEventAsync(project.Id, "SCENE_AUDIO_MUX_COMPLETED", "info",
            $"Scene {scene.SceneIndex} external voice mux completed.",
            new
            {
                projectId = project.Id,
                sceneId = scene.Id,
                scene.SceneIndex,
                rawSceneVideoVersionId = input.SceneVideoVersionId,
                muxedSceneVideoVersionId = sceneVideo.Id,
                rawMediaId = sceneVideo.ResultMediaId,
                muxedMediaId = muxedMedia.Id,
                audioVersionId = input.AudioVersionId,
                input.LogicalRequestId,
                sourceVideoPath,
                finalPath = muxedPath,
                finalUrl = muxedUrl
            }, ct);

        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_READY", "info",
            $"Scene {scene.SceneIndex} external voice final video ready.",
            new
            {
                projectId = project.Id,
                sceneId = scene.Id,
                scene.SceneIndex,
                input.SceneVideoVersionId,
                input.AudioVersionId,
                input.LogicalRequestId,
                sourceVideoPath,
                finalPath = muxedPath,
                url = muxedUrl
            }, ct);
    }

    private string ResolveLocalPath(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(objectKey))
        {
            return objectKey;
        }

        var uploadRoot = _options.CurrentValue.StorageRoot;
        return Path.Combine(_env.ContentRootPath, uploadRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }

    private string ResolveRoot(string? path)
        => Path.IsPathRooted(path) ? path! : Path.Combine(_env.ContentRootPath, path ?? string.Empty);

    private static string BuildMuxMediaObjectKey(string videoRenderRelativePath)
        => $"video-render/registered/{videoRenderRelativePath.TrimStart('/')}";

    private string? ResolveMediaPhysicalPath(string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        var uploadRoot = _configuration["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        return Path.Combine(_env.ContentRootPath, uploadRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }

    internal static void ValidateSceneOwnership(long sceneId, SceneVideoVersionDto sceneVideo, SceneAudioVersionDto sceneAudio)
    {
        if (sceneVideo.SceneId != sceneId || sceneAudio.SceneId != sceneId || sceneVideo.ProjectId != sceneAudio.ProjectId)
        {
            throw new InvalidOperationException("SCENE_AUDIO_MUX_SCENE_ID_MISMATCH");
        }
    }

    internal static SceneVideoVersionCompleteRequest BuildCompletionRequest(
        SceneVideoVersionDto sceneVideo,
        Guid voiceAudioVersionId,
        string finalUrl,
        string finalPath,
        int sceneDurationSeconds,
        Guid? resultMediaId = null)
        => new(
            finalUrl,
            finalPath,
            PosterUrl: sceneVideo.PosterUrl,
            DurationSeconds: sceneVideo.DurationSeconds ?? sceneDurationSeconds,
            MimeType: "video/mp4",
            VoiceAudioVersionId: voiceAudioVersionId,
            ProviderCode: sceneVideo.ProviderCode,
            ModelName: sceneVideo.ModelName,
            ProviderCapabilityId: sceneVideo.ProviderCapabilityId,
            ProviderTaskId: sceneVideo.ProviderTaskId,
            BillingLogicalRequestId: sceneVideo.BillingLogicalRequestId,
            EstimatedUsd: sceneVideo.EstimatedUsd,
            ActualUsd: sceneVideo.ActualUsd,
            ChargedPoints: sceneVideo.ChargedPoints,
            RefundedPoints: sceneVideo.RefundedPoints,
            CostSource: sceneVideo.CostSource,
            AspectRatio: sceneVideo.AspectRatio,
            ResultMediaId: resultMediaId);
}
