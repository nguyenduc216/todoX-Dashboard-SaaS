using System.Globalization;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// Admin settings for EditVideo AI (Dashboard › Cài đặt EditVideo), kept in system.app_settings under "editvideo.ai.*".
/// Prices are not stored here: images, videos and voices use TodoX's own point table (billing.point_rate_config, the
/// same rates the web charges); only music, which that table has no row type for, has its own price here.
/// </summary>
public sealed record EditVideoAiSettings
{
    public const string DefaultVideoFallback = "veo_omni, veo_3_1, gemini-omni-flash-1.2, grok_video_heavy";
    public const string DefaultImageFallback = "google_image_gen_banana_2, imagegen_2_0, seedream_4_5";

    /// <summary>standard | premium: which row of the point table images and videos use.</summary>
    public string ImageTier { get; init; } = "standard";
    public string VideoTier { get; init; } = "standard";

    /// <summary>Models always priced at the premium rate (comma separated).</summary>
    public string PremiumModels { get; init; } = "";

    /// <summary>Points for one music generation (Suno). Null = not configured: music is refused until set.</summary>
    public decimal? MusicPoints { get; init; }

    public string MusicModel { get; init; } = "suno-v5.5";

    public bool FallbackEnabled { get; init; } = true;

    /// <summary>Models tried in order when the chosen one is rejected by the provider (same as the web).</summary>
    public string VideoFallback { get; init; } = DefaultVideoFallback;
    public string ImageFallback { get; init; } = DefaultImageFallback;

    public IReadOnlyList<string> FallbackFor(string kind) => !FallbackEnabled ? [] : List(kind == "image" ? ImageFallback : kind == "video" ? VideoFallback : "");

    public bool IsPremium(string kind, string model) =>
        List(PremiumModels).Contains(model, StringComparer.OrdinalIgnoreCase) || (kind == "image" ? ImageTier : VideoTier) == "premium";

    internal static IReadOnlyList<string> List(string? value) =>
        (value ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>Point prices EditVideo charges, resolved from the settings and TodoX's point table.</summary>
public sealed record EditVideoAiPrices(EditVideoAiSettings Settings, IReadOnlyDictionary<string, decimal> Rates)
{
    /// <summary>Points for one generation, or null when TodoX has no price for it (never guessed).</summary>
    public decimal? For(string kind, string model, string? duration)
    {
        if (kind == "music")
        {
            return Settings.MusicPoints is > 0 ? Settings.MusicPoints : null;
        }

        var tier = Settings.IsPremium(kind, model) ? "premium" : "standard";
        if (!Rates.TryGetValue($"{kind}:{tier}", out var rate))
        {
            return null;
        }

        if (kind == "image")
        {
            return rate;
        }

        var seconds = EditVideoGommoJson.Seconds(duration) ?? EditVideoAiPrices.DefaultVideoSeconds;
        return Math.Ceiling(rate * seconds * 10) / 10;
    }

    public const int DefaultVideoSeconds = 8;

    /// <summary>What is missing for this kind, for the admin and the error message.</summary>
    public string? Missing(string kind, string model)
    {
        if (kind == "music")
        {
            return Settings.MusicPoints is > 0 ? null : "giá điểm cho tạo nhạc (Cài đặt EditVideo › Điểm mỗi bài nhạc)";
        }

        var tier = Settings.IsPremium(kind, model) ? "premium" : "standard";
        return Rates.ContainsKey($"{kind}:{tier}") ? null : $"giá điểm {(kind == "image" ? "ảnh" : "video")} hạng {tier} (Ví điểm › Bảng giá điểm)";
    }
}

public sealed class EditVideoAiSettingsService
{
    private const string Prefix = "editvideo.ai.";
    private const string CacheKey = "editvideo-ai-settings";

    private readonly TodoXConnectionFactory _factory;
    private readonly IPointPricingService _pricing;
    private readonly IMemoryCache _cache;

    public EditVideoAiSettingsService(TodoXConnectionFactory factory, IPointPricingService pricing, IMemoryCache cache)
    {
        _factory = factory;
        _pricing = pricing;
        _cache = cache;
    }

    public async Task<EditVideoAiSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out EditVideoAiSettings? cached) && cached is not null)
        {
            return cached;
        }

