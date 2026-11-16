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

    /// <summary>Points for one music generation (Suno) when no markup is set. Null = not configured.</summary>
    public decimal? MusicPoints { get; init; }

    /// <summary>
    /// Music price = the model's 79AI cost × this factor (2 = sell at twice the cost), per model. Takes precedence
    /// over MusicPoints.
    /// </summary>
    public decimal? MusicMarkup { get; init; }

    public string MusicModel { get; init; } = "suno-v5.5";

    /// <summary>What a customer pays for one TodoX point (1.400.000đ buys 1.400 points → 1.000đ).</summary>
    public decimal VndPerPoint { get; init; } = 1000m;

    /// <summary>What TodoX pays 79AI for one credit.</summary>
    public decimal VndPerCredit { get; init; } = 1m;

    /// <summary>
    /// Floor for images and videos: never sell below the model's 79AI cost × this factor, whatever the point table
    /// says (2 = at least twice the cost). Null = no floor.
    /// </summary>
    public decimal? CostFloorMarkup { get; init; } = 2m;

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
    public decimal VndPerPoint => Settings.VndPerPoint > 0 ? Settings.VndPerPoint : 1000m;
    public decimal VndPerCredit => Settings.VndPerCredit > 0 ? Settings.VndPerCredit : 1m;

    /// <summary>Points for the 79AI cost of credits × markup, rounded up to 0.1.</summary>
    public decimal CostTimes(decimal credits, decimal markup) =>
        Math.Max(0.1m, Math.Ceiling(credits * VndPerCredit * markup / VndPerPoint * 10) / 10);

    /// <summary>
    /// Points for one generation, or null when TodoX has no price for it (never guessed). credits is the 79AI price of
    /// that exact generation: music is priced from it (× markup), images and videos never go below cost × floor.
    /// </summary>
    public decimal? For(string kind, string model, string? duration, decimal? credits = null)
    {
        if (kind == "music")
        {
            if (Settings.MusicMarkup is > 0 && credits is > 0)
            {
                return CostTimes(credits.Value, Settings.MusicMarkup.Value);
            }

            return Settings.MusicPoints is > 0 ? Settings.MusicPoints : null;
        }

        decimal? table = null;
        var tier = Settings.IsPremium(kind, model) ? "premium" : "standard";
        if (Rates.TryGetValue($"{kind}:{tier}", out var rate))
        {
            table = kind == "image"
                ? rate
                : Math.Ceiling(rate * (EditVideoGommoJson.Seconds(duration) ?? DefaultVideoSeconds) * 10) / 10;
        }

        decimal? floor = Settings.CostFloorMarkup is > 0 && credits is > 0 ? CostTimes(credits.Value, Settings.CostFloorMarkup.Value) : null;
        return table is null ? floor : floor is null ? table : Math.Max(table.Value, floor.Value);
    }

    public const int DefaultVideoSeconds = 8;

    /// <summary>What is missing for this kind, for the admin and the error message.</summary>
    public string? Missing(string kind, string model)
    {
        if (kind == "music")
        {
            return Settings.MusicPoints is > 0 || Settings.MusicMarkup is > 0 ? null : "giá điểm cho tạo nhạc (Cài đặt EditVideo › Nhạc AI)";
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
    private readonly IConfiguration? _configuration;
    private readonly Microsoft.Extensions.Options.IOptionsMonitor<EditVideoAiOptions>? _options;

    public EditVideoAiSettingsService(TodoXConnectionFactory factory, IPointPricingService pricing, IMemoryCache cache,
        IConfiguration? configuration = null, Microsoft.Extensions.Options.IOptionsMonitor<EditVideoAiOptions>? options = null)
    {
        _factory = factory;
        _pricing = pricing;
        _cache = cache;
        _configuration = configuration;
        _options = options;
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
            MusicMarkup = decimal.TryParse(Get("music_markup"), NumberStyles.Number, CultureInfo.InvariantCulture, out var markup) && markup > 0 ? markup : null,
            VndPerPoint = Dec(Get("vnd_per_point")) is > 0 and var vpp ? vpp : defaults.VndPerPoint,
            VndPerCredit = Dec(Get("vnd_per_credit")) is > 0 and var vpc ? vpc : defaults.VndPerCredit,
            // An empty value turns the floor off; a missing key keeps the default (×2).
            CostFloorMarkup = rows.ContainsKey("cost_floor_markup") ? Dec(Get("cost_floor_markup")) is > 0 and var f ? f : null : defaults.CostFloorMarkup,
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
            ["music_markup"] = s.MusicMarkup is > 0 ? s.MusicMarkup.Value.ToString(CultureInfo.InvariantCulture) : "",
            ["vnd_per_point"] = (s.VndPerPoint > 0 ? s.VndPerPoint : 1000m).ToString(CultureInfo.InvariantCulture),
            ["vnd_per_credit"] = (s.VndPerCredit > 0 ? s.VndPerCredit : 1m).ToString(CultureInfo.InvariantCulture),
            ["cost_floor_markup"] = s.CostFloorMarkup is > 0 ? s.CostFloorMarkup.Value.ToString(CultureInfo.InvariantCulture) : "",
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

    private static decimal? Dec(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Tier(string? value) => string.Equals(value?.Trim(), "premium", StringComparison.OrdinalIgnoreCase) ? "premium" : "standard";
}
