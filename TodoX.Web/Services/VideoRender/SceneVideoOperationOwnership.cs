using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public static class SceneVideoOperationOwnership
{
    public static void RequireSameOperation(SceneVideoVersionDto version, SceneVideoVersionCreateRequest request)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(request);
        if (version.ProjectId != request.ProjectId || version.SceneId != request.SceneId
            || version.RenderJobId != request.RenderJobId
            || !string.Equals(version.LogicalRequestId, request.LogicalRequestId, StringComparison.Ordinal))
            throw new RenderJobPendingReconciliationException(
                "Existing scene-video logical request belongs to a different operation; ownership transfer is prohibited.");
    }
}
