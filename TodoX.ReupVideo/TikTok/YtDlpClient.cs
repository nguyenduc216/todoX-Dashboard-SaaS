using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TodoX.ReupVideo.TikTok;

/// <summary>
/// TikTok through yt-dlp (https://github.com/yt-dlp/yt-dlp): lists a channel, checks a link and downloads the
/// video file without the TikTok watermark. yt-dlp.exe is fetched from its official GitHub release on first use and
/// can be updated from the window, because TikTok changes often and yt-dlp follows quickly.
/// </summary>
public sealed class YtDlpClient : ITikTokClient
{
    public const string ReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    private readonly AppPaths _paths;
    private readonly Func<string?> _cookiesFile;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _install = new(1, 1);

    public YtDlpClient(AppPaths paths, Func<string?> cookiesFile, HttpClient http)
    {
        _paths = paths;
        _cookiesFile = cookiesFile;
        _http = http;
    }

    public bool IsInstalled => File.Exists(_paths.YtDlpExe);

    public async Task EnsureInstalledAsync(IProgress<string>? log, CancellationToken ct)
    {
        if (IsInstalled)
        {
            return;
        }

        await _install.WaitAsync(ct);
        try
        {
            if (IsInstalled)
            {
                return;
            }

            log?.Report("Đang tải công cụ yt-dlp (lần đầu, ~18 MB)...");
            Directory.CreateDirectory(_paths.ToolsDir);
            var temp = _paths.YtDlpExe + ".download";
            using (var response = await _http.GetAsync(ReleaseUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(temp);
                await response.Content.CopyToAsync(file, ct);
            }

            File.Move(temp, _paths.YtDlpExe, overwrite: true);
            log?.Report("Đã cài yt-dlp.");
        }
        finally
        {
            _install.Release();
        }
    }

    /// <summary>Self-update of yt-dlp (fixes most "TikTok stopped working" problems).</summary>
    public async Task<string> UpdateAsync(IProgress<string>? log, CancellationToken ct)
    {
        await EnsureInstalledAsync(log, ct);
        var result = await RunAsync(["-U"], TimeSpan.FromMinutes(3), ct);
        var text = (result.StdOut + "\n" + result.StdErr).Trim();
        return text.Length == 0 ? $"yt-dlp kết thúc với mã {result.ExitCode}" : text;
    }

    public async Task<string?> VersionAsync(CancellationToken ct)
    {
        if (!IsInstalled)
        {
            return null;
        }

        var result = await RunAsync(["--version"], TimeSpan.FromSeconds(30), ct);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    public async Task<IReadOnlyList<TikTokLink>> ListChannelAsync(string username, int limit, CancellationToken ct)
    {
        await EnsureInstalledAsync(null, ct);
        var args = new List<string> { "--flat-playlist", "-J", "--playlist-end", Math.Max(1, limit).ToString() };
        args.AddRange(CommonArgs());
        args.Add($"https://www.tiktok.com/@{username}");
        var result = await RunAsync(args, TimeSpan.FromMinutes(3), ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(ShortError(result.StdErr) ?? $"yt-dlp lỗi (mã {result.ExitCode})");
        }

        return ParsePlaylist(result.StdOut, username);
    }

    public async Task<TikTokCheckResult> CheckAsync(string url, CancellationToken ct)
    {
        await EnsureInstalledAsync(null, ct);
        var args = new List<string> { "-J", "--no-playlist", "--skip-download" };
        args.AddRange(CommonArgs());
        args.Add(url);
        YtDlpResult result;
        try
        {
            result = await RunAsync(args, TimeSpan.FromMinutes(2), ct);
        }
        catch (TimeoutException ex)
        {
            return new TikTokCheckResult(LinkCheck.Error, null, ex.Message);
        }

        if (result.ExitCode != 0)
        {
            var error = ShortError(result.StdErr) ?? $"yt-dlp lỗi (mã {result.ExitCode})";
            return new TikTokCheckResult(ClassifyError(error), null, error);
        }

        var info = ParseInfo(result.StdOut, url);
        return info is null
            ? new TikTokCheckResult(LinkCheck.Error, null, "Không đọc được thông tin video")
            : new TikTokCheckResult(LinkCheck.Active, info, null);
    }

    public async Task<string> DownloadAsync(string url, string videoId, string folder, CancellationToken ct)
    {
        await EnsureInstalledAsync(null, ct);
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, videoId + ".*"))
        {
            File.Delete(old);
        }

        // Prefer an H.264 mp4 that already has sound (no ffmpeg needed); TikTok's play address has no watermark.
        var args = new List<string>
        {
            "--no-playlist", "--no-part", "--no-mtime",
            "-f", "b[vcodec^=h264][ext=mp4]/b[vcodec^=avc][ext=mp4]/b[ext=mp4]/b",
            "-o", Path.Combine(folder, videoId + ".%(ext)s")
        };
        args.AddRange(CommonArgs());
        args.Add(url);
        var result = await RunAsync(args, TimeSpan.FromMinutes(15), ct);
        var file = Directory.GetFiles(folder, videoId + ".*").FirstOrDefault(f => !f.EndsWith(".part", StringComparison.OrdinalIgnoreCase));
        if (result.ExitCode != 0 || file is null || new FileInfo(file).Length == 0)
        {
            throw new InvalidOperationException("Tải video thất bại: " + (ShortError(result.StdErr) ?? $"mã {result.ExitCode}"));
        }

        return file;
    }

