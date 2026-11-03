using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TodoX.EditVideoTool.Components;

namespace TodoX.EditVideoTool.Media;

public sealed record MediaInfo(string Path, double DurationSeconds, int Width, int Height, double Fps, bool HasVideo, bool HasAudio, string? VideoCodec = null, int Rotation = 0);

/// <summary>Reads clip metadata with ffprobe. Nothing leaves the machine.</summary>
public sealed class MediaProbe
{
    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi"];
    public static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".aac", ".ogg"];

    private readonly ComponentLocator _components;

    public MediaProbe(ComponentLocator components)
    {
        _components = components;
    }

    /// <summary>Expands folders into their video files (sorted by name) and keeps explicit files as given.</summary>
    public static IReadOnlyList<string> ExpandInputs(IEnumerable<string> inputs)
    {
        var result = new List<string>();
        foreach (var raw in inputs)
        {
            var p = raw.Trim().Trim('"');
            if (Directory.Exists(p))
            {
                result.AddRange(Directory.EnumerateFiles(p)
                    .Where(f => VideoExtensions.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(p))
            {
                result.Add(System.IO.Path.GetFullPath(p));
            }
            else
            {
                throw new FileNotFoundException($"Không tìm thấy: {p}");
            }
        }

        return result;
    }

    public async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default)
    {
        var ffprobe = _components.Locate("ffprobe");
        if (!ffprobe.Installed)
        {
            throw new InvalidOperationException("Chưa cài ffprobe. Chạy lại bộ cài TodoX EditVideoTool.");
        }

        var psi = new ProcessStartInfo(ffprobe.Path!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path })
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Không đọc được file {System.IO.Path.GetFileName(path)}: {stderr.Trim()}");
        }

        return Parse(path, stdout);
    }

    internal static MediaInfo Parse(string path, string ffprobeJson)
    {
        using var doc = JsonDocument.Parse(ffprobeJson);
        var root = doc.RootElement;
        double duration = 0;
        if (root.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var d))
        {
            double.TryParse(d.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out duration);
        }

        int width = 0, height = 0, rotation = 0;
        double fps = 0;
        bool hasVideo = false, hasAudio = false;
        string? codec = null;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var s in streams.EnumerateArray())
            {
                var type = s.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                if (type == "video" && !hasVideo && !(s.TryGetProperty("disposition", out var disp) && disp.TryGetProperty("attached_pic", out var ap) && ap.GetInt32() == 1))
                {
                    hasVideo = true;
                    width = s.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    height = s.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                    fps = ParseRate(s.TryGetProperty("avg_frame_rate", out var r) ? r.GetString() : null);
                    codec = s.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                    rotation = TryRotation(s) ?? 0;
                    if (rotation is 90 or 270)
                    {
                        (width, height) = (height, width);
                    }
                }
                else if (type == "audio")
                {
                    hasAudio = true;
                }
            }
        }

        return new MediaInfo(path, duration, width, height, fps, hasVideo, hasAudio, codec, rotation);
    }

    private static int? TryRotation(JsonElement stream)
    {
        if (stream.TryGetProperty("side_data_list", out var sdl))
        {
            foreach (var sd in sdl.EnumerateArray())
            {
                if (sd.TryGetProperty("rotation", out var rot))
                {
                    return Math.Abs(rot.GetInt32()) % 360;
                }
            }
        }

        return null;
    }

    private static double ParseRate(string? rate)
    {
        if (string.IsNullOrEmpty(rate))
        {
            return 0;
        }

        var parts = rate.Split('/');
        return parts.Length == 2
               && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
               && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dd)
               && dd > 0
            ? Math.Round(n / dd, 3)
            : 0;
    }
}
