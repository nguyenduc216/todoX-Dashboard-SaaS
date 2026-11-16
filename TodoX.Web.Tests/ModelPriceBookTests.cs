using System.Text.Json.Nodes;
using TodoX.Web.Services.EditVideoTool;
using TodoX.Web.Services.Pricing;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>One price rule for the dashboard and EditVideo: 79AI cost of the exact generation × markup (2.5–3).</summary>
public sealed class ModelPriceBookTests
{
    private static JsonObject M(string json) => JsonNode.Parse(json)!.AsObject();

    private static readonly JsonObject VeoOmni = M("""
        {"model":"veo_omni","rate_type":"per_unit","prices":[
          {"mode":"flash","resolution":"720p","duration":"4","price":1400},
          {"mode":"flash","resolution":"720p","duration":"8","price":2400},
          {"mode":"flash","resolution":"1080p","duration":"8","price":2700},
          {"mode":"flash","resolution":"4k","duration":"8","price":7000}]}
        """);

    [Fact]
    public void VideoCostIsTheExactRowForModelModeResolutionAndLength()
    {
        Assert.Equal(2400m, ModelCostLookup.Credits(VeoOmni, "flash", "720p", 8));
        Assert.Equal(2700m, ModelCostLookup.Credits(VeoOmni, "flash", "1080P", 8));
        Assert.Equal(1400m, ModelCostLookup.Credits(VeoOmni, null, "720", 4));
        // No 6 s row: the dearest per-second rate of that resolution × 6 (never under-priced).
        Assert.Equal(2100m, ModelCostLookup.Credits(VeoOmni, "flash", "720p", 6));
        // Unknown resolution: the most expensive row.
        Assert.Equal(7000m, ModelCostLookup.Credits(VeoOmni, "flash", null, 8));
    }

    [Fact]
    public void FlatAndPerSecondModelsAreReadTheirOwnWay()
    {
        var veo31 = M("""{"model":"veo_3_1","rate_type":"per_unit","prices":[{"mode":"lite","resolution":"720p","price":800},{"mode":"quality","resolution":"720p","price":6000}]}""");
        Assert.Equal(800m, ModelCostLookup.Credits(veo31, "lite", "720p", 8));
        Assert.Equal(6000m, ModelCostLookup.Credits(veo31, "quality", "720p", 8));

        var motion = M("""{"model":"kling_video_motion_3","rate_type":"per_second","prices":[{"mode":"standard","price":1500},{"mode":"professional","price":2100}]}""");
        Assert.Equal(30000m, ModelCostLookup.Credits(motion, "standard", null, 20));
        Assert.Equal(2100m * 10, ModelCostLookup.Credits(motion, "professional", null, 10));

        Assert.Null(ModelCostLookup.Credits(M("""{"model":"x","prices":[]}"""), null, null, 8));
    }

    [Fact]
    public void PriceIsCostTimesMarkupKeptInsideTheBand()
    {
        var book = new ModelPriceBook
        {
            Models = [new ModelPriceEntry { Kind = "video", Model = "veo_omni" }, new ModelPriceEntry { Kind = "video", Model = "kling_4_0", Markup = 5m },
                new ModelPriceEntry { Kind = "video", Model = "cheap", Markup = 1.2m }]
        };
        Assert.Equal(6m, book.PointsForCredits("video", "veo_omni", 2400m));    // 2.400đ × 2,5 = 6 điểm
        Assert.Equal(43.2m, book.PointsForCredits("video", "kling_4_0", 14400m)); // markup 5 is capped at 3
        Assert.Equal(2.5m, book.PointsForCredits("video", "cheap", 1000m));     // markup 1,2 is raised to 2,5
        Assert.Equal(0.1m, book.Points(10m, 2.5m));                              // never below 0.1
        Assert.Equal(2.5m, book.PointsForCredits("voice", "vbee", 1000m));       // Vbee 1.000đ/voice → 2,5 điểm

        var quote = new ModelPriceQuote("video", "veo_omni", null, 2400m, 2.5m, 6m, 8);
        Assert.Equal(0.75m, quote.PerSecond);
        Assert.True(8 * new ModelPriceQuote("video", "x", null, 1000m, 2.5m, 2.5m, 3).PerSecond >= 2.5m);
    }

