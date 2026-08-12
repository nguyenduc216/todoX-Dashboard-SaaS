using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public static class AiModelPriceVariantKey
{
    public static string Build(AiModelPriceDto price)
        => string.Join(":",
            price.Mode ?? string.Empty,
            price.Resolution ?? string.Empty,
            price.DurationSeconds?.ToString() ?? "0",
            price.Ratio ?? string.Empty,
            price.RateType ?? string.Empty,
            price.UnitType ?? string.Empty);
}
