using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoRepeatableEventsTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Lifecycle_state_events_are_deduplicated_by_content_not_by_pass()
    {
        Assert.True(RVideoRepeatableEvents.IsRepeatable("SCENE_AUDIO_AUTO_ENQUEUE_SKIPPED"));
        Assert.True(RVideoRepeatableEvents.IsRepeatable("SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED"));
        Assert.True(RVideoRepeatableEvents.IsRepeatable("PROJECT_FINAL_MERGE_NOT_READY"));
        // Real progress is never deduplicated.
        Assert.False(RVideoRepeatableEvents.IsRepeatable("PROJECT_MERGED"));
        Assert.False(RVideoRepeatableEvents.IsRepeatable("RVIDEO_VIDEO_FAILED"));

        var a = RVideoRepeatableEvents.Signature("SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED", """{"sceneId":1,"activeJobId":"x","reason":"active"}""");
        var b = RVideoRepeatableEvents.Signature("SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED", """{"sceneId":1,"activeJobId":"y","reason":"active"}""");
        var c = RVideoRepeatableEvents.Signature("SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED", """{"sceneId":2,"activeJobId":"y","reason":"active"}""");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Contains("\"_sig\":\"" + a + "\"", RVideoRepeatableEvents.WithSignature("""{"sceneId":1}""", a));

        var repository = File.ReadAllText(Path.Combine(Root, "TodoX.Web", "Services", "VideoRender", "VideoRenderRepository.cs"));
        Assert.Contains("if (RVideoRepeatableEvents.IsRepeatable(eventType))", repository);
    }

    [Fact]
    public void A_project_without_final_versions_is_not_re_merged_for_the_same_selection()
    {
        var jobs = File.ReadAllText(Path.Combine(Root, "TodoX.Web", "Services", "Render", "RenderJobService.cs"));
        Assert.Contains("NOT EXISTS (SELECT 1 FROM video_render.final_video_versions f", jobs);
        Assert.Contains("ORDER BY j2.created_at DESC LIMIT 1", jobs);
    }

    [Fact]
    public void Failing_scene_work_backs_off_and_stops_until_settings_change()
    {
        RVideoLifecycleBackoff.Reset();
        var settings = new DateTime(2026, 11, 11, 6, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 11, 11, 7, 0, 0, DateTimeKind.Utc);
        Assert.True(RVideoLifecycleBackoff.ShouldAttempt(1, settings, now));
        var first = RVideoLifecycleBackoff.Failed(1, settings, now);
        Assert.Equal(TimeSpan.FromMinutes(1), first.Delay);
        Assert.False(RVideoLifecycleBackoff.ShouldAttempt(1, settings, now.AddSeconds(30)));
        Assert.True(RVideoLifecycleBackoff.ShouldAttempt(1, settings, now.AddMinutes(1)));
        RVideoLifecycleBackoff.Outcome last = first;
        for (var i = 2; i <= RVideoLifecycleBackoff.MaxFailures; i++) last = RVideoLifecycleBackoff.Failed(1, settings, now);
        Assert.True(last.Halted);
        Assert.False(RVideoLifecycleBackoff.ShouldAttempt(1, settings, now.AddDays(3)));
        // The user changed the job: try again.
        Assert.True(RVideoLifecycleBackoff.ShouldAttempt(1, settings.AddMinutes(5), now.AddDays(3)));
        RVideoLifecycleBackoff.Reset();
    }

    [Fact]
    public void Tts_rate_is_brought_into_the_voice_range()
    {
        var voice = new TodoX.Web.Models.AiStudioVoiceDto { MinRate = 0.8m, MaxRate = 1.9m };
        Assert.Equal(1.9m, RVideoSceneAudioAutoChainService.ClampTtsRate(voice, 2.0m));
        Assert.Equal(0.8m, RVideoSceneAudioAutoChainService.ClampTtsRate(voice, 0.5m));
        Assert.Equal(1.2m, RVideoSceneAudioAutoChainService.ClampTtsRate(voice, 1.2m));
        var worker = File.ReadAllText(Path.Combine(Root, "TodoX.Web", "Services", "VideoRender", "RVideoLifecycleWorker.cs"));
        Assert.Contains(".Where(x => !x.HasVideo && !x.VideoAttemptActive && !x.VideoFailedTerminal)", worker);
        Assert.Contains("RVideoLifecycleBackoff.ShouldAttempt(sceneId", worker);
    }

    [Fact]
    public void Retention_keeps_events_the_app_reads_and_money_lines()
    {
        Assert.Contains("RVIDEO_PARENT_BILLED", RVideoEventRetentionWorker.KeepEventTypes);
        var pattern = new System.Text.RegularExpressions.Regex(RVideoEventRetentionWorker.KeepPattern);
        Assert.Matches(pattern, "RVIDEO_VIDEO_BILLING_RESERVED");
        Assert.Matches(pattern, "RVIDEO_AUDIO_BILLING_ANOMALY");
        Assert.DoesNotMatch(pattern, "SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED");
        Assert.Contains("p.status = ANY(@statuses)", RVideoEventRetentionWorker.DeleteBatchSql);
        Assert.Contains("LIMIT @batch", RVideoEventRetentionWorker.DeleteBatchSql);
        var preview = File.ReadAllText(Path.Combine(Root, "TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Preview.razor"));
        Assert.Contains("Error = VideoVersionError(v)", preview);
    }
}
