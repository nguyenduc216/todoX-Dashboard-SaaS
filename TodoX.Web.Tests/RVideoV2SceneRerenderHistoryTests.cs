using System.Text.RegularExpressions;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// RVID-UI-V2-SCENE-RERENDER-HISTORY.1 regression coverage.
/// Locks the V2 scene re-render + media-history + re-merge contract:
///   - dirty-aware "Tạo lại ảnh" / "Tạo lại video" against the ACTIVE media version baseline
///     (not the just-saved draft); undo back to baseline disables the action,
///   - image→video dependency: a changed/selected active image enables video re-render even when the
///     video prompt is unchanged,
///   - draft save does not reset the dirty state against the active output,
///   - V2 Preview exposes image/video history + selection and re-uses the canonical versioning service,
///   - V2 Result exposes an explicit "Ghép lại video" re-merge using the canonical finalization flow,
///   - no second render/version/merge implementation is introduced.
/// </summary>
public sealed class RVideoV2SceneRerenderHistoryTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string WebRoot => Path.Combine(RepoRoot, "TodoX.Web");
    private static string PreviewPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Preview.razor");
    private static string ResultPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Result.razor");
    private static string ShellPath => Path.Combine(WebRoot, "Components", "Pages", "RVideo", "RVideoV2Shell.razor");
    private static string FinalizationPath => Path.Combine(WebRoot, "Services", "VideoRender", "RVideoProjectFinalizationService.cs");

    private static string Preview => File.ReadAllText(PreviewPath);
    private static string Result => File.ReadAllText(ResultPath);
    private static string Shell => File.ReadAllText(ShellPath);
    private static string Finalization => File.ReadAllText(FinalizationPath);

    // ---------------------------------------------------------------
    // 1. Dirty baseline (image) — active output, not the saved draft
    // ---------------------------------------------------------------

    [Fact]
    public void Image_NoBaseline_NonEmptyPrompt_IsEnabled_FirstGeneration()
        => Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderImage("a cat on a beach", null, hasImageBaseline: false));

    [Fact]
    public void Image_EmptyPrompt_IsDisabled()
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderImage("   ", "something", hasImageBaseline: true));

    [Fact]
    public void Image_EditVsActiveBaseline_IsEnabled()
        => Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderImage("a new prompt", "old active prompt", hasImageBaseline: true));

    [Fact]
    public void Image_UndoBackToBaseline_IsDisabled()
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderImage("old active prompt", "old active prompt", hasImageBaseline: true));

    [Fact]
    public void Image_LineEndingOnlyDifference_IsNotDirty()
        // CRLF vs LF must not create a false dirty state (Save must not re-enable the action).
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderImage("line1\r\nline2", "line1\nline2", hasImageBaseline: true));

    // ---------------------------------------------------------------
    // 2. Dirty baseline (video) + image→video dependency
    // ---------------------------------------------------------------

    [Fact]
    public void Video_EditVsActiveBaseline_IsEnabled()
        => Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "new motion", "old motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: Guid.Empty, activeImageVersionId: Guid.Empty, hasAnyImageSource: true));

    [Fact]
    public void Video_UndoBackToBaseline_Disabled_WhenImageUnchanged()
    {
        var sameImage = Guid.NewGuid();
        Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "same motion", "same motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: sameImage, activeImageVersionId: sameImage, hasAnyImageSource: true));
    }

    [Fact]
    public void Video_SamePrompt_ButActiveImageChanged_IsEnabled()
        // Image dependency: the active video was built from a different image version than the one now active.
        => Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "same motion", "same motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: Guid.NewGuid(), activeImageVersionId: Guid.NewGuid(), hasAnyImageSource: true));

    [Fact]
    public void Video_NoActiveVideo_WithSourceImage_IsEnabled()
        => Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "motion", null, hasVideoBaseline: false,
            activeVideoSourceImageVersionId: null, activeImageVersionId: Guid.NewGuid(), hasAnyImageSource: true));

    [Fact]
    public void Video_NoActiveVideo_NoSourceImage_IsDisabled()
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "motion", null, hasVideoBaseline: false,
            activeVideoSourceImageVersionId: null, activeImageVersionId: null, hasAnyImageSource: false));

    [Fact]
    public void Video_EmptyPrompt_IsDisabled()
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "", "old", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: null, activeImageVersionId: null, hasAnyImageSource: true));

    // ---------------------------------------------------------------
    // 2b. Video mode/input policy — RVideo is canonical image_to_video.
    // The scene-video pipeline (render_scene_video → capability
    // rvideo_scene_video_generation, provider 79ai) is image-to-video only:
    // the canonical VideoRenderEligibilityService always emits MissingImage
    // when there is no usable source image, regardless of prompt. There is no
    // text-to-video mode wired for RVideo scenes, so the dirty policy must keep
    // requiring an image source for the first video generation.
    // ---------------------------------------------------------------

    [Fact]
    public void Video_FirstGeneration_NonEmptyPrompt_ButNoImageSource_IsDisabled()
        // Non-empty video prompt alone must never unlock video when there is no source image
        // (would contradict the canonical image-to-video eligibility contract).
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "a detailed motion prompt", null, hasVideoBaseline: false,
            activeVideoSourceImageVersionId: null, activeImageVersionId: null, hasAnyImageSource: false));

    [Fact]
    public void Video_FirstGeneration_WithImageSourceButNoBaseline_IsEnabled()
        // Only an available image source unlocks the very first video generation.
        => Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "a detailed motion prompt", null, hasVideoBaseline: false,
            activeVideoSourceImageVersionId: null, activeImageVersionId: null, hasAnyImageSource: true));

    [Fact]
    public void Video_PromptEditAndImageChange_BothEnable()
    {
        // Prompt edited → enabled regardless of image dependency.
        Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "edited motion", "old motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: null, activeImageVersionId: null, hasAnyImageSource: true));
        // Prompt unchanged but the active image version changed → enabled (image→video dependency).
        Assert.True(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "old motion", "old motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: Guid.NewGuid(), activeImageVersionId: Guid.NewGuid(), hasAnyImageSource: true));
    }

    [Fact]
    public void Video_ActiveVideo_UnknownSourceImage_SamePrompt_IsDisabled()
        // An existing active video whose source-image version is not recorded cannot be diffed against
        // the active image; with an unchanged prompt the action stays disabled (no spurious re-render).
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "same motion", "same motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: null, activeImageVersionId: Guid.NewGuid(), hasAnyImageSource: true));

    [Fact]
    public void Video_UndoPromptBackToBaseline_WithSameImage_IsDisabled()
        => Assert.False(RVideoSceneRerenderDirtyPolicy.CanRerenderVideo(
            "baseline motion", "baseline motion", hasVideoBaseline: true,
            activeVideoSourceImageVersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            activeImageVersionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            hasAnyImageSource: true));

    // ---------------------------------------------------------------
    // 2c. Canonical eligibility contract (behavioral): MissingImage is
    // not eligible, so the image input requirement is enforced by the
    // backend — the two render actions only surface the canonical decision.
    // ---------------------------------------------------------------

    [Fact]
    public void Eligibility_MissingImage_IsNotEligible_CanonicalImageToVideo()
    {
        var report = new VideoRenderEligibilityReport(42L, new[]
        {
            new VideoRenderEligibilityResult(1L, 1, VideoRenderEligibilityStatus.MissingImage,
                "RVIDEO_VIDEO_IMAGE_REQUIRED", "cần ảnh nguồn", null),
            new VideoRenderEligibilityResult(2L, 2, VideoRenderEligibilityStatus.Eligible,
                string.Empty, string.Empty, Guid.NewGuid())
        });
        Assert.Equal(new long[] { 2L }, report.EligibleSceneIds);
        Assert.Equal(1, report.Results.Count(x => x.Status == VideoRenderEligibilityStatus.MissingImage));
    }

    [Fact]
    public void Eligibility_AlreadyCompletedOrActive_IsNotEligible()
    {
        var report = new VideoRenderEligibilityReport(7L, new[]
        {
            new VideoRenderEligibilityResult(1L, 1, VideoRenderEligibilityStatus.AlreadyCompleted, "RVIDEO_VIDEO_ALREADY_COMPLETED", "done"),
            new VideoRenderEligibilityResult(2L, 2, VideoRenderEligibilityStatus.AlreadyActive, "RVIDEO_VIDEO_ALREADY_ACTIVE", "running"),
            new VideoRenderEligibilityResult(3L, 3, VideoRenderEligibilityStatus.InvalidPrompt, "RVIDEO_VIDEO_PROMPT_REQUIRED", "empty")
        });
        Assert.Empty(report.EligibleSceneIds);
    }
// ---------------------------------------------------------------
    // 3. V2 Preview wires the dirty-aware re-render actions
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_InjectsCanonicalRenderActions()
    {
        Assert.Contains("IRVideoSceneImageRenderAction SceneImageRender", Preview);
        Assert.Contains("IRVideoSceneVideoRenderAction SceneVideoRender", Preview);
        Assert.Contains("ISceneMediaVersioningService SceneVersions", Preview);
    }

    [Fact]
    public void Preview_ExposesRerenderButtons_BoundToDirtyPolicy()
    {
        Assert.Contains("HandleRerenderImageAsync", Preview);
        Assert.Contains("HandleRerenderVideoAsync", Preview);
        Assert.Contains("Disabled=\"@(!CanRerenderImage(s))\"", Preview);
        Assert.Contains("Disabled=\"@(!CanRerenderVideo(s))\"", Preview);
        Assert.Contains("RVideoSceneRerenderDirtyPolicy.CanRerenderImage", Preview);
        Assert.Contains("RVideoSceneRerenderDirtyPolicy.CanRerenderVideo", Preview);
    }

    [Fact]
    public void Preview_BaselineUsesActiveVersionSnapshots_NotDraft()
    {
        // The dirty baseline must come from the active version prompt snapshots.
        Assert.Contains("ImagePromptSnapshot", Preview);
        Assert.Contains("VideoPromptSnapshot", Preview);
        Assert.Contains("GetSelectedImageVersionAsync", Preview);
        Assert.Contains("GetSelectedVideoVersionAsync", Preview);
    }

    [Fact]
    public void Preview_SavesDraftBeforeSubmit()
    {
        var img = ExtractMethodBody(Preview, "async Task HandleRerenderImageAsync(");
        Assert.NotNull(img);
        Assert.Contains("SaveDraftOnlyAsync", img!);
        Assert.True(img!.IndexOf("SaveDraftOnlyAsync", StringComparison.Ordinal) < img.IndexOf("EnqueueRerenderAsync", StringComparison.Ordinal));
        var vid = ExtractMethodBody(Preview, "async Task HandleRerenderVideoAsync(");
        Assert.NotNull(vid);
        Assert.True(vid!.IndexOf("SaveDraftOnlyAsync", StringComparison.Ordinal) < vid.IndexOf("EnqueueRerenderAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void Preview_DoesNotAutoRenderOnTyping()
    {
        // Content change handler must only save a draft — never enqueue a render.
        var body = ExtractMethodBody(Preview, "async Task HandleContentChangedAsync(");
        Assert.NotNull(body);
        Assert.DoesNotContain("EnqueueRerenderAsync", body!);
        Assert.Contains("DoSaveAsync", body!);
    }

    [Fact]
    public void Preview_GuardsAgainstDoubleSubmit()
    {
        // The submit gate (_gate) + per-scene rerendering sets prevent duplicate provider jobs.
        Assert.Contains("if(!await _gate.WaitAsync(0))", Preview);
        Assert.Contains("_rerenderingImage", Preview);
        Assert.Contains("_rerenderingVideo", Preview);
    }
// ---------------------------------------------------------------
    // 4. Scene media history (image / video) + selection
    // ---------------------------------------------------------------

    [Fact]
    public void Preview_ExposesImageAndVideoHistory()
    {
        Assert.Contains("OpenImageHistoryAsync", Preview);
        Assert.Contains("OpenVideoHistoryAsync", Preview);
        Assert.Contains("ListImageVersionsAsync", Preview);
        Assert.Contains("ListSceneVideoVersionsAsync", Preview);
        Assert.Contains("MediaHistoryDialog", Preview);
        Assert.Contains("MediaHistoryItem", Preview);
    }

    [Fact]
    public void Preview_HistorySeparatesImageAndVideoTypes()
    {
        var img = ExtractMethodBody(Preview, "async Task OpenImageHistoryAsync(");
        Assert.NotNull(img);
        Assert.Contains("IsVideo = false", img!);
        Assert.Contains("ListImageVersionsAsync", img!);
        var vid = ExtractMethodBody(Preview, "async Task OpenVideoHistoryAsync(");
        Assert.NotNull(vid);
        Assert.Contains("IsVideo = true", vid!);
        Assert.Contains("ListSceneVideoVersionsAsync", vid!);
    }

    [Fact]
    public void Preview_OnlyCompletedVersionsSelectable()
        => Assert.Contains("CanSelect = string.Equals(v.Status, \"completed\"", Preview);

    [Fact]
    public void Preview_SelectVersion_UsesCanonicalSelection_AndConfirmsDraftLoss()
    {
        Assert.Contains("SceneVersions.SelectImageVersionAsync", Preview);
        Assert.Contains("SceneVersions.SelectSceneVideoVersionAsync", Preview);
        Assert.Contains("SelectImageVersionAsync(scene.Id, versionId, AuthState.CurrentUser)", Preview);
        Assert.Contains("SelectSceneVideoVersionAsync(scene.Id, versionId, AuthState.CurrentUser)", Preview);
        // Draft-loss guard uses an explicit confirm dialog (no silent overwrite).
        Assert.Contains("Chọn phiên bản ảnh", Preview);
        Assert.Contains("Chọn phiên bản video", Preview);
    }

    // ---------------------------------------------------------------
    // 5. Explicit re-merge action on the Result page
    // ---------------------------------------------------------------

    [Fact]
    public void Result_ExposesReMerge_UsingCanonicalFinalization()
    {
        Assert.Contains("Ghép lại video", Result);
        Assert.Contains("IRVideoProjectFinalizationService", Result);
        Assert.Contains("TryEnqueueFinalMergeAsync", Result);
        Assert.Contains("RVideoProjectFinalizationContracts.TriggerManual", Result);
    }

    [Fact]
    public void Result_ReMerge_ReportsLoadingAndGuardedOutcomes()
    {
        Assert.Contains("_merging", Result);
        Assert.Contains("result.AlreadyActive", Result);
        Assert.Contains("result.Enqueued", Result);
        Assert.Contains("already_final", Result);
    }

    [Fact]
    public void Shell_PassesReloadCallbackToResult()
        => Assert.Contains("RVideoV2Result JobView=\"_jobView\" OnReloadRequested=\"ReloadAsync\"", Shell);

    // ---------------------------------------------------------------
    // 5b. Re-merge correctness contract (canonical finalization service).
    // Verified behavior (RVideoProjectFinalizationService.TryEnqueueFinalMergeAsync):
    //  - the "already_final" short-circuit only fires when the stored final video still
    //    matches the currently selected scene video versions in scene order
    //    (IsCurrentFinalVideoAsync → ListFinalVideoVersionItemsAsync vs GetSelectedVideoVersionAsync,
    //    OrderBy(SceneIndex) + SequenceEqual). Selecting an older video therefore forces a real re-merge;
    //    a stale FinalVideoUrl is not treated as final.
    //  - duplicate merges are blocked by the canonical EnqueueForLogCodeIfNoneActiveAsync keyed on
    //    rvideo-final-merge:{projectId} → AlreadyActive.
    //  - a not-ready project returns NotEnqueued("not_ready"); failures never become a fake success.
    // ---------------------------------------------------------------

    [Fact]
    public void Finalization_ReMerge_NotShortCircuitedByStaleFinalVideoUrl()
        => Assert.Contains("await IsCurrentFinalVideoAsync(project.Id, project.Scenes, ct)", Finalization);

    [Fact]
    public void Finalization_CurrentFinalVideo_ComparesSelectedVersionsInSceneOrder()
    {
        var body = ExtractMethodBody(Finalization, "private async Task<bool> IsCurrentFinalVideoAsync(");
        Assert.NotNull(body);
        // Uses the canonical active selection, ordered by scene order, and compares item identity.
        Assert.Contains("GetSelectedVideoVersionAsync", body!);
        Assert.Contains("ListFinalVideoVersionItemsAsync", body!);
        Assert.Contains("OrderBy(x => x.SceneIndex)", body!);
        Assert.Contains("SequenceEqual", body!);
    }

    [Fact]
    public void Finalization_BlocksDuplicateMerge_ViaCanonicalLogicalRequestKey()
    {
        var body = ExtractMethodBody(Finalization, "public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync(");
        Assert.NotNull(body);
        Assert.Contains("EnqueueForLogCodeIfNoneActiveAsync", body!);
        Assert.Contains("alreadyActive", body!);
    }

    [Fact]
    public void Finalization_NotReady_ReturnsNotEnqueued_NeverFakeSuccess()
    {
        var body = ExtractMethodBody(Finalization, "public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync(");
        Assert.NotNull(body);
        Assert.Contains("NotEnqueued(\"not_ready\", logicalRequestId)", body!);
        Assert.Contains("RVideoRules.GetSceneReadiness", body!);
    }

    // ---------------------------------------------------------------
    // 6. No second render/version/merge implementation
    // ---------------------------------------------------------------

    [Fact]
    public void NoSecondRenderMergeImplementation()
    {
        Assert.DoesNotContain("new HttpClient", Preview);
        Assert.DoesNotContain("EnqueueAsync(new RenderJobCreateModel", Preview);
        Assert.DoesNotContain("INSERT INTO video_render", Preview);
        var imageAction = File.ReadAllText(Path.Combine(WebRoot, "Services", "VideoRender", "RVideoSceneImageRenderAction.cs"));
        var videoAction = File.ReadAllText(Path.Combine(WebRoot, "Services", "VideoRender", "RVideoSceneVideoRenderAction.cs"));
        Assert.Contains("SceneImageRenderWorkItemHandler.JobTypeName", imageAction);
        Assert.Contains("CreateQueuedImageVersionAsync", imageAction);
        Assert.Contains("SceneVideoRenderHandler.JobTypeName", videoAction);
        Assert.Contains("EnqueueForSceneIfNoneActiveAsync", videoAction);
        Assert.Contains("GetVideoRenderEligibilityAsync", videoAction);
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