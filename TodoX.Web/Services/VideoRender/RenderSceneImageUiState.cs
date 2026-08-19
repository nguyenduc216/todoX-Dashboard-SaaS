using TodoX.Web.Models;

namespace TodoX.Web.Services.VideoRender;

public enum RenderSceneImageVisualState
{
    None,
    Queued,
    ProviderSubmitted,
    ProviderProcessing,
    Downloading,
    Rendering,
    Retrying,
    Ready,
    Failed
}

public static class RenderSceneImageUiState
{
    public static bool IsActive(RenderSceneImageVisualState state)
        => state is RenderSceneImageVisualState.Queued
            or RenderSceneImageVisualState.ProviderSubmitted
            or RenderSceneImageVisualState.ProviderProcessing
            or RenderSceneImageVisualState.Downloading
            or RenderSceneImageVisualState.Rendering
            or RenderSceneImageVisualState.Retrying;

    public static bool ShouldPoll(bool batchActive, IEnumerable<RenderSceneImageVisualState> sceneStates, string? projectStatus)
        => batchActive
           || sceneStates.Any(IsActive)
           || projectStatus is VideoProjectStatuses.Rendering or VideoProjectStatuses.Merging;
}
