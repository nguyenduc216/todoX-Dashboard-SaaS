using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class TimelapseApprovalRegressionTests
{
    [Fact]
    public void ManualPartialImagesDoNotAllowApprovalEvenWhenOneClipIsReady()
    {
        var images = new[]
        {
            Image(100, TimelapseOperationStatuses.Completed, isOriginal: true),
            Image(70, TimelapseOperationStatuses.Completed),
            Image(35, TimelapseOperationStatuses.Rendering),
            Image(0, TimelapseOperationStatuses.Waiting)
        };
        var videos = new[]
        {
            Clip(1, 70, 100),
            Clip(2, 35, 70),
            Clip(3, 0, 35)
        };

        Assert.True(TimelapseVideoOrchestration.IsReady(videos[0], images));
        Assert.False(TimelapseWorkflowReadiness.CanConfirmVideoRender(images, videos, requiresVideoConfirmation: true, videoRenderConfirmed: false));
        Assert.Equal(1, CountReadyVideos(images, videos));
    }

    [Fact]
    public void ManualAllImagesCompletedAllowsApprovalOnlyWhenVideosExist()
    {
        var images = new[]
        {
            Image(0, TimelapseOperationStatuses.Completed, isOriginal: true),
            Image(35, TimelapseOperationStatuses.Completed),
            Image(70, TimelapseOperationStatuses.Completed),
            Image(100, TimelapseOperationStatuses.Completed, isOriginal: true)
        };
        var videos = new[]
        {
            Clip(1, 0, 35),
            Clip(2, 35, 70),
            Clip(3, 70, 100)
        };

        Assert.True(TimelapseWorkflowReadiness.CanConfirmVideoRender(images, videos, requiresVideoConfirmation: true, videoRenderConfirmed: false));
        Assert.False(TimelapseWorkflowReadiness.CanConfirmVideoRender(images, Array.Empty<TimelapseVideoClip>(), requiresVideoConfirmation: true, videoRenderConfirmed: false));
        Assert.Equal(3, CountReadyVideos(images, videos));
    }

    [Fact]
    public void ManualConfirmationRequiresAllTimelineImagesIncludingAnchors()
    {
        var images = new[]
        {
            Image(0, TimelapseOperationStatuses.Completed, isOriginal: true),
            Image(35, TimelapseOperationStatuses.Completed),
            Image(70, TimelapseOperationStatuses.Rendering),
            Image(100, TimelapseOperationStatuses.Completed, isOriginal: true)
        };
        var videos = new[]
        {
            Clip(1, 0, 35),
            Clip(2, 35, 70),
            Clip(3, 70, 100)
        };

        Assert.False(TimelapseWorkflowReadiness.HasAllImagesCompleted(images));
        Assert.False(TimelapseWorkflowReadiness.CanConfirmVideoRender(images, videos, requiresVideoConfirmation: true, videoRenderConfirmed: false));

        images[2].Status = TimelapseOperationStatuses.Completed;

        Assert.True(TimelapseWorkflowReadiness.HasAllImagesCompleted(images));
        Assert.True(TimelapseWorkflowReadiness.CanConfirmVideoRender(images, videos, requiresVideoConfirmation: true, videoRenderConfirmed: false));
    }

    [Fact]
    public void AutoModeStartsReadyClipImmediatelyWhileOtherClipsWait()
    {
        var images = new[]
        {
            Image(100, TimelapseOperationStatuses.Completed, isOriginal: true),
            Image(70, TimelapseOperationStatuses.Completed),
            Image(35, TimelapseOperationStatuses.Rendering),
            Image(0, TimelapseOperationStatuses.Waiting)
        };
        var videos = new[]
        {
            Clip(1, 70, 100),
            Clip(2, 35, 70),
            Clip(3, 0, 35)
        };

        Assert.True(TimelapseVideoOrchestration.IsReady(videos[0], images));
        Assert.False(TimelapseVideoOrchestration.IsReady(videos[1], images));
        Assert.False(TimelapseVideoOrchestration.IsReady(videos[2], images));
    }

    [Fact]
    public void ConfirmVideoRenderPathStillChecksForFullImageApprovalBeforeStartingVideos()
    {
        var workflow = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");

        Assert.Contains("if (!state.CanConfirmVideoRender)", workflow);
        Assert.Contains("Chưa hoàn thành toàn bộ ảnh Timelapse nên chưa thể duyệt để tạo video.", workflow);
        Assert.Contains("CanConfirmVideoRender = TimelapseWorkflowReadiness.CanConfirmVideoRender", workflow);
    }

    [Fact]
    public void TimelapseDetailPageUsesManualApprovalLabelAndWaitingMessage()
    {
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");

        Assert.Contains("DUYỆT ẢNH & TẠO VIDEO", page);
        Assert.Contains("Đang chờ hoàn thành toàn bộ ảnh trước khi duyệt.", page);
        Assert.Contains("Toàn bộ ảnh đã hoàn thành. Hãy kiểm tra các ảnh trước khi bắt đầu tạo video.", page);
    }

    private static TimelapseStageImage Image(int progressPercent, string status, bool isOriginal = false)
        => new()
        {
            ProgressPercent = progressPercent,
            Status = status,
            IsOriginal = isOriginal
        };

    private static TimelapseVideoClip Clip(int clipIndex, int startProgressPercent, int endProgressPercent)
        => new()
        {
            ClipIndex = clipIndex,
            StartProgressPercent = startProgressPercent,
            EndProgressPercent = endProgressPercent,
            Status = TimelapseOperationStatuses.Waiting
        };

    private static int CountReadyVideos(IReadOnlyList<TimelapseStageImage> images, IReadOnlyList<TimelapseVideoClip> videos)
        => videos.Count(clip => clip.Status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Invalidated
            && TimelapseVideoOrchestration.IsReady(clip, images));

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