        using var conn = await _factory.OpenAsync(ct);
        var rows = (await conn.QueryAsync<(string Key, string? Value)>(
            "SELECT setting_key, setting_value FROM system.app_settings WHERE setting_key LIKE 'editvideo.ai.%' AND is_active;"))
            .ToDictionary(r => r.Key[Prefix.Length..], r => r.Value, StringComparer.OrdinalIgnoreCase);
        string? Get(string key) => rows.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var defaults = new EditVideoAiSettings();
        var settings = new EditVideoAiSettings
        {
            ImageTier = Tier(Get("image_tier")),
            VideoTier = Tier(Get("video_tier")),
            PremiumModels = Get("premium_models") ?? defaults.PremiumModels,
            MusicPoints = decimal.TryParse(Get("music_points"), NumberStyles.Number, CultureInfo.InvariantCulture, out var music) && music > 0 ? music : null,
            MusicModel = Get("music_model") ?? defaults.MusicModel,
            FallbackEnabled = !string.Equals(Get("fallback_enabled"), "false", StringComparison.OrdinalIgnoreCase),
            VideoFallback = Get("video_fallback") ?? defaults.VideoFallback,
            ImageFallback = Get("image_fallback") ?? defaults.ImageFallback
        };
        _cache.Set(CacheKey, settings, TimeSpan.FromSeconds(30));
        return settings;
    }

    public async Task<EditVideoAiPrices> PricesAsync(CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);
        var rates = new Dictionary<string, decimal>();
        foreach (var kind in new[] { PointPricingResourceTypes.Image, PointPricingResourceTypes.Video })
        {
            foreach (var tier in new[] { "standard", "premium" })
            {
                try
                {
                    var rate = await _pricing.ResolveRateAsync(null, kind, tier, ct);
                    if (rate.Rate > 0)
                    {
                        rates[$"{kind}:{tier}"] = rate.Rate;
                    }
                }
                catch (InvalidOperationException)
                {
                    // POINT_RATE_NOT_CONFIGURED: left out, reported as missing.
                }
            }
        }

        return new EditVideoAiPrices(settings, rates);
    }

    public async Task SaveAsync(EditVideoAiSettings s, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string>
        {
            ["image_tier"] = Tier(s.ImageTier),
            ["video_tier"] = Tier(s.VideoTier),
            ["premium_models"] = string.Join(", ", EditVideoAiSettings.List(s.PremiumModels)),
            ["music_points"] = s.MusicPoints is > 0 ? s.MusicPoints.Value.ToString(CultureInfo.InvariantCulture) : "",
            ["music_model"] = (s.MusicModel ?? "").Trim(),
            ["fallback_enabled"] = s.FallbackEnabled ? "true" : "false",
            ["video_fallback"] = string.Join(", ", EditVideoAiSettings.List(s.VideoFallback)),
            ["image_fallback"] = string.Join(", ", EditVideoAiSettings.List(s.ImageFallback))
        };

        using var conn = await _factory.OpenAsync(ct);
        foreach (var (key, value) in values)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
                VALUES (gen_random_uuid(), @key, 'editvideo', 'text', @value, 'Cài đặt EditVideo AI', true, now())
                ON CONFLICT (setting_key) DO UPDATE SET setting_value = EXCLUDED.setting_value, is_active = true, updated_at = now();
                """, new { key = Prefix + key, value });
        }

        _cache.Remove(CacheKey);
    }

    private static string Tier(string? value) => string.Equals(value?.Trim(), "premium", StringComparison.OrdinalIgnoreCase) ? "premium" : "standard";
}
