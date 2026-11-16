using System.Globalization;
using System.Text.Json.Nodes;

namespace TodoX.Web.Services.Pricing;

/// <summary>Kinds of AI generation priced by the model price book.</summary>
public static class ModelPriceKinds
{
    public const string Video = "video";
    public const string Image = "image";
    public const string Voice = "voice";
    public const string Music = "music";

    public static IReadOnlyList<string> All { get; } = [Video, Image, Voice, Music];

    public static string Label(string kind) => kind switch
    {
        Video => "Video",
        Image => "Ảnh",
        Voice => "Giọng đọc",
        Music => "Nhạc",
        _ => kind
    };
}

/// <summary>Where a model may be used: the web dashboard (RVideo, Timelapse, RDance), the EditVideo tool, or both.</summary>
public static class ModelPriceEnvs
{
    public const string Both = "both";
    public const string Dashboard = "dashboard";
    public const string EditVideo = "editvideo";

    public static IReadOnlyList<string> All { get; } = [Both, Dashboard, EditVideo];

    public static string Normalize(string? value) =>
        All.FirstOrDefault(e => string.Equals(e, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Both;

    public static bool Allows(string? env, string where) => Normalize(env) is var e && (e == Both || e == where);

    public static string Label(string env) => Normalize(env) switch
    {
        Dashboard => "Dashboard",
        EditVideo => "EditVideo",
        _ => "Cả hai"
    };
}

/// <summary>One model in the price book: its tier label, place in lists and fallback, where it may run, its markup.</summary>
public sealed record ModelPriceEntry
{
    public string Kind { get; init; } = ModelPriceKinds.Video;
    public string Model { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>standard (Thường) | premium (Cao cấp). A label for customers and packages; the price follows the cost.</summary>
    public string Tier { get; init; } = "standard";

    /// <summary>Position in the model pickers and in the fallback chain (smaller first).</summary>
    public int Order { get; init; } = 100;

    public string Env { get; init; } = ModelPriceEnvs.Both;
    public bool Enabled { get; init; } = true;

    /// <summary>Price = 79AI cost × this; null = the book's default markup.</summary>
    public decimal? Markup { get; init; }

    /// <summary>Fixed cost in VND per unit, for providers without a 79AI price list (Vbee: per voice).</summary>
    public decimal? FixedCostVnd { get; init; }

    public string? Note { get; init; }

    public bool IsPremium => string.Equals(Tier, "premium", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// TodoX's single price rule for AI generations, shared by the dashboard and EditVideo: points = the 79AI cost of
/// that exact generation (model, mode, resolution, duration) × markup, with the markup kept between MinMarkup and
/// MaxMarkup. 1 point = VndPerPoint đồng, 1 79AI credit = VndPerCredit đồng.
/// </summary>
public sealed record ModelPriceBook
{
    public decimal VndPerPoint { get; init; } = 1000m;
    public decimal VndPerCredit { get; init; } = 1m;
    public decimal DefaultMarkup { get; init; } = 2.5m;
    public decimal MinMarkup { get; init; } = 2.5m;
    public decimal MaxMarkup { get; init; } = 3m;
    public IReadOnlyList<ModelPriceEntry> Models { get; init; } = [];

    public ModelPriceEntry? Find(string kind, string? model) =>
        string.IsNullOrWhiteSpace(model)
            ? null
            : Models.FirstOrDefault(m => string.Equals(m.Kind, kind, StringComparison.OrdinalIgnoreCase)
                                         && string.Equals(m.Model, model.Trim(), StringComparison.OrdinalIgnoreCase));

    public decimal MarkupFor(ModelPriceEntry? entry)
    {
        var markup = entry?.Markup is > 0 ? entry.Markup.Value : DefaultMarkup;
        var min = MinMarkup > 0 ? MinMarkup : 0m;
        var max = MaxMarkup >= min && MaxMarkup > 0 ? MaxMarkup : decimal.MaxValue;
        return Math.Min(Math.Max(markup, min), max);
    }

    private decimal Vpp => VndPerPoint > 0 ? VndPerPoint : 1000m;
    private decimal Vpc => VndPerCredit > 0 ? VndPerCredit : 1m;

    /// <summary>Points for a cost in VND × markup, rounded up to 0.1 (never below 0.1).</summary>
    public decimal Points(decimal costVnd, decimal markup) =>
        Math.Max(0.1m, Math.Ceiling(costVnd * markup / Vpp * 10) / 10);

    public decimal CostVnd(decimal credits) => credits * Vpc;

    /// <summary>Points for a generation that costs this many 79AI credits.</summary>
    public decimal PointsForCredits(string kind, string? model, decimal credits) =>
        Points(CostVnd(credits), MarkupFor(Find(kind, model)));

    /// <summary>Only models listed in the book, switched on and meant for this environment, may be used.</summary>
    public bool IsAllowed(string kind, string? model, string where) =>
        Find(kind, model) is { } e && e.Enabled && ModelPriceEnvs.Allows(e.Env, where);

    /// <summary>Models of a kind usable in an environment, in the admin's order.</summary>
    public IReadOnlyList<ModelPriceEntry> Ordered(string kind, string where) =>
        Models.Where(m => string.Equals(m.Kind, kind, StringComparison.OrdinalIgnoreCase) && m.Enabled && ModelPriceEnvs.Allows(m.Env, where))
            .OrderBy(m => m.Order).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Fallback only swaps like for like: generation, edit (VFX) or motion-control models.</summary>
    public static string Group(string? model)
    {
        var m = (model ?? "").ToLowerInvariant();
        return m.Contains("edit") ? "edit" : m.Contains("motion") ? "motion" : "generate";
    }

    /// <summary>Sort key for any model code: listed models by their order, unlisted ones after them.</summary>
    public int Rank(string kind, string? model) => Find(kind, model) is { } e ? e.Order : int.MaxValue / 2;
}

/// <summary>What one generation costs and sells for.</summary>
public sealed record ModelPriceQuote(string Kind, string Model, ModelPriceEntry? Entry, decimal CostVnd, decimal Markup, decimal Points, int? Seconds)
{
    /// <summary>Video: points per second of the requested length (rounded up to 0.01) so duration × rate ≥ Points.</summary>
    public decimal PerSecond => Seconds is > 0 ? Math.Ceiling(Points / Seconds.Value * 100) / 100 : Points;

    public string Tier => Entry?.Tier ?? "standard";
}

/// <summary>Reads the cost of one generation from a 79AI model (its "prices" rows), leniently.</summary>
public static class ModelCostLookup
{
    /// <summary>
    /// 79AI credits for model + mode + resolution + seconds. Rows that name a duration are totals; rows without one
    /// are per second when the model's rate_type is per_second, otherwise per generation. Unknown choices take the
    /// most expensive matching row, so TodoX never under-prices. Null when the model has no usable price.
    /// </summary>
    public static decimal? Credits(JsonObject model, string? mode, string? resolution, int? seconds)
    {
        var perSecondModel = string.Equals(Str(model, "rate_type"), "per_second", StringComparison.OrdinalIgnoreCase);
        var rows = (model["prices"] as JsonArray ?? []).OfType<JsonObject>().Where(r => Num(r, "price") is > 0).ToList();
        if (rows.Count == 0)
        {
            return Num(model, "price") is > 0 and var flat ? (perSecondModel && seconds is > 0 ? flat * seconds.Value : flat) : null;
        }

        IEnumerable<JsonObject> Match(IEnumerable<JsonObject> source, string key, string? wanted, Func<string?, string?> norm) =>
            wanted is null || !source.Any(r => Same(norm(Str(r, key)), norm(wanted)))
                ? source
                : source.Where(r => Str(r, key) is null || Same(norm(Str(r, key)), norm(wanted)));

        var matching = Match(rows, "mode", mode, v => v);
        matching = Match(matching, "resolution", resolution, NormRes).ToList();

        var withDuration = matching.Where(r => Str(r, "duration") is not null).ToList();
        if (withDuration.Count > 0)
        {
            if (seconds is > 0)
            {
                var exact = withDuration.Where(r => Sec(Str(r, "duration")) == seconds).ToList();
                if (exact.Count > 0)
                {
                    return exact.Max(r => Num(r, "price")!.Value);
                }

                // No row for that length: scale the most expensive per-second rate of the nearest lengths.
                return Math.Ceiling(withDuration.Where(r => Sec(Str(r, "duration")) is > 0)
                    .Select(r => Num(r, "price")!.Value / Sec(Str(r, "duration"))!.Value)
                    .DefaultIfEmpty(0m).Max() * seconds.Value);
            }

            return withDuration.Max(r => Num(r, "price")!.Value);
        }

        var price = matching.Max(r => Num(r, "price")!.Value);
        var perSecond = perSecondModel || matching.Any(r => string.Equals(Str(r, "rate_type"), "per_second", StringComparison.OrdinalIgnoreCase));
        return perSecond && seconds is > 0 ? price * seconds.Value : price;
    }

    /// <summary>Distinct resolutions a model is priced at (for the admin table).</summary>
    public static IReadOnlyList<string> Resolutions(JsonObject model) =>
        (model["prices"] as JsonArray ?? []).OfType<JsonObject>().Select(r => Str(r, "resolution")).Where(r => r is not null)
            .Select(r => NormRes(r)!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => int.TryParse(new string(r.TakeWhile(char.IsDigit).ToArray()), out var n) ? (r.EndsWith('k') ? n * 1000 : n) : 99999).ToList();

    /// <summary>The cheapest mode of a model (the one TodoX runs by default for its "Thường" price).</summary>
    public static IReadOnlyList<string> Modes(JsonObject model) =>
        (model["prices"] as JsonArray ?? []).OfType<JsonObject>().Select(r => Str(r, "mode")).Where(m => m is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList()!;

    public static string? NormRes(string? value)
    {
        var v = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(v))
        {
            return null;
        }

        return v.All(char.IsDigit) ? v + "p" : v;
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static int? Sec(string? value) =>
        int.TryParse(new string((value ?? "").Trim().TakeWhile(char.IsDigit).ToArray()), out var s) && s > 0 ? s : null;

    public static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v ? (v.TryGetValue<string>(out var s) ? (string.IsNullOrWhiteSpace(s) ? null : s.Trim())
            : v.TryGetValue<decimal>(out var d) ? d.ToString(CultureInfo.InvariantCulture) : null) : null;

    public static decimal? Num(JsonObject o, string key) =>
        o[key] is JsonValue v
            ? v.TryGetValue<decimal>(out var d) ? d
            : v.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : null
            : null;
}

/// <summary>The models TodoX starts with (checked against 79AI prices on 16/11/2026); the admin edits them.</summary>
public static class ModelPriceDefaults
{
    private static ModelPriceEntry E(string kind, int order, string model, string name, string tier, string env = ModelPriceEnvs.Both,
        decimal? fixedCost = null, string? note = null) =>
        new() { Kind = kind, Order = order, Model = model, Name = name, Tier = tier, Env = env, FixedCostVnd = fixedCost, Note = note };

    public static IReadOnlyList<ModelPriceEntry> Models { get; } =
    [
        // Video — Thường
        E("video", 10, "veo_omni", "Veo Omni (Gemini Omni Flash 1.0)", "standard"),
        E("video", 20, "gemini-omni-flash-1.2", "Gemini Omni Flash 1.2", "standard"),
        E("video", 30, "veo_3_1", "Veo 3.1 (Lite / Fast / Quality)", "standard", note: "Quality là bản cao cấp, giá theo chi phí từng bản"),
        E("video", 40, "grok_video_heavy", "Grok Imagine 1.5", "standard"),
        E("video", 50, "hailuo_2_3", "Hailuo 2.3", "standard"),
        E("video", 60, "kling_4_0_flash", "Kling 4.0 Flash", "standard"),
        E("video", 70, "seedance_20_mini", "Seedance 2.0 Mini", "standard"),
        // Video — Cao cấp
        E("video", 80, "kling_video_3_0", "Kling 3.0 Omni", "premium"),
        E("video", 90, "kling_4_0", "Kling 4.0", "premium"),
        E("video", 100, "wan_3_0", "WAN 3.0", "premium"),
        E("video", 110, "seedance_20_pro", "Seedance 2.0", "premium", note: "Timelapse dùng model này"),
        E("video", 120, "happy_horse_1", "Happy Horse 1.0", "premium"),
        E("video", 130, "seedance_25_flash", "Seedance 2.5 Flash", "premium"),
        E("video", 140, "seedance_25_omni", "Seedance 2.5", "premium"),
        // VFX (sửa video có sẵn) — EditVideo
        E("video", 200, "veo_omni_edit", "VFX · Gemini Omni Edit", "standard", ModelPriceEnvs.EditVideo),
        E("video", 210, "seedance_20_pro_edit", "VFX · Seedance 2.0 Omni Edit", "premium", ModelPriceEnvs.EditVideo),
        E("video", 220, "kling_video_3_0_edit", "VFX · Kling 3.0 Edit", "premium", ModelPriceEnvs.EditVideo),
        // Điều khiển chuyển động — RDance
        E("video", 300, "kling_video_motion_3", "Motion · Kling 3.0 Motion Control", "standard", ModelPriceEnvs.Dashboard, note: "RDance"),
        E("video", 310, "kling_4_0_motion", "Motion · Kling 4.0 Motion Control", "premium", ModelPriceEnvs.Dashboard, note: "RDance"),

        // Ảnh
        E("image", 10, "google_image_gen_banana_2", "Nano Banana 2", "standard"),
        E("image", 20, "seedream_4_5", "Seedream 4.5", "standard"),
        E("image", 30, "imagegen_2_0", "GPT Image 2", "standard"),
        E("image", 40, "seedream_5_0", "Seedream 5.0", "standard", note: "Timelapse dùng model này"),
        E("image", 50, "google_image_gen_banana_pro", "Nano Banana Pro", "standard"),
        E("image", 60, "seedream_4_0", "Seedream 4.0", "standard"),
        E("image", 70, "3.0-omni", "Kling 3.0 Omni Image", "standard"),
        E("image", 80, "seedream-5.5", "Seedream 5.5", "standard"),
        E("image", 90, "seedream-5-0-flash", "Seedream 5.0 Flash", "standard"),
        E("image", 100, "gpt-image-2.6-flare", "GPT Image 2.6", "standard"),
        E("image", 110, "midjourney_8_2", "Midjourney 8.2", "premium"),
        E("image", 120, "seedream-5-0-pro", "Seedream 5.0 Pro", "premium"),
        E("image", 130, "gemini-nano-banana-2.1", "Nano Banana 2.1", "premium"),
        E("image", 140, "flux-3-image", "Flux 3", "premium"),
        E("image", 150, "ideogram-4-5", "Ideogram 4.5", "premium"),

        // Giọng đọc
        E("voice", 10, "vbee", "Vbee (mỗi lượt đọc)", "standard", fixedCost: 1000m),
        E("voice", 20, "capcut_tts", "CapCut TTS", "standard"),
        E("voice", 30, "omnivoice_v1", "Omnivoice (clone giọng)", "standard"),
        E("voice", 40, "autoai_speech_1", "Auto TTS v1 (clone giọng)", "standard"),
        E("voice", 50, "eleven_flash_v2_5", "Eleven V2.5", "standard"),
        E("voice", 60, "minimax_speech_2_6_turbo", "Minimax 2.6 Turbo", "standard"),
        E("voice", 70, "eleven_v3", "Eleven V3", "premium"),
        E("voice", 80, "minimax_speech_2_6_hd", "Minimax 2.6 HD", "premium"),
        E("voice", 90, "minimax_speech_2_8_turbo", "Minimax 2.8 Turbo", "premium"),
        E("voice", 100, "minimax_speech_2_8_hd", "Minimax 2.8 HD", "premium"),
        E("voice", 110, "eleven_v4", "Eleven V4", "premium"),

        // Nhạc
        E("music", 10, "suno-v5.5", "Suno 5.5", "standard"),
        E("music", 20, "suno-v5.0", "Suno 5.0", "standard"),
        E("music", 30, "suno-v4.5", "Suno 4.5", "standard"),
        E("music", 40, "suno-v6.0", "Suno 6.0", "premium")
    ];
}

public static class ModelPriceSources
{
    /// <summary>PointPricingRate.Source for rates taken from the model price book.</summary>
    public const string ModelPrice = "model_price";
}

/// <summary>One sample price shown in the admin table (e.g. "720p · 8 giây": cost → points).</summary>
public sealed record ModelPriceSample(string Label, decimal CostVnd, decimal Points);

public static class ModelPriceSamples
{
    public const int VideoSeconds = 8;

    /// <summary>
    /// Typical prices of a 79AI model under the book: video per resolution for 8 s (cheapest mode), image per
    /// resolution (cheapest mode), voice per 1.000 characters, music per generation.
    /// </summary>
    public static IReadOnlyList<ModelPriceSample> For(string kind, JsonObject? model, ModelPriceBook book, ModelPriceEntry? entry)
    {
        var markup = book.MarkupFor(entry);
        ModelPriceSample S(string label, decimal credits) => new(label, book.CostVnd(credits), book.Points(book.CostVnd(credits), markup));

        if (entry?.FixedCostVnd is > 0 && entry.FixedCostVnd is { } fixedCost)
        {
            return [new(kind == ModelPriceKinds.Voice ? "mỗi lượt" : "mỗi lần", fixedCost, book.Points(fixedCost, markup))];
        }

        if (model is null)
        {
            return [];
        }

        if (kind == ModelPriceKinds.Voice)
        {
            return ModelCostLookup.Num(model, "price") is > 0 and var perChar ? [S("1.000 ký tự", perChar * 1000)] : [];
        }

        if (kind == ModelPriceKinds.Music)
        {
            return ModelCostLookup.Credits(model, null, null, null) is > 0 and var c ? [S("mỗi lần (2 bài)", c)] : [];
        }

        var resolutions = ModelCostLookup.Resolutions(model);
        var modes = ModelCostLookup.Modes(model);
        var seconds = kind == ModelPriceKinds.Video ? VideoSeconds : (int?)null;
        var list = new List<ModelPriceSample>();
        foreach (var res in resolutions.Count > 0 ? resolutions : [null!])
        {
            var cheapest = (modes.Count > 0 ? modes : [null!])
                .Where(m => m is null || !m.Contains("vip", StringComparison.OrdinalIgnoreCase) || modes.All(x => x.Contains("vip", StringComparison.OrdinalIgnoreCase)))
                .Select(m => ModelCostLookup.Credits(model, m, res, seconds))
                .Where(c => c is > 0).Select(c => c!.Value).DefaultIfEmpty(0m).Min();
            if (cheapest > 0)
            {
                var label = res ?? (kind == ModelPriceKinds.Video ? "mặc định" : "ảnh");
                list.Add(S(kind == ModelPriceKinds.Video ? $"{label} · {VideoSeconds}s" : label, cheapest));
            }
        }

        return list;
    }
}
