using System.Diagnostics;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.SharedMedia;

namespace TodoX.Web.Services.Landing;

public sealed class LandingIndustryMediaService
{
    private static readonly Dictionary<string, string[]> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"],
        [".png"] = ["image/png"],
        [".webp"] = ["image/webp"],
        [".mp4"] = ["video/mp4"],
        [".webm"] = ["video/webm"]
    };

    private readonly SharedMediaOptions _options;
    private readonly SharedMediaPathService _paths;
    private readonly ILogger<LandingIndustryMediaService> _logger;

    public LandingIndustryMediaService(
        IOptions<SharedMediaOptions> options,
        SharedMediaPathService paths,
        ILogger<LandingIndustryMediaService> logger)
    {
        _options = options.Value;
        _paths = paths;
        _logger = logger;
    }

    public bool IsReady => _paths.IsConfigured && _paths.CanWrite();

    public Task<string> SaveThumbnailAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveAsync(
            file,
            _options.IndustrySolutions.ThumbnailSubfolder,
            _options.IndustrySolutions.AllowedThumbnailExtensions,
            _options.IndustrySolutions.MaxThumbnailBytes,
            transcodeVideo: false,
            ct);

    public Task<string> SaveVideoAsync(IBrowserFile file, CancellationToken ct = default)
        => SaveAsync(
            file,
            _options.IndustrySolutions.VideoSubfolder,
            _options.IndustrySolutions.AllowedVideoExtensions,
            _options.IndustrySolutions.MaxVideoBytes,
            transcodeVideo: true,
            ct);

    public void DeleteIfSharedMedia(string? url) => TryDeleteOld(url);

    private async Task<string> SaveAsync(
        IBrowserFile file,
        string subfolder,
        IReadOnlyCollection<string> allowedExtensions,
        long maxBytes,
        bool transcodeVideo,
        CancellationToken ct)
    {
        if (!_paths.IsConfigured)
        {
            throw new InvalidOperationException("SharedMedia:StorageRoot chưa được cấu hình.");
        }

        if (file.Size <= 0 || file.Size > maxBytes)
        {
            throw new InvalidOperationException($"File vượt quá dung lượng cho phép ({maxBytes / 1024 / 1024} MB).");
        }

        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Định dạng file không được hỗ trợ.");
        }

        if (!AllowedContentTypes.TryGetValue(extension, out var contentTypes)
            || !contentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Content-Type của file không hợp lệ.");
        }

        var tempFolder = _paths.GetIndustryPhysicalFolder(_options.IndustrySolutions.TempSubfolder);
        var finalFolder = _paths.GetIndustryPhysicalFolder(subfolder);
        Directory.CreateDirectory(tempFolder);
        Directory.CreateDirectory(finalFolder);

        if (transcodeVideo && _options.IndustrySolutions.VideoTranscode.Enabled)
        {
            return await SaveBrowserSafeVideoAsync(file, extension, tempFolder, finalFolder, subfolder, maxBytes, ct);
        }

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}{extension}";
        var tempPath = Path.Combine(tempFolder, $"{fileName}.tmp");
        var finalPath = Path.Combine(finalFolder, fileName);

        try
        {
            await using (var target = File.Create(tempPath))
            await using (var source = file.OpenReadStream(maxBytes))
            {
                await source.CopyToAsync(target, ct);
            }

            File.Move(tempPath, finalPath, overwrite: false);
            return _paths.GetIndustryPublicUrl(subfolder, fileName);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task<string> SaveBrowserSafeVideoAsync(
        IBrowserFile file,
        string inputExtension,
        string tempFolder,
        string finalFolder,
        string subfolder,
        long maxBytes,
        CancellationToken ct)
    {
        var transcode = _options.IndustrySolutions.VideoTranscode;
        var token = Guid.NewGuid().ToString("N");
        var inputPath = Path.Combine(tempFolder, $"upload-{token}{inputExtension}");
        var outputTempPath = Path.Combine(tempFolder, $"browser-{token}.mp4");
        var finalFileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{token}.mp4";
        var finalPath = Path.Combine(finalFolder, finalFileName);

        try
        {
            await using (var target = File.Create(inputPath))
            await using (var source = file.OpenReadStream(maxBytes))
            {
                await source.CopyToAsync(target, ct);
            }

            var psi = new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(transcode.FfmpegPath) ? "ffmpeg" : transcode.FfmpegPath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in new[]
            {
                "-hide_banner", "-loglevel", "error", "-y",
                "-i", inputPath,
                "-map", "0:v:0", "-map", "0:a?",
                "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                "-c:v", "libx264",
                "-preset", string.IsNullOrWhiteSpace(transcode.Preset) ? "medium" : transcode.Preset,
                "-crf", Math.Clamp(transcode.Crf, 0, 51).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-pix_fmt", "yuv420p",
                "-profile:v", "high",
                "-level", "4.1",
                "-movflags", "+faststart",
                "-c:a", "aac",
                "-b:a", $"{Math.Max(64, transcode.AudioBitrateKbps)}k",
                "-ar", "48000",
                "-ac", "2",
                outputTempPath
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Không thể khởi động FFmpeg.");
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Không tìm thấy/chạy được FFmpeg tại '{psi.FileName}'. Hãy cấu hình SharedMedia:IndustrySolutions:VideoTranscode:FfmpegPath.", ex);
            }

            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(30, transcode.TimeoutSeconds)));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                throw new InvalidOperationException($"Chuẩn hóa video quá thời gian cho phép ({Math.Max(30, transcode.TimeoutSeconds)} giây).");
            }

            var stderr = await stderrTask;
            _ = await stdoutTask;
            if (process.ExitCode != 0 || !File.Exists(outputTempPath) || new FileInfo(outputTempPath).Length == 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr) ? $"FFmpeg exit code {process.ExitCode}." : stderr.Trim();
                _logger.LogWarning("Industry video transcode failed. exitCode={ExitCode} error={Error}", process.ExitCode, detail);
                throw new InvalidOperationException($"Không thể chuẩn hóa video sang H.264/AAC: {detail}");
            }

            File.Move(outputTempPath, finalPath, overwrite: false);
            _logger.LogInformation("Industry video transcoded to browser-safe MP4. file={FileName}", finalFileName);
            return _paths.GetIndustryPublicUrl(subfolder, finalFileName);
        }
        finally
        {
            TryDeleteFile(inputPath);
            TryDeleteFile(outputTempPath);
        }
    }

    private void TryDeleteOld(string? oldUrl)
    {
        if (string.IsNullOrWhiteSpace(oldUrl))
        {
            return;
        }

        try
        {
            var path = _paths.ResolvePublicUrlToPhysicalPath(oldUrl);
            TryDeleteFile(path);
        }
        catch
        {
            // Best-effort cleanup only; stale media must not break a successful save.
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static void TryKill(Process process)
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
            // Best effort timeout cleanup.
        }
    }
}
