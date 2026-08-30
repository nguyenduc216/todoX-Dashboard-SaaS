using Microsoft.Extensions.Configuration;
using TodoX.Web.Services;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class LegacyPointBillingFeatureFlagsTests
{
    [Fact]
    public void LegacyPointBillingDefaultsToDisabled()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.False(LegacyPointBillingFeatureFlags.IsEnabled(configuration));
        Assert.True(LegacyPointBillingFeatureFlags.IsDisabled(configuration));
        Assert.Equal(0, LegacyPointBillingFeatureFlags.NormalizePointCostEstimate(configuration, 123m));
        Assert.Equal(RenderPointStatuses.NotRequired, LegacyPointBillingFeatureFlags.NormalizePointStatus(configuration, RenderPointStatuses.Pending, 123m));
    }

    [Fact]
    public void LegacyPointFailureDetectorRecognizesOldInsufficientPointMessages()
    {
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure("insufficient_points", "anything"));
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "Không đủ điểm để tạo video."));
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "insufficient point: 173"));
        Assert.True(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "Cần bổ sung thêm: 71 điểm"));
        Assert.False(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "Cần: 173 điểm"));
        Assert.False(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "Hiện có: 102 điểm"));
        Assert.False(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "Cần bổ sung thêm: 71"));
        Assert.False(LegacyPointBillingFeatureFlags.IsLegacyInsufficientPointFailure(null, "provider_failure"));
    }

    [Fact]
    public void SystemVersionFeatureFlagIsPresentInProgramSource()
    {
        var program = File.ReadAllText(RepositoryFile("TodoX.Web", "Program.cs"));

        Assert.Contains("legacyPointBillingEnabled", program);
    }

    private static string RepositoryFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }
}
