using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public static class AiPricingEngine
{
    public static AiModelPriceDto? FindExactPrice(
        IEnumerable<AiModelPriceDto> prices,
        string? mode,
        string? resolution,
        int? durationSeconds,
        string? ratio)
    {
        var normalizedMode = Normalize(mode);
        var normalizedResolution = Normalize(resolution);
        var normalizedRatio = Normalize(ratio);

        return prices.FirstOrDefault(price =>
            price.Active &&
            EqualsOrNull(price.Mode, normalizedMode) &&
            EqualsOrNull(price.Resolution, normalizedResolution) &&
            price.DurationSeconds == durationSeconds &&
            EqualsOrNull(price.Ratio, normalizedRatio));
    }

    public static decimal CalculateInternalUnitCostPoints(decimal providerPrice, decimal providerCreditPerInternalPoint)
    {
        if (providerCreditPerInternalPoint <= 0)
        {
            throw new InvalidOperationException("provider_credit_per_internal_point must be greater than zero.");
        }

        return providerPrice / providerCreditPerInternalPoint;
    }

    public static decimal CalculateSellUnitPoints(
        decimal internalUnitCostPoints,
        AiModelPriceDto? price,
        AiPricingPolicyDto? policy)
    {
        var mode = (price?.SellPriceMode ?? "AUTO").Trim().ToUpperInvariant();
        var markupPercent = price?.MarkupPercent ?? policy?.DefaultMarkupPercent ?? 0m;
        var minimum = Math.Max(price?.MinimumPoints ?? 0m, policy?.MinimumSellPoints ?? 0m);
        var rounding = (price?.RoundingRule ?? policy?.RoundingRule ?? "ROUND").Trim().ToUpperInvariant();

        decimal sell = mode switch
        {
            "FIXED" => price?.SellPoints ?? internalUnitCostPoints,
            "MARKUP" => internalUnitCostPoints * (1m + (markupPercent / 100m)),
            _ => internalUnitCostPoints * (1m + (markupPercent / 100m))
        };

        sell = ApplyMinimum(sell, minimum);
        sell = ApplyRounding(sell, rounding);
        return sell;
    }

    public static decimal ApplyRounding(decimal value, string roundingRule)
        => roundingRule.ToUpperInvariant() switch
        {
            "CEIL" => Math.Ceiling(value),
            "FLOOR" => Math.Floor(value),
            "NONE" => value,
            _ => decimal.Round(value, 2, MidpointRounding.AwayFromZero)
        };

    public static decimal ApplyMinimum(decimal value, decimal minimum)
        => value < minimum ? minimum : value;

    public static EstimateCostResponseDto BuildEstimate(
        AiProviderModelListItemDto model,
        AiPricingPolicyDto? policy,
        AiModelPriceDto? price,
        decimal quantity)
    {
        if (price is null)
        {
            return new EstimateCostResponseDto
            {
                Success = false,
                ErrorCode = "price_not_configured",
                Message = "price_not_configured",
                ProviderModel = model,
                PricingPolicy = policy
            };
        }

        var providerUnit = price.ProviderPrice ?? model.BaseProviderPrice ?? price.ProviderPriceDefault ?? 0m;
        var providerTotal = providerUnit * quantity;
        var providerCredit = policy?.ProviderCreditPerInternalPoint > 0 ? policy.ProviderCreditPerInternalPoint : 1m;
        var internalUnit = price.InternalCostPoints ?? CalculateInternalUnitCostPoints(providerUnit, providerCredit);
        var internalTotal = internalUnit * quantity;
        var sellUnit = price.SellPoints ?? CalculateSellUnitPoints(internalUnit, price, policy);
        var sellTotal = sellUnit * quantity;

        return new EstimateCostResponseDto
        {
            Success = true,
            ProviderModel = model,
            MatchedPrice = price,
            PricingPolicy = policy,
            ProviderUnitCost = providerUnit,
            ProviderTotalCost = providerTotal,
            InternalUnitCostPoints = internalUnit,
            InternalTotalCostPoints = internalTotal,
            SellUnitPoints = sellUnit,
            EstimatedTodoXPoints = sellTotal,
            PriceSource = price.PriceSource
        };
    }

    public static bool IsPriceChanged(AiModelPriceDto before, AiModelPriceDto after)
    {
        return before.ProviderPrice != after.ProviderPrice
            || before.InternalCostPoints != after.InternalCostPoints
            || before.SellPoints != after.SellPoints
            || !string.Equals(before.SellPriceMode, after.SellPriceMode, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(before.RoundingRule, after.RoundingRule, StringComparison.OrdinalIgnoreCase)
            || before.MarkupPercent != after.MarkupPercent
            || before.MinimumPoints != after.MinimumPoints;
    }

    private static bool EqualsOrNull(string? left, string? right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
