using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class VbeeSceneRuntimeTests
{
    [Fact]
    public void SampleRateRetry_IsOneShot_AndPersistsMarker()
    {
        var providerResponse = new JsonObject { ["error"] = "sample rate unsupported" };

        var first = SceneAudioRenderHandler.TryResolveSampleRateRetry(providerResponse, "1013", "sample rate", 24000, false, out var firstFallback);
        var retryJson = SceneAudioRenderHandler.BuildSampleRateRetryConfigJson("""{"stage":"audio"}""", 24000, firstFallback, "req-1");
        var applied = SceneAudioRenderHandler.HasSampleRateRetryApplied(retryJson);
        var second = SceneAudioRenderHandler.TryResolveSampleRateRetry(providerResponse, "1013", "sample rate", 24000, applied, out var secondFallback);

        Assert.True(first);
        Assert.Equal(0, firstFallback);
        Assert.True(applied);
        Assert.False(second);
        Assert.Equal(0, secondFallback);
    }

    [Fact]
    public void MuxCompletion_UsesFinalPath_AndVoiceLinkage()
    {
        var sceneVideo = new SceneVideoVersionDto
        {
            PosterUrl = "https://cdn.example.com/poster.jpg",
            DurationSeconds = 12,
            ProviderCode = "vbee",
            ModelName = "voice-01",
            ProviderCapabilityId = 9,
            ProviderTaskId = "task-123",
            BillingLogicalRequestId = "bill-1",
            EstimatedUsd = 1.23m,
            ActualUsd = 1.11m,
            ChargedPoints = 10,
            RefundedPoints = 0,
            CostSource = "configured_tariff",
            AspectRatio = "16:9",
            ResultMediaId = Guid.NewGuid()
        };

        var request = SceneAudioMuxHandler.BuildCompletionRequest(sceneVideo, Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "https://cdn.example.com/final.mp4", @"D:\tmp\final.mp4", 14);

        Assert.Equal("https://cdn.example.com/final.mp4", request.VideoUrl);
        Assert.Equal(@"D:\tmp\final.mp4", request.VideoPath);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), request.VoiceAudioVersionId);
        Assert.Equal(12, request.DurationSeconds);
    }

    [Fact]
    public void MuxCompletion_UsesMuxedMediaIdInsteadOfRawMediaId()
    {
        var rawMediaId = Guid.NewGuid();
        var muxedMediaId = Guid.NewGuid();
        var sceneVideo = new SceneVideoVersionDto { ResultMediaId = rawMediaId };

        var request = SceneAudioMuxHandler.BuildCompletionRequest(
            sceneVideo,
            Guid.NewGuid(),
            "/uploads/video-render/final.mp4",
            @"D:\uploads\final.mp4",
            8,
            muxedMediaId);

        Assert.Equal(muxedMediaId, request.ResultMediaId);
        Assert.NotEqual(rawMediaId, request.ResultMediaId);
    }

    [Fact]
    public void Finalizer_SkipsWhenFinalMuxAlreadyExists()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var tempFile = Path.Combine(tempRoot, "project-1", "final-scenes", "01", "final.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);
        File.WriteAllText(tempFile, "muxed");
        try
        {
            var video = new SceneVideoVersionDto
            {
                VoiceAudioVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                PublicUrl = "https://cdn.example.com/final.mp4",
                SourceFilePath = tempFile
            };
            var audio = new SceneAudioVersionDto
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            };

            Assert.True(RVideoSceneMediaFinalizerService.ShouldSkipMux(video, audio, AppContext.BaseDirectory, string.Empty));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Finalizer_DoesNotSkipRawVideoStampedWithAudio()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var audioId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var video = new SceneVideoVersionDto
            {
                VoiceAudioVersionId = audioId,
                PublicUrl = "https://cdn.example.com/raw.mp4",
                SourceFilePath = tempFile
            };
            var audio = new SceneAudioVersionDto { Id = audioId };

            Assert.False(RVideoSceneMediaFinalizerService.ShouldSkipMux(video, audio, AppContext.BaseDirectory, string.Empty));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MuxCompletion_RejectsCrossSceneAudio()
    {
        var sceneVideo = new SceneVideoVersionDto { ProjectId = 11, SceneId = 101 };
        var sceneAudio = new SceneAudioVersionDto { ProjectId = 11, SceneId = 202 };

        var ex = Assert.Throws<InvalidOperationException>(() => SceneAudioMuxHandler.ValidateSceneOwnership(101, sceneVideo, sceneAudio));

        Assert.Equal("SCENE_AUDIO_MUX_SCENE_ID_MISMATCH", ex.Message);
    }

    [Fact]
    public void CallbackAuthorization_UsesHeaderOrQuerySecret()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-VBEE-CALLBACK-SECRET"] = "secret-1";

        Assert.Equal(VbeeCallbackAuthorizationStatus.Authorized, SceneAudioEndpoints.GetCallbackAuthorizationStatus(context.Request, "secret-1"));

        context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?secret=secret-1");

        Assert.Equal(VbeeCallbackAuthorizationStatus.Authorized, SceneAudioEndpoints.GetCallbackAuthorizationStatus(context.Request, "secret-1"));
    }
}
