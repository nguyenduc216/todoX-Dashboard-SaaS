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
    public void ProjectDialog_KeepsFourTabs()
    {
        var razor = File.ReadAllText(RazorPath);

        Assert.Equal(4, Regex.Matches(razor, "<MudTabPanel\\s+Text=").Count);
        Assert.Contains("<MudTabPanel Text=\"Video\">", razor);
        Assert.Contains("class=\"scene-image-tab\"", razor);
        Assert.Contains("class=\"scene-video-tab\"", razor);
        Assert.Contains("class=\"render-tab-scroll render-result-scroll\"", razor);
    }

    [Fact]
    public void SceneImageTab_UsesSingleScrollOwnerBelowToolbar()
    {
        var razor = File.ReadAllText(RazorPath);
        var toolbarIndex = razor.IndexOf("class=\"scene-image-toolbar\"", StringComparison.Ordinal);
        var scrollIndex = razor.IndexOf("class=\"scene-list-scroll\"", StringComparison.Ordinal);

        Assert.True(toolbarIndex > 0);
        Assert.True(scrollIndex > toolbarIndex);
        Assert.Single(Regex.Matches(razor, "class=\"scene-list-scroll\""));
    }

    [Fact]
    public void Tabs_HaveDedicatedScrollHosts()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);
        var scrollRule = CssRule(css, ".render-tab-scroll");

        Assert.Contains("class=\"render-tab-scroll render-info-scroll\"", razor);
        Assert.Contains("class=\"render-tab-scroll scene-video-scroll\"", razor);
        Assert.Contains("class=\"render-tab-scroll render-result-scroll\"", razor);
        Assert.Contains("overflow-y: auto", scrollRule);
        Assert.Contains("height: 100%", scrollRule);
        Assert.Contains("flex: 1 1 auto", scrollRule);
    }

    [Fact]
    public void ProjectDialogBody_DoesNotCompeteWithSceneListScroll()
    {
        var css = File.ReadAllText(CssPath);
        var bodyRule = CssRule(css, ".render-project-dialog-body");
        var scrollRule = CssRule(css, ".scene-list-scroll");

        Assert.Contains("overflow: hidden", bodyRule);
        Assert.DoesNotContain("overflow-y: auto", bodyRule);
        Assert.DoesNotContain("\n    height: 0", bodyRule);
        Assert.Contains("overflow-y: auto", scrollRule);
        Assert.Contains("height: 100%", scrollRule);
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
    public void SceneCards_UseCompactLayoutAndBoundedMedia()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);

        Assert.Contains("scene-card scene-card-compact", razor);
        Assert.Contains("Value=\"@draft.ImagePrompt\"", razor);
        Assert.Contains("Lines=\"5\"", Between(razor, "Value=\"@draft.ImagePrompt\"", "Value=\"@draft.Purpose\""));
        Assert.Contains("Lines=\"1\"", Between(razor, "Value=\"@draft.Purpose\"", "@if (_imageHistorySceneId == scene.Id)"));
        Assert.Contains("grid-template-columns: minmax(150px, 190px) minmax(0, 1fr)", CssRule(css, ".scene-workflow"));
        Assert.Contains("width: min(100%, 190px)", CssRule(css, ".scene-media-square"));
        Assert.Contains("max-height: 250px", CssRule(css, ".scene-media-square"));
    }

    [Fact]
    public void VideoCards_HideVoiceFieldsButKeepSceneBindings()
    {
        var razor = File.ReadAllText(RazorPath);
        var sceneTab = Between(razor, "class=\"scene-image-tab\"", "<MudTabPanel Text=\"Video\">");
        var videoTab = Between(razor, "class=\"scene-video-tab\"", "<div class=\"render-tab-scroll render-result-scroll\">");

        Assert.Contains("Value=\"@draft.Voice\"", sceneTab);
        Assert.Contains("Value=\"@draft.VoiceInstruction\"", sceneTab);
        Assert.Contains("Value=\"@draft.MotionPrompt\"", videoTab);
        Assert.DoesNotContain("Value=\"@draft.Voice\"", videoTab);
        Assert.DoesNotContain("Value=\"@draft.VoiceInstruction\"", videoTab);
    }

    [Fact]
    public void SceneAuxiliaryFields_StayUnderImagePromptInsideDetailsColumn()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);
        var workflow = Between(razor, "<div class=\"scene-workflow\">", "@if (_imageHistorySceneId == scene.Id)");
        var detailsIndex = workflow.IndexOf("class=\"scene-workflow-cell scene-details-column\"", StringComparison.Ordinal);
        var promptIndex = workflow.IndexOf("Value=\"@draft.ImagePrompt\"", StringComparison.Ordinal);
        var voiceRowIndex = workflow.IndexOf("class=\"scene-voice-row\"", StringComparison.Ordinal);
        var purposeIndex = workflow.IndexOf("Value=\"@draft.Purpose\"", StringComparison.Ordinal);
        var voiceIndex = workflow.IndexOf("Value=\"@draft.Voice\"", StringComparison.Ordinal);
        var instructionIndex = workflow.IndexOf("Value=\"@draft.VoiceInstruction\"", StringComparison.Ordinal);

        Assert.True(detailsIndex >= 0);
        Assert.True(promptIndex > detailsIndex);
        Assert.True(voiceRowIndex > promptIndex);
        Assert.True(purposeIndex > voiceRowIndex);
        Assert.True(voiceIndex > purposeIndex);
        Assert.True(instructionIndex > voiceIndex);
        Assert.Single(Regex.Matches(workflow, "class=\"scene-voice-row\""));
        Assert.Contains("ValueChanged=\"@((string? value) => UpdateDraft(scene.Id, d => d.Purpose = value))\"", workflow);
        Assert.Contains("ValueChanged=\"@((string? value) => UpdateDraft(scene.Id, d => d.Voice = value))\"", workflow);
        Assert.Contains("ValueChanged=\"@((string? value) => UpdateDraft(scene.Id, d => d.VoiceInstruction = value))\"", workflow);
        Assert.Contains("display: flex", CssRule(css, ".scene-details-column"));
    }

    [Fact]
    public void DialogLayout_UsesHtmlWrappersForHeightChain()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);

        Assert.Contains("<div class=\"render-project-dialog-surface\">", razor);
        Assert.Contains("<header class=\"render-project-dialog-header\">", razor);
        Assert.Contains("<main class=\"render-project-dialog-body\">", razor);
        Assert.DoesNotContain("::deep(", css);
        Assert.Contains(".render-project-dialog-body ::deep .render-project-tabs", css);
        Assert.Contains(".render-project-dialog-body ::deep .mud-tabs-panels", css);
        Assert.DoesNotContain("\n    height: 0", CssRule(css, ".render-project-dialog-body"));
        Assert.DoesNotContain("\n    height: 0", CssRule(css, ".render-project-dialog-body ::deep .mud-tabs-panels"));
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

    private static string Between(string source, string startText, string endText)
    {
        var start = source.IndexOf(startText, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker '{startText}' was not found.");

        var end = source.IndexOf(endText, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker '{endText}' was not found after '{startText}'.");

        return source[start..end];
    }
}
