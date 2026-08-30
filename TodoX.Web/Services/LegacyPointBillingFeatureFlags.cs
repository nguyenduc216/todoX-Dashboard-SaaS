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
            || message.Contains("\u004b\u0068\u00c3\u00b4\u006e\u0067 \u00c4\u2018\u00e1\u00bb\u00a7 \u00c4\u2018\u0069\u00e1\u00bb\u0192\u006d", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient point", StringComparison.OrdinalIgnoreCase)
            || ((message.Contains("Cần bổ sung", StringComparison.OrdinalIgnoreCase)
                 || message.Contains("\u0043\u00e1\u00ba\u00a7\u006e \u0062\u00e1\u00bb\u2022 \u0073\u0075\u006e\u0067", StringComparison.OrdinalIgnoreCase))
                && (message.Contains("điểm", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("\u00c4\u2018\u0069\u00e1\u00bb\u0192\u006d", StringComparison.OrdinalIgnoreCase)));
    }
}
