using System.Text.Json;

namespace TodoX.EditVideoTool;

/// <summary>
/// Choices the customer can change in the TodoX window, kept in settings.json next to the license.
/// </summary>
public sealed class UserSettings
{
    public const int DefaultSkillSyncHours = 6;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Folder that holds one sub-folder per video project (photos, clips, logos the customer adds).</summary>
    public string? ResourceRoot { get; set; }

    /// <summary>How often a running tool checks TodoX for new or changed skills.</summary>
    public int SkillSyncHours { get; set; } = DefaultSkillSyncHours;

    public static string DefaultResourceRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TodoX EditVideo");

    public string EffectiveResourceRoot => string.IsNullOrWhiteSpace(ResourceRoot) ? DefaultResourceRoot : ResourceRoot;

    public TimeSpan SkillSyncInterval => TimeSpan.FromHours(Math.Clamp(SkillSyncHours, 1, 48));

    public static UserSettings Load(AgentPaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(paths.SettingsFile), Json) ?? new UserSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged settings file falls back to defaults rather than stopping the tool.
        }

        return new UserSettings();
    }

    public void Save(AgentPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, Json));
    }
}
