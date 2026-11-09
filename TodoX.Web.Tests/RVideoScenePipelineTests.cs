using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoScenePipelineTests
{
    [Fact]
    public void BuildScenesTakesEveryPromptSceneInOrderWithItsPrompts()
    {
        const string prompt = """
        {"aspect_ratio":"9:16","scenes":[
          {"scene":2,"duration_seconds":6,"scene_purpose":"cta","image_prompt":"img two","motion_prompt":"move two","voice":"Mua ngay"},
          {"scene":1,"duration_seconds":8,"scene_purpose":"hook","image_prompt":"img one","motion_prompt":"move one","voice":"Xin chào"}]}
        """;
        var scenes = RVideoScenePipelineService.BuildScenes(prompt, 14, 8);

        Assert.Equal(new[] { 1, 2 }, scenes.Select(x => x.SceneIndex));
        Assert.Equal(new[] { 8, 6 }, scenes.Select(x => x.DurationSeconds));
        Assert.All(scenes, x => Assert.Equal(VideoSceneStatuses.Draft, x.Status));
        var first = ScenePromptMetadata.FromScene(scenes[0]);
        Assert.Equal("move one", first.MotionPrompt);
        Assert.Equal("Xin chào", first.Voice);
    }

    [Fact]
    public void BuildScenesReturnsNothingForAPromptWithoutScenes()
        => Assert.Empty(RVideoScenePipelineService.BuildScenes("""{"aspect_ratio":"9:16"}""", 16, 8));
}
