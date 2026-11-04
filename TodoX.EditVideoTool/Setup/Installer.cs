using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Setup;

/// <summary>
/// Per-user install (no admin): copies the exe to %LOCALAPPDATA%\TodoX\EditVideoTool\app, pulls components from
/// TodoX's download server (checksum-verified), pre-loads the render browser, registers Claude/Codex, creates
/// shortcuts and an "Apps & features" entry.
/// </summary>
public sealed class Installer
{
    public const string ProductName = "TodoX EditVideo";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TodoXEditVideo";

    private readonly AgentPaths _paths;
    private readonly ComponentInstaller _components;
    private readonly ComponentLocator _locator;
    private readonly RemotionRenderer _remotion;
    private readonly LicenseGuard _license;
    private readonly ClientRegistrar _clients = new();

    public Installer(AgentPaths paths, TodoXServerClient server, ComponentLocator locator, RemotionRenderer remotion, LicenseGuard license)
    {
        _paths = paths;
        _components = new ComponentInstaller(server, paths);
        _locator = locator;
        _remotion = remotion;
        _license = license;
    }

    public string InstalledExe => Path.Combine(_paths.Root, "app", "todox-editvideo.exe");

    public bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ClientStatus> Clients => _clients.Detect(InstalledExe);

    public async Task<bool> InstallAsync(IProgress<string> status, CancellationToken ct)
    {
        status.Report("Bắt đầu cài TodoX EditVideo...");
        Directory.CreateDirectory(Path.GetDirectoryName(InstalledExe)!);
        if (!IsRunningInstalled)
        {
            File.Copy(Environment.ProcessPath!, InstalledExe, overwrite: true);
            status.Report($"Đã chép chương trình vào {InstalledExe}");
        }

        status.Report("Đang lấy danh sách thành phần từ máy chủ TodoX...");
        var manifest = await _components.LatestAsync(ct);
        if (manifest is null)
        {
            status.Report("LỖI: không kết nối được máy chủ TodoX để tải thành phần. Kiểm tra Internet rồi chạy lại.");
            return false;
        }

        foreach (var file in manifest.Components)
        {
            if (_components.InstalledVersion(file.Component!) == (file.Version ?? file.Sha256))
            {
                status.Report($"{ComponentNames.Of(file.Component)}: đã có bản mới nhất");
                continue;
            }

            status.Report($"Đang cài {ComponentNames.Of(file.Component)} ({file.Size / 1048576.0:0} MB)...");
            await _components.InstallComponentAsync(file, status, ct);
        }

        await PrefetchBrowserAsync(status, ct);

        var registered = _clients.RegisterAll(InstalledExe, ReadSkill());
        status.Report(registered.Count > 0
            ? $"Đã kết nối với: {string.Join(", ", registered)}. Hãy khởi động lại các ứng dụng này."
            : "Chưa thấy Claude Desktop / Claude Code / Codex trên máy. Cài một trong số đó rồi bấm \"Kết nối\" trong cửa sổ TodoX.");

        CreateShortcuts();
        WriteUninstallEntry(manifest.Version);
        status.Report("Cài đặt hoàn tất.");
        return true;
    }

    public IReadOnlyList<string> Reconnect() => _clients.RegisterAll(InstalledExe, ReadSkill());

    public void Uninstall(IProgress<string> status)
    {
        try
        {
            _license.DeactivateAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Offline uninstall still removes local files; the slot can be freed from the dashboard.
        }

        _clients.UnregisterAll();
        foreach (var lnk in ShortcutPaths())
        {
            if (File.Exists(lnk))
            {
                File.Delete(lnk);
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        foreach (var dir in new[] { _paths.ComponentsDir, Path.Combine(_paths.Root, "cache"), Path.Combine(_paths.Root, "updates") })
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        status.Report("Đã gỡ TodoX EditVideo. Thư mục chương trình sẽ được xoá khi đóng cửa sổ này.");
        if (IsRunningInstalled)
        {
            // The running exe cannot delete itself; a short-lived cmd does it after we exit.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 >nul & rmdir /s /q \"{Path.GetDirectoryName(InstalledExe)}\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
        else if (File.Exists(InstalledExe))
        {
            Directory.Delete(Path.GetDirectoryName(InstalledExe)!, recursive: true);
        }
    }

    private async Task PrefetchBrowserAsync(IProgress<string> status, CancellationToken ct)
    {
        var dir = _remotion.RemotionDir;
        var node = _locator.Locate("node");
        if (dir is null || !node.Installed || RemotionRenderer.HasRenderBrowser(dir))
        {
            return;
        }

        status.Report("Đang tải trình duyệt dựng hình (~150 MB, chỉ một lần)...");
        var psi = new ProcessStartInfo(node.Path!) { WorkingDirectory = dir, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(Path.Combine(dir, "render.mjs"));
        psi.ArgumentList.Add("--ensure-browser");
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        _ = p.StandardError.ReadToEndAsync(ct);
        var lastPct = -10;
        while (await p.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            var evt = RemotionRenderer.ParseEvent(line);
            if (evt?.Type == "browser_download" && (int)(evt.Percent * 100) - lastPct >= 10)
            {
                lastPct = (int)(evt.Percent * 100);
                status.Report($"  Trình duyệt dựng hình: {lastPct}%");
            }
        }

        await p.WaitForExitAsync(ct);
    }

    internal static string ReadSkill()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SKILL.md");
        return stream is null ? string.Empty : new StreamReader(stream).ReadToEnd();
    }

    private static IEnumerable<string> ShortcutPaths() =>
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.lnk"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{ProductName}.lnk")
    ];

    private void CreateShortcuts()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            foreach (var path in ShortcutPaths())
            {
                dynamic link = shell.CreateShortcut(path);
                link.TargetPath = InstalledExe;
                link.Arguments = "app";
                link.WorkingDirectory = Path.GetDirectoryName(InstalledExe);
                link.Description = "TodoX EditVideo – dựng video bằng AI";
                link.IconLocation = InstalledExe + ",0";
                link.Save();
            }
        }
        catch (Exception)
        {
            // Shortcuts are a convenience; the tool works without them.
        }
    }

    private void WriteUninstallEntry(string version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "TodoX – todox.vn");
        key.SetValue("URLInfoAbout", "https://todox.vn");
        key.SetValue("HelpLink", "https://todox.vn");
        key.SetValue("DisplayIcon", InstalledExe);
        key.SetValue("InstallLocation", _paths.Root);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }
}
