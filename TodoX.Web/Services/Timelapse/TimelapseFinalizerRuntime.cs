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
            await File.WriteAllTextAsync(concatPath, BuildConcatFile(paths), Encoding.UTF8, ct);
            var outputPath = Path.Combine(tempDir, "final.mp4");
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
            await _repo.SaveFinalizerCompletedAsync(item.Id, item.JobId, media.Id, media.ObjectKey!, media.PublicUrl ?? media.FileUrl!, responseJson, ct);
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
            await _repo.SaveFinalizerFailedAsync(item.Id, item.JobId, ex.GetType().Name, ex.Message, "{}", ct);
            await _renderJobs.AddEventAsync(item.JobId, "TIMELAPSE_FINALIZER_FAILED", "Timelapse finalizer failed.",
                new { item.Version, errorCode = ex.GetType().Name, errorMessage = ex.Message }, "error", ct);
            await _coreLifecycle.FailAsync(
                item.JobId,
                item.Snapshot,
                ex.GetType().Name,
                ex.Message,
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
            _logger.LogError("{TimeoutMessage} stderr={Stderr}", timeoutMessage, TruncateForLog(timeoutStderr));
            throw new TimeoutException($"{timeoutMessage} Stderr: {TruncateForLog(timeoutStderr)}");
        }

        var stderr = await ReadProcessOutputAsync(stderrTask);
        _ = await ReadProcessOutputAsync(stdoutTask);
        if (process.ExitCode != 0 || !File.Exists(outputPath))
        {
            _logger.LogError("FFmpeg concat failed with exit code {ExitCode}. stderr={Stderr}", process.ExitCode, TruncateForLog(stderr));
            throw new InvalidOperationException($"FFmpeg concat failed with exit code {process.ExitCode}: {TruncateForLog(stderr)}");
        }
    }

    private static string BuildConcatFile(IEnumerable<string> paths)
        => string.Join(Environment.NewLine, paths.Select(path => $"file '{path.Replace("'", "'\\''")}'"));

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
