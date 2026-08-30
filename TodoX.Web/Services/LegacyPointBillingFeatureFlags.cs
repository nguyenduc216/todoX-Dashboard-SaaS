using Microsoft.Extensions.Configuration;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services;

public static class LegacyPointBillingFeatureFlags
{
    private const string SectionKey = "LegacyPointBilling:Enabled";

    public static bool IsEnabled(IConfiguration configuration)
        => configuration.GetValue(SectionKey, false);

    public static bool IsDisabled(IConfiguration configuration)
        => !IsEnabled(configuration);

    public static decimal NormalizePointCostEstimate(IConfiguration configuration, decimal pointCostEstimate)
        => IsDisabled(configuration) ? 0m : pointCostEstimate;

    public static string NormalizePointStatus(IConfiguration configuration, string? pointStatus, decimal pointCostEstimate)
        => IsDisabled(configuration) || pointCostEstimate <= 0
            ? RenderPointStatuses.NotRequired
            : (string.IsNullOrWhiteSpace(pointStatus) ? RenderPointStatuses.Pending : pointStatus);

    public static bool IsLegacyInsufficientPointFailure(string? errorCode, string? errorMessage)
    {
        if (string.Equals(errorCode, "insufficient_points", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        var message = errorMessage.Trim();
        return message.Contains("Không đủ điểm", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient point", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("Cần bổ sung", StringComparison.OrdinalIgnoreCase)
                && message.Contains("điểm", StringComparison.OrdinalIgnoreCase));
    }
}
