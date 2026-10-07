using System.Text.RegularExpressions;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// RVID-UI-V2-PHASE3A.1 regression coverage.
/// Split into (a) real behavioral tests for the pure <see cref="RVideoPromptBaseline"/> helper and
/// (b) source-contract tests that lock the RVideo V2 Info canonical prompt/reference behavior.
/// </summary>
public sealed class RVideoV2InfoPromptReferencesTests
{
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string WebRoot => Path.Combine(RepoRoot, "TodoX.Web");
    private static string Rp(params string[] p) => File.ReadAllText(Path.Combine(new[] { WebRoot }.Concat(p).ToArray()));
    private static string V2Info => Rp("Components", "Pages", "RVideo", "RVideoV2Info.razor");
    private static string Picker => Rp("Components", "Dialogs", "RVideoCharacterPickerDialog.razor");
    private static string PickerCss => Rp("Components", "Dialogs", "RVideoCharacterPickerDialog.razor.css");
    private static string InfoCss => Rp("Components", "Pages", "RVideo", "RVideoV2Info.razor.css");

    // ---------------------------------------------------------------
    // (a) Pure behavioral tests: RVideoPromptBaseline
    // ---------------------------------------------------------------

    [Fact] public void Normalize_ConvertsCrlfAndCrToLf()
    {
        Assert.Equal("a\nb\nc", RVideoPromptBaseline.Normalize("a\r\nb\rc"));
    }

    [Fact] public void Normalize_NullBecomesEmpty()
    {
        Assert.Equal(string.Empty, RVideoPromptBaseline.Normalize(null));
    }

    [Fact] public void IsEmpty_TrueForNullWhitespaceEmpty_FalseForContent()
    {
        Assert.True(RVideoPromptBaseline.IsEmpty(null));
        Assert.True(RVideoPromptBaseline.IsEmpty(""));
        Assert.True(RVideoPromptBaseline.IsEmpty("   \r\n\t"));
        Assert.False(RVideoPromptBaseline.IsEmpty("xin chào"));
    }

    [Fact] public void AreEqual_IgnoresLineEndingStyle()
    {
        Assert.True(RVideoPromptBaseline.AreEqual("một\r\nhai", "một\nhai"));
        Assert.True(RVideoPromptBaseline.AreEqual("một\rhai", "một\nhai"));
    }

    [Fact] public void AreEqual_Ordinal_DistinguishesContent()
    {
        Assert.False(RVideoPromptBaseline.AreEqual("một", "Một"));
        Assert.False(RVideoPromptBaseline.AreEqual("a b", "a  b"));
    }

    [Fact] public void IsDirty_FirstTime_NonEmptyWorkingIsDirty()
    {
        // brand new job: no baseline yet, first Generate must be allowed for non-empty input
        Assert.True(RVideoPromptBaseline.IsDirty("prompt đầu tiên", baseline: "", hasBaseline: false));
    }

    [Fact] public void IsDirty_EmptyWorkingIsNeverDirty()
    {
        Assert.False(RVideoPromptBaseline.IsDirty("", baseline: "x", hasBaseline: true));
        Assert.False(RVideoPromptBaseline.IsDirty("   \r\n", baseline: "", hasBaseline: false));
        Assert.False(RVideoPromptBaseline.IsDirty(null, baseline: "x", hasBaseline: true));
    }

    [Fact] public void IsDirty_UnchangedWorking_IsNotDirty()
    {
        Assert.False(RVideoPromptBaseline.IsDirty("giữ nguyên", baseline: "giữ nguyên", hasBaseline: true));
    }

    [Fact] public void IsDirty_CrlfVsLfEdit_IsNotDirty()
    {
        // server persisted with LF; textarea emits CRLF -> must NOT be considered a change
        Assert.False(RVideoPromptBaseline.IsDirty("dòng 1\r\ndòng 2", baseline: "dòng 1\ndòng 2", hasBaseline: true));
    }

