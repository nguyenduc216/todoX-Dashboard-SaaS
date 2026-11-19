namespace TodoX.ReupVideo;

/// <summary>Per-user data layout under %LOCALAPPDATA%\TodoX\ReupVideo (no admin rights needed).</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LibraryFile => Path.Combine(Root, "library.json");
    public string HistoryFile => Path.Combine(Root, "history.json");
    public string ToolsDir => Path.Combine(Root, "tools");
    public string YtDlpExe => Path.Combine(ToolsDir, "yt-dlp.exe");
    public string DownloadsDir => Path.Combine(Root, "downloads");
    public string LogsDir => Path.Combine(Root, "logs");

    public static AppPaths Default()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("TODOX_REUPVIDEO_HOME");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new AppPaths(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AppPaths(Path.Combine(localAppData, "TodoX", "ReupVideo"));
    }
}
