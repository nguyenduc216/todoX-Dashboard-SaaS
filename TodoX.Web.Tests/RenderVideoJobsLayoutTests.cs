using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

public class RenderVideoJobsLayoutTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = Path.Combine(RepoRoot, "TodoX.Web");
    private static readonly string RazorPath = Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor");
    private static readonly string CssPath = Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor.css");

    [Fact]
    public void ProjectDialog_KeepsExpectedFourWorkflowTabs()
    {
        var razor = File.ReadAllText(RazorPath);

        Assert.Equal(4, Regex.Matches(razor, "<MudTabPanel\\s+Text=").Count);
        Assert.Contains("<MudTabPanel Text=\"Thông tin\">", razor);
        Assert.Contains("<MudTabPanel Text=\"Scene / Hình ảnh\">", razor);
        Assert.Contains("<MudTabPanel Text=\"Video\">", razor);
        Assert.Contains("<MudTabPanel Text=\"Kết quả\">", razor);
    }

    [Fact]
    public void SceneImageTab_UsesDedicatedScrollOwnerBelowToolbar()
    {
        var razor = File.ReadAllText(RazorPath);
        var toolbarIndex = razor.IndexOf("class=\"scene-image-toolbar\"", StringComparison.Ordinal);
        var scrollIndex = razor.IndexOf("class=\"scene-list-scroll\"", StringComparison.Ordinal);

        Assert.True(toolbarIndex > 0);
        Assert.True(scrollIndex > toolbarIndex);
    }

    [Fact]
    public void ProjectDialogBody_DoesNotCompeteWithSceneListScroll()
    {
        var css = File.ReadAllText(CssPath);
        var bodyRule = CssRule(css, ".render-project-dialog-body");
        var scrollRule = CssRule(css, ".scene-list-scroll");

        Assert.Contains("overflow: hidden", bodyRule);
        Assert.DoesNotContain("overflow-y: auto", bodyRule);
        Assert.Contains("height: 0", bodyRule);
        Assert.Contains("overflow-y: auto", scrollRule);
        Assert.Contains("height: 0", scrollRule);
    }

    [Fact]
    public void VideoTabGrid_UsesThreeTwoOneResponsiveColumns()
    {
        var css = File.ReadAllText(CssPath);

        Assert.Contains(".scene-video-grid", css);
        Assert.Contains("grid-template-columns: repeat(3, minmax(0, 1fr))", css);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr))", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr)", css);
        Assert.Contains(".scene-video-grid > *", css);
        Assert.Contains("min-width: 0", css);
        Assert.Contains("object-fit: contain", CssRule(css, ".scene-media-video"));
    }

    [Fact]
    public void DialogLayout_UsesRealHtmlWrappersForHeightChain()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);

        Assert.Contains("<div class=\"render-project-dialog-surface\">", razor);
        Assert.Contains("<header class=\"render-project-dialog-header\">", razor);
        Assert.Contains("<main class=\"render-project-dialog-body\">", razor);
        Assert.DoesNotContain("::deep(", css);
        Assert.Contains(".render-project-dialog-body ::deep .render-project-tabs", css);
        Assert.Contains(".render-project-dialog-body ::deep .mud-tabs-panels", css);
    }

    private static string CssRule(string css, string selector)
    {
        var start = css.IndexOf(selector, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Selector '{selector}' was not found.");

        var open = css.IndexOf('{', start);
        Assert.True(open >= 0, $"Selector '{selector}' has no opening brace.");

        var close = css.IndexOf('}', open);
        Assert.True(close >= 0, $"Selector '{selector}' has no closing brace.");

        return css.Substring(open + 1, close - open - 1);
    }
}
