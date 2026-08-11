using System.Net;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class AiProviderDurationPricingTests
{
    [Fact]
    public async Task CatalogClient_NormalizesVeoOmniDurationsAndVerifiedPrices()
    {
        var client = CreateClient(
            """
            {
              "models": [
                {
                  "model": "veo-omni",
                  "id_base": "veo",
                  "name": "VEO Omni",
                  "status": "ON",
                  "server": "gommo",
                  "rate_type": "credit",
                  "modes": ["flash"],
                  "durations": [4, 6, 8, 10],
                  "resolutions": ["720p"],
                  "prices": [
                    { "mode": "flash", "duration": 4, "resolution": "720p", "price": 1260, "price_default": 1400 }
                  ]
                }
              ]
            }
            """);

        var result = await client.FetchAsync(Provider());
        var model = Assert.Single(result.Models);

        Assert.Equal("veo-omni", model.ProviderModelCode);
        Assert.Equal("veo", model.ProviderModelIdBase);
        Assert.Equal("gommo", model.ServerCode);
        Assert.Equal([4, 6, 8, 10], model.SupportedDurations);
        Assert.Contains(model.Prices, x => x.Mode == "flash" && x.Resolution == "4K" && x.DurationSeconds == 10 && x.ProviderPrice == 7200);
    }

    [Fact]
    public async Task CatalogClient_KeepsSeedanceDurationsWithoutInventingPrices()
    {
        var client = CreateClient(
            """
            {
              "models": [
                {
                  "model": "seedance-2-pro",
                  "name": "Seedance 2 Pro",
                  "status": "ON",
                  "media_type": "video",
                  "durations": [5, 10],
                  "modes": ["fast", "professional"]
                }
              ]
            }
            """);

        var result = await client.FetchAsync(Provider());
        var model = Assert.Single(result.Models);

        Assert.Equal([5, 10], model.SupportedDurations);
        Assert.Empty(model.Prices);
    }

    [Fact]
    public void EstimateCost_MultipliesQuantityBySceneCount()
    {
        var model = new AiProviderModelListItemDto { Id = 1, ProviderId = 2, DisplayName = "VEO Omni" };
        var policy = new AiPricingPolicyDto { ProviderCreditPerInternalPoint = 1000, DefaultMarkupPercent = 20, MinimumSellPoints = 1, RoundingRule = "CEIL", Enabled = true, IsDefault = true };
        var price = new AiModelPriceDto { ProviderPrice = 1800, InternalCostPoints = 1.8m, SellPoints = 3, SellPriceMode = "FIXED", Active = true };

        var estimate = AiPricingEngine.BuildEstimate(model, policy, price, quantity: 4);

        Assert.True(estimate.Success);
        Assert.Equal(7200, estimate.ProviderTotalCost);
        Assert.Equal(7.2m, estimate.InternalTotalCostPoints);
        Assert.Equal(12, estimate.EstimatedTodoXPoints);
    }

    [Fact]
    public void ProviderSync_PreservesFixedSellPointsAndMarksRemovedPricesInactive()
    {
        var pricingRepository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiPricingRepository.cs");
        var syncService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");

        Assert.Contains("sell_points, sell_price_mode", pricingRepository);
        Assert.DoesNotContain("sell_points = EXCLUDED.sell_points", pricingRepository);
        Assert.DoesNotContain("sell_price_mode = EXCLUDED.sell_price_mode", pricingRepository);
        Assert.Contains("PRICE_DISABLED", syncService);
        Assert.Contains("MarkPriceInactiveAsync", syncService);
    }

    [Fact]
    public void ProviderSync_UsesPerProviderNonBlockingLockForDailyAndManualSync()
    {
        var syncService = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderSyncService.cs");
        var worker = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderCatalogSyncWorker.cs");

        Assert.Contains("ConcurrentDictionary<long, SemaphoreSlim>", syncService);
        Assert.Contains("WaitAsync(0", syncService);
        Assert.Contains("AiProviderCatalogSync", worker);
        Assert.Contains("DailyHourLocal", worker);
    }

    private static Ai79CatalogClient CreateClient(string json)
        => new(new HttpClient(new JsonHandler(json)) { BaseAddress = new Uri("https://catalog.local") });

    private static AiProviderDetailDto Provider()
        => new()
        {
            Id = 79,
            ProviderCode = "79ai",
            ProviderName = "79AI",
            BaseUrl = "https://catalog.local",
            ConfigJson = """{"catalog":{"video_models_path":"/catalog/video-models"}}"""
        };

    private static string ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string _json;

        public JsonHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json)
            });
    }
}