    [Fact] public void IsDirty_EditedContent_IsDirty()
    {
        Assert.True(RVideoPromptBaseline.IsDirty("bản sửa", baseline: "bản gốc", hasBaseline: true));
    }
// ---------------------------------------------------------------
    // (b) Canonical prompt source = persisted user_input of the active generation
    // ---------------------------------------------------------------

    [Fact] public void Prompt_SourceIsCanonicalUserInput()
    {
        Assert.Contains("HydratePromptFromActiveAsync", V2Info);
        Assert.Contains("ag.UserInput", V2Info);
        Assert.Contains("_baselineInput", V2Info);
        Assert.Contains("_hasBaseline", V2Info);
    }

    [Fact] public void Prompt_NoGeneratedJsonFallback()
    {
        // Must never reconstruct the prompt from generated JSON or classify JSON-looking input.
        Assert.DoesNotContain("LooksLikeJson", V2Info);
        Assert.DoesNotContain("OriginalPrompt", V2Info);
    }

    [Fact] public void Prompt_HydrateGuardedOnActiveChange()
    {
        Assert.Contains("_lastWorkingProjectId", V2Info);
        Assert.Contains("_lastWorkingActiveId", V2Info);
        Assert.Contains("ActivePromptGenerationId", V2Info);
    }

    [Fact] public void Prompt_HonestEmptyStateWhenActiveMissing()
    {
        // Missing/unloadable active generation surfaces an honest empty-state, not generated content.
        var seg = Segment(V2Info, "HydratePromptFromActiveAsync");
        Assert.Contains("_workingInput = string.Empty", seg);
        Assert.Contains("Không tải được nội dung prompt đang active", V2Info);
    }

    // ---------------------------------------------------------------
    // (c) Dirty-aware Generate
    // ---------------------------------------------------------------

    [Fact] public void Generate_DirtyAwareGate()
    {
        Assert.Contains("CanGenerate =>", V2Info);
        Assert.Contains("RVideoPromptBaseline.IsDirty(_workingInput, _baselineInput, _hasBaseline)", V2Info);
        Assert.Contains("CanGenerateFirstTime =>", V2Info);
        Assert.Contains("RVideoPromptBaseline.IsEmpty(_workingInput)", V2Info);
    }

    [Fact] public void Generate_GuardedByDirtyOrFirstTime()
    {
        Assert.Contains("if(!CanGenerate && !CanGenerateFirstTime) return;", V2Info);
    }

    [Fact] public void Generate_BaselineAdvancesOnlyOnConfirmedReload()
    {
        Assert.Contains("WaitForActiveGenerationAsync", V2Info);
        Assert.Contains("outcome.Result.GenerationId", V2Info);
        Assert.Contains("_baselineInput = _workingInput", V2Info);
    }

    [Fact] public void WorkingInputChanged_IsLocalOnly_NoServerWrite()
    {
        var seg = Segment(V2Info, "HandleWorkingInputChanged");
        Assert.Contains("_workingInput = v", seg);
        Assert.Contains("_status = null", seg);
        // typing must not trigger a settings/job write
        Assert.DoesNotContain("UpdateAsync", seg);
        Assert.DoesNotContain("BuildSafeUpdate", seg);
    }

    [Fact] public void History_ReloadHydratesBaseline()
    {
        var seg = Segment(V2Info, "HandleHistoryAsync");
        Assert.Contains("SetActiveProjectGenerationAsync", seg);
        Assert.Contains("OnReloadRequested", V2Info);
    }

    [Fact] public void Reload_RehydratesPrompt()
    {
        var seg = Segment(V2Info, "HandleReloadAsync");
        Assert.Contains("HydratePromptFromActiveAsync", seg);
    }
// ---------------------------------------------------------------
    // (d) References: canonical Upload / Character sources + upload icon + mutation
    // ---------------------------------------------------------------

