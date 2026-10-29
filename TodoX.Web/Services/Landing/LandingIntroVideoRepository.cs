using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services;

namespace TodoX.Web.Services.Landing;

public sealed class LandingIntroVideoSettings
{
    public bool IsEnabled { get; set; }
    public string? VideoUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string PlaybackMode { get; set; } = "AUTOPLAY_LOOP";
    public bool MutedByDefault { get; set; } = true;
    public bool ShowControls { get; set; } = true;
}

public sealed class LandingIntroVideoRepository(TodoXConnectionFactory db, AuthStateService auth)
{
    public async Task<LandingIntroVideoSettings> GetAsync(CancellationToken ct = default)
    {
        using var connection = await db.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<LandingIntroVideoSettings>(new CommandDefinition("""
            select is_enabled as IsEnabled, video_url as VideoUrl, poster_url as PosterUrl,
                   playback_mode as PlaybackMode, muted_by_default as MutedByDefault,
                   show_controls as ShowControls
            from landing.intro_video_settings where id = 1
            """, cancellationToken: ct)) ?? new();
    }

    public async Task SaveAsync(LandingIntroVideoSettings settings, string updatedBy, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        if (updatedBy != auth.CurrentUser!.UserId.ToString()) throw new UnauthorizedAccessException("Invalid user identity.");
        Validate(settings);
        using var connection = await db.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            insert into landing.intro_video_settings
                (id, is_enabled, video_url, poster_url, playback_mode, muted_by_default, show_controls, updated_by)
            values (1, @IsEnabled, @VideoUrl, @PosterUrl, @PlaybackMode, @MutedByDefault, @ShowControls, @UpdatedBy)
            on conflict (id) do update set is_enabled = excluded.is_enabled, video_url = excluded.video_url,
                poster_url = excluded.poster_url, playback_mode = excluded.playback_mode,
                muted_by_default = excluded.muted_by_default, show_controls = excluded.show_controls,
                updated_at = now(), updated_by = excluded.updated_by
            """, new { settings.IsEnabled, settings.VideoUrl, settings.PosterUrl, settings.PlaybackMode,
                settings.MutedByDefault, settings.ShowControls, UpdatedBy = updatedBy }, cancellationToken: ct));
    }

    public static void Validate(LandingIntroVideoSettings settings)
    {
        if (settings.PlaybackMode is not ("AUTOPLAY_LOOP" or "CLICK_TO_PLAY" or "AUTOPLAY_ONCE"))
            throw new ArgumentException("Chế độ phát không hợp lệ.", nameof(settings));
        if (settings.IsEnabled && string.IsNullOrWhiteSpace(settings.VideoUrl))
            throw new ArgumentException("Cần tải video trước khi kích hoạt.", nameof(settings));
        if (settings.VideoUrl is not null && !IsMediaUrl(settings.VideoUrl, "videos"))
            throw new ArgumentException("Video URL không hợp lệ.", nameof(settings));
        if (settings.PosterUrl is not null && !IsMediaUrl(settings.PosterUrl, "posters"))
            throw new ArgumentException("Poster URL không hợp lệ.", nameof(settings));
    }

    private static bool IsMediaUrl(string url, string folder)
        => url.StartsWith($"/media/landing/intro/{folder}/", StringComparison.Ordinal)
            && System.Text.RegularExpressions.Regex.IsMatch(url, @"^/media/landing/intro/(videos|posters)/[a-f0-9]{32}\.(mp4|webm|jpg|jpeg|png|webp)$");
}