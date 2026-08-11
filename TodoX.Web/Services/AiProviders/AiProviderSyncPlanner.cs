using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public static class AiProviderSyncPlanner
{
    public static IReadOnlyList<string> GetMissingCodes(
        IReadOnlyCollection<string> existingCodes,
        IReadOnlyCollection<string> incomingCodes)
    {
        var incoming = new HashSet<string>(incomingCodes.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        return existingCodes.Where(code => !incoming.Contains(code)).ToList();
    }

    public static bool HasPriceChange(IReadOnlyList<AiModelPriceDto> before, IReadOnlyList<AiModelPriceDto> after)
    {
        var beforeMap = before.ToDictionary(x => PriceKey(x), x => x, StringComparer.OrdinalIgnoreCase);
        var afterMap = after.ToDictionary(x => PriceKey(x), x => x, StringComparer.OrdinalIgnoreCase);
        if (beforeMap.Count != afterMap.Count)
        {
            return true;
        }

        foreach (var pair in beforeMap)
        {
            if (!afterMap.TryGetValue(pair.Key, out var afterPrice))
            {
                return true;
            }

            if (AiPricingEngine.IsPriceChanged(pair.Value, afterPrice))
            {
                return true;
            }
        }

        return false;
    }

    private static string PriceKey(AiModelPriceDto price)
        => string.Join(":", price.Mode ?? string.Empty, price.Resolution ?? string.Empty, price.DurationSeconds?.ToString() ?? string.Empty, price.Ratio ?? string.Empty);
}
