namespace TodoX.Web.Services.AiProviders;

public static class AiProviderModelStatusNormalizer
{
    public static string Normalize(string? providerStatus)
    {
        var value = string.IsNullOrWhiteSpace(providerStatus)
            ? null
            : providerStatus.Trim().ToUpperInvariant();

        if (value is null)
        {
            return "UNKNOWN";
        }

        return value switch
        {
            "ON" or "ACTIVE" or "ENABLED" or "AVAILABLE" or "ONLINE" or "READY" => "ON",
            "MAINTENANCE" or "MAINTAINING" or "MAINTENANCE_MODE" => "MAINTENANCE",
            "DISABLED" or "OFF" or "INACTIVE" or "UNAVAILABLE" => "DISABLED",
            "DEPRECATED" or "RETIRED" or "END_OF_LIFE" or "EOL" => "DEPRECATED",
            "UNKNOWN" => "UNKNOWN",
            _ => "UNKNOWN"
        };
    }

    public static bool IsKnown(string? providerStatus)
    {
        var normalized = Normalize(providerStatus);
        return normalized != "UNKNOWN" || string.Equals(providerStatus?.Trim(), "UNKNOWN", StringComparison.OrdinalIgnoreCase);
    }
}
