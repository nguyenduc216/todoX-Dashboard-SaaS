using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class PointModuleRegressionTests
{
    [Fact]
    public void PointPricingCalculatorUsesCountAndSecondsFormulas()
    {
        var imageRate = new PointPricingRate(PointPricingResourceTypes.Image, ServiceSellPriceQualityTiers.Standard, 3000, "per_render", "global");
        var videoRate = new PointPricingRate(PointPricingResourceTypes.Video, ServiceSellPriceQualityTiers.Standard, 1500, "per_second", "global");
        var voiceRate = new PointPricingRate(PointPricingResourceTypes.Voice, ServiceSellPriceQualityTiers.Standard, 500, "per_render", "global");

        var estimate = PointPricingCalculator.Estimate(6, imageRate, 48, videoRate, 6, voiceRate);

        Assert.Equal(18000, estimate.Image.Points);
        Assert.Equal(72000, estimate.Video.Points);
        Assert.Equal(3000, estimate.Voice.Points);
        Assert.Equal(93000, estimate.TotalPoints);
    }

    [Fact]
    public void TimelapseSellPriceSnapshotCarriesUnifiedEstimate()
    {
        var imageRate = new PointPricingRate(PointPricingResourceTypes.Image, ServiceSellPriceQualityTiers.Standard, 3000, "per_render", "global");
        var videoRate = new PointPricingRate(PointPricingResourceTypes.Video, ServiceSellPriceQualityTiers.Standard, 1500, "per_second", "global");
        var voiceRate = new PointPricingRate(PointPricingResourceTypes.Voice, ServiceSellPriceQualityTiers.Standard, 500, "per_render", "global");
        var estimate = PointPricingCalculator.Estimate(5, imageRate, 36, videoRate, 0, voiceRate);

        var snapshot = TimelapseSellPriceSnapshot.FromPointEstimate(estimate, 6);

        Assert.Equal(15000, snapshot.ImageSubtotal);
        Assert.Equal(54000, snapshot.VideoSubtotal);
        Assert.Equal(0, snapshot.VoiceSubtotal);
        Assert.Equal(69000, snapshot.TotalPoints);
    }

    [Fact]
    public void PreRenderUsagePlanSumsExplicitSceneDurations()
    {
        var plan = new PreRenderUsagePlan(
            null,
            4,
            ServiceSellPriceQualityTiers.Standard,
            new[]
            {
                new PreRenderVideoScene(1, 5),
                new PreRenderVideoScene(2, 7),
                new PreRenderVideoScene(3, 6),
                new PreRenderVideoScene(4, 8),
                new PreRenderVideoScene(5, 5),
                new PreRenderVideoScene(6, 9)
            },
            ServiceSellPriceQualityTiers.Standard,
            6,
            ServiceSellPriceQualityTiers.Standard,
            true).Validate();

        Assert.Equal(40, plan.VideoSeconds);
        Assert.Equal(4, plan.ImageCount);
        Assert.Equal(6, plan.VoiceCount);
        Assert.Equal(40, plan.ToPricingRequest().VideoSeconds);
    }

    [Fact]
    public void PreRenderUsagePlanRejectsMissingSceneDuration()
    {
        var plan = new PreRenderUsagePlan(
            null, 0, ServiceSellPriceQualityTiers.Standard,
            new[] { new PreRenderVideoScene(1, 0) },
            ServiceSellPriceQualityTiers.Standard, 0,
            ServiceSellPriceQualityTiers.Standard, false);

        var exception = Assert.Throws<InvalidOperationException>(() => plan.Validate());

        Assert.Equal("VIDEO_SCENE_DURATION_REQUIRED", exception.Message);
    }

    [Fact]
    public void CoreBillingUsesPointPricingService()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "Services", "Platform", "CoreBillingService.cs"));

        Assert.Contains("IPointPricingService", source);
        Assert.Contains("PointPricingEstimateRequest", source);
        Assert.Contains("PointPricingEstimate?", source);
    }

    [Fact]
    public void PointModuleMigrationSeedsRatesForEveryTenant()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "database", "migrations", "20260902_point_module.sql"));

        Assert.Contains("FROM system.tenants", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM crm.customers", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PointModuleMigrationConstrainsResourceUnitPairs()
    {
        var sql = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "database", "migrations", "20260902_point_module.sql"));

        Assert.Contains("chk_point_rate_config_resource_unit", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chk_service_point_rate_override_resource_unit", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(resource_type) = 'video' AND lower(unit) = 'per_second'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(resource_type) = 'image' AND lower(unit) = 'per_render'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower(resource_type) = 'voice' AND lower(unit) = 'per_render'", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot
        => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
