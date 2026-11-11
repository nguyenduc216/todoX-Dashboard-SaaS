using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Setup;

/// <summary>A TodoX EditVideo already on this machine; the setup then updates it in its own folder.</summary>
public sealed record ExistingInstall(string Root, string? Version);

/// <summary>
/// Per-user install (no admin) into the folder picked in the setup wizard: copies the exe to {folder}\app, pulls
/// components from TodoX's download server (checksum-verified), pre-loads the render browser, connects the chosen
/// assistants, creates shortcuts, optionally starts with Windows, and registers in "Apps &amp; features".
/// Every step is written to an <see cref="InstallLog"/>.
/// </summary>
public sealed class Installer
{
    public const string ProductName = "TodoX EditVideo";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "TodoX EditVideo";

    private readonly AgentPaths _paths;
    private readonly ComponentInstaller _components;
    private readonly ComponentLocator _locator;
    private readonly RemotionRenderer _remotion;
    private readonly LicenseGuard _license;
    private readonly SkillSync _skills;
    private readonly ClientRegistrar _clients = new();

    public Installer(AgentPaths paths, TodoXServerClient server, ComponentLocator locator, RemotionRenderer remotion, LicenseGuard license, SkillSync skills)
    {
        _skills = skills;
        _paths = paths;
        _components = new ComponentInstaller(server, paths);
        _locator = locator;
        _remotion = remotion;
        _license = license;
    }

    public string InstalledExe => _paths.AppExe;

    public bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<ClientStatus> Clients => _clients.Detect(InstalledExe);

    /// <summary>The install Windows knows about, else one in the pre-1.1 or default folder.</summary>
    public static ExistingInstall? FindExisting()
    {
        var candidates = new[] { AgentPaths.RegisteredRoot(), AgentPaths.LegacyRoot, AgentPaths.DefaultInstallRoot };
        foreach (var root in candidates.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exe = new AgentPaths(root).AppExe;
            if (File.Exists(exe))
            {
                return new ExistingInstall(root, ExeVersion(exe));
            }
        }

        return null;
    }

