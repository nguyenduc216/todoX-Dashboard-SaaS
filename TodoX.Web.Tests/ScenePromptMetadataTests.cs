using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class ScenePromptMetadataTests
{
    [Fact]
    public void Parse_ReadsAllKnownFields()
    {
        var metadata = ScenePromptMetadata.Parse(
            "scene_purpose: Intro | image_prompt: A studio shot | motion_prompt: Slow dolly | voice: Xin chao | voice_instruction: Warm voice");

        Assert.Equal("Intro", metadata.ScenePurpose);
        Assert.Equal("A studio shot", metadata.ImagePrompt);
        Assert.Equal("Slow dolly", metadata.MotionPrompt);
        Assert.Equal("Xin chao", metadata.Voice);
        Assert.Equal("Warm voice", metadata.VoiceInstruction);
    }

    [Fact]
    public void SerializeThenParse_RetainsKnownAndUnknownFields()
    {
        var metadata = new ScenePromptMetadata
        {
            ScenePurpose = "Purpose",
            ImagePrompt = "Image prompt",
            MotionPrompt = "Motion prompt",
            Voice = "Voice",
            VoiceInstruction = "Instruction"
        };
        metadata.Extra["camera"] = "50mm";

        var parsed = ScenePromptMetadata.Parse(metadata.Serialize());

        Assert.Equal("Purpose", parsed.ScenePurpose);
        Assert.Equal("Image prompt", parsed.ImagePrompt);
        Assert.Equal("Motion prompt", parsed.MotionPrompt);
        Assert.Equal("Voice", parsed.Voice);
        Assert.Equal("Instruction", parsed.VoiceInstruction);
        Assert.Equal("50mm", parsed.Extra["camera"]);
    }

    [Fact]
    public void FromScene_UsesSceneColumnsAsCanonicalPrompts()
    {
        var scene = new VideoProjectSceneDto
        {
            ScenePrompt = "scene_purpose: Old | image_prompt: old image | motion_prompt: old motion | voice: Old voice | voice_instruction: Old instruction",
            ImagePrompt = "new image",
            VideoPrompt = "new motion"
        };

        var metadata = ScenePromptMetadata.FromScene(scene);

        Assert.Equal("new image", metadata.ImagePrompt);
        Assert.Equal("new motion", metadata.MotionPrompt);
        Assert.Equal("Old voice", metadata.Voice);
        Assert.Equal("Old instruction", metadata.VoiceInstruction);
    }

    [Fact]
    public void EditingVoice_DoesNotLoseImageMotionOrUnknownMetadata()
    {
        var metadata = ScenePromptMetadata.Parse(
            "scene_purpose: Intro | image_prompt: Image | motion_prompt: Motion | voice: Old voice | voice_instruction: Old instruction | lens: wide");

        metadata.Voice = "New voice";
        var serialized = metadata.Serialize();
        var parsed = ScenePromptMetadata.Parse(serialized);

        Assert.Equal("Image", parsed.ImagePrompt);
        Assert.Equal("Motion", parsed.MotionPrompt);
        Assert.Equal("New voice", parsed.Voice);
        Assert.Equal("Old instruction", parsed.VoiceInstruction);
        Assert.Equal("wide", parsed.Extra["lens"]);
        Assert.Equal(1, CountKey(serialized, "voice:"));
        Assert.Equal(1, CountKey(serialized, "voice_instruction:"));
    }

    [Fact]
    public void Parse_LegacyFreeText_FallsBackToScenePurpose()
    {
        var metadata = ScenePromptMetadata.Parse("A legacy scene prompt without keyed metadata");

        Assert.Equal("A legacy scene prompt without keyed metadata", metadata.ScenePurpose);
    }

    [Fact]
    public void Serialize_NormalizesNewLinesAndPipeSeparators()
    {
        var metadata = new ScenePromptMetadata
        {
            ImagePrompt = "Line one\r\nLine two | with separator",
            MotionPrompt = "Move"
        };

        var serialized = metadata.Serialize();

        Assert.DoesNotContain("\r", serialized);
        Assert.DoesNotContain("\n", serialized);
        Assert.Contains("Line one Line two / with separator", serialized);
    }

    private static int CountKey(string source, string key)
        => source.Split(key, StringSplitOptions.None).Length - 1;
}
