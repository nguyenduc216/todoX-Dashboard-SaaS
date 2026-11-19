using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TodoX.ReupVideo.Core;

public sealed record UpdateInfo(Version Version, string FileName, string Url, string Sha256, long Size);

/// <summary>
/// Self-update from todox.vn. The check is a plain GET of the public release info (no data about this computer is
/// sent): https://todox.vn/api/landing/apps/reupvideo → version, file, sha256. A newer build is downloaded to
/// %LOCALAPPDATA%\TodoX\ReupVideo\updates, its SHA-256 checked, then swapped in place of the running exe and restarted.
/// </summary>
public sealed partial class AppUpdater
{
    public const string SiteBase = "https://todox.vn";
    public const string ReleaseApi = SiteBase + "/api/landing/apps/reupvideo";
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private readonly AppPaths _paths;
    private readonly HttpClient _http;
    private readonly Version _current;

    public AppUpdater(AppPaths paths, HttpClient http, Version current)
    {
        _paths = paths;
        _http = http;
        _current = current;
    }

    public string UpdatesDir => Path.Combine(_paths.Root, "updates");

    /// <summary>Newer release, or null when up to date / the site cannot be reached.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "todoX-ReupVideo/" + _current.ToString(3));
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return Parse(await response.Content.ReadAsStringAsync(ct), _current, SiteBase);
    }

    internal static UpdateInfo? Parse(string json, Version current, string siteBase)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!Version.TryParse(Str(root, "version"), out var version) || Normalize(version) <= Normalize(current))
        {
            return null;
        }

        var fileName = Str(root, "fileName");
        var fileUrl = Str(root, "fileUrl");
        var sha = Str(root, "sha256");
        if (fileName is null || fileUrl is null || sha is null || !ExeName().IsMatch(fileName) || sha.Length != 64)
        {
            return null;
        }

        // Only files from todox.vn itself.
        var url = fileUrl.StartsWith('/') ? siteBase + fileUrl : fileUrl;
        if (!url.StartsWith(siteBase + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var size = root.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
        return new UpdateInfo(version, fileName, url, sha.ToUpperInvariant(), size);
    }

    /// <summary>Downloads (or reuses an already downloaded copy) and checks SHA-256; returns the file path.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, CancellationToken ct)
    {
        Directory.CreateDirectory(UpdatesDir);
        var target = Path.Combine(UpdatesDir, info.FileName);
        if (File.Exists(target) && await HashAsync(target, ct) == info.Sha256)
        {
            return target;
        }

        foreach (var old in Directory.GetFiles(UpdatesDir))
        {
            TryDelete(old);
        }

        var temp = target + ".download";
        using (var response = await _http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(temp);
            await response.Content.CopyToAsync(file, ct);
        }

        var hash = await HashAsync(temp, ct);
        if (hash != info.Sha256)
        {
            TryDelete(temp);
            throw new InvalidOperationException("File tải về không khớp mã kiểm tra, đã huỷ cập nhật.");
        }

        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// Puts the new exe in place and starts it; the caller then exits. A file named like a release
    /// ("todoX-ReupVideo-1.0.0.exe") is replaced by the new release name next to it; any other name is kept.
    /// The new process waits for this one to exit and deletes the old file.
    /// </summary>
    public static void ApplyAndRestart(string newExe, string newFileName, bool startMinimized)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được file ứng dụng.");
        var folder = Path.GetDirectoryName(current)!;
        string target;
        string cleanup;
        if (ExeName().IsMatch(Path.GetFileName(current)))
        {
            target = Path.Combine(folder, newFileName);
            File.Copy(newExe, target, overwrite: true);
            cleanup = current;
        }
        else
        {
            // A running exe cannot be overwritten but can be renamed.
            cleanup = current + ".old";
            File.Move(current, cleanup, overwrite: true);
            File.Copy(newExe, current, overwrite: true);
            target = current;
        }

        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = folder };
        start.ArgumentList.Add("--after-update");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--cleanup");
        start.ArgumentList.Add(cleanup);
        if (startMinimized)
        {
            start.ArgumentList.Add("--minimized");
        }

        Process.Start(start);
    }

    /// <summary>Called by the new process: wait for the old one, then remove the old file (best effort).</summary>
    public static void FinishUpdate(string[] args)
    {
        var pidAt = Array.IndexOf(args, "--after-update");
        if (pidAt >= 0 && pidAt + 1 < args.Length && int.TryParse(args[pidAt + 1], out var pid))
        {
            try
            {
                using var old = Process.GetProcessById(pid);
                old.WaitForExit(30_000);
            }
            catch (ArgumentException)
            {
                // already gone
            }
        }

        var cleanAt = Array.IndexOf(args, "--cleanup");
        var cleanup = cleanAt >= 0 && cleanAt + 1 < args.Length ? args[cleanAt + 1] : null;
        var current = Environment.ProcessPath;
        foreach (var path in new[] { cleanup, current is null ? null : current + ".old" })
        {
            if (path is not null && !string.Equals(path, current, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            {
                for (var i = 0; i < 10 && File.Exists(path); i++)
                {
                    TryDelete(path);
                    if (File.Exists(path))
                    {
                        Thread.Sleep(500);
                    }
                }
            }
        }
    }

    internal static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));

    [GeneratedRegex(@"^todoX-ReupVideo-[0-9][0-9.]*\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex ExeName();

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // in use: next time
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}
