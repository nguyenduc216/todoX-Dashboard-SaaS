using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class RenderSceneImageUiStateTests
{
    [Theory]
    [InlineData(RenderSceneImageVisualState.Queued, true)]
    [InlineData(RenderSceneImageVisualState.Rendering, true)]
    [InlineData(RenderSceneImageVisualState.Retrying, true)]
    [InlineData(RenderSceneImageVisualState.Ready, false)]
    [InlineData(RenderSceneImageVisualState.Failed, false)]
    [InlineData(RenderSceneImageVisualState.None, false)]
    public void IsActive_OnlyReturnsTrueForNonTerminalRenderStates(RenderSceneImageVisualState state, bool expected)
    {
        Assert.Equal(expected, RenderSceneImageUiState.IsActive(state));
    }

    [Fact]
    public void ShouldPoll_DoesNotPollForStableSceneReadyProject()
    {
        var shouldPoll = RenderSceneImageUiState.ShouldPoll(
            batchActive: false,
            sceneStates: new[] { RenderSceneImageVisualState.Ready, RenderSceneImageVisualState.Failed },
            projectStatus: VideoProjectStatuses.SceneReady);

        Assert.False(shouldPoll);
    }

    [Fact]
    public void ShouldPoll_PollsWhenOnlyOneSceneIsRendering()
    {
        var shouldPoll = RenderSceneImageUiState.ShouldPoll(
            batchActive: false,
            sceneStates: new[] { RenderSceneImageVisualState.Ready, RenderSceneImageVisualState.Rendering },
            projectStatus: VideoProjectStatuses.SceneReady);

        Assert.True(shouldPoll);
    }

    [Theory]
    [InlineData(VideoProjectStatuses.Rendering)]
    [InlineData(VideoProjectStatuses.Merging)]
    public void ShouldPoll_PollsForActiveProjectPipeline(string projectStatus)
    {
        var shouldPoll = RenderSceneImageUiState.ShouldPoll(
            batchActive: false,
            sceneStates: new[] { RenderSceneImageVisualState.Ready },
            projectStatus: projectStatus);

        Assert.True(shouldPoll);
    }
}
