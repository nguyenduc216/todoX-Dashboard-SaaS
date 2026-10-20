namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Pure, UI-facing dirty/baseline policy for the RVideo V2 scene re-render actions
/// (RVID-UI-V2-SCENE-RERENDER-HISTORY.1). Given the working prompt and the ACTIVE media version
/// snapshots it decides whether "Tạo lại ảnh" / "Tạo lại video" should be enabled.
///
/// Rules:
///  - Image: enabled when no image baseline exists yet (first generation) or the working image prompt
///    differs from the active image version's prompt snapshot. Undo back to baseline disables it.
///  - Video: enabled when the working video prompt differs from the active video version's prompt
///    snapshot, OR when the active image version changed since the active video was created
///    (image→video dependency), OR when no video exists yet but a source image is available.
/// Line endings are normalised (see <see cref="RVideoPromptBaseline"/>) so CRLF/LF never creates a
/// false dirty state. Draft saves must not reset the baseline — the caller always passes the ACTIVE
/// version snapshot, never the just-saved draft.
/// </summary>
public static class RVideoSceneRerenderDirtyPolicy
{
    /// <summary>True when the image re-render action should be enabled.</summary>
    public static bool CanRerenderImage(string? workingImagePrompt, string? activeImagePromptBaseline, bool hasImageBaseline)
    {
        if (RVideoPromptBaseline.IsEmpty(workingImagePrompt)) return false;
        return RVideoPromptBaseline.IsDirty(workingImagePrompt, activeImagePromptBaseline, hasImageBaseline);
    }

    /// <summary>True when the video re-render action should be enabled.</summary>
    public static bool CanRerenderVideo(
        string? workingVideoPrompt,
        string? activeVideoPromptBaseline,
        bool hasVideoBaseline,
        Guid? activeVideoSourceImageVersionId,
        Guid? activeImageVersionId,
        bool hasAnyImageSource)
    {
        if (RVideoPromptBaseline.IsEmpty(workingVideoPrompt)) return false;

        // Prompt edited against an EXISTING active video → re-render.
        if (hasVideoBaseline && RVideoPromptBaseline.IsDirty(workingVideoPrompt, activeVideoPromptBaseline, hasBaseline: true))
        {
            return true;
        }

        // Image→video dependency: an existing active video that was rendered from a different image
        // version than the one now active must be re-rendered even when the video prompt is unchanged.
        if (hasVideoBaseline && activeVideoSourceImageVersionId is not null && activeImageVersionId is not null)
        {
            return activeVideoSourceImageVersionId != activeImageVersionId;
        }

        // No active video yet: allow the first video generation only when a source image exists.
        return !hasVideoBaseline && hasAnyImageSource;
    }
}