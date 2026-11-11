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
}
