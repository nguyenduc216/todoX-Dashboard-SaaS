using Microsoft.Win32;

namespace TodoX.EditVideoTool;

/// <summary>
/// Per-user install layout: one folder (chosen in the setup wizard) holds the program (app\), the components,
/// the license, settings and logs. Everything is per user, so installing needs no admin rights.
/// </summary>
public sealed class AgentPaths
{
    public const string ExeName = "todox-editvideo.exe";
    public const string MarkerFileName = "install.json";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TodoXEditVideo";

    public AgentPaths(string root)
    {
        Root = root;
    }

    public string Root { get; }
    public string AppDir => Path.Combine(Root, "app");
    public string AppExe => Path.Combine(AppDir, ExeName);
    public string InstallMarker => Path.Combine(Root, MarkerFileName);
    public string ComponentsDir => Path.Combine(Root, "components");
    public string LicenseFile => Path.Combine(Root, "license.json");
    public string LogsDir => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string SkillsStateFile => Path.Combine(Root, "skills.json");

    /// <summary>Where versions before 1.1 always installed; still found and updated in place.</summary>
    public static string LegacyRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodoX", "EditVideoTool");

    /// <summary>Default folder offered by the setup wizard, next to other per-user programs.</summary>
    public static string DefaultInstallRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TodoX EditVideo");

    /// <summary>
    /// The installed exe finds its own folder; anything else (the downloaded setup, Claude's first start) uses the
    /// folder Windows has registered for TodoX EditVideo, then the pre-1.1 default.
    /// </summary>
    public static AgentPaths Default()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_HOME");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new AgentPaths(overrideRoot);
        }

        if (RootOfInstalledExe(Environment.ProcessPath) is { } own)
        {
            return new AgentPaths(own);
        }

        return new AgentPaths(RegisteredRoot() is { } registered && Directory.Exists(registered) ? registered : LegacyRoot);
    }

    /// <summary>"…\TodoX EditVideo" for "…\TodoX EditVideo\app\todox-editvideo.exe" when that folder is an install.</summary>
    internal static string? RootOfInstalledExe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !string.Equals(Path.GetFileName(exePath), ExeName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var appDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
        if (appDir is null || !string.Equals(Path.GetFileName(appDir), "app", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var root = Path.GetDirectoryName(appDir);
        return root is not null && (File.Exists(Path.Combine(root, MarkerFileName)) || Directory.Exists(Path.Combine(root, "components")))
            ? root
            : null;
    }

    public static string? RegisteredRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string is { Length: > 0 } location ? location : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
