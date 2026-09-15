using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoVoiceRuntimeTests
{
    [Fact]
    public void NativeVoicePrompt_PreservesVisualPromptAndAddsSpeechContract()
    {
        var prompt = RVideoRules.ComposeNativeVoicePrompt(
            "A worker walks through a construction site.",
            "Xin chao moi nguoi",
            "Warm, confident delivery.");

        Assert.Contains("A worker walks through a construction site.", prompt);
        Assert.Contains("[NATIVE SPEECH]", prompt);
        Assert.Contains("Xin chao moi nguoi", prompt);
        Assert.Contains("[VOICE / DELIVERY]", prompt);
        Assert.Contains("[LIP SYNC]", prompt);
    }

    [Fact]
    public void NativeAndNoneScenesAreFinalReadyWithoutExternalAudio()
    {
        var video = new SceneVideoVersionDto
        {
            Id = Guid.NewGuid(),
            Status = "completed"
        };
        var scene = new VideoProjectSceneDto
        {
            VoiceText = "No external audio should be required."
        };

        Assert.True(RVideoRules.IsSceneFinalReady(
            scene,
            new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.Native },
            video,
            null));
        Assert.True(RVideoRules.IsSceneFinalReady(
            scene,
            new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.None },
            video,
            null));
    }

    [Fact]
    public void LibrarySceneIsNotFinalReadyUntilMuxLinksSelectedAudio()
    {
        var audio = new SceneAudioVersionDto
        {
            Id = Guid.NewGuid(),
            Status = "completed"
        };
        var scene = new VideoProjectSceneDto
        {
            SelectedAudioVersionId = audio.Id,
            VoiceText = "Per-scene narration."
        };
        var video = new SceneVideoVersionDto
        {
            Id = Guid.NewGuid(),
            Status = "completed"
        };

        Assert.False(RVideoRules.IsSceneFinalReady(
            scene,
            new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.Library },
            video,
            audio));

        video.VoiceAudioVersionId = audio.Id;
        Assert.True(RVideoRules.IsSceneFinalReady(
            scene,
            new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.Library },
            video,
            audio));
    }

    [Fact]
    public void SceneReadinessReportsAudioAndMuxGatesForLibraryVoice()
    {
        var audio = new SceneAudioVersionDto
        {
            Id = Guid.NewGuid(),
            Status = "completed"
        };
        var video = new SceneVideoVersionDto
        {
            Id = Guid.NewGuid(),
            Status = "completed"
        };
        var scene = new VideoProjectSceneDto
        {
            SceneIndex = 12,
            SelectedAudioVersionId = audio.Id,
            VoiceText = "Per-scene narration."
        };
        var readiness = RVideoRules.GetSceneReadiness(
            scene,
            new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.Library },
            video,
            audio);

        Assert.True(readiness.VideoReady);
        Assert.True(readiness.AudioRequired);
        Assert.True(readiness.AudioReady);
        Assert.False(readiness.MuxReady);
        Assert.Equal("scene video/audio mux linkage is missing", readiness.Reason);
    }

    [Fact]
    public void HydrationRulesDeriveOnlyCanonicalSceneVoiceAndPreserveUserText()
    {
        var scene = new VideoProjectSceneDto
        {
            VoiceText = "User-edited dialogue",
            ScenePrompt = new ScenePromptMetadata
            {
                Voice = "Canonical dialogue",
                VoiceInstruction = "Warm delivery"
            }.Serialize()
        };

        Assert.Equal("User-edited dialogue", RVideoRules.ResolveSceneVoiceText(scene));
        Assert.Equal("Warm delivery", RVideoRules.ResolveSceneVoiceInstruction(scene));
    }

    [Fact]
    public void NoCanonicalSceneVoiceDoesNotBecomeExternalAudioWork()
    {
        var scene = new VideoProjectSceneDto
        {
            Title = "Scene title",
            ScenePrompt = new ScenePromptMetadata { ScenePurpose = "Visual only" }.Serialize()
        };

        Assert.False(RVideoRules.HasSceneVoice(scene));
        Assert.False(RVideoRules.RequiresExternalVoice(
            scene,
            new RVideoJobSettingsDto { VoiceMode = RVideoVoiceModes.Library }));
    }
}