    [Fact] public void References_HeaderAndCount()
    {
        Assert.Contains("Hình ảnh tham chiếu (@InputReferences.Count)", V2Info);
    }

    [Fact] public void References_UploadIcon_Tooltip_And_HiddenInputFile()
    {
        // Compact Upload icon button: upload icon + tooltip + a fully hidden native file input.
        Assert.Contains("Icons.Material.Filled.Upload", V2Info);
        Assert.Contains("rv2-ref-btn", V2Info);
        Assert.Contains("rv2-ref-file", V2Info);
        Assert.Contains("Tải ảnh tham chiếu", V2Info);
        Assert.Contains("<InputFile Class=\"rv2-ref-file\" OnChange=\"UploadReferenceAsync\"", V2Info);
        // The native input must never render its filename chrome.
        Assert.Contains("aria-hidden=\"true\"", V2Info);
        // No plain-text "Chọn tệp" button anywhere in this component.
        Assert.DoesNotContain("Chọn tệp", V2Info);
    }

    [Fact] public void References_ExactlyTwoIconButtons_UploadAndCharacter()
    {
        // Header reference controls = exactly two compact icon buttons (Upload + Character).
        Assert.Contains("Icons.Material.Filled.Upload", V2Info);
        Assert.Contains("Icons.Material.Filled.FaceRetouchingNatural", V2Info);
        Assert.Contains("Tải ảnh tham chiếu", V2Info);
        Assert.Contains("Chọn từ Character", V2Info);
        // Upload is a <label> acting as a button (native input fully hidden inside).
        Assert.Contains("role=\"button\"", V2Info);
        Assert.Contains("@onclick=\"TriggerUploadPickerAsync\"", V2Info);
        // Keyboard parity for the label-based control.
        Assert.Contains("@onkeydown=\"HandleUploadKeyDownAsync\"", V2Info);
    }

    [Fact] public void References_ActiveSourceHighlight_SingleAtATime()
    {
        // Exactly one reference source is highlighted: Character XOR Upload.
        Assert.Contains("ActiveReferenceSource", V2Info);
        Assert.Contains("rv2-ref-btn--active", V2Info);
        // Upload button binds the active class to the upload source.
        Assert.Contains("ActiveReferenceSource==\"upload\" ? \"rv2-ref-btn--active\"", V2Info);
        // Character button binds the active class to the character source.
        Assert.Contains("ActiveReferenceSource==\"character\" ? \"rv2-ref-btn--active\"", V2Info);
    }

    [Fact] public void References_CharacterPickerIcon_And_Dialog()
    {
        Assert.Contains("Icons.Material.Filled.FaceRetouchingNatural", V2Info);
        Assert.Contains("OpenCharacterPickerAsync", V2Info);
        Assert.Contains("RVideoCharacterPickerDialog", V2Info);
    }

    [Fact] public void References_InputSourcesOnly_ScenesNotProjected()
    {
        var seg = Segment(V2Info, "InputReferences");
        Assert.Contains("UploadedCharacterUrl", seg);
        Assert.Contains("SourceImageUrl", seg);
        Assert.DoesNotContain("StaticImageUrl", seg);
    }

    [Fact] public void References_MutationsUseCanonicalOverrides()
    {
        // All reference mutations funnel through the safe update helper with overrides.
        Assert.Contains("RVideoJobUpdateHelper.BuildSafeUpdate", V2Info);
        Assert.Contains("settingsOverride", V2Info);
        Assert.Contains("sourceImageUrlOverride", V2Info);
        Assert.Contains("UploadReferenceAsync", V2Info);
        Assert.Contains("RemoveReferenceAsync", V2Info);
        Assert.Contains("BuildReferenceSettings", V2Info);
    }

    [Fact] public void References_UploadPersistsViaMediaService()
    {
        var seg = Segment(V2Info, "UploadReferenceAsync");
        Assert.Contains("MediaFiles.SaveAsync", seg);
        Assert.Contains("rvideo_character", seg);
    }

