using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Landing.Services;

namespace TodoX.Landing.Data;

public sealed class ShortVideoPackage
{
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public long Price { get; set; }
    public int VideoCount { get; set; }
    public int BonusVideos { get; set; }
    public List<string> Features { get; set; } = [];
    public bool Featured { get; set; }
}

public sealed class ShortVideoConfiguration
{
    // Default visible: the landing keeps showing the entry point when settings are not available yet.
    public bool ShowOnLanding { get; set; } = true;
    public IntroVideoConfiguration Intro { get; set; } = new();
    // Empty list means the page keeps its built-in default pricing.
    public List<ShortVideoPackage> Packages { get; set; } = [];
}

public sealed partial class ShortVideoRepository(LandingConnectionFactory db, ILogger<ShortVideoRepository> logger, IOptions<SharedMediaOptions> media)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class Row
    {
        public bool ShowOnLanding { get; set; }
        public bool IntroEnabled { get; set; }
        public string? VideoUrl { get; set; }
        public string? PosterUrl { get; set; }
        public string PlaybackMode { get; set; } = "CLICK_TO_PLAY";
        public bool MutedByDefault { get; set; }
        public bool ShowControls { get; set; }
        public string? Packages { get; set; }
    }

    public async Task<ShortVideoConfiguration> GetPublicAsync(CancellationToken ct = default)
    {
        try
        {
            using var connection = await db.OpenAsync(ct);
            var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
                select show_on_landing as ShowOnLanding, intro_enabled as IntroEnabled, video_url as VideoUrl,
                       poster_url as PosterUrl, playback_mode as PlaybackMode, muted_by_default as MutedByDefault,
                       show_controls as ShowControls, packages::text as Packages
                from landing.short_video_settings where id = 1
                """, cancellationToken: ct));
            if (row is null) return new();
            return new ShortVideoConfiguration
            {
                ShowOnLanding = row.ShowOnLanding,
                Intro = BuildIntro(row, media.Value.StorageRoot),
                Packages = ParsePackages(row.Packages)
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Landing short-video settings unavailable.");
            return new();
        }
    }

    private static IntroVideoConfiguration BuildIntro(Row row, string? root)
    {
        if (!row.IntroEnabled || row.PlaybackMode is not ("AUTOPLAY_LOOP" or "CLICK_TO_PLAY" or "AUTOPLAY_ONCE")
            || string.IsNullOrWhiteSpace(row.VideoUrl) || !IsMediaUrl(row.VideoUrl, "videos")
            || string.IsNullOrWhiteSpace(root) || !File.Exists(Path.Combine(root, "landing", "short-video", "videos", Path.GetFileName(row.VideoUrl))))
            return new();
        var poster = row.PosterUrl is not null && IsMediaUrl(row.PosterUrl, "posters")
            && File.Exists(Path.Combine(root, "landing", "short-video", "posters", Path.GetFileName(row.PosterUrl)))
            ? row.PosterUrl : null;
        return new IntroVideoConfiguration
        {
            Enabled = true, VideoUrl = row.VideoUrl, PosterUrl = poster, PlaybackMode = row.PlaybackMode,
            MutedByDefault = row.MutedByDefault, ShowControls = row.ShowControls
        };
    }

    public static List<ShortVideoPackage> ParsePackages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var packages = JsonSerializer.Deserialize<List<ShortVideoPackage>>(json, Json) ?? [];
            return packages.Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.VideoCount > 0 && p.Price >= 0 && p.BonusVideos >= 0)
                .Take(4).ToList();
        }
        catch (JsonException) { return []; }
    }

    public static bool IsMediaUrl(string url, string folder)
        => url.StartsWith($"/media/landing/short-video/{folder}/", StringComparison.Ordinal) && MediaUrl().IsMatch(url);

    [GeneratedRegex(@"^/media/landing/short-video/(videos|posters)/[a-f0-9]{32}\.(mp4|webm|jpg|jpeg|png|webp)$")]
    private static partial Regex MediaUrl();
}
