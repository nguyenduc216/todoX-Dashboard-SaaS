using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderMockHandler : IRenderJobHandler
{
    public const string JobTypeName = "render_video_job";
    public string JobType => JobTypeName;

    private readonly ILogger<VideoRenderMockHandler> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    private readonly VideoRenderRepository _repo;
    private readonly IMediaFileService _media;
    private readonly TenantContext _tenant;
    private readonly ISceneMediaVersioningService _versions;

    public VideoRenderMockHandler(ILogger<VideoRenderMockHandler> logger, IWebHostEnvironment env, IOptionsMonitor<VideoRenderOptions> options, VideoRenderRepository repo, IMediaFileService media, TenantContext tenant, ISceneMediaVersioningService versions)
    {
        _logger = logger;
        _env = env;
        _options = options;
        _repo = repo;
        _media = media;
        _tenant = tenant;
        _versions = versions;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var input = JsonSerializer.Deserialize<VideoProjectCreateRequest>(job.InputJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Video job input invalid.");

        var projectId = TryReadLong(job.InputJson, "projectId") ?? TryReadLong(job.PromptJson, "projectId") ?? 0;
        if (projectId <= 0)
        {
            throw new InvalidOperationException("Thieu projectId trong job input.");
        }
        var options = _options.CurrentValue;
        var storageRoot = ResolveRoot(options.StorageRoot);
        var publicBase = options.PublicBase.TrimEnd('/');
        var projectFolder = projectId <= 0 ? job.Id.ToString("N") : projectId.ToString();
        var projectRoot = Path.Combine(storageRoot, projectFolder);
        Directory.CreateDirectory(projectRoot);

        var mockSource = ResolvePath(options.MockSceneVideoPath);
        if (string.IsNullOrWhiteSpace(mockSource) || !File.Exists(mockSource))
        {
            throw new FileNotFoundException("MockSceneVideoPath khong ton tai. Hay cau hinh file video mau.", mockSource);
        }

        var project = await _repo.GetProjectAsync(projectId, ct)
            ?? throw new InvalidOperationException("Khong tim thay project video.");

        var scenes = project.Scenes.OrderBy(x => x.SceneIndex).ToList();
        if (scenes.Count == 0)
        {
            throw new InvalidOperationException("Project chua co scene nao.");
        }

        await _repo.AddProjectEventAsync(projectId, "JOB_STARTED", "info", "Video job mock started.", new { job.Id, job.JobType, job.Status }, ct);
        await _repo.UpdateProjectAsync(projectId, VideoProjectStatuses.Rendering, errorMessage: null, ct: ct);

        foreach (var scene in scenes)
        {
            await RenderSceneMockAsync(project, scene, mockSource, projectRoot, publicBase, job.Id, ct);
        }

        await MergeAsync(project, scenes, projectRoot, publicBase, job.Id, ct);
    }

    private async Task RenderSceneMockAsync(VideoProjectDto project, VideoProjectSceneDto scene, string mockSource, string projectRoot, string publicBase, Guid jobId, CancellationToken ct)
    {
        var versioningEnabled = await _versions.IsEnabledAsync(SceneMediaVersioningFlags.SceneVideos, ct);
        SceneVideoVersionDto? version = null;
        if (versioningEnabled)
        {
            var selectedImage = await _versions.GetSelectedImageVersionAsync(scene.Id, ct);
            version = await _versions.CreateQueuedSceneVideoVersionAsync(new SceneVideoVersionCreateRequest(
                project.Id,
                scene.Id,
                selectedImage?.Id,
                project.UserId,
                project.CustomerId,
                jobId,
                $"scene-video-job-{jobId:N}-scene-{scene.Id}",
                scene.ImagePrompt,
                scene.VideoPrompt,
                SceneSnapshot: new
                {
                    scene.Id,
                    scene.ProjectId,
                    scene.SceneIndex,
                    scene.Title,
                    scene.DurationSeconds,
                    scene.ScenePrompt,
                    scene.ImagePrompt,
                    scene.VideoPrompt,
                    sourceImageVersionId = selectedImage?.Id
                },
                RenderConfigSnapshot: new { source = "mock_scene_video", mockSource = Path.GetFileName(mockSource) }), ct);
        }

        var sceneFolder = version is null
            ? Path.Combine(projectRoot, $"scene_{scene.SceneIndex:00}")
            : Path.Combine(projectRoot, "scenes", scene.Id.ToString(), "videos", version.Id.ToString("N"), "output");
        Directory.CreateDirectory(sceneFolder);
        var targetVideo = Path.Combine(sceneFolder, version is null ? "scene.mp4" : "scene-video.mp4");
        File.Copy(mockSource, targetVideo, true);
        var relative = Path.GetRelativePath(ResolveRoot(_options.CurrentValue.StorageRoot), targetVideo).Replace(Path.DirectorySeparatorChar, '/');
        var url = $"{publicBase}/{relative}";
        if (version is not null)
        {
            await _versions.CompleteSceneVideoVersionAsync(version.Id, new SceneVideoVersionCompleteRequest(
                url,
                targetVideo,
                PosterUrl: null,
                scene.DurationSeconds,
                "video/mp4"), ct);
        }

        await _repo.UpdateSceneAsync(scene.Id, VideoSceneStatuses.VideoReady, videoUrl: url, videoPath: targetVideo, errorMessage: null, ct: ct);
        await _repo.AddProjectEventAsync(project.Id, "SCENE_VIDEO_READY", "info", $"Scene {scene.SceneIndex} ready.", new { scene.Id, scene.SceneIndex, url }, ct);
    }

    private async Task MergeAsync(VideoProjectDto project, List<VideoProjectSceneDto> scenes, string projectRoot, string publicBase, Guid jobId, CancellationToken ct)
    {
        var versioningEnabled = await _versions.IsEnabledAsync(SceneMediaVersioningFlags.FinalVideos, ct);
        FinalVideoVersionDto? version = null;
        if (versioningEnabled)
        {
            version = await _versions.CreateQueuedFinalVideoVersionAsync(new FinalVideoVersionCreateRequest(
                project.Id,
                project.UserId,
                project.CustomerId,
                jobId,
                $"final-video-job-{jobId:N}-project-{project.Id}",
                CompositionConfigSnapshot: new { source = "mock_merge", sceneCount = scenes.Count, scenes = scenes.Select(x => new { x.Id, x.SceneIndex, x.SceneVideoPath }) },
                TransitionConfigSnapshot: new { mode = "copy_concat" },
                AudioConfigSnapshot: new { },
                SubtitleConfigSnapshot: new { }), ct);
        }

        var finalDir = version is null
            ? Path.Combine(projectRoot, "final")
            : Path.Combine(projectRoot, "final-videos", version.Id.ToString("N"), "output");
        Directory.CreateDirectory(finalDir);
        var finalPath = Path.Combine(finalDir, version is null ? "final.mp4" : "final-video.mp4");
        var concat = Path.Combine(finalDir, "concat.txt");
        var mergeItems = version is null
            ? scenes.OrderBy(x => x.SceneIndex).Select(scene => new MergeInput(scene.Id, scene.SceneIndex, null, scene.SceneVideoPath)).ToList()
            : (await _versions.ListFinalVideoVersionItemsAsync(version.Id, ct))
                .Select(item => new MergeInput(item.SceneId, item.ItemOrder, item.SceneVideoVersionId, item.SourceFilePath))
                .ToList();
        ValidateMergeInputs(mergeItems);
        var lines = mergeItems.Select(item => $"file '{Path.GetFullPath(item.VideoPath ?? string.Empty).Replace("'", "''")}'").ToArray();
        await File.WriteAllLinesAsync(concat, lines, Encoding.UTF8, ct);
        await WriteCompositionManifestAsync(finalDir, version, mergeItems, ct);

        var ffmpeg = _options.CurrentValue.FfmpegPath;
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = $"-y -f concat -safe 0 -i \"{concat}\" -c copy \"{finalPath}\"",
            WorkingDirectory = finalDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Khong khoi dong duoc FFmpeg.");
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(finalDir, "ffmpeg.log"), stdout + Environment.NewLine + stderr, ct);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg merge failed. ExitCode={process.ExitCode}");
        }

        var relative = Path.GetRelativePath(ResolveRoot(_options.CurrentValue.StorageRoot), finalPath).Replace(Path.DirectorySeparatorChar, '/');
        var url = $"{publicBase}/{relative}";
        if (version is not null)
        {
            await _versions.CompleteFinalVideoVersionAsync(version.Id, new FinalVideoVersionCompleteRequest(
                url,
                finalPath,
                PosterUrl: null,
                DurationSeconds: scenes.Sum(x => (decimal)x.DurationSeconds),
                "video/mp4"), ct);
        }

        await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Completed, url, finalPath, null, ct);
        await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGED", "info", "Final video merged.", new { finalPath, url }, ct);
    }

    private static void ValidateMergeInputs(IReadOnlyList<MergeInput> items)
    {
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Final video version has no scene video items.");
        }

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.VideoPath) || !File.Exists(item.VideoPath))
            {
                throw new InvalidOperationException($"Scene video version input is missing. sceneId={item.SceneId} sceneVideoVersionId={item.SceneVideoVersionId}");
            }
        }
    }

    private static Task WriteCompositionManifestAsync(string finalDir, FinalVideoVersionDto? version, IReadOnlyList<MergeInput> items, CancellationToken ct)
    {
        var manifestPath = Path.Combine(finalDir, "..", "manifests", "composition.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        var json = JsonSerializer.Serialize(new
        {
            finalVideoVersionId = version?.Id,
            createdAtUtc = DateTimeOffset.UtcNow,
            items = items.Select(x => new { x.SceneId, x.ItemOrder, x.SceneVideoVersionId, x.VideoPath })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return File.WriteAllTextAsync(manifestPath, json, ct);
    }

    private sealed record MergeInput(long SceneId, int ItemOrder, Guid? SceneVideoVersionId, string? VideoPath);

    private string ResolveRoot(string? path)
        => Path.IsPathRooted(path) ? path! : Path.Combine(_env.ContentRootPath, path ?? string.Empty);

    private string? ResolvePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : (Path.IsPathRooted(path) ? path : Path.Combine(_env.ContentRootPath, path));

    private static long? TryReadLong(string json, string key)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return doc.RootElement.TryGetProperty(key, out var value) &&
               (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsedId)
                || value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out parsedId))
            ? parsedId
            : null;
    }
}