    [Fact] public void References_CharacterSnapshotShape()
    {
        // Library snapshot must carry the canonical identity fields.
        var seg = Segment(V2Info, "OpenCharacterPickerAsync");
        Assert.Contains("masterImageUrl", seg);
        Assert.Contains("storageKey", seg);
        Assert.Contains("LIBRARY", V2Info);
    }

    [Fact] public void References_SingleCharacterLimit()
    {
        Assert.Contains("MaxCharacterReferences = 1", V2Info);
        Assert.Contains("MaxSelectable", V2Info);
    }

    [Fact] public void References_PerCardRemoveButton()
    {
        Assert.Contains("RemoveReferenceAsync", V2Info);
        Assert.Contains("Gỡ tham chiếu", V2Info);
        Assert.Contains("rv2-ref-foot", V2Info);
        Assert.Contains("rv2-ref-kind", V2Info);
    }

    [Fact] public void References_SingleSlot_NeverDuplicatesSameImage()
    {
        // RVID-UI-V2-PHASE3A.1: the projection produces at most ONE card (Upload XOR Character) so the
        // same physical image can never appear twice (upload mode previously added both an Upload and
        // a Character card). The character slot returns early, before the project-upload fallback.
        var seg = Segment(V2Info, "InputReferences");
        // Character slot wins and returns a single item.
        Assert.Contains("return list;", seg);
        // Exactly one character slot; scenes are still excluded.
        Assert.Contains("CharacterMode", seg);
        Assert.DoesNotContain("StaticImageUrl", seg);
    }

    [Fact] public void References_ActiveSource_MatchesProjectionSource()
    {
        // Both the highlight and the projection derive the source from the same canonical character
        // slot, guaranteeing the highlighted icon always matches the (single) projected card.
        var active = Segment(V2Info, "ActiveReferenceSource");
        Assert.Contains("RVideoCharacterModes.Upload", active);
        Assert.Contains("return \"upload\"", active);
        var refs = Segment(V2Info, "InputReferences");
        Assert.Contains("isUploadMode", refs);
        Assert.Contains("\"upload\"", refs);
        Assert.Contains("\"character\"", refs);
    }

    // ---------------------------------------------------------------
    // (g) Canonical raw user source recovery (no wrapper/instruction leak)
    // ---------------------------------------------------------------

    [Fact] public void Prompt_RawSource_UsesVerifiedInverse()
    {
        // The persisted user_input holds the assembled agent input; V2 must recover the raw source with
        // the verified inverse (PromptAssistantEndpoints.ExtractUserInput) exactly like the legacy route.
        Assert.Contains("ResolveRawUserInput", V2Info);
        Assert.Contains("PromptAssistantEndpoints.ExtractUserInput", V2Info);
        Assert.Contains("CONTENT MODE:", V2Info);
    }

    [Fact] public void Prompt_HydrateReadsRawSource_NotStoredWrapper()
    {
        // All three hydrate paths (active generation, post-generate confirm, history apply) route through
        // the raw-source helper; none binds ag.UserInput directly to the textarea.
        var hydrate = Segment(V2Info, "HydratePromptFromActiveAsync");
        Assert.Contains("ResolveRawUserInput(ag.UserInput)", hydrate);
        Assert.DoesNotContain("_workingInput = ag.UserInput", hydrate);
        var history = Segment(V2Info, "HandleHistoryAsync");
        Assert.Contains("ResolveRawUserInput(last.UserInput)", history);
    }

    [Fact] public void Prompt_ResolveHelper_EmptyStateOnUnrecoverableWrapper()
    {
        // A recognized wrapper that cannot be unwrapped yields the fallback (honest empty state),
        // never the wrapper text; a non-wrapper value is returned verbatim (no user content stripped).
        var seg = Segment(V2Info, "ResolveRawUserInput");
        Assert.Contains("StartsWith(\"CONTENT MODE:\"", seg);
        Assert.Contains("return fallback;", seg);
        Assert.Contains("return stored;", seg);
    }

