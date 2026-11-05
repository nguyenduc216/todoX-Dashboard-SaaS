using System.Text.Json.Nodes;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>Customer-chosen generation options; null = not chosen.</summary>
public sealed record EditVideoAiSelection(string? Ratio, string? Resolution, string? Mode, string? Duration);

/// <summary>Price of one generation, or which option the customer still has to choose.</summary>
public sealed record EditVideoAiQuote(decimal Credits, EditVideoAiSelection Selection, string? Problem = null, string? Field = null, IReadOnlyList<string>? Choices = null)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Reads Gommo's model catalog and task JSON. Everything sent on to the customer is rebuilt from a few known
/// fields, so Gommo's account data (author, account ids, permissions) never passes through.
/// </summary>
public static class EditVideoGommoJson
{
    private static readonly string[] PriceKeys = ["mode", "resolution", "duration"];

    public static IEnumerable<JsonObject> Models(JsonNode? root) =>
        (root?["data"] as JsonArray ?? root as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(m => !string.IsNullOrWhiteSpace(Str(m, "model")) && !string.Equals(Str(m, "status"), "OFF", StringComparison.OrdinalIgnoreCase));

    public static JsonObject? FindModel(JsonNode? root, string model) =>
        Models(root).FirstOrDefault(m => string.Equals(Str(m, "model"), model.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Allowed values of an option list ("ratios", "resolutions", "modes", "durations").</summary>
    public static IReadOnlyList<string> Choices(JsonObject model, string list) =>
        (model[list] as JsonArray ?? [])
            .Select(x => x is JsonObject o ? Str(o, "type") ?? Str(o, "value") ?? Str(o, "name") : Scalar(x))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Gommo credit cost for one generation with the given options. Options the price table needs but the
    /// customer has not chosen are filled only when the model offers a single value; otherwise the quote asks.
    /// </summary>
    public static EditVideoAiQuote Quote(JsonObject model, EditVideoAiSelection selection)
    {
        foreach (var (field, list, value) in new[]
                 {
                     ("ratio", "ratios", selection.Ratio), ("resolution", "resolutions", selection.Resolution),
                     ("mode", "modes", selection.Mode), ("duration", "durations", selection.Duration)
                 })
        {
            var choices = Choices(model, list);
            if (value is not null && choices.Count > 0 && !choices.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return new EditVideoAiQuote(0, selection, $"Giá trị {field} '{value}' không có trong model này.", field, choices);
            }
        }

        var chosen = new Dictionary<string, string?>
        {
            ["mode"] = selection.Mode,
            ["resolution"] = selection.Resolution,
            ["duration"] = selection.Duration
        };
        var rows = (model["prices"] as JsonArray ?? []).OfType<JsonObject>().Where(r => Num(r, "price") is not null).ToList();
        if (rows.Count == 0)
        {
            return Num(model, "price") is { } flat and > 0m
                ? new EditVideoAiQuote(flat, selection)
                : new EditVideoAiQuote(0, selection, "Model này chưa có bảng giá.", "price");
        }

        var matching = rows.Where(r => PriceKeys.All(k => chosen[k] is null || Str(r, k) is not { } v || string.Equals(v, chosen[k], StringComparison.OrdinalIgnoreCase))).ToList();
        if (matching.Count == 0)
        {
            return new EditVideoAiQuote(0, selection, "Model này không có tổ hợp lựa chọn đó.", "combination",
                rows.Select(Describe).Distinct().Take(30).ToList());
        }

        foreach (var key in PriceKeys)
        {
            var values = matching.Select(r => Str(r, key)).Where(v => v is not null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (chosen[key] is null && values.Count == 1)
            {
                chosen[key] = values[0];
            }
            else if (chosen[key] is null && values.Count > 1)
            {
                return new EditVideoAiQuote(0, selection, $"Cần chọn {key}.", key, values!);
            }
        }

        var filled = selection with { Mode = chosen["mode"], Resolution = chosen["resolution"], Duration = chosen["duration"] };
        if (filled.Ratio is null && Choices(model, "ratios") is { Count: 1 } onlyRatio)
        {
            filled = filled with { Ratio = onlyRatio[0] };
        }

        return new EditVideoAiQuote(matching.Max(r => Num(r, "price")!.Value), filled);
    }

    /// <summary>Gommo credits → TodoX points, rounded up to 0.1.</summary>
    public static decimal Points(decimal credits, decimal vndPerCredit, decimal markup, decimal vndPerPoint)
    {
        if (credits <= 0 || vndPerPoint <= 0)
        {
            return 0;
        }

        var points = credits * vndPerCredit * markup / vndPerPoint;
        return Math.Max(0.1m, Math.Ceiling(points * 10) / 10);
    }

    /// <summary>A model as the customer sees it: options, abilities and the point price range.</summary>
    public static object Shape(JsonObject model, string kind, Func<decimal, decimal> toPoints, bool withPrices)
    {
        var rows = (model["prices"] as JsonArray ?? []).OfType<JsonObject>().Where(r => Num(r, "price") is not null).ToList();
        var credits = rows.Count > 0 ? rows.Select(r => Num(r, "price")!.Value).ToList() : [Num(model, "price") ?? 0];
        var abilities = new[]
            {
                "startText", "startImage", "startImageAndEnd", "withReference", "withSubject", "withMotion", "withEdit",
                "extendVideo", "withMultiShots", "withLipsync", "withUpscale"
            }
            .Where(f => model[f] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : v.TryGetValue<int>(out var i) && i != 0))
            .ToList();

        return new
        {
            model = Str(model, "model"),
            name = Str(model, "name"),
            kind,
            description = Str(model, "description") is { Length: > 240 } d ? d[..240] + "…" : Str(model, "description"),
            ratios = Choices(model, "ratios"),
            resolutions = Choices(model, "resolutions"),
            modes = Choices(model, "modes"),
            durations = Choices(model, "durations"),
            abilities,
            maxReferenceImages = Num(model, "maxSubject"),
            pointsFrom = toPoints(credits.Min()),
            pointsTo = toPoints(credits.Max()),
            prices = withPrices
                ? rows.Select(r => new
                {
                    mode = Str(r, "mode"),
                    resolution = Str(r, "resolution"),
                    duration = Str(r, "duration"),
                    points = toPoints(Num(r, "price")!.Value)
                }).ToList()
                : null
        };
    }

    public static string? IdBase(JsonNode? root) =>
        Str(root?["imageInfo"], "id_base") ?? Str(root?["videoInfo"], "id_base") ?? Str(root?["data"], "id_base") ?? Str(root, "id_base");

    /// <summary>The task object for one id_base anywhere in a Gommo response (single status or a list).</summary>
    public static JsonObject? FindTask(JsonNode? node, string idBase)
    {
        switch (node)
        {
            case JsonObject obj:
                if (string.Equals(Str(obj, "id_base"), idBase, StringComparison.OrdinalIgnoreCase) && Str(obj, "status") is not null)
                {
                    return obj;
                }

                foreach (var (_, child) in obj)
                {
                    if (FindTask(child, idBase) is { } found)
                    {
                        return found;
                    }
                }

                break;
            case JsonArray arr:
                foreach (var child in arr)
                {
                    if (FindTask(child, idBase) is { } found)
                    {
                        return found;
                    }
                }

                break;
        }

        return null;
    }

    /// <summary>running | succeeded | failed.</summary>
    public static string Status(JsonObject task) => Ai79TaskStatusNormalizer.Normalize(Str(task, "status")) switch
    {
        Ai79TaskStatusNormalizer.Success => "succeeded",
        Ai79TaskStatusNormalizer.Failed => "failed",
        _ => "running"
    };

    /// <summary>Result file URLs, best first.</summary>
    public static IReadOnlyList<string> Outputs(JsonObject task, string kind)
    {
        var urls = new List<string?>();
        if (kind == "video")
        {
            urls.Add(Str(task, "download_url"));
            urls.AddRange((task["resolutions"] as JsonArray ?? []).OfType<JsonObject>().Select(r => Str(r, "url")));
            urls.Add(Str(task, "url"));
        }
        else
        {
            urls.Add(Str(task, "url"));
            urls.Add(Str(task, "url_preview"));
        }

        return urls
            .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .Select(u => u!)
            .Distinct()
            .Take(1)
            .ToList();
    }

    public static string? ErrorMessage(JsonNode? node) =>
        Str(node, "error_message") ?? Str(node, "message") ?? Str(node, "msg") ?? Str(node, "error");

    internal static string? Str(JsonNode? node, string key) =>
        node is JsonObject obj && obj.TryGetPropertyValue(key, out var value) ? Scalar(value) : null;

    private static string? Scalar(JsonNode? value) => value is JsonValue v
        ? v.TryGetValue<string>(out var s) ? (string.IsNullOrWhiteSpace(s) ? null : s.Trim())
        : v.TryGetValue<long>(out var l) ? l.ToString()
        : v.TryGetValue<decimal>(out var d) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : null
        : null;

    private static decimal? Num(JsonNode? node, string key) =>
        node is JsonObject obj && obj[key] is JsonValue v
            ? v.TryGetValue<decimal>(out var d) ? d
            : v.TryGetValue<string>(out var s) && decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p
            : null
            : null;

    private static string Describe(JsonObject row) =>
        string.Join(" / ", PriceKeys.Select(k => Str(row, k)).Where(v => v is not null));
}
