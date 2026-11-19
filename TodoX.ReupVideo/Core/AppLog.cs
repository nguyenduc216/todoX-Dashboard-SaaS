using Microsoft.Win32;

namespace TodoX.ReupVideo.Core;

/// <summary>Log shown in the window and kept per day in logs\yyyy-MM-dd.log.</summary>
public sealed class AppLog : IProgress<string>
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public event Action<string>? Line;

    public AppLog(AppPaths paths)
    {
        _paths = paths;
    }

    public void Report(string value)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {value}";
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_paths.LogsDir);
                File.AppendAllText(Path.Combine(_paths.LogsDir, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // logging must never break a run
        }

        Line?.Invoke(line);
    }
}

/// <summary>"Khởi động cùng Windows": a per-user Run entry (no admin rights), started minimized to the tray.</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TodoXReupVideo";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
