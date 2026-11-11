using System.Diagnostics;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Setup;

/// <summary>What the customer should know about updates right now.</summary>
public sealed record UpdateInfo(
    string Current,
    string? Latest,
    string? MinSupported,
    string? Notes,
    /// <summary>The newer exe is already on disk; it runs after Claude/Codex (or the window) restarts.</summary>
    bool Staged)
{
    public bool UpdateAvailable => HealthCheck.IsNewer(Latest, Current);

    /// <summary>TodoX no longer supports the running version: tools stop until the customer restarts on the new one.</summary>
    public bool BelowMinimum => HealthCheck.IsNewer(MinSupported, Current);
}

/// <summary>
/// Updates from TodoX's release manifest. A newer exe is downloaded, verified and swapped in place (Windows lets a
/// running exe be renamed), so the next start runs the new version. Components are updated by the window app /
/// "update" command, never in the middle of an MCP session. The customer is told through a Windows notification,
/// the window app's banner, and a one-time note Claude/Codex passes on.
/// </summary>
public sealed class Updater
{
    public const string RestartHint = "Tắt hẳn rồi mở lại Claude/Codex (và cửa sổ TodoX EditVideo nếu đang mở) để dùng bản mới.";
    private static readonly TimeSpan RecheckEvery = TimeSpan.FromHours(6);

    private readonly ComponentInstaller _components;
    private readonly AgentPaths _paths;
    private readonly INotifier _notifier;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ReleaseManifest? _last;
    private string? _stagedVersion;
    private string? _notifiedVersion;
    private int _assistantNoticeTaken;

    public Updater(TodoXServerClient server, AgentPaths paths, INotifier notifier)
    {
        _components = new ComponentInstaller(server, paths);
        _paths = paths;
        _notifier = notifier;
    }

    public string InstalledExe => Path.Combine(_paths.Root, "app", "todox-editvideo.exe");

    /// <summary>Latest known state, from the last manifest fetched in this process (no network).</summary>
    public UpdateInfo Current => Describe(_last);

    /// <summary>Fetches the manifest and reports, without downloading anything.</summary>
    public async Task<UpdateInfo> CheckAsync(CancellationToken ct)
    {
        var manifest = await _components.LatestAsync(ct);
        if (manifest is not null)
        {
            _last = manifest;
        }

        return Describe(_last);
    }

    /// <summary>MCP mode: check now and every few hours, so a Claude left open for days still gets the update.</summary>
    public async Task RunPeriodicallyAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAndStageAsync(ct, notify: true);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Updates must never break the MCP session.
            }

            try
            {
                await Task.Delay(RecheckEvery, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Safe while the MCP server runs: installs components this machine does not have yet (a feature added after
    /// the customer installed, e.g. Whisper — nothing is using them), then stages a newer exe. Components that
    /// already exist are only replaced by the window app / "update", never under a running edit.
    /// Returns the staged exe version or null.
    /// </summary>
    public async Task<string?> CheckAndStageAsync(CancellationToken ct, IProgress<string>? status = null, bool notify = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            CleanupOldExes();
            var manifest = await _components.LatestAsync(ct);
            if (manifest is null)
            {
                return null;
            }

            _last = manifest;
            foreach (var missing in manifest.Components.Where(f => _components.InstalledVersion(f.Component!) is null))
            {
                status?.Report($"Cài thêm thành phần mới: {ComponentNames.Of(missing.Component)}");
                await _components.InstallComponentAsync(missing, status, ct);
            }

            if (manifest.App is not { } app || !HealthCheck.IsNewer(manifest.Version, StatusService.Version))
            {
                return null;
            }

            // Already swapped in by this or another TodoX process (window app, an earlier Claude session).
            if (!IsStaged(manifest.Version))
            {
                if (!IsRunningInstalled)
                {
                    return null;
                }

                status?.Report($"Đang tải TodoX EditVideo {manifest.Version}...");
                var downloaded = await _components.DownloadVerifiedAsync(app, status, ct);
                SwapExe(downloaded);
            }

            _stagedVersion = manifest.Version;
            if (notify && _notifiedVersion != manifest.Version)
            {
                _notifiedVersion = manifest.Version;
                _notifier.Show($"TodoX EditVideo {manifest.Version} đã sẵn sàng", ToastText(manifest));
            }

            return manifest.Version;
        }
        finally
        {
            _gate.Release();
        }
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
            : $"Đã tải bản {staged}. {RestartHint}";
    }

    /// <summary>
    /// Text Claude/Codex should pass on to the customer, given once per MCP session when a newer version exists.
    /// </summary>
    public string? TakeAssistantNotice()
    {
        var info = Current;
        if (!info.UpdateAvailable || info.BelowMinimum || Interlocked.Exchange(ref _assistantNoticeTaken, 1) == 1)
        {
            return null;
        }

        return $"[Thông báo TodoX cho người dùng] Đã có TodoX EditVideo {info.Latest}" +
               (CleanNotes(info.Latest, info.Notes) is { Length: > 0 } notes ? $" ({notes})" : "") +
               $". Bản mới {(info.Staged ? "đã được tải về máy" : "đang được tải về")}. {RestartHint} " +
               "Hãy báo ngắn gọn cho người dùng một lần ở cuối câu trả lời; công việc hiện tại vẫn làm tiếp bình thường.";
    }

    /// <summary>Message that stops the tools when TodoX no longer supports this version; null when it is fine.</summary>
    public string? MinimumProblem()
    {
        var info = Current;
        if (!info.BelowMinimum)
        {
            return null;
        }

        return $"Bản TodoX EditVideo trên máy ({info.Current}) đã cũ, TodoX yêu cầu từ bản {info.MinSupported} trở lên. " +
               $"Bản mới {(info.Staged ? "đã được tải về máy" : "đang được tải về")}: {RestartHint} " +
               "Nếu vẫn chưa được, mở TodoX EditVideo và bấm \"Cập nhật ngay\".";
    }

    internal static string ToastText(ReleaseManifest manifest)
    {
        var notes = CleanNotes(manifest.Version, manifest.Notes);

        // Windows cuts balloon text at about 250 characters; keep the restart hint visible.
        var room = 240 - RestartHint.Length;
        if (notes.Length > room)
        {
            notes = notes[..Math.Max(0, room - 1)].TrimEnd() + "…";
        }

        return notes.Length == 0 ? RestartHint : $"{notes}\n{RestartHint}";
    }

    /// <summary>Release notes without the leading "1.2.0: " the release script writes.</summary>
    public static string CleanNotes(string? version, string? notes)
    {
        var text = notes?.Trim() ?? "";
        var prefix = $"{version}: ";
        return version is not null && text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..].Trim() : text;
    }

    private UpdateInfo Describe(ReleaseManifest? manifest) =>
        new(StatusService.Version, manifest?.Version, manifest?.MinSupported, manifest?.Notes,
            manifest is not null && (_stagedVersion == manifest.Version || IsStaged(manifest.Version)));

    private bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the installed exe on disk is already this version (or newer) even if an older one runs.</summary>
    private bool IsStaged(string version)
    {
        try
        {
            if (!File.Exists(InstalledExe))
            {
                return false;
            }

            var onDisk = FileVersionInfo.GetVersionInfo(InstalledExe).ProductVersion?.Split('+')[0];
            return Version.TryParse(onDisk, out var disk) && Version.TryParse(version, out var wanted) && disk >= wanted;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SwapExe(string downloaded)
    {
        var current = InstalledExe;
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
