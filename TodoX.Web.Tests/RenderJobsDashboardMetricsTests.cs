using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class RenderJobsDashboardMetricsTests
{
    [Fact]
    public void BuildStats_CountsProjectStatusesOnce()
    {
        var projects = new[]
        {
            Project(VideoProjectStatuses.Rendering, sceneCount: 7),
            Project(VideoProjectStatuses.Merging, sceneCount: 4),
            Project(VideoProjectStatuses.Completed, sceneCount: 9),
            Project(VideoProjectStatuses.Draft, sceneCount: 5),
            Project(VideoProjectStatuses.SceneReady, sceneCount: 6),
            Project(VideoProjectStatuses.ReadyToMerge, sceneCount: 8),
            Project(VideoProjectStatuses.Failed, sceneCount: 3),
        };

        var stats = RenderJobsDashboardMetrics.BuildStats(projects);

        Assert.Equal(7, stats.Total);
        Assert.Equal(2, stats.Processing);
        Assert.Equal(1, stats.Completed);
        Assert.Equal(3, stats.Queued);
        Assert.Equal(1, stats.Failed);
    }

    private static VideoProjectListItemDto Project(string status, int sceneCount)
        => new()
        {
            Id = sceneCount,
            Status = status,
            SceneCount = sceneCount,
            ImageReadyCount = sceneCount,
            VideoReadyCount = sceneCount
        };
}
