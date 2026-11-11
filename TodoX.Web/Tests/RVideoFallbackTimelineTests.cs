using TodoX.Web.Models;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoFallbackTimelineTests
{
    private static VideoProjectEventDto E(long id, string type, string json, int minute)
        => new() { Id = id, EventType = type, DataJson = json, CreatedAt = new DateTime(2026, 11, 11, 13, minute, 0, DateTimeKind.Utc) };

    [Fact]
    public void Latest_attempt_of_the_scene_reads_as_failed_switched_exhausted()
    {
        var events = new[]
        {
            // Older attempt of the same scene: ignored.
            E(1, "RVIDEO_VIDEO_FALLBACK_FAILED", """{"sceneId":42,"renderJobId":"old","model":"grok_video_heavy","mode":"normal","failureClassification":"TRANSIENT_PROVIDER_FAILURE"}""", 1),
            // Other scene: ignored.
            E(2, "RVIDEO_VIDEO_FALLBACK_FAILED", """{"sceneId":7,"renderJobId":"j2","model":"veo_omni","mode":"flash"}""", 2),
            E(3, "RVIDEO_VIDEO_FALLBACK_FAILED", """{"sceneId":42,"renderJobId":"j1","model":"veo_omni","mode":"flash","failureClassification":"INVALID_INPUT","providerErrorCode":"CONTENT","errorMessage":"prompt rejected"}""", 3),
            E(4, "RVIDEO_VIDEO_FALLBACK_STARTED", """{"sceneId":42,"renderJobId":"j1","fromModel":"veo_omni","fromMode":"flash","toModel":"veo_3_1","toMode":"fast","failureClassification":"INVALID_INPUT"}""", 4),
            E(5, "RVIDEO_VIDEO_FALLBACK_EXHAUSTED", """{"sceneId":42,"renderJobId":"j1","model":"grok_video_heavy","mode":"normal","failureClassification":"MODEL_PROVIDER_FAILURE"}""", 5),
            E(6, "SCENE_AUDIO_QUEUED", """{"sceneId":42}""", 6)
        };
        var steps = RVideoFallbackTimeline.ForScene(events, 42);
        Assert.Equal(["failed", "switched", "exhausted"], steps.Select(s => s.Kind));
        Assert.Equal("Veo 3.1 Omni", steps[0].Model);
        Assert.Contains("từ chối", steps[0].Reason);
        Assert.Equal("prompt rejected", steps[0].ErrorDetail);
        Assert.Equal("Veo 3.1 Fast", steps[1].Model);
        Assert.Equal("Veo 3.1 Omni", steps[1].FromModel);
        Assert.Equal("Grok Video", steps[2].Model);
        Assert.Empty(RVideoFallbackTimeline.ForScene(events, 99));
    }

    [Fact]
    public void Dialogue_longer_than_the_scene_is_flagged()
    {
        // Scene 2 of job 6c0954eb: 33 words.
        const string text = "chưa hiểu thấu bản chất đàn ông đã dám trao thân gửi phận, và chưa biết nuôi dạy con thế nào đã dám sinh con để cái. Cuộc đời họ là một ván cược may rủi,";
        var atRate1 = RVideoVoiceTiming.Estimate(text, 1.0m, 8);
        Assert.False(atRate1.Fits);
        Assert.True(atRate1.NeededSeconds > 8 && atRate1.NeededSeconds <= 15);
        Assert.True(RVideoVoiceTiming.Estimate(text, 2.0m, 8).Fits);
        Assert.True(RVideoVoiceTiming.Estimate("", 1.0m, 4).Fits);
    }
}
