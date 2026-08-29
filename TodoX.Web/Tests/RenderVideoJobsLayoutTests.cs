using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

public class RenderVideoJobsLayoutTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = File.Exists(Path.Combine(RepoRoot, "Components", "Pages", "RenderVideoJobs.razor"))
        ? RepoRoot
        : Path.Combine(RepoRoot, "TodoX.Web");
    private static readonly string RazorPath = Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor");
    private static readonly string PageCssPath = Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor.css");
    private static readonly string FrameCssPath = Path.Combine(WebRoot, "Components", "Shared", "RenderMediaFrame.razor.css");

    [Fact]
    public void PreviewTab_UsesSharedMediaFrameAndNoMiddleColumn()
    {
        var razor = File.ReadAllText(RazorPath);
        var previewTab = Between(razor, "<MudTabPanel Text=\"Xem trước\">", "<MudTabPanel Text=\"Kết quả\">");

        Assert.Contains("<RenderMediaFrame IsVideo=\"false\"", previewTab);
        Assert.Contains("<RenderMediaFrame IsVideo=\"true\"", previewTab);
        Assert.Equal(2, Regex.Matches(previewTab, "AspectRatio=\"@ProjectAspectRatio\"").Count);
        Assert.Equal(2, Regex.Matches(previewTab, "<RenderMediaFrame\\s+IsVideo=").Count);
        Assert.DoesNotContain("scene-details-column", previewTab);
        Assert.DoesNotContain("Đang tạo ảnh tĩnh qua AI provider", previewTab);
        Assert.Contains("Đang tạo ảnh", razor);
    }

    [Fact]
    public void SharedMediaFrame_MovesChildVisualCssOutOfParentStylesheet()
    {
        var pageCss = File.ReadAllText(PageCssPath);
        var frameCss = File.ReadAllText(FrameCssPath);

        Assert.Contains(".scene-media-square", frameCss);
        Assert.Contains(".scene-media-image", frameCss);
        Assert.Contains(".scene-image-frame", frameCss);
        Assert.Contains(".scene-video-ready-label", frameCss);
        Assert.Contains("aspect-ratio: var(--render-media-aspect-ratio, 1 / 1)", frameCss);
        Assert.DoesNotContain(".scene-media-square", pageCss);
        Assert.DoesNotContain(".scene-image-frame", pageCss);
        Assert.DoesNotContain(".scene-video-ready", pageCss);
        Assert.DoesNotContain(".scene-thumb-placeholder", pageCss);
    }

    [Fact]
    public void PreviewTab_RendersVideoFrameBeforeVideoActions()
    {
        var source = File.ReadAllText(RazorPath);
        var videoFrameIndex = source.IndexOf("<RenderMediaFrame IsVideo=\"true\"", StringComparison.Ordinal);
        var actionsIndex = source.IndexOf("<div class=\"scene-media-actions\">", videoFrameIndex, StringComparison.Ordinal);

        Assert.True(videoFrameIndex >= 0);
        Assert.True(actionsIndex >= 0);
        Assert.True(videoFrameIndex < actionsIndex);
    }

    [Fact]
    public void VideoActions_UseConditionalExternalVoiceIconAndAudioDialog()
    {
        var source = File.ReadAllText(RazorPath);

        Assert.Contains("RequiresExternalVoice(scene)", source);
        Assert.Contains("ResolveVoiceMediaState(scene)", source);
        Assert.Contains("OpenSceneVoicePlayerAsync(scene)", source);
        Assert.Contains("SceneAudioVersionDialog", source);
        Assert.Contains("Đang tạo giọng đọc", source);
        Assert.Contains("Nghe giọng đọc", source);
        Assert.Contains("Tạo giọng đọc thất bại", source);
    }

    [Fact]
    public void ResultTab_RendersManualFinalMergeAction()
    {
        var source = File.ReadAllText(RazorPath);

        Assert.Contains("Hoàn tất video", source);
        Assert.Contains("Đang hoàn tất...", source);
        Assert.Contains("IsFinalMergeActive", source);
    }

    private static string Between(string source, string startText, string endText)
    {
        var start = source.IndexOf(startText, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker '{startText}' was not found.");

        var end = source.IndexOf(endText, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker '{endText}' was not found after '{startText}'.");

        return source[start..end];
    }
}