    private IEnumerable<string> CommonArgs()
    {
        yield return "--encoding";
        yield return "utf-8";
        yield return "--no-warnings";
        yield return "--no-progress";
        yield return "--socket-timeout";
        yield return "30";
        var cookies = _cookiesFile();
        if (!string.IsNullOrWhiteSpace(cookies) && File.Exists(cookies))
        {
            yield return "--cookies";
            yield return cookies;
        }
    }

    // ------------------------------------------------------------------ parsing (internal for tests)

    internal static List<TikTokLink> ParsePlaylist(string json, string username)
    {
        var links = new List<TikTokLink>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return links;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            var url = Str(entry, "url") ?? Str(entry, "webpage_url");
            var parsed = TikTokLinks.ParseVideoUrl(url);
            var id = Str(entry, "id") ?? parsed?.VideoId;
            if (string.IsNullOrEmpty(id) || !id.All(char.IsDigit))
            {
                continue;
            }

            var author = parsed?.User ?? username;
            links.Add(new TikTokLink(
                id,
                parsed is null ? TikTokLinks.VideoUrlOf(author, id) : TikTokLinks.VideoUrlOf(parsed.Value.User, id),
                author,
                Str(entry, "description") ?? Str(entry, "title"),
                Time(entry)));
        }

        return links;
    }

    internal static TikTokVideoInfo? ParseInfo(string json, string url)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var parsed = TikTokLinks.ParseVideoUrl(Str(root, "webpage_url") ?? url) ?? TikTokLinks.ParseVideoUrl(url);
        var id = Str(root, "id") ?? parsed?.VideoId;
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var author = Str(root, "uploader") ?? parsed?.User ?? "";
        var description = Str(root, "description") ?? Str(root, "title") ?? "";
        var tags = new List<string>();
        if (root.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in tagArray.EnumerateArray())
            {
                var value = tag.ValueKind == JsonValueKind.String ? tag.GetString()?.Trim().TrimStart('#') : null;
                if (!string.IsNullOrEmpty(value) && !tags.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    tags.Add(value);
                }
            }
        }

        foreach (var tag in TikTokLinks.ExtractHashtags(description))
        {
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        double? duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : null;
        return new TikTokVideoInfo(
            id,
            TikTokLinks.VideoUrlOf(author.Length > 0 ? author : parsed?.User ?? "_", id),
            author,
            Str(root, "channel") ?? Str(root, "creator"),
            description,
            tags,
            duration,
            Time(root));
    }

    internal static LinkCheck ClassifyError(string error)
    {
        var e = error.ToLowerInvariant();
        // Our IP/region being blocked or a network failure says nothing about the video itself.
        if (e.Contains("ip address is blocked") || e.Contains("timed out") || e.Contains("connection") || e.Contains("http error 5")
            || e.Contains("http error 429") || e.Contains("unable to download webpage") || e.Contains("getaddrinfo"))
        {
            return LinkCheck.Error;
        }

        string[] gone =
        [
            "not available", "unavailable", "private", "removed", "deleted", "does not exist", "no longer",
            "status code 10204", "status code 10216", "status code 10222", "status code 10101", "http error 404"
        ];
        return gone.Any(e.Contains) ? LinkCheck.Unavailable : LinkCheck.Error;
    }

    internal static string? ShortError(string stderr)
    {
        var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.LastOrDefault(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault();
        if (line is null)
        {
            return null;
        }

        line = line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) ? line[6..].Trim() : line;
        return line.Length > 300 ? line[..300] + "…" : line;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static DateTime? Time(JsonElement e) =>
        e.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds((long)t.GetDouble()).LocalDateTime
            : null;

    // ------------------------------------------------------------------ process

    private sealed record YtDlpResult(int ExitCode, string StdOut, string StdErr);

    private async Task<YtDlpResult> RunAsync(IEnumerable<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(_paths.YtDlpExe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = _paths.ToolsDir
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUTF8"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Không chạy được yt-dlp.exe");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // already gone
            }

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"yt-dlp chạy quá {timeout.TotalMinutes:0} phút");
        }

        return new YtDlpResult(process.ExitCode, await stdout, await stderr);
    }
}
