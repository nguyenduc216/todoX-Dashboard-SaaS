using System.Text;
using System.Text.Json;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseServiceSplitRegressionTests
{
    [Fact]
    public void TimelapseServicesUseVerifiedLiveProfileCategories()
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TIMELAPSE_CONSTRUCTION"] = "construction_exterior",
            ["TIMELAPSE_LIVING_ROOM"] = "interior_livingroom",
            ["TIMELAPSE_BEDROOM"] = "interior_bedroom",
            ["TIMELAPSE_KITCHEN"] = "interior_kitchen",
            ["TIMELAPSE_POOL"] = "pool_construction",
            ["TIMELAPSE_INFRASTRUCTURE"] = "road_bridge_construction",
            ["TIMELAPSE_LANDSCAPE"] = "landscape"
        };

        Assert.Equal(expected.Count, TimelapseServiceCatalog.Services.Count);
        foreach (var service in TimelapseServiceCatalog.Services)
        {
            Assert.Equal(expected[service.ServiceCode], service.Category);
        }
    }

    [Theory]
    [InlineData("TIMELAPSE_CONSTRUCTION", "construction_exterior")]
    [InlineData("TIMELAPSE_LIVING_ROOM", "interior_livingroom")]
    [InlineData("TIMELAPSE_BEDROOM", "interior_bedroom")]
    [InlineData("TIMELAPSE_KITCHEN", "interior_kitchen")]
    [InlineData("TIMELAPSE_POOL", "pool_construction")]
    [InlineData("TIMELAPSE_INFRASTRUCTURE", "road_bridge_construction")]
    [InlineData("TIMELAPSE_LANDSCAPE", "landscape")]
    public void SelectedServiceCategoryIsTheCategoryUsedForProfileLookup(string serviceCode, string category)
    {
        Assert.True(TimelapseServiceCatalog.TryGet(serviceCode, out var definition));
        Assert.Equal(category, definition.Category);
    }

    [Fact]
    public void LandscapeContinuityResolverAcceptsOnlyVerifiedProfiles71Through73()
    {
        foreach (var selectNo in new[] { 71, 72, 73 })
        {
            var json = JsonSerializer.Serialize(new { profileJson = new { select_no = selectNo, category = "landscape" } });
            var profile = TimelapsePromptResolver.ResolveLandscapeContinuityProfile(json);

            Assert.NotNull(profile);
            Assert.Equal(selectNo, profile.SelectNo);
        }

        foreach (var selectNo in new[] { 70, 74 })
        {
            var json = JsonSerializer.Serialize(new { profileJson = new { select_no = selectNo, category = "landscape" } });
            Assert.Null(TimelapsePromptResolver.ResolveLandscapeContinuityProfile(json));
        }
    }

    [Fact]
    public void SplitSqlCopiesAllActiveLegacyImageAndVideoScenePricesAndUpdatesConflicts()
    {
        var sql = ReadSql();

        Assert.Contains("FROM catalog.service_sell_prices p", sql);
        Assert.Contains("lower(legacy.service_code) = 'construction_video'", sql);
        Assert.Contains("p.is_active = true", sql);
        Assert.Contains("legacy_prices.asset_type", sql);
        Assert.Contains("legacy_prices.quality_tier", sql);
        Assert.Contains("legacy_prices.duration_seconds", sql);
        Assert.Contains("sell_points = EXCLUDED.sell_points", sql);
        Assert.Contains("display_label = EXCLUDED.display_label", sql);
        Assert.Contains("is_active = EXCLUDED.is_active", sql);
        Assert.Contains("sort_order = EXCLUDED.sort_order", sql);
        Assert.DoesNotContain("('video_scene', 'standard', 6", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("('video_scene', 'premium', 6", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitSqlLeavesLegacyConstructionVideoForJobsButHidesItFromNewSelection()
    {
        var sql = ReadSql();

        Assert.Contains("WHERE lower(service_code) = 'construction_video'", sql);
        Assert.Contains("SET status = 'inactive'", sql);
        Assert.Contains("ON CONFLICT (service_id, asset_type, quality_tier, (COALESCE(duration_seconds, 0)))", sql);
    }

    private static string ReadSql()
        => File.ReadAllText(
            Path.Combine(RepoRoot, "database", "manual", "timelapse", "20260828_split_timelapse_services.sql"),
            Encoding.UTF8);

    private static string RepoRoot
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
