using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// RVID-UI-V2-PREVIEW-LOAD.1 regression coverage.
/// Locks the V2 Preview load-orchestration contract:
///   - selected/first scene is prioritized and rendered before all scenes/media complete,
///   - heavy media is mounted only for the selected scene (on demand),
///   - rapid scene switching is guarded against stale content,
///   - loading / empty / error / ready states are distinct,
///   - per-scene media retry does NOT create render/provider requests,
///   - existing P3B scene actions are preserved.
/// These are source-contract tests (the component is markup+code-behind rendered by Blazor),
/// plus pure behavioral assertions where a testable helper exists.
/// </summary>
public sealed class RVideoV2PreviewLoadTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string WebRoot => Path.Combine(RepoRoot, "TodoX.Web");
    private static string PreviewPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Preview.razor");
    private static string PreviewCssPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Preview.razor.css");

    private static string Preview => File.ReadAllText(PreviewPath);
    private static string PreviewCss => File.ReadAllText(PreviewCssPath);

    // ---------------------------------------------------------------
    // 1. Selected / first scene prioritized BEFORE other scenes/media
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_DoesNotBlockFirstRender_OnModelOptions()
    {
        // The blocking OnInitializedAsync + redundant OnParametersSetAsync model-option fetch
        // (which previously delayed first paint) must be gone.
        Assert.DoesNotContain("protected override async Task OnInitializedAsync()", Preview);
        Assert.DoesNotContain("OnParametersSetAsync", Preview);
        // Options load in the background after first render.
        Assert.Contains("OnAfterRenderAsync", Preview);
        Assert.Contains("_optsReady", Preview);
    }

    [Fact]
    public void Preview_SelectsFirstSceneSynchronously_InParametersSet()
    {
        Assert.Contains("protected override void OnParametersSet()", Preview);
        Assert.Contains("SelectedSceneId = JobView.Project.Scenes.OrderBy(x=>x.SceneIndex).First().Id", Preview);
    }

    [Fact]
    public void Preview_UsesCachedModelOptions_ToAvoidSecondFetch()
    {
        // Reuses the existing GetCachedOptions() from IRVideoSceneVideoModelOptionsService (no new service).
        Assert.Contains("ModelOptions.GetCachedOptions()", Preview);
        Assert.Contains("ModelOptions.GetOptionsAsync()", Preview);
    }

    // ---------------------------------------------------------------
    // 2. Heavy media loads on demand (only selected scene)
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_HeavyVideo_MountedOnlyForSelectedScene()
    {
        // The <video> element with preload=metadata must be gated behind SelectedSceneId==s.Id.
        Assert.Contains("SelectedSceneId==s.Id && !string.IsNullOrWhiteSpace(s.SceneVideoUrl)", Preview);
        // Non-selected scenes with videos render a lightweight lazy placeholder instead.
        Assert.Contains("rv2-vid-lazy", Preview);
    }

    [Fact]
    public void Preview_HeavyVideo_HasBoundedPreload()
    {
        // Video must not eagerly download full content.
        Assert.Contains("preload=\"metadata\"", Preview);
        Assert.DoesNotContain("preload=\"auto\"", Preview);
    }

    [Fact]
    public void Preview_ImagesUseLazyLoading()
    {
        Assert.Contains("loading=\"lazy\"", Preview);
    }

    // ---------------------------------------------------------------
    // 3. Rapid switching -> no stale content / version guard
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_SceneSwitch_HasVersionGuard()
    {
        Assert.Contains("_sceneVersion++", Preview);
        Assert.Contains("void SelectScene(long id)", Preview);
    }

    [Fact]
    public void Preview_SceneSwitch_GuardsNoOpReselect()
    {
        // Re-selecting the current scene must be a no-op (no redundant work).
        Assert.Contains("if(SelectedSceneId == id) return;", Preview);
    }

    // ---------------------------------------------------------------
    // 4. Loading / empty / error / ready states are distinct
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_HasLoadingSkeleton_WhenJobViewNull()
    {
        Assert.Contains("rv2-skeleton-grid", Preview);
        Assert.Contains("rv2-skeleton-row", Preview);
        Assert.Contains(".rv2-skeleton-row", PreviewCss);
    }

    [Fact]
    public void Preview_HasMetadataReady_ButOptionsLoading_State()
    {
        // Shell data present but options not yet ready => metadata + skeleton (not blank, not blocked).
        Assert.Contains("else if(!_optsReady)", Preview);
    }

    [Fact]
    public void Preview_HasEmptyMediaState()
    {
        // Distinct "Chưa có media" / "Chưa có" empty markers survive.
        Assert.Contains("Chưa có media", Preview);
        Assert.Contains("Chưa có", Preview);
    }

    [Fact]
    public void Preview_HasMediaErrorState_WithRetry()
    {
        Assert.Contains("rv2-media-error", Preview);
        Assert.Contains(".rv2-media-error", PreviewCss);
        Assert.Contains("RetryMediaLoad", Preview);
        Assert.Contains("@onerror", Preview);
    }

    [Fact]
    public void Preview_ErrorIsPerScene_DoesNotBlankWholePreview()
    {
        // Errors tracked per scene id in a dictionary => one failing scene cannot blank the list.
        Assert.Contains("Dictionary<long,bool> _mediaErrors", Preview);
        Assert.Contains("_mediaErrors.TryGetValue", Preview);
    }

    // ---------------------------------------------------------------
    // 5. Retry load does NOT create render/provider request
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_RetryLoad_DoesNotInvokeRenderOrProvider()
    {
        var retry = ExtractMethodBody(Preview, "void RetryMediaLoad(long sceneId)");
        Assert.NotNull(retry);
        // Retry only clears the local error flag so the existing <video> re-mounts its src.
        Assert.Contains("_mediaErrors.Remove(sceneId)", retry!);
        foreach (var forbidden in new[] { "Render", "Provider", "SceneVideoRender", "Enqueue", "Generate", "Charge", "Point" })
            Assert.DoesNotContain(forbidden, retry!);
    }

    // ---------------------------------------------------------------
    // 6. Existing P3B scene actions preserved (no regression)
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("HandleAddSceneAsync")]
    [InlineData("HandleEditAsync")]
    [InlineData("HandleSaveAsync")]
    [InlineData("HandleMoveAsync")]
    [InlineData("HandleDeleteAsync")]
    [InlineData("HandleContentChangedAsync")]
    [InlineData("HandleModelChangedAsync")]
    [InlineData("HandleDurationChangedAsync")]
    public void Preview_ExistingActionsPreserved(string action)
    {
        Assert.Contains(action, Preview);
    }

    [Fact]
    public void Preview_ReusesExistingServices_NoSecondBusinessLogic()
    {
        foreach (var svc in new[]
        {
            "IRVideoSceneDraftValidator", "IRVideoSceneDraftSaveAction", "IRVideoSceneCollectionAction",
            "IRVideoSceneVideoModelOptionsService", "IRVideoSceneAddDefaultResolver"
        })
            Assert.Contains(svc, Preview);
        // No bespoke preview render/fetch service introduced.
        Assert.DoesNotContain("IRVideoPreviewLoadService", Preview);
        Assert.DoesNotContain("new HttpClient", Preview);
    }

    [Fact]
    public void Preview_DisposesResources()
    {
        Assert.Contains("@implements IDisposable", Preview);
        Assert.Contains("public void Dispose()", Preview);
    }

    [Fact]
    public void Preview_MobileNav_UsesSelectScene()
    {
        // Mobile scene nav must go through SelectScene (version guard), not raw assignment.
        Assert.Contains("@onclick=\"@(()=>SelectScene(s.Id))\"", Preview);
    }

    // ---------------------------------------------------------------
    // 7. RVID-UI-V2-PREVIEW-LOAD.2: mobile active-scene indicator + prewarmed options
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_MobileNav_MarksActiveScene()
    {
        // The active scene dot must carry the rv2-nav-active marker bound to the selected scene id.
        Assert.Contains("@(SelectedSceneId==s.Id?\"rv2-nav-active\":\"\")", Preview);
    }

    [Fact]
    public void Preview_ActiveNavCss_HasShapeCue_NotColorOnly()
    {
        // Active ring must be identifiable by shape (check badge), not color alone, and must not
        // depend on !important to beat the status-specificity rules.
        Assert.Contains(".rv2-nav-dot.rv2-nav-active", PreviewCss);
        Assert.Contains(".rv2-nav-dot.rv2-nav-active::after", PreviewCss);
        Assert.Contains("#ffca28", PreviewCss);
        // Higher-specificity selector (0,2,0) => no !important needed anywhere in the active rule line.
        var activeLine = PreviewCss
            .Split('\n')
            .First(l => l.Contains(".rv2-nav-dot.rv2-nav-active{"));
        Assert.DoesNotContain("!important", activeLine);
    }

    [Fact]
    public void Preview_Options_PrewarmedInParametersSet_ReusedInAfterRender()
    {
        // Options fetch is started off the render path (OnParametersSet) and the same task is
        // awaited once in OnAfterRenderAsync, avoiding a second fetch / skeleton-content flicker.
        Assert.Contains("_optsTask", Preview);
        var setBody = ExtractMethodBody(Preview, "protected override void OnParametersSet()");
        Assert.NotNull(setBody);
        Assert.Contains("_optsTask = ModelOptions.GetOptionsAsync()", setBody!);
        var afterBody = ExtractMethodBody(Preview, "protected override async Task OnAfterRenderAsync(bool firstRender)");
        Assert.NotNull(afterBody);
        Assert.Contains("_optsTask ??= ModelOptions.GetOptionsAsync()", afterBody!);
        Assert.Contains("await _optsTask", afterBody!);
    }

    private static string? ExtractMethodBody(string text, string signature)
    {
        var idx = text.IndexOf(signature, StringComparison.Ordinal);
        if (idx < 0) return null;
        var open = text.IndexOf('{', idx);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(open, i - open + 1);
            }
        }
        return text.Substring(open);
    }
}