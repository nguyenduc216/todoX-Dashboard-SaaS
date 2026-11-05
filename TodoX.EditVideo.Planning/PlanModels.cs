namespace TodoX.EditVideo.Planning;

/// <summary>
/// What the local tool reports about one clip. Only technical metadata: the server never sees file names,
/// paths or footage. <see cref="Id"/> is an opaque handle the tool maps back to its local file.
/// </summary>
public sealed record ClipInput(string Id, double DurationSeconds, int Width, int Height, bool HasVideo, bool HasAudio);

/// <summary>The customer's brief, as passed through by the assistant.</summary>
public sealed record PlanRequest(
    IReadOnlyList<ClipInput> Clips,
    string? Aspect = null,
    double? TargetSeconds = null,
    string? Title = null,
    string? Subtitle = null,
    string? Script = null,
    string? OutroText = null,
    bool HasMusic = false,
    bool HasVoiceover = false,
    double? VoiceoverSeconds = null,
    string? StyleId = null,
    string? AccentColor = null,
    // The TodoX skill version guiding the video; its fixed settings fill whatever the customer left unset.
    string? Skill = null,
    int? SkillVersion = null);

// The plan mirrors TodoX.EditVideoTool.Templates/src/types.ts (serialized camelCase). Media `Src` values are
// clip ids ("c0", "music", "voiceover"); the tool swaps them for local paths before rendering.
public sealed record PlanClip(string Src, double From, double Duration, bool KeepAudio);
public sealed record PlanCaption(string Text, double Start, double End);
public sealed record PlanIntro(string Title, string Subtitle, double Seconds);
public sealed record PlanOutro(string Text, double Seconds);
public sealed record PlanAudio(string Src, double Volume);

public sealed record PlanStyle(
    string Id,
    string Name,
    string Accent,
    string Background,
    string Text,
    string FontFamily,
    string CaptionStyle,
    string Transition,
    double ZoomAmount);

public sealed record AutoEditPlan(
    int Width,
    int Height,
    int Fps,
    IReadOnlyList<PlanClip> Clips,
    double TransitionSeconds,
    PlanIntro? Intro,
    PlanOutro? Outro,
    IReadOnlyList<PlanCaption> Captions,
    PlanAudio? Voiceover,
    PlanAudio? Music,
    string AccentColor,
    PlanStyle Style)
{
    public double TotalSeconds
    {
        get
        {
            var scenes = (Intro is null ? 0 : 1) + Clips.Count + (Outro is null ? 0 : 1);
            var sum = (Intro?.Seconds ?? 0) + Clips.Sum(c => c.Duration) + (Outro?.Seconds ?? 0);
            return Math.Round(sum - Math.Max(0, scenes - 1) * TransitionSeconds, 3);
        }
    }
}
