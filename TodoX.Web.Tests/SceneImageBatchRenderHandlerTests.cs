using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

public class SceneImageBatchRenderHandlerTests
{
    [Fact]
    public void ShouldRenderScene_WhenOnlyMissingOrFailed_SkipsSuccessfulImages()
    {
        Assert.False(SceneImageBatchRenderHandler.ShouldRenderScene(new VideoProjectSceneDto
        {
            StaticImageUrl = "https://cdn/scene.png",
            Status = VideoSceneStatuses.ImageReady
        }, onlyMissingOrFailed: true));
    }

    [Fact]
    public void ShouldRenderScene_WhenOnlyMissingOrFailed_IncludesMissingAndFailed()
    {
        Assert.True(SceneImageBatchRenderHandler.ShouldRenderScene(new VideoProjectSceneDto
        {
            StaticImageUrl = null,
            Status = VideoSceneStatuses.Draft
        }, onlyMissingOrFailed: true));

        Assert.True(SceneImageBatchRenderHandler.ShouldRenderScene(new VideoProjectSceneDto
        {
            StaticImageUrl = "https://cdn/old.png",
            Status = VideoSceneStatuses.Failed
        }, onlyMissingOrFailed: true));
    }
}
