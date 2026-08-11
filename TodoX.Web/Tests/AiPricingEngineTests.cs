using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiPricingEngineTests
{
    [Fact]
    public void FindExactPrice_MatchesExactVariant()
    {
        var prices = new[]
        {
            new AiModelPriceDto { Mode = "AUTO", Resolution = "720p", DurationSeconds = 6, Ratio = "16:9", Active = true },
            new AiModelPriceDto { Mode = "AUTO", Resolution = "1080p", DurationSeconds = 6, Ratio = "16:9", Active = true }
        };

        var matched = AiPricingEngine.FindExactPrice(prices, "AUTO", "720p", 6, "16:9");

        Assert.NotNull(matched);
        Assert.Equal("720p", matched!.Resolution);
    }

    [Fact]
    public void BuildEstimate_ReturnsPriceNotConfiguredWhenMissing()
    {
        var model = new AiProviderModelListItemDto { Id = 1, ProviderId = 2, DisplayName = "Seedance" };

        var result = AiPricingEngine.BuildEstimate(model, null, null, 1);

        Assert.False(result.Success);
        Assert.Equal("price_not_configured", result.ErrorCode);
    }

    [Fact]
    public void BuildEstimate_AppliesAutoMarkupAndRounding()
    {
        var model = new AiProviderModelListItemDto { Id = 1, ProviderId = 2, DisplayName = "Seedance" };
        var policy = new AiPricingPolicyDto { ProviderCreditPerInternalPoint = 10, DefaultMarkupPercent = 20, RoundingRule = "ROUND", Enabled = true, IsDefault = true };
        var price = new AiModelPriceDto { ProviderPrice = 100, SellPriceMode = "AUTO", MarkupPercent = 20, Active = true };

        var result = AiPricingEngine.BuildEstimate(model, policy, price, 2);

        Assert.True(result.Success);
        Assert.Equal(10, result.InternalUnitCostPoints);
        Assert.Equal(12, result.SellUnitPoints);
        Assert.Equal(24, result.EstimatedTodoXPoints);
    }

    [Fact]
    public void BuildEstimate_RespectsFixedSellPrice()
    {
        var model = new AiProviderModelListItemDto { Id = 1, ProviderId = 2, DisplayName = "Seedance" };
        var policy = new AiPricingPolicyDto { ProviderCreditPerInternalPoint = 10, DefaultMarkupPercent = 20, RoundingRule = "ROUND", Enabled = true, IsDefault = true };
        var price = new AiModelPriceDto { ProviderPrice = 100, SellPriceMode = "FIXED", SellPoints = 18, Active = true };

        var result = AiPricingEngine.BuildEstimate(model, policy, price, 1);

        Assert.True(result.Success);
        Assert.Equal(18, result.SellUnitPoints);
    }

    [Fact]
    public void SyncPlanner_FindsMissingCodes_AndPriceChange()
    {
        var missing = AiProviderSyncPlanner.GetMissingCodes(
            new[] { "a", "b", "c" },
            new[] { "a", "c" });

        Assert.Equal(new[] { "b" }, missing);

        var before = new[]
        {
            new AiModelPriceDto { Mode = "AUTO", Resolution = "720p", DurationSeconds = 6, Ratio = "16:9", ProviderPrice = 100, SellPoints = 10, Active = true }
        };
        var after = new[]
        {
            new AiModelPriceDto { Mode = "AUTO", Resolution = "720p", DurationSeconds = 6, Ratio = "16:9", ProviderPrice = 120, SellPoints = 10, Active = true }
        };

        Assert.True(AiProviderSyncPlanner.HasPriceChange(before, after));
    }
}
