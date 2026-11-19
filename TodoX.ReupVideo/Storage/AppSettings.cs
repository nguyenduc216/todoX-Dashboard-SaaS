using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoX.ReupVideo.Storage;

public enum ScheduleMode
{
    /// <summary>Only when the user presses "Thực hiện ngay".</summary>
    Manual,
    /// <summary>Every day at the listed times (HH:mm).</summary>
    DailyTimes,
    /// <summary>Every N minutes.</summary>
    Interval
}

public enum FacebookPostKind
{
    /// <summary>Reels (9:16, up to 90 s); longer videos fall back to a normal video post.</summary>
    Reels,
    /// <summary>Normal page video post.</summary>
    Video
}

public sealed class TikTokSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>Username without "@", e.g. "todox.vn".</summary>
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    /// <summary>Ticked = videos of this channel are cloned. New channels are ticked by default.</summary>
    public bool Enabled { get; set; } = true;
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastScanAt { get; set; }
    public string? LastScanResult { get; set; }

    [JsonIgnore] public string ProfileUrl => $"https://www.tiktok.com/@{Username}";
    public override string ToString() => DisplayName is { Length: > 0 } name ? $"@{Username} ({name})" : $"@{Username}";
}

public sealed class FacebookPage
{
    public string PageId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Page access token, DPAPI-protected (current Windows user only). Never stored in plain text.</summary>
    public string ProtectedToken { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public FacebookPostKind PostKind { get; set; } = FacebookPostKind.Reels;
    public DateTime? LastCheckAt { get; set; }
    public string? LastCheckResult { get; set; }

    public override string ToString() => $"{Name} ({PageId})";
}

public sealed class ScheduleSettings
{
    public ScheduleMode Mode { get; set; } = ScheduleMode.Manual;
    public List<string> DailyTimes { get; set; } = ["08:00", "12:00", "20:00"];
    public int IntervalMinutes { get; set; } = 120;
    public DateTime? LastRunAt { get; set; }
}

public sealed class PostSettings
{
    /// <summary>Newest links fetched per TikTok channel on each update.</summary>
    public int ScanLimitPerChannel { get; set; } = 20;
    /// <summary>At most this many TikTok videos are published per run (each to every ticked fanpage).</summary>
    public int MaxVideosPerRun { get; set; } = 3;
    /// <summary>Pause between two Facebook posts, to look less like spam.</summary>
    public int DelaySecondsBetweenPosts { get; set; } = 30;
    public bool NewestFirst { get; set; } = true;
    /// <summary>0 = no limit. Otherwise TikTok videos older than this are not posted.</summary>
    public int SkipOlderThanDays { get; set; }
    /// <summary>{caption} = full TikTok text with hashtags, {text} = text without hashtags, {hashtags}, {author}, {link}.</summary>
    public string CaptionTemplate { get; set; } = "{caption}";
    public string ExtraHashtags { get; set; } = "";
    public bool KeepDownloadedFiles { get; set; }
    /// <summary>Optional Netscape cookies.txt for TikTok when it blocks anonymous listing.</summary>
    public string? TikTokCookiesFile { get; set; }
    /// <summary>A link/page pair that failed this many times is not retried automatically.</summary>
    public int MaxAttempts { get; set; } = 3;
}

public sealed class AppSettings
{
    public List<TikTokSource> TikTokSources { get; set; } = [];
    public List<FacebookPage> FacebookPages { get; set; } = [];
    public ScheduleSettings Schedule { get; set; } = new();
    public PostSettings Post { get; set; } = new();
    public bool StartWithWindows { get; set; }

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load(AppPaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(paths.SettingsFile), Json) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // A broken settings file must not stop the app; keep a copy for support and start fresh.
            TryBackup(paths.SettingsFile);
        }

        return new AppSettings();
    }

    public void Save(AppPaths paths)
    {
        var temp = paths.SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, paths.SettingsFile, overwrite: true);
    }

    internal static void TryBackup(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Copy(file, file + $".broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
        }
        catch (Exception)
        {
            // best effort
        }
    }
}
