using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public class AiImageBillingTests
{
    [Fact]
    public void ConfiguredCost_ConvertsPointsToUsdUsingTodoXPolicy()
    {
        var cost = AiImageBillingCost.FromConfiguredPoints(0.064m, 8000m, 10000m);

        Assert.Equal(0.08m, cost.ProviderEstimatedCostUsd);
        Assert.Equal(0.064m, cost.ProviderCostPoints);
        Assert.Equal(0.064m, cost.CustomerChargedPoints);
        Assert.Null(cost.ProviderActualCostUsd);
        Assert.Equal("configured_tariff", cost.ProviderCostSource);
    }

    [Fact]
    public async Task YEScaleAccountSnapshot_ReturnsNotSupportedUntilBalanceEndpointIsVerified()
    {
        var service = new YEScaleAccountService();

        var snapshot = await service.GetSnapshotAsync(forceRefresh: true);

        Assert.Equal(YEScaleBalanceStatus.NotSupported, snapshot.Status);
        Assert.Null(snapshot.BalanceUsd);
        Assert.Contains("not verified", snapshot.Message);
    }
}
