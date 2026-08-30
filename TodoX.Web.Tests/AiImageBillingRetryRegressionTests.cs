using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiImageBillingRetryRegressionTests
{
    [Fact]
    public void LegacyPointFailureDetectorRequiresExplicitPointEvidence()
    {
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(
            null,
            "\u004b\u0068\u00f4\u006e\u0067 \u0111\u1ee7 \u0111\u0069\u1ec3\u006d \u0111\u1ec3 \u0074\u1ea1\u006f \u0076\u0069\u0064\u0065\u006f"));
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "insufficient points: 173"));
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(
            null,
            "\u0043\u1ea7\u006e \u0062\u1ed5 \u0073\u0075\u006e\u0067 \u0074\u0068\u00ea\u006d: 76 \u0111\u0069\u1ec3\u006d"));
        Assert.False(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(
            null,
            "\u0054\u0068\u0069\u1ebf\u0075 \u1ea3\u006e\u0068. \u0043\u1ea7\u006e: 1 \u1ea3\u006e\u0068 \u0111\u1ea7\u0075 \u0076\u00e0\u006f."));
        Assert.False(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(
            null,
            "\u0050\u0072\u006f\u0076\u0069\u0064\u0065\u0072 \u0068\u0069\u1ec7\u006e \u0063\u00f3: 0 \u0065\u006e\u0064\u0070\u006f\u0069\u006e\u0074."));
    }

    [Fact]
    public void LegacyBillingDisabledNormalizesUnsubmittedInsufficientRecordForProviderRetry()
    {
        var source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TodoX.Web",
            "Services",
            "AiProviders",
            "AiImageBillingService.cs"));

        Assert.Contains("legacyBillingDisabled: true", source);
        Assert.Contains("existing.Status == \"insufficient\"", source);
        Assert.Contains("string.IsNullOrWhiteSpace(existing.ProviderTaskId)", source);
        Assert.Contains("SET status = 'not_required'", source);
        Assert.Contains("customer_charged_points = 0", source);
        Assert.Contains("system_charged_points = 0", source);
        Assert.Contains("provider_task_id AS ProviderTaskId", source);
        Assert.Contains("new AiImageBillingReservation(", source);
        Assert.Contains("\"not_required\"", source);
        Assert.Contains("true,\r\n                true,", source);
        Assert.Contains("existing.Id,\r\n                null,\r\n                null);", source);
        Assert.Contains("return HandleExistingReservation(existing);", source);
        Assert.Contains("if (existing.Status is \"completed\")", source);
        Assert.Contains("return new AiImageBillingReservation(true, false", source);
    }
}
