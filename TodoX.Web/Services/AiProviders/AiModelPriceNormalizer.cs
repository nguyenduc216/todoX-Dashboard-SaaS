using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public static class AiModelPriceNormalizer
{
    private static readonly HashSet<string> SellPriceModes = new(StringComparer.Ordinal)
    {
        "AUTO",
        "FIXED",
        "MARKUP"
    };

    private static readonly HashSet<string> RoundingRules = new(StringComparer.Ordinal)
    {
        "CEIL",
        "FLOOR",
        "ROUND",
        "NONE"
    };

    public static bool NormalizeForCatalog(AiModelPriceDto price, string providerCode, out string? ignoredReason)
    {
        price.Active = true;
        return Normalize(price, providerCode, "catalog", rejectNegativeSellFields: true, out ignoredReason);
    }

    public static bool NormalizeForManualSave(AiModelPriceDto price, string providerCode, out string? ignoredReason)
        => Normalize(price, providerCode, "manual", rejectNegativeSellFields: true, out ignoredReason);

    private static bool Normalize(
        AiModelPriceDto price,
        string providerCode,
        string defaultPriceSource,
        bool rejectNegativeSellFields,
        out string? ignoredReason)
    {
        ignoredReason = null;

        price.Mode = NormalizeNullable(price.Mode);
        price.Resolution = NormalizeNullable(price.Resolution);
        price.Ratio = NormalizeNullable(price.Ratio);
        price.RateType = NormalizeNullable(price.RateType) ?? "per_unit";
        price.UnitType = NormalizeNullable(price.UnitType) ?? "request";
        price.ProviderPriceUnit = NormalizeProviderPriceUnit(providerCode, NormalizeNullable(price.ProviderPriceUnit) ?? "credit");
        price.SellPriceMode = NormalizeSellPriceMode(price.SellPriceMode);
        price.MinimumPoints = Math.Max(price.MinimumPoints ?? 0, 0);
        price.RoundingRule = NormalizeRoundingRule(price.RoundingRule);
        price.PriceSource = NormalizeNullable(price.PriceSource) ?? defaultPriceSource;
        price.EffectiveFrom ??= DateTime.UtcNow;

        if (price.DurationSeconds is <= 0)
        {
            ignoredReason = "invalid duration_seconds";
            return false;
        }

        if (price.ProviderPrice is < 0)
        {
            ignoredReason = "invalid provider_price";
            return false;
        }

        if (price.ProviderPriceDefault is < 0)
        {
            ignoredReason = "invalid provider_price_default";
            return false;
        }

        if (price.InternalCostPoints is < 0)
        {
            ignoredReason = "invalid internal_cost_points";
            return false;
        }

        if (rejectNegativeSellFields && price.SellPoints is < 0)
        {
            ignoredReason = "invalid sell_points";
            return false;
        }

        if (price.EffectiveTo is not null && price.EffectiveTo <= price.EffectiveFrom)
        {
            ignoredReason = "invalid effective range";
            return false;
        }

        return true;
    }

    private static string NormalizeProviderPriceUnit(string providerCode, string unit)
        => string.Equals(providerCode, "79ai", StringComparison.OrdinalIgnoreCase)
            ? unit.Trim().ToLowerInvariant() switch
            {
                "79ai_credit" or "credits" => "credit",
                _ => unit.Trim()
            }
            : unit.Trim();

    private static string NormalizeSellPriceMode(string? mode)
    {
        var normalized = NormalizeNullable(mode)?.ToUpperInvariant() ?? "AUTO";
        return SellPriceModes.Contains(normalized) ? normalized : "AUTO";
    }

    private static string NormalizeRoundingRule(string? rule)
    {
        var normalized = NormalizeNullable(rule)?.ToUpperInvariant() ?? "CEIL";
        return RoundingRules.Contains(normalized) ? normalized : "CEIL";
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