    [Fact] public void Prompt_NoHeuristicJsonStripping()
    {
        // No generic regex / "looks like JSON" heuristics may strip user content.
        Assert.DoesNotContain("LooksLikeJson", V2Info);
        Assert.DoesNotContain("IsLikelyJson", V2Info);
    }
// ---------------------------------------------------------------
    // (e) Character picker dialog contract
    // ---------------------------------------------------------------

    [Fact] public void Picker_VerticalMultiSelect()
    {
        Assert.Contains("rv2-char-dialog-body", Picker);
        Assert.Contains("HashSet<long> _selectedIds", Picker);
        Assert.Contains("Toggle", Picker);
    }

    [Fact] public void Picker_CancelDoesNotMutate()
    {
        var seg = Segment(Picker, "Cancel");
        Assert.Contains("MudDialog.Cancel()", seg);
        Assert.DoesNotContain("DialogResult.Ok", seg);
    }

    [Fact] public void Picker_ConfirmReturnsSelection()
    {
        Assert.Contains("DialogResult.Ok(_selectedIds.ToList())", Picker);
    }

    [Fact] public void Picker_EnforcesMaxSelectable()
    {
        Assert.Contains("MaxSelectable", Picker);
        Assert.Contains("CanConfirm", Picker);
        Assert.Contains("_selectedIds.Count <= m", Picker);
    }

    [Fact] public void Picker_LoadsActiveCharacters()
    {
        Assert.Contains("Characters.GetActiveCharactersAsync", Picker);
        Assert.Contains("ActiveCharacterDto", Picker);
    }

    [Fact] public void Picker_HasScrollableBodyCss()
    {
        Assert.Contains("overflow-y:auto", PickerCss);
        Assert.Contains(".rv2-char-row", PickerCss);
    }

    // ---------------------------------------------------------------
    // (f) Layout + new CSS classes exist
    // ---------------------------------------------------------------

    [Fact] public void Layout_ResponsiveGrid()
    {
        Assert.Contains(".rv2-info-grid{display:grid", InfoCss);
        Assert.Contains("@media(max-width:900px)", InfoCss);
    }

    [Fact] public void Css_NewReferenceClassesPresent()
    {
        Assert.Contains(".rv2-section-head", InfoCss);
        Assert.Contains(".rv2-ref-btn", InfoCss);
        Assert.Contains(".rv2-ref-btn--active", InfoCss);
        Assert.Contains(".rv2-ref-file", InfoCss);
        Assert.Contains(".rv2-ref-foot", InfoCss);
        Assert.Contains(".rv2-ref-kind", InfoCss);
        // Native file input is fully hidden (off-screen positioning) so it can never show filename text.
        Assert.Contains("left:-9999px", InfoCss);
        Assert.DoesNotContain(".rv2-upload-wrap", InfoCss);
    }

    /// <summary>
    /// Returns the body of the member whose declaration contains <paramref name="memberName"/>.
    /// Anchors to the nearest preceding member-declaration boundary so markup references
    /// (e.g. OnClick="HandleHistoryAsync") are not mistaken for the definition.
    /// </summary>
    private static string Segment(string source, string memberName)
    {
        var declRegex = new Regex(@"(?m)^[ \t]*(?:private|protected|public|internal)\b[^\n]*");
        Match? target = null;
        foreach (Match dm in declRegex.Matches(source))
        {
            if (Regex.IsMatch(dm.Value, @"\b" + Regex.Escape(memberName) + @"\b")) { target = dm; break; }
        }
        Assert.NotNull(target);
        var start = target!.Index;
        var slice = source.Substring(start);
        // end at the next declaration line after this one
        var next = Regex.Match(slice.Substring(target.Length), @"(?m)^[ \t]*(?:private|protected|public|internal)\b[^\n]*");
        return next.Success ? slice.Substring(0, target.Length + next.Index) : slice;
    }
}