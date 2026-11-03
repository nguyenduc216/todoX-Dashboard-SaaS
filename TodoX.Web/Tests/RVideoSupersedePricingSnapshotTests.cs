using System.Reflection;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoSupersedePricingSnapshotTests
{
    [Theory]
    [InlineData(4, "0.8", "3.2")]
    [InlineData(6, "0.8", "4.8")]
    [InlineData(8, "0.8", "6.4")]
    [InlineData(6, "1.2", "7.2")]
    public void QuoteUsesTheSameDurationAndRateAsCompletion(int seconds, string rateText, string expectedText)
    {
        var rate = decimal.Parse(rateText, System.Globalization.CultureInfo.InvariantCulture);
        var scene = new VideoProjectSceneDto { Id=42, ProjectId=1, DurationSeconds=seconds, VideoPrompt="prompt" };
        var input = new SceneVideoRenderWorkItemInput { ProjectId=1, SceneId=42, DurationSeconds=seconds,
            RequestedDurationSeconds=seconds, VideoPrompt="prompt", ModelName="chosen", RequestedMode="flash",
            SelectedSourceImageVersionId=Guid.NewGuid() };
        var pricing = new PointPricingRate("video","standard",rate,"per_second","service_override");
        var quote = RVideoSceneVideoOperationSettings.Capture(scene, input, "Chosen", null, pricing);
        input.DurationSeconds=99; scene.VideoPrompt="changed";
        Assert.Equal(decimal.Parse(expectedText,System.Globalization.CultureInfo.InvariantCulture),quote.EstimatedPoints);
        Assert.Equal(RVideoSceneVideoCompletionService.CalculateActualVideoPoints(seconds,rate),quote.EstimatedPoints);
        Assert.Equal(seconds,quote.CreateWorkItem().RequestedDurationSeconds);
        Assert.Equal("prompt",quote.CreateWorkItem().VideoPrompt);
        Assert.NotEqual(RVideoSceneVideoOperationSettings.CaptureSceneConfig(scene),quote.SceneConfigJson);
        Assert.True(quote.MatchesRate(pricing));
        Assert.False(quote.MatchesRate(pricing with { Rate=rate+1 }));
        Assert.False(quote.MatchesRate(pricing with { QualityTier="premium" }));
        Assert.False(quote.MatchesRate(pricing with { Unit="per_job" }));
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("premium")]
    public void QuoteReusesCanonicalConfiguredTier(string tier)
    {
        var route = new VideoProviderRoute(1,2,"provider","image_to_video","model",null,
            System.Text.Json.JsonSerializer.Serialize(new { quality_tier=tier }),0);
        Assert.Equal(tier,SceneVideoRenderHandler.ResolveQualityTier(route));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreSubmitPriceCheckUsesTheConfirmedServiceAndTier(bool priceChanged)
    {
        var serviceId = Guid.NewGuid();
        var scene = new VideoProjectSceneDto { Id=42, ProjectId=1, DurationSeconds=6, VideoPrompt="prompt" };
        var input = new SceneVideoRenderWorkItemInput { ProjectId=1, SceneId=42, ModelName="selected",
            RequestedMode="quality", DurationSeconds=6, VideoPrompt="prompt" };
        var rate = new PointPricingRate("video", "premium", 1.2m, "per_second", "service_override",serviceId);
        var quote = RVideoSceneVideoOperationSettings.Capture(scene,input,"Selected",serviceId,rate);
        var pricing = new PriceProbe(rate with { Rate=priceChanged ? 2m : rate.Rate });
        Assert.Equal(!priceChanged, await quote.IsPriceCurrentAsync(pricing));
        Assert.Equal(serviceId, pricing.ServiceId);
        Assert.Equal("premium", pricing.Tier);
        Assert.False(await quote.IsPriceCurrentAsync(null));
        Assert.Equal(7.2m, quote.EstimatedPoints);
    }

    [Fact]
    public void IdenticalSettingsRetainQuoteButNewModelModeOrImageRequiresNewConsent()
    {
        var scene = new VideoProjectSceneDto { Id=42, ProjectId=1, DurationSeconds=6, VideoPrompt="prompt" };
        var input = new SceneVideoRenderWorkItemInput { ProjectId=1, SceneId=42, ModelName="selected",
            RequestedMode="flash", DurationSeconds=6, VideoPrompt="prompt", SelectedSourceImageVersionId=Guid.NewGuid() };
        var rate = new PointPricingRate("video", "standard", 0.8m, "per_second", "global");
        var quote = RVideoSceneVideoOperationSettings.Capture(scene,input,"Selected",null,rate);
        Assert.Equal(quote,RVideoSceneVideoOperationSettings.Capture(scene,input,"Selected",null,rate));
        input.ModelName="different"; input.RequestedMode="quality";
        var changed=RVideoSceneVideoOperationSettings.Capture(scene,input,"Different",null,rate with { QualityTier="premium",Rate=1.2m });
        Assert.NotEqual(quote,changed);
        Assert.Equal(7.2m,changed.EstimatedPoints);
        input.SelectedSourceImageVersionId=Guid.NewGuid();
        Assert.NotEqual(changed,RVideoSceneVideoOperationSettings.Capture(scene,input,"Different",null,rate with { QualityTier="premium",Rate=1.2m }));
    }

    private sealed class PriceProbe(PointPricingRate rate) : IPointPricingService
    {
        public Guid? ServiceId; public string? Tier;
        public Task<PointPricingRate> ResolveRateAsync(Guid? serviceId,string resourceType,string qualityTier,CancellationToken ct=default)
        { Assert.Equal("video",resourceType); ServiceId=serviceId; Tier=qualityTier; return Task.FromResult(rate); }
        public Task<PointPricingEstimate> EstimateAsync(PointPricingEstimateRequest request,CancellationToken ct=default)
            => throw new NotSupportedException();
    }

}
