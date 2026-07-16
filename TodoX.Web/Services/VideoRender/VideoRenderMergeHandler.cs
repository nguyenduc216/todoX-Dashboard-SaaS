using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderMergeHandler : IRenderJobHandler
{
    public const string JobTypeName = "merge_video_job";
    public string JobType => JobTypeName;

    private readonly ILogger<VideoRenderMergeHandler> _logger;
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    private readonly VideoRenderRepository _repo;
    private readonly IWebHostEnvironment _env;
    private readonly ISceneMediaVersioningService _versions;

    public VideoRenderMergeHandler(ILogger<VideoRenderMergeHandler> logger, IOptionsMonitor<VideoRenderOptions> options, VideoRenderRepository repo, IWebHostEnvironment env, ISceneMediaVersioningService versions)
    {
        _logger = logger;
        _options = options;
        _repo = repo;
        _env = env;
        _versions = versions;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var projectId = TryReadLong(job.InputJson, "projectId") ?? TryReadLong(job.PromptJson, "projectId") ?? throw new InvalidOperationException("Thieu projectId trong job input.");
        var project = await _repo.GetProjectAsync(projectId, ct) ?? throw new InvalidOperationException("Khong tim thay project video.");
        if (!project.Scenes.Any() || project.Scenes.Any(x => x.Status != VideoSceneStatuses.VideoReady))
        {
            throw new InvalidOperationException("Chua du scene video ready de merge.");
        }

        await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Merging, errorMessage: null, ct: ct);
        var root = ResolveRoot(_options.CurrentValue.StorageRoot);
        var projectRoot = Path.Combine(root, project.JobFolder);
        var versioningEnabled = await _versions.IsEnabledAsync(SceneMediaVersioningFlags.FinalVideos, ct);
        FinalVideoVersionDto? version = null;
        if (versioningEnabled)
        {
            version = await _versions.CreateQueuedFinalVideoVersionAsync(new FinalVideoVersionCreateRequest(
                project.Id,
                project.UserId,
                project.CustomerId,
                job.Id,
                $"final-video-job-{job.Id:N}-project-{project.Id}",
                CompositionConfigSnapshot: new { source = "merge_video_job", sceneCount = project.Scenes.Count, scenes = project.Scenes.OrderBy(x => x.SceneIndex).Select(x => new { x.Id, x.SceneIndex, x.SceneVideoPath }) },
                TransitionConfigSnapshot: new { mode = "copy_concat" },
                AudioConfigSnapshot: new { },
                SubtitleConfigSnapshot: new { }), ct);
        }

        try
        {
            var finalDir = version is null
                ? Path.Combine(projectRoot, "final")
                : Path.Combine(projectRoot, "final-videos", version.Id.ToString("N"), "output");
            Directory.CreateDirectory(finalDir);
            var concat = Path.Combine(finalDir, "concat.txt");
            var finalPath = Path.Combine(finalDir, version is null ? "final.mp4" : "final-video.mp4");
            var mergeItems = version is null
                ? project.Scenes.OrderBy(x => x.SceneIndex).Select(scene => new MergeInput(scene.Id, scene.SceneIndex, null, scene.SceneVideoPath)).ToList()
                : (await _versions.ListFinalVideoVersionItemsAsync(version.Id, ct))
                    .Select(item => new MergeInput(item.SceneId, item.ItemOrder, item.SceneVideoVersionId, item.SourceFilePath))
                    .ToList();
            ValidateMergeInputs(mergeItems);
            var lines = mergeItems.Select(item => $"file '{Path.GetFullPath(item.VideoPath ?? string.Empty).Replace("'", "''")}'").ToArray();
            await File.WriteAllLinesAsync(concat, lines, Encoding.UTF8, ct);
            await WriteCompositionManifestAsync(finalDir, version, mergeItems, ct);

            var ffmpegPath = _options.CurrentValue.FfmpegPath;
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
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

            var relative = Path.GetRelativePath(root, finalPath).Replace(Path.DirectorySeparatorChar, '/');
            var url = $"{_options.CurrentValue.PublicBase.TrimEnd('/')}/{relative}";
            if (version is not null)
            {
                await _versions.CompleteFinalVideoVersionAsync(version.Id, new FinalVideoVersionCompleteRequest(
                    url,
                    finalPath,
                    PosterUrl: null,
                    DurationSeconds: project.Scenes.Sum(x => (decimal)x.DurationSeconds),
                    "video/mp4"), ct);
            }

            await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Completed, url, finalPath, null, ct);
            await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGED", "info", "Final video merged.", new { finalPath, url }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version is not null)
            {
                await _versions.FailFinalVideoVersionAsync(version.Id, ex.GetType().Name, ex.Message, ct);
            }

            await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Failed, errorMessage: ex.Message, ct: ct);
            await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGE_FAILED", "error", "Final video merge failed.", new { versionId = version?.Id, error = ex.Message }, ct);
            throw;
        }
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
