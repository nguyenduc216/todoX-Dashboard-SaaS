using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoSceneLinkAndSplitTests
{
    private static VideoProjectSceneDto Scene(long id, int index, string? linkMode = null, Guid? source = null, int duration = 8)
    {
        var metadata = new ScenePromptMetadata { ImagePrompt = "img", MotionPrompt = "motion" };
        if (linkMode is not null) metadata.Extra[RVideoSceneLink.ModeKey] = linkMode;
        if (source is Guid s) metadata.Extra[RVideoSceneLink.SourceKey] = s.ToString();
        return new VideoProjectSceneDto { Id = id, SceneIndex = index, DurationSeconds = duration, ScenePrompt = metadata.Serialize(), ImagePrompt = "img", VideoPrompt = "motion" };
    }

    private static SceneVideoVersionDto Video(Guid id, string status = "completed") => new() { Id = id, Status = status };

    [Fact]
    public void FirstSceneIsNeverLinkedAndLaterScenesFollowTheirMode()
    {
        var scenes = new[] { Scene(1, 1, RVideoSceneLinkModes.Regenerate), Scene(2, 2, RVideoSceneLinkModes.Frame), Scene(3, 3) };
        Assert.False(RVideoSceneLink.IsLinked(scenes[0], scenes));
        Assert.True(RVideoSceneLink.IsLinked(scenes[1], scenes));
        Assert.False(RVideoSceneLink.IsLinked(scenes[2], scenes));
        Assert.Equal(1, RVideoSceneLink.Previous(scenes[1], scenes)!.Id);
        Assert.Equal(RVideoSceneLinkModes.Frame, RVideoSceneLink.GetMode(scenes[1]));
        Assert.Null(RVideoSceneLinkModes.Normalize("bogus"));
    }

    [Fact]
    public void LinkedSceneWaitsForThePreviousCompletedVideoItWasBuiltFrom()
    {
        var previousVideoId = Guid.NewGuid();
        var linked = Scene(2, 2, RVideoSceneLinkModes.Regenerate, previousVideoId);
        Assert.True(RVideoSceneLink.IsLinkCurrent(linked, Video(previousVideoId)));
        Assert.False(RVideoSceneLink.IsLinkCurrent(linked, Video(previousVideoId, "processing")));
        Assert.False(RVideoSceneLink.IsLinkCurrent(linked, Video(Guid.NewGuid())));   // previous scene re-rendered
        Assert.False(RVideoSceneLink.IsLinkCurrent(linked, null));
        Assert.False(RVideoSceneLink.IsLinkCurrent(Scene(3, 3, RVideoSceneLinkModes.Frame), Video(previousVideoId)));
    }

    [Fact]
    public void NewLinkedProjectMarksEveryLaterSceneForRegenerateLinking()
    {
        var scenes = new List<VideoProjectSceneDto> { Scene(1, 1), Scene(2, 2), Scene(3, 3, RVideoSceneLinkModes.Frame) };
        RVideoSceneLinkService.ApplyLinkedToNewScenes(scenes);
        Assert.Null(RVideoSceneLink.GetMode(scenes[0]));
        Assert.Equal(RVideoSceneLinkModes.Regenerate, RVideoSceneLink.GetMode(scenes[1]));
        Assert.Equal(RVideoSceneLinkModes.Frame, RVideoSceneLink.GetMode(scenes[2])); // explicit choice kept
    }

    [Theory]
    [InlineData(13, 7, 6)]
    [InlineData(12, 6, 6)]
    [InlineData(8, 4, 4)]
    public void SplitHalvesDurationWithTheOddSecondFirst(int duration, int first, int second)
    {
        Assert.True(RVideoSceneSplit.CanSplit(duration));
        Assert.Equal((first, second), RVideoSceneSplit.SplitDuration(duration));
    }

    [Fact]
    public void ScenesShorterThanTwoMinimumPartsCannotBeSplit()
    {
        Assert.False(RVideoSceneSplit.CanSplit(7));
    }

    [Fact]
    public void SplitVoiceBreaksAtTheMostBalancedSentenceBoundary()
    {
        var (first, second) = RVideoSceneSplit.SplitVoice("Xin chào cả nhà. Hôm nay có gì mới? Sản phẩm rất đẹp! Mua ngay nhé.");
        Assert.Equal("Xin chào cả nhà. Hôm nay có gì mới?", first);
        Assert.Equal("Sản phẩm rất đẹp! Mua ngay nhé.", second);

        var (wordsFirst, wordsSecond) = RVideoSceneSplit.SplitVoice("một hai ba bốn");
        Assert.Equal("một hai", wordsFirst);
        Assert.Equal("ba bốn", wordsSecond);
        Assert.Equal((null, null), RVideoSceneSplit.SplitVoice(null));
    }

    [Fact]
    public void SplitPromptsMarkBothPartsAsAContinuation()
    {
        Assert.Contains("Part 1 of 2", RVideoSceneSplit.PartPrompt("A woman walks to the door", 1));
        Assert.StartsWith("A woman walks to the door", RVideoSceneSplit.PartPrompt("A woman walks to the door", 2));
        Assert.Contains("continue seamlessly", RVideoSceneSplit.PartPrompt("A woman walks to the door", 2));
    }
}
