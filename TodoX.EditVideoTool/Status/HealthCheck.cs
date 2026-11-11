using System.Text.Json;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Setup;

namespace TodoX.EditVideoTool.Status;

public sealed record HealthItem(string Id, string Name, bool Ok, string Detail, string? Fix = null);

public sealed record HealthReport(
    string Version,
    bool Ok,
    bool ServerReachable,
    string LicenseState,
    string? LatestVersion,
    bool UpdateAvailable,
    IReadOnlyList<HealthItem> Items)
{
    public IEnumerable<HealthItem> Problems => Items.Where(i => !i.Ok);
}

/// <summary>
/// Pre-flight check run before every piece of work and by the window app's "Kiểm tra hệ thống":
/// components, Remotion + its browser, TodoX connectivity, license and updates. Problems are reported to TodoX
/// when it is reachable; when it is not, the user gets a Windows notification and the 15-second retry.
/// </summary>
public sealed class HealthCheck
{
    private readonly ComponentLocator _components;
    private readonly RemotionRenderer _remotion;
    private readonly LicenseGuard _license;
    private readonly TodoXServerClient _server;
    private readonly ServerConnectivity _connectivity;
    private readonly AgentPaths _paths;
    private readonly Transcriber _transcriber;

    public HealthCheck(ComponentLocator components, RemotionRenderer remotion, LicenseGuard license, TodoXServerClient server, ServerConnectivity connectivity, AgentPaths paths, Transcriber transcriber)
    {
        _transcriber = transcriber;
        _components = components;
        _remotion = remotion;
        _license = license;
        _server = server;
        _connectivity = connectivity;
        _paths = paths;
    }

    /// <param name="serverAttempts">Connectivity attempts (15 s apart); 0 = until cancelled.</param>
    public async Task<HealthReport> RunAsync(int serverAttempts = 1, Action<string>? status = null, bool reportProblems = true, CancellationToken ct = default)
    {
        var items = new List<HealthItem>();

        foreach (var (id, name) in new[] { ("ffmpeg", "Bộ ghép & xử lý video"), ("ffprobe", "Bộ đọc thông tin video"), ("node", ComponentNames.Of("node")) })
        {
            var c = _components.Locate(id);
            items.Add(new HealthItem(id, name, c.Installed, c.Installed ? "sẵn sàng" : "chưa cài", c.Installed ? null : "Chạy lại bộ cài TodoX EditVideo."));
        }

        var remotionDir = _remotion.RemotionDir;
        items.Add(new HealthItem("remotion", ComponentNames.Of("remotion"), remotionDir is not null,
            remotionDir is not null ? "sẵn sàng" : "chưa cài", remotionDir is null ? "Chạy lại bộ cài TodoX EditVideo." : null));
        var browser = remotionDir is not null && RemotionRenderer.HasRenderBrowser(remotionDir);
        items.Add(new HealthItem("browser", "Trình duyệt dựng hình", browser,
            browser ? "sẵn sàng" : "chưa tải (sẽ tự tải ~150 MB ở lần dựng đầu)", null));

        items.Add(new HealthItem("whisper", ComponentNames.Of("whisper"), _transcriber.IsAvailable,
            _transcriber.IsAvailable ? "sẵn sàng" : "chưa cài (tự tải khi cập nhật)", null));

        status?.Invoke("Đang kết nối máy chủ TodoX...");
        var reachable = await _connectivity.EnsureConnectedAsync(serverAttempts, status, ct);
        items.Add(new HealthItem("server", "Kết nối máy chủ TodoX", reachable,
            reachable ? _server.ServerUrl : "không kết nối được", reachable ? null : "Kiểm tra Internet / tường lửa, tool tự thử lại sau 15 giây."));

        var license = await _license.EnsureAsync(ct);
        items.Add(new HealthItem("license", "License", license.IsUsable, license.Message,
            license.IsUsable ? null : "Kích hoạt bằng license key TodoX trong cửa sổ TodoX EditVideo."));

        string? latest = null;
        string? minSupported = null;
        string? notes = null;
        var updateAvailable = false;
        if (reachable)
        {
            var release = await _server.LatestReleaseAsync(ct);
            if (release.Ok && release.Value.TryGetProperty("version", out var v))
            {
                latest = v.GetString();
                updateAvailable = IsNewer(latest, StatusService.Version);
                minSupported = release.Value.TryGetProperty("minSupported", out var m) ? m.GetString() : null;
                notes = release.Value.TryGetProperty("notes", out var n) ? n.GetString() : null;
            }
        }

        var outdated = IsNewer(minSupported, StatusService.Version);
        items.Add(new HealthItem("update", "Phiên bản", !updateAvailable,
            outdated ? $"{StatusService.Version} đã cũ, TodoX yêu cầu từ bản {minSupported} (bản mới nhất {latest})"
            : updateAvailable ? $"{StatusService.Version} → có bản mới {latest}" + (string.IsNullOrWhiteSpace(notes) ? "" : $" — {notes.Trim()}")
            : $"{StatusService.Version} (mới nhất)",
            updateAvailable ? $"Bản mới được tải tự động. {Updater.RestartHint}" : null));

        var report = new HealthReport(StatusService.Version, !outdated && items.All(i => i.Ok || i.Id is "browser" or "update" or "whisper"),
            reachable, license.Code, latest, updateAvailable, items);

        if (reportProblems && reachable && license.IsUsable && report.Problems.Any(p => p.Id is not "update"))
        {
            await using var session = await SessionLogger.StartAsync(_server, _paths, "health", ct);
            foreach (var p in report.Problems)
            {
                session.Error($"health_{p.Id}", p.Detail);
            }
        }

        return report;
    }

    public static bool IsNewer(string? candidate, string current) =>
        Version.TryParse(candidate, out var a) && Version.TryParse(current, out var b) && a > b;

    public static string Describe(HealthReport report) => JsonSerializer.Serialize(report, Mcp.StatusTools.Json);
}
