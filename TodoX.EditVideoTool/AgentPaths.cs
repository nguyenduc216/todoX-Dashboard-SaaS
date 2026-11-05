namespace TodoX.EditVideoTool;

/// <summary>
/// Per-user install layout. Everything lives under %LOCALAPPDATA% so installing needs no admin rights.
/// </summary>
public sealed class AgentPaths
{
    public AgentPaths(string root)
    {
        Root = root;
    }

    public string Root { get; }
    public string ComponentsDir => Path.Combine(Root, "components");
    public string LicenseFile => Path.Combine(Root, "license.json");
    public string LogsDir => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string SkillsStateFile => Path.Combine(Root, "skills.json");

    public static AgentPaths Default()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("TODOX_EDITVIDEO_HOME");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new AgentPaths(overrideRoot);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new AgentPaths(Path.Combine(localAppData, "TodoX", "EditVideoTool"));
    }
}
