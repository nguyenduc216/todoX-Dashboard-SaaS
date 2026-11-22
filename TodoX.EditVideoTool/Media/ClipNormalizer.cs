using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TodoX.EditVideoTool.Components;

namespace TodoX.EditVideoTool.Media;

/// <summary>
/// Makes phone footage safe and fast for the renderer: rotated, HEVC/ProRes or larger-than-1080p clips get a
/// cached 1080p H.264 proxy (constant 30 fps). Everything else is used as-is. All local, nothing uploaded.
/// </summary>
public sealed class ClipNormalizer
{
    public const int MaxLongSide = 1920;
    private static readonly string[] Friendly = ["h264", "vp8", "vp9", "av1"];
    private static readonly string[] EncoderPreference = ["h264_nvenc", "h264_qsv", "h264_amf", "h264_mf", "libopenh264", "libx264", "mpeg4"];

    private readonly ComponentLocator _components;
    private readonly AgentPaths _paths;
    private string? _encoder;

    public ClipNormalizer(ComponentLocator components, AgentPaths paths)
    {
        _components = components;
        _paths = paths;
    }

    public static bool NeedsProxy(MediaInfo info) =>
        info.HasVideo
        && (info.Rotation != 0
            || !Friendly.Contains(info.VideoCodec?.ToLowerInvariant() ?? "")
            || Math.Max(info.Width, info.Height) > MaxLongSide * 1.2);

    public async Task<string> PrepareAsync(MediaInfo info, CancellationToken ct = default)
    {
        if (!NeedsProxy(info))
        {
            return info.Path;
        }

        var file = new FileInfo(info.Path);
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}"))).ToLowerInvariant();
        var cacheDir = Path.Combine(_paths.Root, "cache");
        Directory.CreateDirectory(cacheDir);
        var proxy = Path.Combine(cacheDir, key[..20] + ".mp4");
        if (File.Exists(proxy))
        {
            return proxy;
        }

        var encoder = await PickEncoderAsync(ct);
        var scale = info.Width >= info.Height
            ? $"scale='min({MaxLongSide},iw)':-2"
            : $"scale=-2:'min({MaxLongSide},ih)'";
        var args = new List<string> { "-v", "error", "-y", "-i", info.Path, "-vf", $"{scale},fps=30,format=yuv420p", "-c:v", encoder };
        args.AddRange(encoder switch
        {
            "libx264" => ["-preset", "veryfast", "-crf", "20"],
            "mpeg4" => ["-q:v", "3"],
            _ => ["-b:v", "8M"]
        });
        args.AddRange(["-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", proxy + ".part.mp4"]);

        await RunFfmpegAsync(args, ct);
        File.Move(proxy + ".part.mp4", proxy, overwrite: true);
        return proxy;
    }

    /// <summary>General-purpose conversion to a widely compatible MP4 (tool todox_convert).</summary>
    public async Task<string> ConvertAsync(string input, string output, CancellationToken ct = default)
    {
        var encoder = await PickEncoderAsync(ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var args = new List<string> { "-v", "error", "-y", "-i", input, "-vf", $"scale='if(gt(iw,ih),min({MaxLongSide},iw),-2)':'if(gt(iw,ih),-2,min({MaxLongSide},ih))',format=yuv420p", "-c:v", encoder };
        args.AddRange(encoder == "libx264" ? ["-preset", "medium", "-crf", "20"] : encoder == "mpeg4" ? ["-q:v", "3"] : ["-b:v", "8M"]);
        args.AddRange(["-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart", output]);
        await RunFfmpegAsync(args, ct);
        return Path.GetFullPath(output);
    }

    /// <summary>
    /// Shot changes inside [from, from + seconds] of a video, in seconds from <paramref name="from"/>. Used so a cutaway
    /// taken from an edited video does not flash a frame or two of the neighbouring shot.
    /// </summary>
    public async Task<IReadOnlyList<double>> ShotCutsAsync(string path, double from, double seconds, CancellationToken ct = default)
    {
        var log = await RunFfmpegAsync(["-hide_banner", "-ss", Num(from), "-i", path, "-t", Num(seconds), "-an",
            "-vf", "scale=320:-2,select='gt(scene,0.25)',showinfo", "-f", "null", "-"], ct, captureError: true);
        return System.Text.RegularExpressions.Regex.Matches(log, @"pts_time:([0-9.]+)")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    /// <summary>
    /// Picks a start near <paramref name="from"/> (at most <see cref="ShotNudge"/> s away) so the cutaway does not begin
    /// or end on a shot change: none in its first 0.45 s or last 0.35 s. <paramref name="cuts"/> are absolute times in
    /// the clip. Returns <paramref name="from"/> when it is already clean or nothing nearby is.
    /// </summary>
    public static double AvoidShotCuts(double from, double used, double clipSeconds, IReadOnlyList<double> cuts)
    {
        bool Bad(double s) => cuts.Any(c => (c > s && c < s + 0.45) || (c > s + used - 0.35 && c < s + used));
        if (!Bad(from))
        {
            return from;
        }

        var candidates = cuts.SelectMany(c => new[] { c + 0.05, c - used - 0.05 })
            .Where(s => s >= 0 && s + used <= clipSeconds && Math.Abs(s - from) <= ShotNudge && !Bad(s))
            .OrderBy(s => Math.Abs(s - from))
            .ToList();
        return candidates.Count > 0 ? Math.Round(candidates[0], 3) : from;
    }

    public const double ShotNudge = 0.6;

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> PickEncoderAsync(CancellationToken ct)
    {
        if (_encoder is not null)
        {
            return _encoder;
        }

        var listed = await RunFfmpegAsync(["-hide_banner", "-encoders"], ct, captureOutput: true);
        // Hardware encoders are listed even without the GPU; probe each with a 1-frame test before trusting it.
        foreach (var e in EncoderPreference.Where(e => listed.Contains($" {e} ", StringComparison.Ordinal)))
        {
            try
            {
                await RunFfmpegAsync(["-v", "error", "-f", "lavfi", "-i", "color=c=black:s=320x240:d=0.1", "-c:v", e, "-f", "null", "-"], ct);
                return _encoder = e;
            }
            catch (InvalidOperationException)
            {
                // try the next encoder
            }
        }

        return _encoder = "mpeg4";
    }

    private async Task<string> RunFfmpegAsync(IReadOnlyList<string> args, CancellationToken ct, bool captureOutput = false, bool captureError = false)
    {
        var ffmpeg = _components.Locate("ffmpeg");
        if (!ffmpeg.Installed)
        {
            throw new InvalidOperationException("Chưa cài bộ ghép & xử lý video. Chạy lại bộ cài TodoX EditVideo.");
        }

        // stdin must never be inherited: in MCP mode it is the assistant's channel, and ffmpeg reads stdin by default.
        var psi = new ProcessStartInfo(ffmpeg.Path!) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("-nostdin");
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        proc.StandardInput.Close();
        using var reg = ct.Register(() => { try { proc.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = (await stderr).Trim();
            throw new InvalidOperationException("Bộ xử lý video báo lỗi: " + (err.Length > 400 ? err[^400..] : err));
        }

        return captureError ? await stderr : captureOutput ? await stdout : string.Empty;
    }
}
