using System.Text.Json;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Tests;

public sealed class StatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "todox-editvideo-tests", Guid.NewGuid().ToString("N"));
    private readonly string _pathDir;

    public StatusTests()
    {
        Directory.CreateDirectory(_root);
        _pathDir = Path.Combine(_root, "on-path");
        Directory.CreateDirectory(_pathDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static LicenseGuard Guard(AgentPaths paths) =>
        new(new LicenseStore(paths), new LicenseVerifier(), new FakeDevice(), new LicenseApiClient(new HttpClient(new FakeHandler())));

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    [Fact]
    public async Task Fresh_install_reports_not_activated_and_missing_components()
    {
        var paths = new AgentPaths(Path.Combine(_root, "home"));
        var status = await new StatusService(paths, new ComponentLocator(paths, () => _pathDir), Guard(paths)).GetAsync();

        Assert.Equal("not_activated", status.LicenseState);
        Assert.All(status.Components, c => Assert.False(c.Installed));
        Assert.Equal(ComponentLocator.KnownComponents, status.Components.Select(c => c.Name));
    }

    [Fact]
    public void Todox_installed_component_wins_over_path()
    {
        var paths = new AgentPaths(Path.Combine(_root, "home"));
        var bundledDir = Path.Combine(paths.ComponentsDir, "ffmpeg", "bin");
        Directory.CreateDirectory(bundledDir);
        File.WriteAllText(Path.Combine(bundledDir, Exe("ffmpeg")), "");
        File.WriteAllText(Path.Combine(_pathDir, Exe("ffmpeg")), "");
        File.WriteAllText(Path.Combine(_pathDir, Exe("node")), "");

        var locator = new ComponentLocator(paths, () => _pathDir);

        var ffmpeg = locator.Locate("ffmpeg");
        Assert.Equal("todox", ffmpeg.Source);
        Assert.StartsWith(paths.ComponentsDir, ffmpeg.Path);
        Assert.Equal("path", locator.Locate("node").Source);
        Assert.False(locator.Locate("ffprobe").Installed);
    }

    [Fact]
    public async Task Health_check_offline_reports_every_problem_without_calling_home()
    {
        var paths = new AgentPaths(Path.Combine(_root, "home"));
        var locator = new ComponentLocator(paths, () => _pathDir);
        var handler = new FakeHandler();
        handler.Replies.Enqueue((System.Net.HttpStatusCode.OK, null)); // ping fails
        var server = new TodoX.EditVideoTool.Server.TodoXServerClient(new HttpClient(handler), new LicenseStore(paths), new FakeDevice());
        var notifier = new CountingNotifier();
        var connectivity = new TodoX.EditVideoTool.Server.ServerConnectivity(server, notifier, (_, _) => Task.CompletedTask);
        var health = new HealthCheck(locator, new TodoX.EditVideoTool.Media.RemotionRenderer(paths, locator), Guard(paths), server, connectivity, paths);

        var report = await health.RunAsync(serverAttempts: 1);

        Assert.False(report.Ok);
        Assert.False(report.ServerReachable);
        Assert.Equal("not_activated", report.LicenseState);
        Assert.Contains(report.Problems, p => p.Id == "server");
        Assert.Contains(report.Problems, p => p.Id == "ffmpeg");
        Assert.Single(notifier.Shown);
        Assert.Single(handler.Requests); // no session/log upload when offline
        using var doc = JsonDocument.Parse(HealthCheck.Describe(report));
        Assert.Equal(StatusService.Version, doc.RootElement.GetProperty("version").GetString());
    }
}
