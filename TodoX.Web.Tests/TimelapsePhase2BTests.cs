using TodoX.Web.Models.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public class TimelapsePhase2BTests
{
    [Theory]
    [InlineData(3, new[] { 0, 35, 70, 100 }, new[] { 70, 35, 0 })]
    [InlineData(4, new[] { 0, 25, 50, 75, 100 }, new[] { 75, 50, 25, 0 })]
    [InlineData(5, new[] { 0, 20, 40, 60, 80, 100 }, new[] { 80, 60, 40, 20, 0 })]
    [InlineData(6, new[] { 0, 25, 40, 55, 70, 85, 100 }, new[] { 85, 70, 55, 40, 25, 0 })]
    public void StageGraph_BuildsImagesVideosAndReverseGenerationOrder(int sceneCount, int[] images, int[] generatedOrder)
    {
        var graph = TimelapseStageGraphBuilder.Build(sceneCount);

        Assert.Equal(images, graph.ImageProgressions);
        Assert.Equal(sceneCount, graph.VideoClips.Count);
        Assert.Equal(generatedOrder, graph.GeneratedImageOrder);
        Assert.DoesNotContain(100, graph.GeneratedImageOrder);
        Assert.Equal(images.Zip(images.Skip(1), (start, end) => (start, end)), graph.VideoClips.Select(x => (x.StartProgressPercent, x.EndProgressPercent)));
    }

    [Fact]
    public void Invalidation_Rerender35InvalidatesEarlierImageAndRelatedVideos()
    {
        var plan = TimelapseStageGraphBuilder.PlanImageRerender(3, 35);

        Assert.Equal(new[] { 0 }, plan.ImageProgressions);
        Assert.Equal(new[] { 1, 2 }, plan.VideoClips.Select(x => x.ClipIndex));
        Assert.True(plan.FinalOutput);
    }

    [Fact]
    public void Invalidation_Rerender70InvalidatesEarlierImagesAndAllVideos()
    {
        var plan = TimelapseStageGraphBuilder.PlanImageRerender(3, 70);

        Assert.Equal(new[] { 0, 35 }, plan.ImageProgressions);
        Assert.Equal(new[] { 1, 2, 3 }, plan.VideoClips.Select(x => x.ClipIndex));
        Assert.True(plan.FinalOutput);
    }

    [Fact]
    public void Invalidation_RerenderVideoInvalidatesFinalOnly()
    {
        var plan = TimelapseStageGraphBuilder.PlanVideoRerender(3, 2);

        Assert.Empty(plan.ImageProgressions);
        Assert.Equal(new[] { 2 }, plan.VideoClips.Select(x => x.ClipIndex));
        Assert.True(plan.FinalOutput);
    }

    [Fact]
    public void Invalidation_ReplaceOriginalInvalidatesEveryGeneratedImageVideoAndFinal()
    {
        var plan = TimelapseStageGraphBuilder.PlanOriginalReplacement(3);

        Assert.Equal(new[] { 0, 35, 70 }, plan.ImageProgressions);
        Assert.Equal(new[] { 1, 2, 3 }, plan.VideoClips.Select(x => x.ClipIndex));
        Assert.True(plan.FinalOutput);
    }

    [Fact]
    public void WorkflowState_UsesChildOperationsForEditAndStartRules()
    {
        var activeImage = new TimelapseWorkflowState
        {
            ParentStatus = TimelapseParentStatuses.Draft,
            HasActiveOperations = true,
            CanEditRequest = false,
            CanStartRender = false
        };
        var stoppedFailure = new TimelapseWorkflowState
        {
            ParentStatus = TimelapseParentStatuses.Failed,
            HasActiveOperations = false,
            CanEditRequest = true,
            CanStartRender = true
        };

        Assert.False(activeImage.CanEditRequest);
        Assert.False(activeImage.CanStartRender);
        Assert.True(stoppedFailure.CanEditRequest);
        Assert.True(stoppedFailure.CanStartRender);
    }

    [Fact]
    public void ImageProgress_ExcludesOriginalAndCalculatesCompletedGeneratedStages()
    {
        var images = new[]
        {
            Stage(0, TimelapseOperationStatuses.Completed),
            Stage(25, TimelapseOperationStatuses.Completed),
            Stage(50, TimelapseOperationStatuses.Completed),
            Stage(75, TimelapseOperationStatuses.Rendering),
            Stage(100, TimelapseOperationStatuses.Completed, isOriginal: true)
        };

        var progress = TimelapseProgress.CalculateImageProgress(images);

        Assert.Equal(3, progress.Completed);
        Assert.Equal(4, progress.Total);
        Assert.Equal(75, progress.Percent);
    }

    [Fact]
    public void VideoReadiness_RequiresBothBoundaryImages()
    {
        var clip = new TimelapseVideoClip
        {
            StartProgressPercent = 75,
            EndProgressPercent = 100
        };
        var readyImages = new[]
        {
            Stage(75, TimelapseOperationStatuses.Completed),
            Stage(100, TimelapseOperationStatuses.Completed, isOriginal: true)
        };
        var missingStartImage = new[]
        {
            Stage(75, TimelapseOperationStatuses.Rendering),
            Stage(100, TimelapseOperationStatuses.Completed, isOriginal: true)
        };

        Assert.True(TimelapseVideoOrchestration.IsReady(clip, readyImages));
        Assert.False(TimelapseVideoOrchestration.IsReady(clip, missingStartImage));
    }

    [Fact]
    public void VideoReadiness_HonorsConfirmationGate()
    {
        var clip = new TimelapseVideoClip
        {
            StartProgressPercent = 75,
            EndProgressPercent = 100
        };
        var images = new[]
        {
            Stage(75, TimelapseOperationStatuses.Completed),
            Stage(100, TimelapseOperationStatuses.Completed, isOriginal: true)
        };

        Assert.False(TimelapseVideoOrchestration.IsReady(clip, images, requiresConfirmation: true));
        Assert.True(TimelapseVideoOrchestration.IsReady(clip, images, requiresConfirmation: true, videoRenderConfirmed: true));
    }

    [Fact]
    public void CompletedVideoPreview_RequiresCompletedStatusAndPublicUrl()
    {
        Assert.True(TimelapseVideoOrchestration.HasCompletedPreview(new TimelapseVideoClip
        {
            Status = TimelapseOperationStatuses.Completed,
            PublicUrl = "/media/timelapse/clip.mp4"
        }));
        Assert.False(TimelapseVideoOrchestration.HasCompletedPreview(new TimelapseVideoClip
        {
            Status = TimelapseOperationStatuses.Rendering,
            PublicUrl = "/media/timelapse/clip.mp4"
        }));
        Assert.False(TimelapseVideoOrchestration.HasCompletedPreview(new TimelapseVideoClip
        {
            Status = TimelapseOperationStatuses.Completed
        }));
    }

    [Theory]
    [InlineData(TimelapseParentStatuses.Draft, "Bản nháp")]
    [InlineData(TimelapseParentStatuses.GeneratingImages, "Đang tạo ảnh")]
    [InlineData(TimelapseParentStatuses.ImagesReady, "Ảnh đã sẵn sàng")]
    [InlineData(TimelapseParentStatuses.GeneratingVideos, "Đang tạo video")]
    [InlineData(TimelapseParentStatuses.VideosReady, "Video đã sẵn sàng")]
    [InlineData(TimelapseParentStatuses.Finalizing, "Đang ghép video")]
    [InlineData(TimelapseParentStatuses.Completed, "Hoàn thành")]
    [InlineData(TimelapseParentStatuses.Failed, "Thất bại")]
    [InlineData(TimelapseParentStatuses.Paused, "Tạm dừng")]
    public void ParentStatusText_IsCustomerFriendlyVietnamese(string status, string expected)
        => Assert.Equal(expected, TimelapseStatusText.Parent(status));

    [Fact]
    public void VideoCards_KeepMainPreviewSeparateFromCompactInputThumbnails()
    {
        var detail = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");
        var videoCardStart = detail.IndexOf("video-stage-card", StringComparison.Ordinal);
        var previewIndex = detail.IndexOf("PreviewClass(\"video-preview\"", videoCardStart, StringComparison.Ordinal);
        var thumbnailsIndex = detail.IndexOf("class=\"clip-input-thumbnails\"", previewIndex, StringComparison.Ordinal);
        var footerIndex = detail.IndexOf("class=\"tl-video-card-footer\"", thumbnailsIndex, StringComparison.Ordinal);

        Assert.True(videoCardStart >= 0);
        Assert.True(previewIndex > videoCardStart);
        Assert.True(thumbnailsIndex > previewIndex);
        Assert.True(footerIndex > thumbnailsIndex);
        Assert.Contains("Ảnh đầu @clip.StartProgressPercent%", detail, StringComparison.Ordinal);
        Assert.Contains("Ảnh cuối @clip.EndProgressPercent%", detail, StringComparison.Ordinal);
        Assert.Contains("clip-input-image", detail, StringComparison.Ordinal);
        Assert.Contains("clip-input-placeholder", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"clip-thumbs\"", detail, StringComparison.Ordinal);

        var normalizedCss = css.ReplaceLineEndings("\n");
        Assert.Contains(".video-preview {\n    aspect-ratio: 16 / 9;", normalizedCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(72px, 88px) 20px minmax(72px, 88px);", css, StringComparison.Ordinal);
        Assert.Contains("width: min(100%, 208px);", css, StringComparison.Ordinal);
        Assert.Contains("object-fit: cover;", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 700px)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(72px, 80px) 18px minmax(72px, 80px);", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".clip-thumbs img", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapsePhase2B_SourceContracts_ArePresent()
    {
        var detail = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var detailCss = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var profiles = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProfileRepository.cs");
        var migration = ReadSource("database", "migrations", "20260813_timelapse_phase_2b_render_workflow.sql");
        var program = ReadSource("TodoX.Web", "Program.cs");

        Assert.Contains("YÊU CẦU", detail, StringComparison.Ordinal);
        Assert.Contains("SCENE", detail, StringComparison.Ordinal);
        Assert.Contains("KẾT QUẢ", detail, StringComparison.Ordinal);
        Assert.Contains("HÌNH ẢNH", detail, StringComparison.Ordinal);
        Assert.Contains("VIDEO", detail, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.Image", detail, StringComparison.Ordinal);
        Assert.Contains("Icons.Material.Filled.Movie", detail, StringComparison.Ordinal);
        Assert.Contains("Đang tạo ảnh...", detail, StringComparison.Ordinal);
        Assert.Contains("Đang tạo video...", detail, StringComparison.Ordinal);
        Assert.Contains("Đang tạo video", detail, StringComparison.Ordinal);
        Assert.Contains("Đang ghép video cuối cùng...", detail, StringComparison.Ordinal);
        Assert.Contains("Đang chờ hoàn thiện kết quả...", detail, StringComparison.Ordinal);
        Assert.Contains("TimelapseParentStatuses.Finalizing", detail, StringComparison.Ordinal);
        Assert.Contains("tl-loading-skeleton", detail, StringComparison.Ordinal);
        Assert.Contains("tl-loading-shimmer", detail, StringComparison.Ordinal);
        Assert.Contains("tl-pulse", detail, StringComparison.Ordinal);
        Assert.Contains("tl-flash-soft", detail, StringComparison.Ordinal);
        Assert.Contains("tl-status-dot", detail, StringComparison.Ordinal);
        Assert.Contains("tl-active-render", detail, StringComparison.Ordinal);
        Assert.Contains("tl-active-scanline", detail, StringComparison.Ordinal);
        Assert.Contains("IsRenderingOperation", detail, StringComparison.Ordinal);
        Assert.Contains("OperationStateClass", detail, StringComparison.Ordinal);
        Assert.Contains("is-waiting", detail, StringComparison.Ordinal);
        Assert.Contains("is-rendering", detail, StringComparison.Ordinal);
        Assert.Contains("image-stage-card", detail, StringComparison.Ordinal);
        Assert.Contains("video-stage-card", detail, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", detail, StringComparison.Ordinal);
        Assert.Contains("Hoàn thành video", detail, StringComparison.Ordinal);
        Assert.Contains("Tải video", detail, StringComparison.Ordinal);
        Assert.Contains("PeriodicTimer(TimeSpan.FromSeconds(4))", detail, StringComparison.Ordinal);
        Assert.Contains("LoadInitialAsync", detail, StringComparison.Ordinal);
        Assert.Contains("RefreshJobStateAsync", detail, StringComparison.Ordinal);
        Assert.Contains("_job = null;", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("await RefreshJobStateAsync();\n                _job = null;", detail, StringComparison.Ordinal);
        Assert.Contains("await RefreshJobStateAsync()", detail, StringComparison.Ordinal);
        Assert.Contains("@bind-ActivePanelIndex=\"_activeTabIndex\"", detail, StringComparison.Ordinal);
        Assert.Contains("_activeTabIndex = 1", detail, StringComparison.Ordinal);
        Assert.Contains("KeepPanelsAlive=\"true\"", detail, StringComparison.Ordinal);
        Assert.Contains("EnsureImageCards", detail, StringComparison.Ordinal);
        Assert.Contains("EnsureVideoCards", detail, StringComparison.Ordinal);
        Assert.Contains("TimelapseProgress.CalculateImageProgress(ImageCards)", detail, StringComparison.Ordinal);
        Assert.Contains("ImageProgress.Completed", detail, StringComparison.Ordinal);
        Assert.Contains("ImageProgress.Percent", detail, StringComparison.Ordinal);
        Assert.Contains("private int CompletedVideos => VideoCards.Count", detail, StringComparison.Ordinal);
        Assert.Contains("private int TotalVideos => VideoCards.Count", detail, StringComparison.Ordinal);
        Assert.Contains("Đang chờ render", detail, StringComparison.Ordinal);
        Assert.Contains("Đang chờ ảnh", detail, StringComparison.Ordinal);
        Assert.Contains("hoàn thành", detail, StringComparison.Ordinal);
        Assert.Contains("ImageFailureCount", detail, StringComparison.Ordinal);
        Assert.Contains("VideoFailureCount", detail, StringComparison.Ordinal);
        Assert.Contains("@foreach (var clip in VideoCards)", detail, StringComparison.Ordinal);
        Assert.Contains("TimelapseVideoOrchestration.HasCompletedPreview", detail, StringComparison.Ordinal);
        Assert.Contains("FormatClipStartedAt", detail, StringComparison.Ordinal);
        Assert.Contains("clip.CompletedAt", detail, StringComparison.Ordinal);
        Assert.Contains("Bắt đầu tạo video", detail, StringComparison.Ordinal);
        Assert.Contains("ConfirmVideoRenderAsync", detail, StringComparison.Ordinal);
        Assert.Contains("Hệ thống tiếp tục xử lý kể cả khi bạn rời trang.", detail, StringComparison.Ordinal);
        Assert.Contains("TimelapseSellPricing.CustomerQualityLabel", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Fast", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("Professional", detail, StringComparison.Ordinal);

        Assert.Contains(".tl-loading-shimmer", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-loading-skeleton", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-pulse", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-flash-soft", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-video-wave", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-active-render", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-image-scanline", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-video-scanline", detailCss, StringComparison.Ordinal);
        Assert.Contains("tl-active-border-pulse", detailCss, StringComparison.Ordinal);
        Assert.Contains("tl-active-sweep", detailCss, StringComparison.Ordinal);
        Assert.Contains("tl-preview-scan-down", detailCss, StringComparison.Ordinal);
        Assert.Contains(".tl-final-loading", detailCss, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", detailCss, StringComparison.Ordinal);

        Assert.Contains("pg_advisory_xact_lock", workflow, StringComparison.Ordinal);
        Assert.Contains("GetRenderProfileAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("StartOrResumeAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("RetryImageAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("RetryVideoAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("StartFinalizerAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("HasActiveOperations", workflow, StringComparison.Ordinal);
        Assert.Contains("CanEditRequest", workflow, StringComparison.Ordinal);
        Assert.Contains("CanStartRender", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_RENDER_STARTED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_IMAGE_RERENDER_REQUESTED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_VIDEO_RERENDER_REQUESTED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_VIDEO_RENDER_CONFIRMED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_FINALIZER_STARTED", workflow, StringComparison.Ordinal);
        Assert.Contains("start_img.status='COMPLETED'", workflow, StringComparison.Ordinal);
        Assert.Contains("end_img.status='COMPLETED'", workflow, StringComparison.Ordinal);
        Assert.Contains("requireVideoConfirmation", workflow, StringComparison.Ordinal);
        Assert.Contains("videoRenderConfirmed", workflow, StringComparison.Ordinal);
        Assert.Contains("input_json=jsonb_set", workflow, StringComparison.Ordinal);
        Assert.Contains("started_at AS StartedAt", workflow, StringComparison.Ordinal);
        Assert.Contains("completed_at AS CompletedAt", workflow, StringComparison.Ordinal);

        Assert.Contains("to_jsonb(p)::text AS ProfileJson", profiles, StringComparison.Ordinal);
        Assert.Contains("public.todox_timelapse_prompt_profiles", profiles, StringComparison.Ordinal);
        Assert.DoesNotContain("generic construction prompt", workflow, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("CREATE TABLE IF NOT EXISTS timelapse.timelapse_image_stages", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS timelapse.timelapse_image_stage_versions", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS timelapse.timelapse_video_clips", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS timelapse.timelapse_video_clip_versions", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE IF NOT EXISTS timelapse.timelapse_final_outputs", migration, StringComparison.Ordinal);
        Assert.Contains("provider_task_id", migration, StringComparison.Ordinal);
        Assert.Contains("prompt_snapshot_json", migration, StringComparison.Ordinal);
        Assert.Contains("GENERATING_IMAGES", migration, StringComparison.Ordinal);
        Assert.Contains("FINALIZING", migration, StringComparison.Ordinal);

        Assert.Contains("AddScoped<ITimelapseWorkflowService, TimelapseWorkflowService>", program, StringComparison.Ordinal);
    }

    private static TimelapseStageImage Stage(int progress, string status, bool isOriginal = false)
        => new()
        {
            ProgressPercent = progress,
            Status = status,
            IsOriginal = isOriginal
        };

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