    [Fact]
    public void OrderEnvironmentAndSwitchDecideWhatIsOfferedAndTheFallback()
    {
        var book = new ModelPriceBook
        {
            Models =
            [
                new ModelPriceEntry { Kind = "video", Model = "b", Name = "B", Order = 20 },
                new ModelPriceEntry { Kind = "video", Model = "a", Name = "A", Order = 10, Env = ModelPriceEnvs.Dashboard },
                new ModelPriceEntry { Kind = "video", Model = "c", Name = "C", Order = 5, Env = ModelPriceEnvs.EditVideo },
                new ModelPriceEntry { Kind = "video", Model = "off", Name = "Off", Order = 1, Enabled = false }
            ]
        };
        Assert.Equal(["c", "b"], book.Ordered("video", ModelPriceEnvs.EditVideo).Select(e => e.Model));
        Assert.Equal(["a", "b"], book.Ordered("video", ModelPriceEnvs.Dashboard).Select(e => e.Model));
        Assert.False(book.IsAllowed("video", "off", ModelPriceEnvs.EditVideo));
        Assert.False(book.IsAllowed("video", "a", ModelPriceEnvs.EditVideo));
        Assert.False(book.IsAllowed("video", "not-listed", ModelPriceEnvs.Dashboard));
        Assert.True(book.Rank("video", "not-listed") > book.Rank("video", "b"));
        Assert.Equal("edit", ModelPriceBook.Group("veo_omni_edit"));
        Assert.Equal("motion", ModelPriceBook.Group("kling_video_motion_3"));
        Assert.Equal("generate", ModelPriceBook.Group("veo_omni"));
    }

    [Fact]
    public void StoredBookKeepsAdminChoicesAndGainsNewDefaults()
    {
        var parsed = ModelPriceService.Parse("""{"defaultMarkup":3,"models":[{"kind":"video","model":"veo_omni","name":"Veo","order":99,"enabled":false,"env":"weird"}]}""");
        Assert.Equal(3m, parsed.DefaultMarkup);
        var veo = parsed.Find("video", "VEO_OMNI")!;
        Assert.False(veo.Enabled);
        Assert.Equal(99, veo.Order);
        Assert.Equal(ModelPriceEnvs.Both, veo.Env);
        Assert.NotNull(parsed.Find("voice", "vbee"));
        Assert.Equal(1000m, parsed.Find("voice", "vbee")!.FixedCostVnd);
        Assert.NotNull(ModelPriceService.Parse(null).Find("video", "kling_4_0"));
        Assert.NotNull(ModelPriceService.Parse("not json").Find("music", "suno-v6.0"));
    }

    [Fact]
    public void EditVideoPricesComeFromTheBookWhenItIsLoaded()
    {
        var book = ModelPriceService.Parse(null);
        var prices = new EditVideoAiPrices(new EditVideoAiSettings(), new Dictionary<string, decimal>(), book);
        Assert.Equal(7.5m, prices.For("music", "suno-v5.5", null, 3000m));
        Assert.Equal(10m, prices.For("music", "suno-v6.0", null, 4000m));
        Assert.Equal(36m, prices.For("video", "kling_4_0", "8", 14400m));
        Assert.Null(prices.Missing("video", "kling_4_0"));
    }

    [Fact]
    public void RVideoFollowsTheBookOrderAndSkipsSwitchedOffModels()
    {
        var book = ModelPriceService.Parse("""{"models":[{"kind":"video","model":"grok_video_heavy","order":1},{"kind":"video","model":"veo_omni","order":50,"enabled":false}]}""");
        var order = RVideoVideoModelPolicy.BookOrder(book);
        Assert.StartsWith("79ai|grok_video_heavy", order[0]);
        Assert.Contains("79ai|veo_omni|flash", RVideoVideoModelPolicy.BookExcluded(book));
        Assert.Equal("720p", RVideoVideoModelPolicy.PriceQuery(null, null, null, 8).Resolution);
        Assert.Equal("veo_omni", RVideoVideoModelPolicy.PriceQuery(null, null, "1080p", 8).Model);
    }

    [Fact]
    public void AdminSamplesShowCostAndPrice()
    {
        var book = ModelPriceService.Parse(null);
        var samples = ModelPriceSamples.For("video", VeoOmni, book, book.Find("video", "veo_omni"));
        Assert.Contains(samples, s => s.Label == "720p · 8s" && s.CostVnd == 2400m && s.Points == 6m);
        var vbee = ModelPriceSamples.For("voice", null, book, book.Find("voice", "vbee"));
        Assert.Equal(2.5m, Assert.Single(vbee).Points);
    }
}
