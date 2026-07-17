using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

internal sealed record RenderJobsDashboardStats(int Total, int Processing, int Completed, int Queued, int Failed);

internal static class RenderJobsDashboardMetrics
{
    public static RenderJobsDashboardStats BuildStats(IEnumerable<VideoProjectListItemDto> projects)
    {
        var list = projects.ToList();
        return new RenderJobsDashboardStats(
            list.Count,
            list.Count(x => x.Status is VideoProjectStatuses.Rendering or VideoProjectStatuses.Merging),
            list.Count(x => x.Status == VideoProjectStatuses.Completed),
            list.Count(x => x.Status is VideoProjectStatuses.Draft or VideoProjectStatuses.SceneReady or VideoProjectStatuses.ReadyToMerge),
            list.Count(x => x.Status == VideoProjectStatuses.Failed));
    }
}
