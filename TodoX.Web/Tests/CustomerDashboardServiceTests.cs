using System.Text;
using TodoX.Web.Services;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class CustomerDashboardServiceTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = File.Exists(Path.Combine(RepoRoot, "Services", "CustomerDashboardService.cs"))
        ? RepoRoot
        : Path.Combine(RepoRoot, "TodoX.Web");
    private static readonly string ServicePath = Path.Combine(WebRoot, "Services", "CustomerDashboardService.cs");

    [Fact]
    public void StatusRules_HandleMixedCaseProcessingCompletedAndFailedStatuses()
    {
        Assert.True(CustomerDashboardStatusRules.IsProcessingStatus("RUNNING"));
        Assert.True(CustomerDashboardStatusRules.IsProcessingStatus("Generating"));
        Assert.True(CustomerDashboardStatusRules.IsCompletedStatus("COMPLETED"));
        Assert.True(CustomerDashboardStatusRules.IsFailedStatus("FAILED"));
    }

    [Fact]
    public void DashboardService_ProcessesQueriesSequentiallyAndNormalizesProcessingStatuses()
    {
        var source = File.ReadAllText(ServicePath, Encoding.UTF8);

        Assert.Contains("var renderCounts = await conn.QuerySingleAsync<DashboardCountsRow>", source);
        Assert.Contains("var danceCounts = await conn.QuerySingleAsync<DashboardCountsRow>", source);
        Assert.Contains("var recentRender = (await conn.QueryAsync<RecentRenderJobRow>", source);
        Assert.Contains("var recentDance = (await conn.QueryAsync<RecentDanceJobRow>", source);
        Assert.DoesNotContain("Task.WhenAll(renderCountsTask, danceCountsTask, recentRenderTask, recentDanceTask, charactersTask)", source);
        Assert.Contains("lower(status) = ANY(@processingStatuses)", source);
        Assert.DoesNotContain("status = ANY(@processingStatuses)", source);
        Assert.DoesNotContain("DanceSell", GetSupportedRenderJobTypesSection(source));
    }

    private static string GetSupportedRenderJobTypesSection(string source)
    {
        var start = source.IndexOf("public static readonly string[] SupportedRenderJobTypes", StringComparison.Ordinal);
        Assert.True(start >= 0);

        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        Assert.True(end > start);

        return source[start..(end + 2)];
    }
}
