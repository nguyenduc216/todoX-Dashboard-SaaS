using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services;

namespace TodoX.Web.Services.Landing;

public sealed class LandingShortVideoPackage
{
    public string Name { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public long Price { get; set; }
    public int VideoCount { get; set; }
    public int BonusVideos { get; set; }
    public List<string> Features { get; set; } = [];
    public bool Featured { get; set; }
}

public sealed class LandingShortVideoSettings
{
    public bool ShowOnLanding { get; set; } = true;
    public bool IntroEnabled { get; set; }
    public string? VideoUrl { get; set; }
    public string? PosterUrl { get; set; }
    public string PlaybackMode { get; set; } = "CLICK_TO_PLAY";
    public bool MutedByDefault { get; set; }
    public bool ShowControls { get; set; } = true;
    public List<LandingShortVideoPackage> Packages { get; set; } = LandingShortVideoRepository.DefaultPackages();
}

public sealed class LandingShortVideoRepository(TodoXConnectionFactory db, AuthStateService auth)
{
    public const int MaxPackages = 4;
    public const int MaxFeatures = 6;
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

    public async Task<LandingShortVideoSettings> GetAsync(CancellationToken ct = default)
    {
        using var connection = await db.OpenAsync(ct);
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            select show_on_landing as ShowOnLanding, intro_enabled as IntroEnabled, video_url as VideoUrl,
                   poster_url as PosterUrl, playback_mode as PlaybackMode, muted_by_default as MutedByDefault,
                   show_controls as ShowControls, packages::text as Packages
            from landing.short_video_settings where id = 1
            """, cancellationToken: ct));
        if (row is null) return new();
        var packages = ParsePackages(row.Packages);
        return new LandingShortVideoSettings
        {
            ShowOnLanding = row.ShowOnLanding,
            IntroEnabled = row.IntroEnabled,
            VideoUrl = row.VideoUrl,
            PosterUrl = row.PosterUrl,
            PlaybackMode = row.PlaybackMode,
            MutedByDefault = row.MutedByDefault,
            ShowControls = row.ShowControls,
            Packages = packages.Count > 0 ? packages : DefaultPackages()
        };
    }

    public async Task SaveAsync(LandingShortVideoSettings settings, string updatedBy, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        if (updatedBy != auth.CurrentUser!.UserId.ToString()) throw new UnauthorizedAccessException("Invalid user identity.");
        Normalize(settings);
        Validate(settings);
        using var connection = await db.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            insert into landing.short_video_settings
                (id, show_on_landing, intro_enabled, video_url, poster_url, playback_mode, muted_by_default, show_controls, packages, updated_by)
            values (1, @ShowOnLanding, @IntroEnabled, @VideoUrl, @PosterUrl, @PlaybackMode, @MutedByDefault, @ShowControls, cast(@Packages as jsonb), @UpdatedBy)
            on conflict (id) do update set show_on_landing = excluded.show_on_landing, intro_enabled = excluded.intro_enabled,
                video_url = excluded.video_url, poster_url = excluded.poster_url, playback_mode = excluded.playback_mode,
                muted_by_default = excluded.muted_by_default, show_controls = excluded.show_controls,
                packages = excluded.packages, updated_at = now(), updated_by = excluded.updated_by
            """, new
        {
            settings.ShowOnLanding, settings.IntroEnabled, settings.VideoUrl, settings.PosterUrl, settings.PlaybackMode,
            settings.MutedByDefault, settings.ShowControls,
            Packages = JsonSerializer.Serialize(settings.Packages, Json), UpdatedBy = updatedBy
        }, cancellationToken: ct));
    }

    public static List<LandingShortVideoPackage> ParsePackages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<LandingShortVideoPackage>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static void Normalize(LandingShortVideoSettings settings)
    {
        foreach (var package in settings.Packages)
        {
            package.Name = package.Name?.Trim() ?? "";
            package.Subtitle = package.Subtitle?.Trim() ?? "";
            package.Features = (package.Features ?? []).Select(f => f?.Trim() ?? "").Where(f => f.Length > 0).ToList();
        }
    }

    public static void Validate(LandingShortVideoSettings settings)
    {
        if (settings.PlaybackMode is not ("AUTOPLAY_LOOP" or "CLICK_TO_PLAY" or "AUTOPLAY_ONCE"))
            throw new ArgumentException("Chế độ phát không hợp lệ.", nameof(settings));
        if (settings.IntroEnabled && string.IsNullOrWhiteSpace(settings.VideoUrl))
            throw new ArgumentException("Cần tải video trước khi kích hoạt.", nameof(settings));
        if (settings.VideoUrl is not null && !IsMediaUrl(settings.VideoUrl, "videos"))
            throw new ArgumentException("Video URL không hợp lệ.", nameof(settings));
        if (settings.PosterUrl is not null && !IsMediaUrl(settings.PosterUrl, "posters"))
            throw new ArgumentException("Poster URL không hợp lệ.", nameof(settings));
        if (settings.Packages is null || settings.Packages.Count is 0 or > MaxPackages)
            throw new ArgumentException($"Cần từ 1 đến {MaxPackages} gói dịch vụ.", nameof(settings));
        foreach (var package in settings.Packages)
        {
            if (package.Name.Length is 0 or > 60) throw new ArgumentException("Tên gói phải có từ 1 đến 60 ký tự.", nameof(settings));
            if (package.Subtitle.Length > 80) throw new ArgumentException("Mô tả ngắn của gói tối đa 80 ký tự.", nameof(settings));
            if (package.Price is < 0 or > 10_000_000_000) throw new ArgumentException("Giá gói không hợp lệ.", nameof(settings));
            if (package.VideoCount is < 1 or > 10_000) throw new ArgumentException("Số video của gói không hợp lệ.", nameof(settings));
            if (package.BonusVideos is < 0 or > 10_000) throw new ArgumentException("Số video tặng không hợp lệ.", nameof(settings));
            if (package.Features.Count > MaxFeatures || package.Features.Any(f => f.Length > 120))
                throw new ArgumentException($"Mỗi gói tối đa {MaxFeatures} lợi ích, mỗi dòng tối đa 120 ký tự.", nameof(settings));
        }
    }

    public static bool IsMediaUrl(string url, string folder)
        => url.StartsWith($"/media/landing/short-video/{folder}/", StringComparison.Ordinal)
            && Regex.IsMatch(url, @"^/media/landing/short-video/(videos|posters)/[a-f0-9]{32}\.(mp4|webm|jpg|jpeg|png|webp)$");

    public static List<LandingShortVideoPackage> DefaultPackages() =>
    [
        new() { Name = "Gói Start", Subtitle = "Khởi động hiệu quả", Price = 3_000_000, VideoCount = 25, BonusVideos = 5,
            Features = ["Phù hợp doanh nghiệp nhỏ, cá nhân kinh doanh", "Duy trì kênh ổn định", "Đa dạng chủ đề, bám sát thương hiệu"] },
        new() { Name = "Gói Business", Subtitle = "Tối ưu hiệu quả", Price = 6_000_000, VideoCount = 50, BonusVideos = 15, Featured = true,
            Features = ["Phù hợp doanh nghiệp vừa", "Tăng tần suất, mở rộng chủ đề", "Tiết kiệm chi phí, hiệu quả cao"] },
        new() { Name = "Gói Pro", Subtitle = "Phát triển mạnh mẽ", Price = 10_000_000, VideoCount = 80, BonusVideos = 20,
            Features = ["Phù hợp doanh nghiệp lớn", "Đa dạng nội dung, chiến dịch dài hạn", "Tối ưu chi phí, phát triển thương hiệu bền vững"] }
    ];
}
