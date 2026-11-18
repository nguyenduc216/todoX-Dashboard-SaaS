using System.Diagnostics;

namespace TodoX.EditVideoTool.Server;

/// <summary>Whether the TodoX EditVideo window is open on this computer, and opening it.</summary>
public interface IAppWindow
{
    bool IsRunning { get; }

    /// <summary>The customer allowed the assistant to open the window by itself (setup tick, Settings, or in chat).</summary>
    bool AutoOpen { get; set; }

    /// <summary>Opens the window and waits until it is up; false when it did not start.</summary>
    Task<bool> OpenAsync(CancellationToken ct);
}

/// <summary>
/// The window holds a named mutex while it runs (<see cref="Hold"/>); the MCP server (another process started by
/// Claude) checks it and starts "todox-editvideo.exe window" when allowed.
/// </summary>
public sealed class AppWindow : IAppWindow
{
    public const string MutexName = @"Local\TodoX.EditVideo.Window";

    private readonly AgentPaths _paths;

    public AppWindow(AgentPaths paths) => _paths = paths;

    /// <summary>Called by the window on start; keep the returned mutex alive until it closes.</summary>
    public static Mutex Hold() => new(initiallyOwned: false, MutexName);

    public bool IsRunning
    {
        get
        {
            if (!Mutex.TryOpenExisting(MutexName, out var m))
            {
                return false;
            }

            m.Dispose();
            return true;
        }
    }

    public bool AutoOpen
    {
        get => UserSettings.Load(_paths).AutoOpenApp == true;
        set
        {
            var s = UserSettings.Load(_paths);
            s.AutoOpenApp = value;
            s.Save(_paths);
        }
    }

    public async Task<bool> OpenAsync(CancellationToken ct)
    {
        if (IsRunning)
        {
            return true;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe, "window") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }

        var until = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(500, ct);
            if (IsRunning)
            {
                await Task.Delay(1500, ct); // let the window finish drawing before a dialog appears over it
                return true;
            }
        }

        return false;
    }
}
