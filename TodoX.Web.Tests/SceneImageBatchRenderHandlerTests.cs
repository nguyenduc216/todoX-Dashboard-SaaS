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

    [Fact]
    public void SceneImageLogicalRequest_ForBatch_IsStableForSameJobAndScene()
    {
        var jobId = Guid.NewGuid();

        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, jobId);
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image", 42, jobId);

        Assert.Equal(first, second);
        Assert.Contains(jobId.ToString("N"), first);
        Assert.Contains("scene-42", first);
    }

    [Fact]
    public void SceneImageLogicalRequest_ForUserRerender_IsNewOperation()
    {
        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", 42, null);
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", 42, null);

        Assert.NotEqual(first, second);
        Assert.StartsWith("render_job_scene_image_rerender-scene-42-", first);
    }
}
