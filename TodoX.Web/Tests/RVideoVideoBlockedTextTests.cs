using TodoX.Web.Components.Pages.RVideo;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoVideoBlockedTextTests
{
    [Fact]
    public void Blocked_scene_video_says_why()
    {
        Assert.Contains("đang có job video chạy", RVideoUserText.VideoBlocked(true, "SceneJobInFlightOrManualReview", "processing"));
        Assert.Contains("chưa được xác minh", RVideoUserText.VideoBlocked(false, "ExistingIntentUnresolved", null));
        Assert.Contains("chưa được xác minh", RVideoUserText.VideoBlocked(false, "pending_reconciliation", null));
        Assert.Contains("chờ TodoX xác minh", RVideoUserText.VideoBlocked(false, "SceneVersionInFlightOrReconciliation", null));
        Assert.DoesNotContain("Chưa thể thử lại", RVideoUserText.VideoBlocked(false, "None", null));
    }

    [Fact]
    public void Preview_no_longer_shows_the_bare_retry_message_for_video()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var preview = File.ReadAllText(Path.Combine(root, "TodoX.Web", "Components", "Pages", "RVideo", "RVideoV2Preview.razor"));
        Assert.Contains("RVideoUserText.VideoBlocked(currentState.HasActiveJob", preview);
        Assert.Contains("RVIDEO_V2_VIDEO_RENDER_BLOCKED", preview);
    }

    [Fact]
    public void Start_confirmation_only_mentions_what_the_job_will_do()
    {
        var plain = RVideoUserText.AutoRunConfirm(3, 0, false, false);
        Assert.DoesNotContain("nối tiếp", plain);
        Assert.DoesNotContain("tạo ảnh cho", plain);
        Assert.Contains("3 scene", plain);
        var full = RVideoUserText.AutoRunConfirm(4, 2, false, true);
        Assert.Contains("tạo ảnh cho 2 scene chưa có ảnh", full);
        Assert.Contains("nối tiếp", full);
        Assert.Contains("ảnh tham chiếu chung", RVideoUserText.AutoRunConfirm(2, 2, true, false));
    }
}
