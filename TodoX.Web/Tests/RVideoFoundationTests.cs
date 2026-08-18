using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoFoundationTests
{
    [Fact]
    public void ImportAcceptsNarrationAliasesAndNormalizesIndexes()
    {
        var service = new RVideoSceneJsonService();
        var scenes = service.Import("""
        {
          "video_title": "Demo",
          "scenes": [
            { "scene": 7, "duration_seconds": 4, "image_prompt": "one", "voice_over": "hello" },
            { "scene": 2, "duration_seconds": 10, "image_prompt": "two", "script": "world" }
          ]
        }
        """);

        Assert.Equal(new[] { 1, 2 }, scenes.Select(x => x.SceneIndex));
        Assert.Equal("hello", scenes[0].DialogueText);
        Assert.Equal("world", scenes[1].DialogueText);
        Assert.Equal(10, scenes[1].DurationSeconds);
    }

    [Fact]
    public void ExportOrdersScenesAndPreservesRuntimeFields()
    {
        var service = new RVideoSceneJsonService();
        var json = service.Export("Demo", new[]
        {
            new RVideoSceneEditorItem(2, "End", 6, "b", "move b", "bye", null, "bad", 1.1m),
            new RVideoSceneEditorItem(1, "Hook", 4, "a", "move a", "hi", null, null, null)
        });

        Assert.True(json.IndexOf("\"scene\": 1", StringComparison.Ordinal) < json.IndexOf("\"scene\": 2", StringComparison.Ordinal));
        Assert.Contains("\"dialogue_text\": \"hi\"", json);
        Assert.Contains("\"negative_prompt\": \"bad\"", json);
    }

    [Fact]
    public void AutoLifecycleMovesFromImagesToVideoAndThenFinalizer()
    {
        var imageReady = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.ImageReady, VideoSceneStatuses.ImageReady }, false);
        var videoReady = RVideoRules.Evaluate(RVideoExecutionModes.Auto,
            new[] { VideoSceneStatuses.VideoReady, VideoSceneStatuses.VideoReady }, false);

        Assert.True(imageReady.ShouldQueueVideo);
        Assert.False(imageReady.ShouldFinalize);
        Assert.True(videoReady.ShouldFinalize);
        Assert.Equal(RVideoStages.Result, videoReady.Stage);
    }

    [Fact]
    public void ManualLifecycleStopsAfterImageTerminal()
    {
        var decision = RVideoRules.Evaluate(RVideoExecutionModes.Manual,
            new[] { VideoSceneStatuses.ImageReady }, false);

        Assert.Equal(RVideoStages.Image, decision.Stage);
        Assert.False(decision.ShouldQueueVideo);
        Assert.False(decision.ShouldFinalize);
    }

    [Fact]
    public void SceneDurationAndLibraryVoiceAreValidated()
    {
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateScene(
            new RVideoSceneEditorItem(1, "Hook", 5, "prompt", null, null, null, null, null)));
        Assert.Throws<InvalidOperationException>(() => RVideoRules.ValidateSettings(new RVideoJobSettingsRequest
        {
            VoiceMode = RVideoVoiceModes.Library
        }));
    }
}
