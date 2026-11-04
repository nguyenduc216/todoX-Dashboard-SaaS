using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Setup;

/// <summary>
/// Checks TodoX's release manifest at start-up. A newer exe is downloaded, verified and swapped in place (Windows
/// lets a running exe be renamed), so the next start runs the new version. Components are updated by the window
/// app / "update" command, never in the middle of an MCP session.
/// </summary>
public sealed class Updater
{
    private readonly ComponentInstaller _components;
    private readonly AgentPaths _paths;

    public Updater(TodoXServerClient server, AgentPaths paths)
    {
        _components = new ComponentInstaller(server, paths);
        _paths = paths;
    }

    /// <summary>
    /// Safe while the MCP server runs: installs components this machine does not have yet (a feature added after
    /// the customer installed, e.g. Whisper — nothing is using them), then stages a newer exe. Components that
    /// already exist are only replaced by the window app / "update", never under a running edit.
    /// Returns the staged exe version or null.
    /// </summary>
    public async Task<string?> CheckAndStageAsync(CancellationToken ct, IProgress<string>? status = null)
    {
        CleanupOldExes();
        var manifest = await _components.LatestAsync(ct);
        if (manifest is null)
        {
            return null;
        }

        foreach (var missing in manifest.Components.Where(f => _components.InstalledVersion(f.Component!) is null))
        {
            status?.Report($"Cài thêm thành phần mới: {ComponentNames.Of(missing.Component)}");
            await _components.InstallComponentAsync(missing, status, ct);
        }

        if (manifest.App is not { } app || !HealthCheck.IsNewer(manifest.Version, StatusService.Version))
        {
            return null;
        }

        var downloaded = await _components.DownloadVerifiedAsync(app, null, ct);
        SwapExe(downloaded, manifest.Version);
        return manifest.Version;
    }

    public async Task<string> CheckAndApplyAsync(IProgress<string> status, CancellationToken ct)
    {
        var manifest = await _components.LatestAsync(ct);
        if (manifest is null)
        {
            return ServerConnectivity.OfflineText;
        }

        foreach (var file in manifest.Components.Where(f => _components.InstalledVersion(f.Component!) != (f.Version ?? f.Sha256)))
        {
            status.Report($"Cập nhật {ComponentNames.Of(file.Component)}");
            await _components.InstallComponentAsync(file, status, ct);
        }

        var staged = await CheckAndStageAsync(ct, status);
        return staged is null
            ? $"Đang dùng bản mới nhất ({StatusService.Version})."
            : $"Đã tải bản {staged}. Đóng và mở lại TodoX EditVideo (và Claude/Codex) để dùng bản mới.";
    }

    private void SwapExe(string downloaded, string version)
    {
        var current = Environment.ProcessPath!;
        var installed = Path.Combine(_paths.Root, "app", "todox-editvideo.exe");
        // Only the installed copy updates itself; a downloaded setup file is left alone.
        if (!string.Equals(Path.GetFullPath(current), Path.GetFullPath(installed), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var old = $"{current}.{StatusService.Version}.old";
        File.Move(current, old, overwrite: true);
        try
        {
            File.Move(downloaded, current);
        }
        catch (Exception)
        {
            File.Move(old, current, overwrite: true);
            throw;
        }
    }

    private void CleanupOldExes()
    {
        var dir = Path.Combine(_paths.Root, "app");
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var old in Directory.EnumerateFiles(dir, "*.old"))
        {
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
