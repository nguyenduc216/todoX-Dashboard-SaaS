using System.Diagnostics;
using System.Globalization;
using TodoX.Web.Services.SharedMedia;

namespace TodoX.Web.Services.Landing;

/// <summary>
/// Re-encodes landing videos (intro, /video-ngan, /mau-video) for web playback: shorter side capped at 720px,
/// bitrate capped so a modest server uplink can stream it, and the MP4 index moved to the front (faststart)
/// so playback starts before the whole file is downloaded.
/// </summary>
public static class LandingWebVideoTranscoder
{
    public const int MaxShortSide = 720;
    public const int MaxVideoKbps = 2500;
    public const int AudioKbps = 128;

    public static IReadOnlyList<string> BuildArguments(string inputPath, string outputPath, VideoTranscodeOptions options) =>
    [
        "-hide_banner", "-loglevel", "error", "-y",
        "-i", inputPath,
        "-map", "0:v:0", "-map", "0:a?",
        // Cap the shorter side (portrait width / landscape height) without upscaling; keep even dimensions.
        "-vf", $"scale=w='if(lte(iw,ih),min({MaxShortSide},iw),-2)':h='if(lte(iw,ih),-2,min({MaxShortSide},ih))'",
        "-c:v", "libx264",
        "-preset", string.IsNullOrWhiteSpace(options.Preset) ? "medium" : options.Preset,
        "-crf", Math.Clamp(options.Crf, 0, 51).ToString(CultureInfo.InvariantCulture),
        "-maxrate", $"{MaxVideoKbps}k",
        "-bufsize", $"{MaxVideoKbps * 2}k",
        "-pix_fmt", "yuv420p",
        "-profile:v", "high",
        "-level", "4.1",
        "-movflags", "+faststart",
        "-c:a", "aac",
        "-b:a", $"{AudioKbps}k",
        "-ar", "48000",
        "-ac", "2",
        outputPath
    ];

    public static async Task TranscodeAsync(string inputPath, string outputPath, VideoTranscodeOptions options, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(options.FfmpegPath) ? "ffmpeg" : options.FfmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in BuildArguments(inputPath, outputPath, options))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Không thể khởi động FFmpeg.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new IntroUploadException(IntroUploadError.Other,
                $"Không tìm thấy/chạy được FFmpeg tại '{psi.FileName}'. Hãy cấu hình SharedMedia:IndustrySolutions:VideoTranscode:FfmpegPath.", ex);
        }

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var timeoutSeconds = Math.Max(30, options.TimeoutSeconds);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (ct.IsCancellationRequested) throw;
            throw new IntroUploadException(IntroUploadError.Other, $"Tối ưu video quá thời gian cho phép ({timeoutSeconds} giây).");
        }

        var stderr = await stderrTask;
        _ = await stdoutTask;
        if (process.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? $"FFmpeg exit code {process.ExitCode}." : stderr.Trim();
            throw new IntroUploadException(IntroUploadError.InvalidFile, $"Không thể tối ưu video cho web: {detail}");
        }
    }
}
