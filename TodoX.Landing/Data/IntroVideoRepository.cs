using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Landing.Services;

namespace TodoX.Landing.Data;

public sealed class IntroVideoConfiguration
{
    public bool Enabled { get; set; }
    public string? VideoUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string PlaybackMode { get; set; } = "AUTOPLAY_LOOP";
    public bool MutedByDefault { get; set; } = true;
    public bool ShowControls { get; set; } = true;
}

public sealed class IntroVideoRepository(LandingConnectionFactory db, ILogger<IntroVideoRepository> logger, IOptions<SharedMediaOptions> media)
{
    public async Task<IntroVideoConfiguration> GetPublicAsync(CancellationToken ct = default)
    {
        try
        {
            using var connection = await db.OpenAsync(ct);
            var row = await connection.QuerySingleOrDefaultAsync<IntroVideoConfiguration>(new CommandDefinition("""
                select is_enabled as Enabled, video_url as VideoUrl, poster_url as PosterUrl,
                       playback_mode as PlaybackMode, muted_by_default as MutedByDefault,
                       show_controls as ShowControls from landing.intro_video_settings where id = 1
                """, cancellationToken: ct));
            if (row is null || row.PlaybackMode is not ("AUTOPLAY_LOOP" or "CLICK_TO_PLAY" or "AUTOPLAY_ONCE")
                || string.IsNullOrWhiteSpace(row.VideoUrl) || !ValidUrl(row.VideoUrl, "videos"))
                return new();
            if (row.PosterUrl is not null && !ValidUrl(row.PosterUrl, "posters")) row.PosterUrl = null;
            var root = media.Value.StorageRoot;
            if (string.IsNullOrWhiteSpace(root) || !File.Exists(Path.Combine(root, "landing", "intro", "videos", Path.GetFileName(row.VideoUrl))))
                return new();
            if (row.PosterUrl is not null && !File.Exists(Path.Combine(root, "landing", "intro", "posters", Path.GetFileName(row.PosterUrl))))
                row.PosterUrl = null;
            return row;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Landing intro video settings unavailable.");
            return new();
        }
    }

    private static bool ValidUrl(string url, string folder)
        => url.StartsWith($"/media/landing/intro/{folder}/", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(url, @"^/media/landing/intro/(videos|posters)/[a-f0-9]{32}\.(mp4|webm|jpg|jpeg|png|webp)$");
}