    public static string? ExeVersion(string exe)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0];
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Null when TodoX EditVideo can be installed in this folder, else why not (for the wizard).</summary>
    public static string? CheckFolder(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
        {
            return "Chọn một thư mục đầy đủ, ví dụ C:\\Users\\...\\TodoX EditVideo.";
        }

        if (Path.GetPathRoot(root) is { } drive && string.Equals(Path.TrimEndingDirectorySeparator(drive), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            return "Không cài thẳng vào gốc ổ đĩa. Chọn hoặc tạo một thư mục riêng.";
        }

        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".todox-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return "Không ghi được vào thư mục này (thư mục cần quyền quản trị hoặc ổ đĩa không dùng được). Hãy chọn thư mục khác.";
        }
    }

    /// <summary>Command-line install ("install"): every assistant found, shortcuts, start with Windows.</summary>
    public async Task<bool> InstallAsync(IProgress<string> status, CancellationToken ct)
    {
        var existing = FindExisting();
        var log = new InstallLog(existing is null ? "install" : "update", existing?.Version, _paths.Root, new InstallOptions());
        log.Line += e => status.Report(e.Message);
        var ok = await InstallAsync(new InstallOptions(), log, ct);
        log.Finish(ok);
        log.Save(_paths);
        return ok;
    }

    /// <summary>
    /// Runs every install step. Program, server and components must succeed; the rest (browser, assistants,
    /// skills, shortcuts) are recorded as failed but do not stop the install.
    /// </summary>
    public async Task<bool> InstallAsync(InstallOptions options, InstallLog log, CancellationToken ct)
    {
        log.Info($"TodoX EditVideo {StatusService.Version} · thư mục {_paths.Root}");

        if (!await StepAsync(log, "copy", "Chép chương trình TodoX EditVideo", () => Task.FromResult(CopyProgram())))
        {
            return false;
        }

        ReleaseManifest? manifest = null;
        if (!await StepAsync(log, "server", "Kết nối máy chủ TodoX", async () =>
            {
                manifest = await _components.LatestAsync(ct)
                           ?? throw new InvalidOperationException("Không kết nối được máy chủ TodoX. Kiểm tra Internet rồi bấm \"Thử lại\".");
                return $"bản mới nhất {manifest.Version}";
            }))
        {
            return false;
        }

        foreach (var file in manifest!.Components)
        {
            var id = "component-" + file.Component;
            var title = $"Cài {ComponentNames.Of(file.Component)}";
            if (!await StepAsync(log, id, title, async () =>
                {
                    if (_components.InstalledVersion(file.Component!) == (file.Version ?? file.Sha256))
                    {
                        return "đã có bản mới nhất";
                    }

                    log.Info($"Đang tải {file.Size / 1048576.0:0} MB...", id);
                    await _components.InstallComponentAsync(file, log.ProgressFor(id), ct);
                    return file.Version is null ? "đã cài" : $"đã cài bản {file.Version}";
                }))
            {
                return false;
            }
        }

        await StepAsync(log, "browser", "Tải trình duyệt dựng hình", () => PrefetchBrowserAsync(log.ProgressFor("browser"), ct));

        await StepAsync(log, "resources", "Tạo thư mục tư liệu dự án", () =>
        {
            var resources = UserSettings.Load(_paths).EffectiveResourceRoot;
            Directory.CreateDirectory(resources);
            return Task.FromResult(resources);
        });

        var detected = _clients.Detect(InstalledExe).Where(c => c.Detected).ToList();
        ConnectStep(log, "claude", "Kết nối vào Claude", detected.Where(c => ClientRegistrar.IsClaude(c.Id)).ToList(), options.ConnectClaude);
        ConnectStep(log, "codex", "Kết nối vào Codex", detected.Where(c => c.Id == "codex").ToList(), options.ConnectCodex);

        var license = await _license.EnsureAsync(ct);
        if (!license.IsUsable)
        {
            log.Skip("skills", "Tải bộ skill TodoX", "sẽ tải sau khi nhập license");
        }
        else if (!_clients.SkillRoots().Any())
        {
            log.Skip("skills", "Tải bộ skill TodoX", "máy chưa có Claude / Codex");
        }
        else
        {
            await StepAsync(log, "skills", "Tải bộ skill TodoX", async () =>
            {
                var result = await _skills.SyncAsync(notify: false, ct);
                return result.Ok ? result.Describe() : throw new InvalidOperationException(result.Describe());
            });
        }

        await StepAsync(log, "shortcuts", options.DesktopIcon ? "Tạo biểu tượng Desktop và Start menu" : "Tạo lối tắt trong Start menu",
            () => Task.FromResult(CreateShortcuts(options.DesktopIcon)));

        if (options.StartWithWindows)
        {
            await StepAsync(log, "startup", "Tự khởi động cùng Windows", () => Task.FromResult(SetStartWithWindows(true)));
        }
        else
        {
            SetStartWithWindows(false);
            log.Skip("startup", "Tự khởi động cùng Windows", "bạn đã bỏ chọn");
        }

        await StepAsync(log, "register", "Đăng ký với Windows", () =>
        {
            WriteUninstallEntry(StatusService.Version);
            WriteMarker(options);
            return Task.FromResult($"phiên bản {StatusService.Version}");
        });

        return true;
    }

    /// <summary>After a license is entered in the wizard: install the TodoX skills into Claude / Codex.</summary>
    public async Task<string> InstallSkillsAsync(InstallLog log, CancellationToken ct)
    {
        if (!_clients.SkillRoots().Any())
        {
            log.Skip("skills", "Tải bộ skill TodoX", "máy chưa có Claude / Codex");
            return "Máy chưa có Claude / Codex nên chưa cài bộ skill.";
        }

        var message = "";
        await StepAsync(log, "skills", "Tải bộ skill TodoX", async () =>
        {
            var result = await _skills.SyncAsync(notify: false, ct);
            message = result.Describe();
            return result.Ok ? message : throw new InvalidOperationException(message);
        });
        return message;
    }

    public IReadOnlyList<string> Reconnect() => _clients.RegisterAll(InstalledExe, ReadSkill());

    public bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
    }

    public void Uninstall(IProgress<string> status)
    {
        // The machine keeps its license slot: reinstalling here reactivates with the same key, and moving to
        // another machine goes through TodoX (dashboard). Only this account's local license file is removed.
        _license.ForgetLocal();

        _skills.RemoveAll();
        _clients.UnregisterAll();
        SetStartWithWindows(false);
        foreach (var lnk in ShortcutPaths(desktop: true))
        {
            if (File.Exists(lnk))
            {
                File.Delete(lnk);
            }
        }

        Registry.CurrentUser.DeleteSubKeyTree(AgentPaths.UninstallKey, throwOnMissingSubKey: false);
        foreach (var dir in new[] { _paths.ComponentsDir, Path.Combine(_paths.Root, "cache"), Path.Combine(_paths.Root, "updates") })
        {
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        if (File.Exists(_paths.InstallMarker))
        {
            File.Delete(_paths.InstallMarker);
        }

        status.Report("Đã gỡ TodoX EditVideo. Thư mục chương trình sẽ được xoá khi đóng cửa sổ này.");
        if (IsRunningInstalled)
        {
            // The running exe cannot delete itself; a short-lived cmd does it after we exit.
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 >nul & rmdir /s /q \"{_paths.AppDir}\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
        else if (File.Exists(InstalledExe))
        {
            Directory.Delete(_paths.AppDir, recursive: true);
        }
    }

    private static async Task<bool> StepAsync(InstallLog log, string id, string title, Func<Task<string>> run)
    {
        log.Start(id, title);
        try
        {
            log.Done(id, await run());
            return true;
        }
        catch (OperationCanceledException)
        {
            log.Fail(id, "đã huỷ");
            throw;
        }
        catch (Exception ex)
        {
            log.Fail(id, ex.Message);
            return false;
        }
    }

    private void ConnectStep(InstallLog log, string id, string title, IReadOnlyList<ClientStatus> found, bool wanted)
    {
        if (found.Count == 0)
        {
            return; // not on this machine: the wizard does not show it either
        }

        if (!wanted)
        {
            log.Skip(id, title, "bạn đã bỏ chọn");
            return;
        }

        log.Start(id, title);
        try
        {
            var ids = found.Select(c => c.Id).ToHashSet();
            var done = _clients.RegisterAll(InstalledExe, ReadSkill(), ids.Contains);
            log.Done(id, $"{string.Join(", ", done)} — tắt hẳn rồi mở lại để dùng TodoX");
        }
        catch (Exception ex)
        {
            log.Fail(id, ex.Message);
        }
    }

    /// <summary>Puts this exe in {folder}\app. A running copy (Claude still open) is moved aside, not overwritten.</summary>
    private string CopyProgram()
    {
        Directory.CreateDirectory(_paths.AppDir);
        if (IsRunningInstalled)
        {
            return "đang chạy từ thư mục cài đặt";
        }

        var target = InstalledExe;
        if (File.Exists(target) && HealthCheck.IsNewer(ExeVersion(target), StatusService.Version))
        {
            return $"giữ bản {ExeVersion(target)} trên máy (mới hơn bộ cài)";
        }

        try
        {
            File.Copy(Environment.ProcessPath!, target, overwrite: true);
        }
        catch (IOException)
        {
            // Claude/Codex is running the old exe: Windows lets it be renamed, and the new one takes its place.
            File.Move(target, $"{target}.{ExeVersion(target) ?? "old"}.old", overwrite: true);
            File.Copy(Environment.ProcessPath!, target, overwrite: true);
        }

        return target;
    }

    private async Task<string> PrefetchBrowserAsync(IProgress<string> status, CancellationToken ct)
    {
        var dir = _remotion.RemotionDir;
        var node = _locator.Locate("node");
        if (dir is null || !node.Installed)
        {
            return "sẽ tải ở lần dựng đầu tiên";
        }

        if (RemotionRenderer.HasRenderBrowser(dir))
        {
            return "đã có trên máy";
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
                status.Report($"Trình duyệt dựng hình: {lastPct}%");
            }
        }

        await p.WaitForExitAsync(ct);
        return RemotionRenderer.HasRenderBrowser(dir) ? "đã tải xong" : "chưa tải được, sẽ thử lại ở lần dựng đầu tiên";
    }

    internal static string ReadSkill()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SKILL.md");
        return stream is null ? string.Empty : new StreamReader(stream).ReadToEnd();
    }

    private static IEnumerable<string> ShortcutPaths(bool desktop)
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{ProductName}.lnk");
        if (desktop)
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{ProductName}.lnk");
        }
    }

    private string CreateShortcuts(bool desktop)
    {
        if (!desktop)
        {
            var desktopLink = ShortcutPaths(desktop: true).Last();
            if (File.Exists(desktopLink))
            {
                File.Delete(desktopLink);
            }
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows không cho tạo lối tắt.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        foreach (var path in ShortcutPaths(desktop))
        {
            dynamic link = shell.CreateShortcut(path);
            link.TargetPath = InstalledExe;
            link.Arguments = "app";
            link.WorkingDirectory = _paths.AppDir;
            link.Description = "TodoX EditVideo – dựng video bằng AI";
            link.IconLocation = InstalledExe + ",0";
            link.Save();
        }

        return desktop ? "Desktop và Start menu" : "Start menu";
    }

    /// <summary>At sign-in the exe runs "autostart": checks for updates and skills quietly, then exits.</summary>
    private string SetStartWithWindows(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            key.SetValue(RunValue, $"\"{InstalledExe}\" autostart");
            return "tự kiểm tra cập nhật mỗi khi mở máy";
        }

        key.DeleteValue(RunValue, throwOnMissingValue: false);
        return "đã tắt";
    }

    private void WriteMarker(InstallOptions options)
    {
        File.WriteAllText(_paths.InstallMarker, JsonSerializer.Serialize(new
        {
            version = StatusService.Version,
            installedAt = DateTimeOffset.Now,
            options
        }, InstallLog.Json));
    }

    private void WriteUninstallEntry(string version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AgentPaths.UninstallKey);
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
