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

    [Fact]
    public void PreviewTab_UsesSharedMediaFrameAndNoMiddleColumn()
    {
        var razor = File.ReadAllText(RazorPath);
        var previewTab = Between(razor, "<MudTabPanel Text=\"Xem trước\">", "<MudTabPanel Text=\"Kết quả\">");

        Assert.Contains("<RenderMediaFrame IsVideo=\"false\"", previewTab);
        Assert.Contains("<RenderMediaFrame IsVideo=\"true\"", previewTab);
        Assert.Equal(2, Regex.Matches(previewTab, "<RenderMediaFrame\\s+IsVideo=").Count);
        Assert.DoesNotContain("scene-details-column", previewTab);
        Assert.DoesNotContain("Đang tạo ảnh tĩnh qua AI provider", previewTab);
        Assert.Contains("Đang tạo ảnh", razor);
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
