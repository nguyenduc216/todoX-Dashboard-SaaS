using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseFinalizerRuntime
{
    Task ProcessAsync(TimelapseFinalizerWorkItem item, CancellationToken ct = default);
}

public sealed class TimelapseFinalizerRuntime : ITimelapseFinalizerRuntime
{
    internal static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly IMediaFileService _media;
    private readonly ITimelapseWorkerRepository _repo;
    private readonly ITimelapseCoreLifecycleBridge _coreLifecycle;
    private readonly IRenderJobService _renderJobs;
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseFinalizerRuntime> _logger;

    public TimelapseFinalizerRuntime(
        IWebHostEnvironment env,
        IConfiguration config,
        IMediaFileService media,
        ITimelapseWorkerRepository repo,
        ITimelapseCoreLifecycleBridge coreLifecycle,
        IRenderJobService renderJobs,
        IOptions<TimelapseProviderWorkerOptions> options,
        ILogger<TimelapseFinalizerRuntime> logger)
    {
        _env = env;
        _config = config;
        _media = media;
        _repo = repo;
        _coreLifecycle = coreLifecycle;
        _renderJobs = renderJobs;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(TimelapseFinalizerWorkItem item, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "todox-timelapse", item.JobId.ToString("N"), item.Version.ToString());
        try
        {
            Directory.CreateDirectory(tempDir);
            var clips = item.Clips.OrderBy(x => x.ClipIndex).ToList();
            if (clips.Count == 0 || clips.Any(x => string.IsNullOrWhiteSpace(x.ObjectKey)))
            {
                throw new InvalidOperationException("Timelapse finalizer requires completed clips with stored media.");
            }

            var storageProvider = _config["Storage:Provider"] ?? "local";
            if (!string.Equals(storageProvider, "local", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Timelapse finalizer requires local media storage; remote storage needs a media download adapter.");
            }

            foreach (var clip in clips.Where(x => x.MediaId.HasValue))
            {
                var clipMedia = await _media.GetAsync(clip.MediaId!.Value, ct);
                if (clipMedia is not null && !string.Equals(clipMedia.StorageProvider, "local", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Timelapse clip media is stored by a non-local provider and cannot be passed to FFmpeg as a physical path.");
                }
            }

            var paths = clips.Select(x => ResolveObjectKey(x.ObjectKey!)).ToList();
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Timelapse clip media file was not found.", path);
                }
            }

            var concatPath = Path.Combine(tempDir, "concat.txt");
            await File.WriteAllTextAsync(concatPath, BuildConcatFile(paths), Utf8NoBom, ct);
            var outputPath = Path.Combine(tempDir, "final.mp4");
            _logger.LogInformation("TIMELAPSE_FINALIZER_FFMPEG_START jobId={JobId} version={Version} clips={ClipCount} concatPath={ConcatPath} outputPath={OutputPath}",
                item.JobId, item.Version, paths.Count, concatPath, outputPath);
            foreach (var path in paths)
            {
                _logger.LogInformation("TIMELAPSE_FINALIZER_FFMPEG_CLIP jobId={JobId} version={Version} path={ClipPath}",
                    item.JobId, item.Version, path);
            }

            await RunFfmpegAsync(concatPath, outputPath, ct);
            var bytes = await File.ReadAllBytesAsync(outputPath, ct);
            var objectKey = $"timelapse/{DateTime.UtcNow:yyyyMM}/{item.JobId:N}/final-v{item.Version}.mp4";
            var media = await _media.SaveBinaryAtObjectKeyAsync(
                bytes,
                objectKey,
                $"timelapse-{item.JobId:N}-v{item.Version}.mp4",
                "video/mp4",
                "timelapse_final_video",
                item.UserId,
                item.CustomerId,
                item.TenantId,
                ct);

            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                clipOrder = clips.Select(x => x.ClipIndex),
                mediaId = media.Id,
                media.ObjectKey,
                publicUrl = media.PublicUrl ?? media.FileUrl,
                ffmpeg = "concat_demuxer_copy"
            });
            if (!await _repo.SaveFinalizerCompletedAsync(item.Id, item.JobId, media.Id, media.ObjectKey!, media.PublicUrl ?? media.FileUrl!, responseJson, ct))
            {
                _logger.LogWarning("TIMELAPSE_FINALIZER_COMPLETE_STALE jobId={JobId} version={Version}",
                    item.JobId, item.Version);
                return;
            }

