using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class YEScaleVideoPricingResolverTests
{
    [Fact]
    public void Resolve_UsesDurationRuleForGrokVideo()
    {
        var resolver = new YEScaleVideoPricingResolver();
        var option = new ProviderOptionDto
        {
            ModelName = "grok-video",
            UnitCostPoints = 0.11m
        };
        var capability = new AiProviderCapabilityDto
        {
            ConfigJson = """
            {
              "pricing": {
                "rules": [
                  { "match": { "duration": 6 }, "chargedPoints": 0.112, "providerEstimatedCostUsd": 0.14, "costSource": "configured_tariff" },
                  { "match": { "duration": 10 }, "chargedPoints": 0.16, "providerEstimatedCostUsd": 0.20, "costSource": "configured_tariff" }
                ]
              }
            }
            """
        };

        var resolved = resolver.Resolve(option, capability, "9:16", "720P", 10, hasSourceImage: true);

        Assert.Equal("grok-video", resolved.ModelName);
        Assert.Equal("i2v(img_ref)", resolved.Mode);
        Assert.Equal(10, resolved.DurationSeconds);
        Assert.Equal(0.16m, resolved.ChargedPoints);
        Assert.Equal(0.20m, resolved.ProviderEstimatedCostUsd);
        Assert.Equal("configured_tariff", resolved.CostSource);
        Assert.Contains("\"durationSeconds\":10", resolved.TariffSnapshotJson);
    }

    [Fact]
    public void Resolve_UsesConfiguredModeForOmniFlash()
    {
        var resolver = new YEScaleVideoPricingResolver();
        var option = new ProviderOptionDto
        {
            ModelName = "omni-flash",
            UnitCostPoints = 0.296m
        };
        var capability = new AiProviderCapabilityDto
        {
            ConfigJson = """
            {
              "mode": "v2v",
              "pricing": {
                "rules": [
                  { "match": { "mode": "i2v(img_ref)" }, "chargedPoints": 0.296, "providerEstimatedCostUsd": 0.37 },
                  { "match": { "mode": "v2v" }, "chargedPoints": 0.376, "providerEstimatedCostUsd": 0.47 }
                ]
              }
            }
            """
        };

        var resolved = resolver.Resolve(option, capability, "16:9", "720P", 4, hasSourceImage: true);

        Assert.Equal("v2v", resolved.Mode);
        Assert.Equal(0.376m, resolved.ChargedPoints);
        Assert.Equal(0.47m, resolved.ProviderEstimatedCostUsd);
        Assert.Contains("\"ruleKey\":\"omni-flash|v2v|4\"", resolved.TariffSnapshotJson);
    }
}
