using System.Text.Json;
using TodoX.EditVideoTool.Components;
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

    private static string Exe(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    [Fact]
    public void Fresh_install_reports_not_activated_and_missing_components()
    {
        var paths = new AgentPaths(Path.Combine(_root, "home"));
        var status = new StatusService(paths, new ComponentLocator(paths, () => _pathDir)).Get();

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
    public void Status_tool_returns_camel_case_json()
    {
        var paths = new AgentPaths(Path.Combine(_root, "home"));
        var tool = new StatusTools(new StatusService(paths, new ComponentLocator(paths, () => _pathDir)));

        using var doc = JsonDocument.Parse(tool.Status());

        Assert.Equal("not_activated", doc.RootElement.GetProperty("licenseState").GetString());
        Assert.Equal(StatusService.Version, doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(3, doc.RootElement.GetProperty("components").GetArrayLength());
    }
}
