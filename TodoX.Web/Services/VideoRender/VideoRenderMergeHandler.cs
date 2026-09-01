using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderMergeHandler : IRenderJobHandler
{
    public const string JobTypeName = RenderJobTypes.MergeProjectVideo;
    public string JobType => JobTypeName;

    private readonly ILogger<VideoRenderMergeHandler> _logger;
    private readonly IOptionsMonitor<VideoRenderOptions> _options;
    private readonly VideoRenderRepository _repo;
    private readonly IWebHostEnvironment _env;
    private readonly ISceneMediaVersioningService _versions;
    private readonly RVideoJobSettingsRepository _settings;
    private readonly IRVideoJobService _rvideoJobs;
    private readonly IConfiguration _configuration;

    public VideoRenderMergeHandler(ILogger<VideoRenderMergeHandler> logger, IOptionsMonitor<VideoRenderOptions> options, VideoRenderRepository repo, IWebHostEnvironment env, ISceneMediaVersioningService versions, RVideoJobSettingsRepository settings, IRVideoJobService rvideoJobs, IConfiguration configuration)
    {
        _logger = logger;
        _options = options;
        _repo = repo;
        _env = env;
        _versions = versions;
        _settings = settings;
        _rvideoJobs = rvideoJobs;
        _configuration = configuration;
    }

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        var projectId = TryReadLong(job.InputJson, "projectId") ?? TryReadLong(job.PromptJson, "projectId") ?? throw new InvalidOperationException("Thieu projectId trong job input.");
        var project = await _repo.GetProjectAsync(projectId, ct) ?? throw new InvalidOperationException("Khong tim thay project video.");
        if (!project.Scenes.Any())
        {
            throw new InvalidOperationException("Project khong co scene de merge.");
        }

        var settings = await _settings.GetAsync(project.Id, ct);
        if (settings is null)
        {
            throw new RenderJobDeferredException("RVideo settings are not ready for merge.");
        }

        foreach (var scene in project.Scenes)
        {
            var selectedVideo = await _versions.GetSelectedVideoVersionAsync(scene.Id, ct);
            var selectedAudio = await _versions.GetSelectedAudioVersionAsync(scene.Id, ct);
            if (!RVideoRules.IsSceneFinalReady(scene, settings, selectedVideo, selectedAudio))
            {
                throw new RenderJobDeferredException($"Scene {scene.SceneIndex} is not final-ready for merge.");
            }
        }

        await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Merging, errorMessage: null, ct: ct);
        var mergeableScenes = project.Scenes
            .Where(x => x.Status == VideoSceneStatuses.VideoReady)
            .OrderBy(x => x.SceneIndex)
            .ToList();
        if (mergeableScenes.Count == 0)
            throw new InvalidOperationException("Chua co scene video ready de merge.");
        var root = ResolveRoot(_options.CurrentValue.StorageRoot);
        var projectRoot = Path.Combine(root, project.JobFolder);
        var versioningEnabled = await _versions.IsEnabledAsync(SceneMediaVersioningFlags.FinalVideos, ct);
        FinalVideoVersionDto? version = null;
        IReadOnlyList<MergeInput> mergeItems;
        if (versioningEnabled)
        {
            version = await _versions.CreateQueuedFinalVideoVersionAsync(new FinalVideoVersionCreateRequest(
                project.Id,
                project.UserId,
                project.CustomerId,
                job.Id,
                $"final-video-job-{job.Id:N}-project-{project.Id}",
                CompositionConfigSnapshot: new { source = "merge_video_job", sceneCount = mergeableScenes.Count, failedSceneCount = project.Scenes.Count - mergeableScenes.Count, scenes = mergeableScenes.Select(x => new { x.Id, x.SceneIndex, x.SceneVideoPath }) },
                TransitionConfigSnapshot: new { mode = "copy_concat" },
                AudioConfigSnapshot: new { music = settings?.MusicSnapshotJson, musicVolume = settings?.MusicVolume },
                SubtitleConfigSnapshot: new { }), ct);
            mergeItems = await BuildCurrentMergeItemsAsync(mergeableScenes, ct);
        }
        else
        {
            mergeItems = await BuildCurrentMergeItemsAsync(mergeableScenes, ct);
        }

        try
        {
            var finalDir = version is null
                ? Path.Combine(projectRoot, "final")
                : Path.Combine(projectRoot, "final-videos", version.Id.ToString("N"), "output");
            Directory.CreateDirectory(finalDir);
            var concat = Path.Combine(finalDir, "concat.txt");
            var finalPath = Path.Combine(finalDir, version is null ? "final.mp4" : "final-video.mp4");
            ValidateMergeInputs(mergeItems);
            var lines = BuildConcatLines(mergeItems.Select(item => item.VideoPath ?? string.Empty));
            await File.WriteAllLinesAsync(concat, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            await WriteCompositionManifestAsync(finalDir, version, mergeItems, ct);

            var ffmpegPath = _options.CurrentValue.FfmpegPath;
            var timeout = TimeSpan.FromMinutes(Math.Max(1, _options.CurrentValue.MergeTimeoutMinutes));
            var copyResult = await RunFfmpegAsync(ffmpegPath, finalDir, BuildCopyConcatArguments(concat, finalPath), timeout, ct);
            await File.WriteAllTextAsync(Path.Combine(finalDir, "ffmpeg-copy.log"), copyResult.ToLogText(), ct);
            if (copyResult.ExitCode != 0)
            {
                await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGE_COPY_FAILED", "warning",
                    "Fast copy concat failed; attempting normalized transcode fallback.",
                    new
                    {
                        projectId = project.Id,
                        finalVideoVersionId = version?.Id,
                        exitCode = copyResult.ExitCode,
                        stderrTail = SafeTail(copyResult.Stderr),
                        inputs = mergeItems.Select(x => new { x.SceneId, x.SceneVideoVersionId }).ToArray()
                    }, ct);

                await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGE_TRANSCODE_FALLBACK_STARTED", "info",
                    "Normalized final merge transcode fallback started.",
                    new { projectId = project.Id, finalVideoVersionId = version?.Id, inputCount = mergeItems.Count }, ct);
                var fallbackResult = await RunFfmpegAsync(ffmpegPath, finalDir, BuildTranscodeConcatArguments(concat, finalPath), timeout, ct);
                await File.WriteAllTextAsync(Path.Combine(finalDir, "ffmpeg-fallback.log"), fallbackResult.ToLogText(), ct);
                if (fallbackResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"FFmpeg merge fallback failed. ExitCode={fallbackResult.ExitCode}. {SafeTail(fallbackResult.Stderr)}");
                }

                await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGE_TRANSCODE_FALLBACK_COMPLETED", "info",
                    "Normalized final merge transcode fallback completed.",
                    new { projectId = project.Id, finalVideoVersionId = version?.Id, inputCount = mergeItems.Count }, ct);
            }

            if (TryResolveBackgroundMusic(settings, out var musicPath, out var musicVolume))
            {
                var mixedPath = Path.Combine(finalDir, version is null ? "final-music.mp4" : "final-video-music.mp4");
                var hasAudio = await HasAudioStreamAsync(ffmpegPath, finalPath, ct);
                var musicResult = await RunFfmpegAsync(
                    ffmpegPath,
                    finalDir,
                    hasAudio
                        ? BuildMusicMixArguments(finalPath, mixedPath, musicPath, musicVolume)
                        : BuildMusicOnlyArguments(finalPath, mixedPath, musicPath, musicVolume),
                    timeout,
                    ct);
                await File.WriteAllTextAsync(Path.Combine(finalDir, "ffmpeg-music.log"), musicResult.ToLogText(), ct);
                if (musicResult.ExitCode != 0)
                {
                    throw new InvalidOperationException($"FFmpeg music mix failed. ExitCode={musicResult.ExitCode}. {SafeTail(musicResult.Stderr)}");
                }
                File.Copy(mixedPath, finalPath, true);
            }

            var relative = Path.GetRelativePath(root, finalPath).Replace(Path.DirectorySeparatorChar, '/');
            var url = $"{_options.CurrentValue.PublicBase.TrimEnd('/')}/{relative}";
            if (version is not null)
            {
                await _versions.CompleteFinalVideoVersionAsync(version.Id, new FinalVideoVersionCompleteRequest(
                    url,
                    finalPath,
                    PosterUrl: null,
                    DurationSeconds: RVideoRules.CalculateMergedDuration(mergeableScenes),
                    "video/mp4"), ct);
            }

            await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Completed, url, finalPath, null, ct);
            await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Completed, ct);
            var finalDurationSeconds = RVideoRules.CalculateMergedDuration(mergeableScenes);
            await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGED", "info", "Final video merged.", new
            {
                finalPath,
                url,
                mergedSceneCount = mergeableScenes.Count,
                failedSceneCount = project.Scenes.Count - mergeableScenes.Count,
                mergedSceneIndexes = mergeableScenes.Select(x => x.SceneIndex).ToArray(),
                finalDurationSeconds
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (version is not null)
            {
                await _versions.FailFinalVideoVersionAsync(version.Id, ex.GetType().Name, ex.Message, ct);
            }

            if (job.AttemptCount < job.MaxAttempts)
            {
                await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Merging, errorMessage: ex.Message, ct: ct);
                await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Merging, ct);
                await _repo.AddProjectEventAsync(project.Id, "PROJECT_MERGE_RETRYABLE_FAILED", "warning",
                    "Final merge failed and will be retried without making the project terminal.",
                    new { job.Id, job.AttemptCount, job.MaxAttempts, error = ex.Message }, ct);
                throw;
            }

            await _repo.UpdateProjectAsync(project.Id, VideoProjectStatuses.Failed, errorMessage: ex.Message, ct: ct);
            await _rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Failed, ct);
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

    internal static string[] BuildCopyConcatArguments(string concatPath, string outputPath)
        =>
        [
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatPath,
            "-c", "copy",
            outputPath
        ];

    internal static string[] BuildTranscodeConcatArguments(string concatPath, string outputPath)
        =>
        [
            "-y",
            "-f", "concat",
            "-safe", "0",
            "-i", concatPath,
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-ar", "48000",
            "-ac", "2",
            "-movflags", "+faststart",
            outputPath
        ];

    internal static string[] BuildMusicMixArguments(string inputVideoPath, string outputPath, string musicPath, decimal musicVolume)
        =>
        [
            "-y",
            "-i", inputVideoPath,
            "-stream_loop", "-1",
            "-i", musicPath,
            "-filter_complex", $"[1:a]volume={musicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}[bg];[0:a][bg]amix=inputs=2:duration=first:dropout_transition=0[a]",
            "-map", "0:v:0",
            "-map", "[a]",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-shortest",
            outputPath
        ];

    internal static string[] BuildMusicOnlyArguments(string inputVideoPath, string outputPath, string musicPath, decimal musicVolume)
        =>
        [
            "-y",
            "-i", inputVideoPath,
            "-stream_loop", "-1",
            "-i", musicPath,
            "-filter_complex", $"[1:a]volume={musicVolume.ToString(System.Globalization.CultureInfo.InvariantCulture)}[bg]",
            "-map", "0:v:0",
            "-map", "[bg]",
            "-c:v", "copy",
            "-c:a", "aac",
            "-b:a", "192k",
            "-shortest",
            outputPath
        ];

    internal static string[] BuildConcatLines(IEnumerable<string> videoPaths)
        => videoPaths.Select(BuildConcatLine).ToArray();

    internal static string BuildConcatLine(string videoPath)
        => $"file '{Path.GetFullPath(videoPath).Replace('\\', '/').Replace("'", "''")}'";

    private static async Task<FfmpegResult> RunFfmpegAsync(
        string ffmpegPath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Khong khoi dong duoc FFmpeg.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        await process.WaitForExitAsync(timeoutCts.Token);
        return new FfmpegResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<bool> HasAudioStreamAsync(string ffmpegPath, string inputPath, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Khong khoi dong duoc FFmpeg.");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return process.ExitCode == 0 && (error.Contains("Audio:", StringComparison.OrdinalIgnoreCase) || output.Contains("Audio:", StringComparison.OrdinalIgnoreCase));
    }

    internal static string SafeTail(string? value, int maxChars = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxChars ? normalized : normalized[^maxChars..];
    }

    private sealed record MergeInput(long SceneId, int ItemOrder, Guid? SceneVideoVersionId, string? VideoPath);

    private sealed record FfmpegResult(int ExitCode, string Stdout, string Stderr)
    {
        public string ToLogText()
            => Stdout + Environment.NewLine + Stderr;
    }

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

    private async Task<IReadOnlyList<MergeInput>> BuildCurrentMergeItemsAsync(IReadOnlyList<VideoProjectSceneDto> scenes, CancellationToken ct)
    {
        var items = new List<MergeInput>(scenes.Count);
        foreach (var scene in scenes.OrderBy(x => x.SceneIndex))
        {
            var selectedVideo = await _versions.GetSelectedVideoVersionAsync(scene.Id, ct)
                ?? throw new InvalidOperationException("Project is missing selected completed scene video versions.");
            var videoPath = selectedVideo.SourceFilePath ?? scene.SceneVideoPath;
            items.Add(new MergeInput(scene.Id, scene.SceneIndex, selectedVideo.Id, ResolveRenderPhysicalPath(videoPath)));
        }

        return items;
    }

    private bool TryResolveBackgroundMusic(RVideoJobSettingsDto? settings, out string musicPath, out decimal musicVolume)
    {
        musicPath = string.Empty;
        musicVolume = 0m;
        if (settings is null || settings.MusicVolume <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(settings.MusicSnapshotJson))
        {
            return false;
        }

        JsonObject? snapshot;
        try
        {
            snapshot = JsonNode.Parse(settings.MusicSnapshotJson) as JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }

        var storageKey = ReadString(snapshot, "storageKey", "ObjectKey");
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return false;
        }

        musicPath = ResolveMediaPhysicalPath(storageKey);
        if (!File.Exists(musicPath))
        {
            return false;
        }

        musicVolume = Math.Clamp(settings.MusicVolume, 0m, 1m);
        return true;
    }

    private string ResolveMediaPhysicalPath(string objectKey)
    {
        var uploadRoot = _configuration["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        return Path.Combine(_env.ContentRootPath, uploadRoot, objectKey.Replace('/', Path.DirectorySeparatorChar));
    }

    private string? ResolveRenderPhysicalPath(string? objectKeyOrPath)
    {
        if (string.IsNullOrWhiteSpace(objectKeyOrPath))
        {
            return objectKeyOrPath;
        }

        if (Path.IsPathRooted(objectKeyOrPath))
        {
            return objectKeyOrPath;
        }

        return Path.Combine(ResolveRoot(_options.CurrentValue.StorageRoot), objectKeyOrPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string? ReadString(JsonObject? snapshot, params string[] keys)
    {
        if (snapshot is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (snapshot[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }
}
