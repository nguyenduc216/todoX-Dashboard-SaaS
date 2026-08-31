using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

public class RenderVideoJobsLayoutTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = Path.Combine(RepoRoot, "TodoX.Web");
    private static readonly string RazorPath = Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor");
    private static readonly string CssPath = Path.Combine(WebRoot, "Components", "Pages", "RenderVideoJobs.razor.css");
    private static readonly string ErrorDialogPath = Path.Combine(WebRoot, "Components", "Dialogs", "SceneRenderErrorDetailDialog.razor");
    private static readonly string VersioningServicePath = Path.Combine(WebRoot, "Services", "VideoRender", "SceneMediaVersioningService.cs");

    [Fact]
    public void ProjectDialog_KeepsFiveTabs()
    {
        var razor = File.ReadAllText(RazorPath);

        Assert.Equal(3, Regex.Matches(razor, "<MudTabPanel\\s+Text=").Count);
        Assert.Contains("<MudTabPanel Text=\"Thông tin\">", razor);
        Assert.Contains("class=\"scene-image-tab\"", razor);
        Assert.Contains("RenderMediaFrame IsVideo=\"true\"", razor);
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
        var css = File.ReadAllText(Path.Combine(WebRoot, "Components", "Shared", "RenderMediaFrame.razor.css"));

        Assert.Contains(".scene-media-video", css);
        Assert.Contains("object-fit: contain", CssRule(css, ".scene-media-video"));
    }

    [Fact]
    public void SceneCards_UseCompactLayoutAndBoundedMedia()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);
        var mediaCss = File.ReadAllText(Path.Combine(WebRoot, "Components", "Shared", "RenderMediaFrame.razor.css"));

        Assert.Contains("scene-card scene-card-compact", razor);
        Assert.Contains("RenderMediaFrame IsVideo=\"false\"", razor);
        Assert.Contains("ResolveImageMediaState(sceneState)", razor);
        Assert.Contains("RenderMediaFrame IsVideo=\"true\"", razor);
        Assert.Contains("ResolveVideoMediaState(scene)", razor);
        Assert.Contains("scene-surface-grid", razor);
        Assert.Contains("width: min(100%, var(--render-media-max-width, 220px))", CssRule(mediaCss, ".scene-media-square"));
        Assert.Contains("max-height: min(60dvh, var(--render-media-max-height, 240px))", CssRule(mediaCss, ".scene-media-square"));
    }

    [Fact]
    public void ResultTab_ShowsMergeStateImmediatelyAndLocksFinalMergeAction()
    {
        var source = File.ReadAllText(RazorPath);

        Assert.Contains("private bool _finalMergeFinalizing;", source);
        Assert.Contains("_finalMergeFinalizing = true;", source);
        Assert.Contains("project.Status = VideoProjectStatuses.Merging;", source);
        Assert.Contains("private bool IsFinalMergeProcessing => IsFinalMergeActive && !IsFinalMergeFailed;", source);
        Assert.Contains("EmptyText=\"Đang ghép video...\"", source);
        Assert.Contains("State=\"MediaRenderState.Rendering\"", source);
        Assert.Contains("Disabled=\"@(!CanClickFinalMerge)\"", source);
    }

    [Fact]
    public void ResultTab_HandlesFinalMergeFailureWithoutBrowserReload()
    {
        var source = File.ReadAllText(RazorPath);

        Assert.Contains("private bool IsFinalMergeFailed", source);
        Assert.Contains("FinalMergeErrorText", source);
        Assert.Contains("State=\"MediaRenderState.Failed\"", source);
        Assert.Contains("Ghép video thất bại", source);
        Assert.Contains("await ReloadAsync();", source);
    }

    [Fact]
    public void SceneImageToolbar_HidesMissingImageButtonWhenNoActionableSceneImages()
    {
        var source = File.ReadAllText(RazorPath);
        var helper = Between(source, "private bool IsSceneMissingOrFailedImage", "private static bool IsActiveSceneImageVersion");

        Assert.Contains("@if (HasActionableMissingSceneImages)", source);
        Assert.Contains("private bool HasActionableMissingSceneImages", source);
        Assert.Contains("_project.Scenes.Any(IsSceneMissingOrFailedImage)", source);
        Assert.Contains("IsActiveSceneImageVersion(current)", source);
        Assert.Contains("IsFailedSceneImageVersion(current)", source);
        Assert.DoesNotContain("PublicUrl", helper);
    }

    [Fact]
    public void ReloadRefreshesSceneImageVideoFinalAndJobsState()
    {
        var source = File.ReadAllText(RazorPath);

        Assert.Contains("await LoadSceneImageVersionsForStateAsync(_project.Scenes);", source);
        Assert.Contains("await LoadSceneVideoVersionsForStateAsync(_project.Scenes);", source);
        Assert.Contains("await LoadSceneAudioVersionsForStateAsync(_project.Scenes);", source);
        Assert.Contains("await LoadFinalHistoryAsync(showSnackbar: false);", source);
        Assert.Contains("await ReloadJobsAsync();", source);
        Assert.Contains("await InvokeAsync(StateHasChanged);", source);
    }

    [Fact]
    public void VideoCards_HideVoiceFieldsButKeepSceneBindings()
    {
        var razor = File.ReadAllText(RazorPath);
        var sceneTab = Between(razor, "class=\"scene-image-tab\"", "<MudTabPanel Text=\"Kết quả\">");
        var videoTab = sceneTab;

        Assert.Contains("RenderMediaFrame IsVideo=\"false\"", sceneTab);
        Assert.Contains("RenderMediaFrame IsVideo=\"true\"", videoTab);
        Assert.Contains("State=\"@ResolveVideoMediaState(scene)\"", videoTab);
        Assert.DoesNotContain("Value=\"@draft.Voice\"", videoTab);
        Assert.DoesNotContain("Value=\"@draft.VoiceInstruction\"", videoTab);
    }

    [Fact]
    public void VoiceModeSelector_IsModeAwareAndUsesPersistedSceneVoiceFallback()
    {
        var razor = File.ReadAllText(RazorPath);

        Assert.Contains("if (_voiceMode == RVideoVoiceModes.Library)", razor);
        Assert.Contains("if (_voiceMode == RVideoVoiceModes.Native)", razor);
        Assert.Contains("VoiceCatalogCode = _voiceMode == RVideoVoiceModes.Library ? _voiceCatalogCode : null", razor);
        Assert.Contains("VoiceSnapshot = _voiceMode == RVideoVoiceModes.Library", razor);
        Assert.Contains("RVideoRules.ResolveSceneVoiceText(scene)", razor);
    }

    [Fact]
    public void VideoCards_ShowOmniFlashPromptCounterAndBlockInvalidPrompt()
    {
        var razor = File.ReadAllText(RazorPath);
        Assert.Contains("ResolveVideoMediaState(scene)", razor);
        Assert.Contains("Disabled=\"@(!CanCreateSceneVideo(scene, draft))\"", razor);
        Assert.Contains("VideoPromptValidator.CountUnicodeScalars", razor);
        Assert.Contains("VideoPromptValidator.ResolveMaxPromptCharacters", razor);
    }

    [Fact]
    public void FailedStatusBadge_IsClickableInImageAndVideoTabs()
    {
        var razor = File.ReadAllText(RazorPath);

        Assert.Contains("@RenderSceneStatusBadge(scene, \"image\")", razor);
        Assert.Contains("ResolveVideoSceneStatus(scene)", razor);
        Assert.Contains("scene-failed-status-trigger", razor);
        Assert.Contains("@onclick:stopPropagation=\"true\"", razor);
        Assert.Contains("@onkeydown:stopPropagation=\"true\"", razor);
        Assert.Contains("OpenSceneErrorKeyDownAsync", razor);
        Assert.Contains("GetSceneRenderErrorDetailAsync(scene.Id, taskType", razor);
    }

    [Fact]
    public void SceneErrorDialog_HasCopyActionsAndJsonBlocks()
    {
        var dialog = File.ReadAllText(ErrorDialogPath);

        Assert.Contains("Provider Request", dialog);
        Assert.Contains("Provider Response", dialog);
        Assert.Contains("Provider Error", dialog);
        Assert.Contains("Copy lỗi", dialog);
        Assert.Contains("Copy JSON", dialog);
        Assert.Contains("navigator.clipboard.writeText", dialog);
    }

    [Fact]
    public void SceneErrorDetailService_RedactsSensitiveJsonKeys()
    {
        var service = File.ReadAllText(VersioningServicePath);

        Assert.Contains("GetSceneRenderErrorDetailAsync", service);
        Assert.Contains("RedactSensitiveJson", service);
        Assert.Contains("\"***REDACTED***\"", service);
        Assert.Contains("authorization", service);
        Assert.Contains("access_key", service);
        Assert.Contains("provider_usage_json", service);
        Assert.Contains("todox_ai_provider_usage_log", service);
    }

    [Fact]
    public void PerSceneImageRerender_EnqueuesPersisted79AiWorkItem()
    {
        var razor = File.ReadAllText(RazorPath);
        var method = Between(razor, "private async Task RerenderSceneImageAsync", "private bool IsSceneRendering");

        Assert.DoesNotContain("RerenderSceneImageWithOpenRouterAsync", method);
        Assert.Contains("CreateQueuedImageVersionAsync", method);
        Assert.Contains("SceneImageRenderWorkItemHandler.JobTypeName", method);
        Assert.Contains("SceneImageRenderContext.RVideoCapabilityCode", method);
        Assert.Contains("ResolveCharacterReferenceMediaIdAsync", method);
        Assert.Contains("requireReference: reference.ReferenceRequested", method);
        Assert.Contains("reference.Source", method);
        Assert.Contains("RVideoSceneImageReferenceSelection.Resolve", method);
        Assert.Contains("RequestedModel = model.Model", method);
        Assert.Contains("ModelAttemptIndex = model.AttemptIndex", method);
    }

    [Fact]
    public void SceneImageRenderStates_DefineLocalFlashAndShimmerKeyframes()
    {
        var css = File.ReadAllText(Path.Combine(WebRoot, "Components", "Shared", "RenderMediaFrame.razor.css"));

        Assert.Contains("animation: scene-image-frame-flash", css);
        Assert.Contains("animation: scene-image-shimmer-flash", css);
        Assert.Contains("@keyframes scene-image-frame-flash", css);
        Assert.Contains("@keyframes scene-image-shimmer-flash", css);
        Assert.Contains(".scene-image-submitted", css);
        Assert.Contains(".scene-image-processing", css);
        Assert.DoesNotContain("avatar-card-flash", css);
        Assert.DoesNotContain("avatar-render-flash", css);
    }

    [Fact]
    public void SceneAuxiliaryFields_StayUnderImagePromptInsideDetailsColumn()
    {
        var razor = File.ReadAllText(RazorPath);
        var css = File.ReadAllText(CssPath);
        Assert.Contains("scene-surface-grid", razor);
        Assert.Contains("RenderMediaFrame IsVideo=\"false\"", razor);
        Assert.Contains("RenderMediaFrame IsVideo=\"true\"", razor);
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
