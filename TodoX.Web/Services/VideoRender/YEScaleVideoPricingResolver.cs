using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.VideoRender;

public sealed record YEScaleVideoResolvedPrice(
    string ModelName,
    string? Mode,
    int? DurationSeconds,
    decimal ChargedPoints,
    decimal? ProviderEstimatedCostUsd,
    string CostSource,
    string RuleKey,
    string TariffSnapshotJson);

public interface IYEScaleVideoPricingResolver
{
    YEScaleVideoResolvedPrice Resolve(
        ProviderOptionDto option,
        AiProviderCapabilityDto capability,
        string aspectRatio,
        string resolution,
        int durationSeconds,
        bool hasSourceImage);
}

public sealed class YEScaleVideoPricingResolver : IYEScaleVideoPricingResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public YEScaleVideoResolvedPrice Resolve(
        ProviderOptionDto option,
        AiProviderCapabilityDto capability,
        string aspectRatio,
        string resolution,
        int durationSeconds,
        bool hasSourceImage)
    {
        var model = option.ModelName?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Thiếu model YEScale video.");
        }

        var config = YEScaleVideoModelMapper.ParseConfig(null, capability.ConfigJson);
        var mode = ResolveMode(model, config.Mode, hasSourceImage);

        var rules = ParseRules(capability.ConfigJson);
        var matched = rules.FirstOrDefault(rule => Matches(rule.Match, model, mode, durationSeconds));
        if (matched is not null)
        {
            var chargedPoints = matched.ChargedPoints
                ?? matched.ProviderEstimatedCostUsd
                ?? option.UnitCostPoints;
            return new YEScaleVideoResolvedPrice(
                model,
                mode,
                durationSeconds,
                chargedPoints,
                matched.ProviderEstimatedCostUsd,
                matched.CostSource ?? "configured_tariff",
                matched.RuleKey ?? BuildRuleKey(model, mode, durationSeconds),
                BuildSnapshotJson(model, mode, durationSeconds, aspectRatio, resolution, matched, chargedPoints));
        }

        var fallbackUsd = TryReadDecimal(capability.ConfigJson, "provider_estimated_cost_usd");
        return new YEScaleVideoResolvedPrice(
            model,
            mode,
            durationSeconds,
            option.UnitCostPoints,
            fallbackUsd,
            "configured_tariff_fallback",
            BuildRuleKey(model, mode, durationSeconds),
            BuildSnapshotJson(model, mode, durationSeconds, aspectRatio, resolution, null, option.UnitCostPoints, fallbackUsd));
    }

    private static string ResolveMode(string model, string? configuredMode, bool hasSourceImage)
    {
        if (!string.Equals(model, "omni-flash", StringComparison.OrdinalIgnoreCase))
        {
            return "i2v(img_ref)";
        }

        if (!string.IsNullOrWhiteSpace(configuredMode))
        {
            return configuredMode.Trim();
        }

        return hasSourceImage ? "i2v(img_ref)" : "t2v";
    }

    private static bool Matches(PricingMatch match, string model, string mode, int durationSeconds)
    {
        if (!string.IsNullOrWhiteSpace(match.Model)
            && !string.Equals(match.Model, model, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(match.Mode)
            && !string.Equals(match.Mode, mode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (match.DurationSeconds.HasValue && match.DurationSeconds.Value != durationSeconds)
        {
            return false;
        }

        return true;
    }

    private static List<PricingRule> ParseRules(string? capabilityConfigJson)
    {
        if (string.IsNullOrWhiteSpace(capabilityConfigJson))
        {
            return new List<PricingRule>();
        }

        try
        {
            using var doc = JsonDocument.Parse(capabilityConfigJson);
            if (!doc.RootElement.TryGetProperty("pricing", out var pricing)
                || !pricing.TryGetProperty("rules", out var rules)
                || rules.ValueKind != JsonValueKind.Array)
            {
                return new List<PricingRule>();
            }

            var result = new List<PricingRule>();
            foreach (var item in rules.EnumerateArray())
            {
                var match = item.TryGetProperty("match", out var matchElement)
                    ? new PricingMatch(
                        Model: ReadString(matchElement, "model"),
                        Mode: ReadString(matchElement, "mode"),
                        DurationSeconds: ReadInt(matchElement, "duration") ?? ReadInt(matchElement, "durationSeconds"))
                    : new PricingMatch(null, null, null);
                result.Add(new PricingRule(
                    match,
                    TryGetDecimal(item, "chargedPoints"),
                    TryGetDecimal(item, "providerEstimatedCostUsd") ?? TryGetDecimal(item, "yescaleUsd"),
                    ReadString(item, "costSource"),
                    ReadString(item, "ruleKey")));
            }

            return result;
        }
        catch (JsonException)
        {
            return new List<PricingRule>();
        }
    }

    private static string BuildSnapshotJson(
        string model,
        string mode,
        int durationSeconds,
        string aspectRatio,
        string resolution,
        PricingRule? matchedRule,
        decimal chargedPoints,
        decimal? fallbackUsd = null)
        => JsonSerializer.Serialize(new
        {
            model,
            mode,
            durationSeconds,
            aspectRatio,
            resolution,
            chargedPoints,
            providerEstimatedCostUsd = matchedRule?.ProviderEstimatedCostUsd ?? fallbackUsd,
            costSource = matchedRule?.CostSource ?? "configured_tariff_fallback",
            ruleKey = matchedRule?.RuleKey ?? BuildRuleKey(model, mode, durationSeconds),
            matchedRule = matchedRule is null ? null : new
            {
                match = new
                {
                    matchedRule.Match.Model,
                    matchedRule.Match.Mode,
                    duration = matchedRule.Match.DurationSeconds
                },
                matchedRule.ChargedPoints,
                matchedRule.ProviderEstimatedCostUsd
            },
            capturedAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);

    private static string BuildRuleKey(string model, string mode, int durationSeconds)
        => $"{model}|{mode}|{durationSeconds}";

    private static decimal? TryReadDecimal(string? json, string name)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryGetDecimal(doc.RootElement, name);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static decimal? TryGetDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private sealed record PricingRule(
        PricingMatch Match,
        decimal? ChargedPoints,
        decimal? ProviderEstimatedCostUsd,
        string? CostSource,
        string? RuleKey);

    private sealed record PricingMatch(string? Model, string? Mode, int? DurationSeconds);
}