            _logger.LogInformation("TIMELAPSE_FINALIZER_COMPLETE jobId={JobId} version={Version} clips={ClipCount} mediaId={MediaId}",
                item.JobId, item.Version, clips.Count, media.Id);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_FINALIZER_COMPLETE", "Timelapse final video saved to TodoX media.",
                new { item.Version, mediaId = media.Id, clipOrder = clips.Select(x => x.ClipIndex).ToArray() }, ct: ct);
            await _coreLifecycle.CompleteAsync(
                item,
                media.Id,
                media.ObjectKey!,
                media.PublicUrl ?? media.FileUrl!,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TIMELAPSE_FINALIZER_FAILED jobId={JobId} version={Version}", item.JobId, item.Version);
            var saved = await _repo.SaveFinalizerFailedAsync(item.Id, item.JobId, ex.GetType().Name, ex.Message, "{}", ct);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_FINALIZER_FAILED", "Timelapse finalizer failed.",
                new { item.Version, errorCode = ex.GetType().Name, errorMessage = ex.Message }, "error", ct);
            if (!saved)
            {
                return;
            }
            await _coreLifecycle.FailAsync(
                item.JobId,
                item.Snapshot,
                ex.GetType().Name,
                "Có lỗi xảy ra khi hoàn thiện video.",
                CoreFailureBillingPolicy.KeepCharge,
                ct);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private string ResolveObjectKey(string objectKey)
    {
        var uploadRoot = _config["Storage:LocalUploadRoot"] ?? "wwwroot/uploads";
        var absRoot = Path.GetFullPath(Path.Combine(_env.ContentRootPath, uploadRoot));
        var absPath = Path.GetFullPath(Path.Combine(absRoot, objectKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!absPath.StartsWith(absRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Timelapse media object key is invalid.");
        }

        return absPath;
    }

    private async Task RunFfmpegAsync(string concatPath, string outputPath, CancellationToken ct)
    {
        var ffmpeg = _config["VideoRender:FfmpegPath"] ?? _config["RenderQueue:FfmpegPath"] ?? "ffmpeg";
        var timeoutSeconds = Math.Max(1, _options.FinalizerFfmpegTimeoutSeconds);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("concat");
        startInfo.ArgumentList.Add("-safe");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(concatPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("copy");
        startInfo.ArgumentList.Add(outputPath);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start FFmpeg.");
        var stderrTask = process.StandardError.ReadToEndAsync();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(waitCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            var timeoutStderr = await ReadProcessOutputAsync(stderrTask);
            _ = await ReadProcessOutputAsync(stdoutTask);
            var timeoutMessage = $"FFmpeg concat timed out after {timeoutSeconds} seconds.";
            _logger.LogError("{TimeoutMessage} stderr={Stderr}", timeoutMessage, timeoutStderr);
            throw new TimeoutException(BuildFfmpegFailureMessage(timeoutMessage, timeoutStderr));
        }

        var stderr = await ReadProcessOutputAsync(stderrTask);
        _ = await ReadProcessOutputAsync(stdoutTask);
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.LogError("FFmpeg concat failed with exit code {ExitCode}. stderr={Stderr}", process.ExitCode, stderr);
            throw new InvalidOperationException(BuildFfmpegFailureMessage($"FFmpeg concat failed with exit code {process.ExitCode}.", stderr));
        }
    }

    internal static string BuildConcatFile(IEnumerable<string> paths)
        => string.Join(
            Environment.NewLine,
            paths.Select(path =>
            {
                var normalized = Path.GetFullPath(path)
                    .Replace('\\', '/')
                    .Replace("'", "'\\''");

                return $"file '{normalized}'";
            }));

    internal static string BuildFfmpegFailureMessage(string prefix, string? stderr)
    {
        var tail = ExtractUsefulFfmpegTail(stderr);
        var message = string.IsNullOrWhiteSpace(tail)
            ? prefix
            : $"{prefix} {tail}";

        return TruncateForLog(message);
    }

    internal static string ExtractUsefulFfmpegTail(string? stderr, int maxLines = 16)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return string.Empty;
        }

        var usefulLines = stderr
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Where(line => !IsFfmpegBannerLine(line))
            .TakeLast(maxLines);

        return string.Join(Environment.NewLine, usefulLines);
    }

    private static bool IsFfmpegBannerLine(string line)
        => line.StartsWith("ffmpeg version", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("built with", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("libav", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("libsw", StringComparison.OrdinalIgnoreCase)
           || line.StartsWith("libpostproc", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static async Task<string> ReadProcessOutputAsync(Task<string> outputTask)
    {
        try
        {
            var completed = await Task.WhenAny(outputTask, Task.Delay(TimeSpan.FromSeconds(5)));
            return completed == outputTask ? await outputTask : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static string TruncateForLog(string? value, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
