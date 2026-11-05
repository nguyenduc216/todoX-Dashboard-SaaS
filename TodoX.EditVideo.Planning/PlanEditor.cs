namespace TodoX.EditVideo.Planning;

/// <summary>One step-by-step change to an existing plan, as asked by the assistant through a todox_* tool.</summary>
public sealed record PlanSceneInput(string Clip, double? From = null, double? Duration = null);

public sealed record PlanEdit(
    string Op,
    // cut
    int? Scene = null,
    double? From = null,
    double? Duration = null,
    // arrange
    IReadOnlyList<PlanSceneInput>? Scenes = null,
    string? Transition = null,
    double? TransitionSeconds = null,
    // captions
    string? Script = null,
    IReadOnlyList<PlanCaption>? Captions = null,
    string? CaptionStyle = null,
    // titles ("" removes)
    string? Title = null,
    string? Subtitle = null,
    string? OutroText = null,
    // audio
    bool? Music = null,
    double? MusicVolume = null,
    bool? Voiceover = null,
    double? VoiceoverVolume = null,
    bool? KeepClipAudio = null,
    // look
    string? StyleId = null,
    string? AccentColor = null);

/// <summary>The current signed plan plus the clip metadata it was built from (still only ids and durations).</summary>
public sealed record PlanEditRequest(string Document, IReadOnlyList<ClipInput> Clips, PlanEdit Edit);

/// <summary>
/// Applies one edit to a plan the server issued earlier. Every rule (lengths, caption timing, looks) is checked
/// here, so the tool can only render what TodoX re-signs.
/// </summary>
public static class PlanEditor
{
    public const double MinSceneSeconds = 0.5;
    public const double MaxTotalSeconds = 600;
    public const int MaxScenes = 300;
    public static readonly string[] CaptionStyles = ["karaoke", "box", "pop", "plain"];
    public static readonly string[] Transitions = ["fade", "slide", "mix"];

    public static AutoEditPlan Apply(AutoEditPlan plan, IReadOnlyList<ClipInput> clips, PlanEdit edit)
    {
        var known = clips.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var result = edit.Op?.Trim().ToLowerInvariant() switch
        {
            "cut" => Cut(plan, known, edit),
            "arrange" => Arrange(plan, known, edit),
            "captions" => SetCaptions(plan, edit),
            "titles" => SetTitles(plan, edit),
            "audio" => SetAudio(plan, known, edit),
            "look" => SetLook(plan, edit),
            _ => throw new ArgumentException("Thao tác không hợp lệ. Dùng: cut, arrange, captions, titles, audio, look.")
        };

        if (result.TotalSeconds > MaxTotalSeconds)
        {
            throw new ArgumentException($"Video dài tối đa {MaxTotalSeconds / 60:0} phút.");
        }

        // Scenes moved or shortened: keep only captions that still fit inside the video.
        var end = result.TotalSeconds;
        return result with
        {
            Captions = result.Captions.Where(c => c.Start < end).Select(c => c with { End = Math.Min(c.End, end) }).ToList()
        };
    }

    private static AutoEditPlan Cut(AutoEditPlan plan, IReadOnlyDictionary<string, ClipInput> known, PlanEdit edit)
    {
        var index = (edit.Scene ?? 0) - 1;
        if (index < 0 || index >= plan.Clips.Count)
        {
            throw new ArgumentException($"Cảnh {edit.Scene} không có. Video có {plan.Clips.Count} cảnh (đánh số từ 1).");
        }

        var scene = plan.Clips[index];
        var clips = plan.Clips.ToList();
        clips[index] = Trim(scene with { From = edit.From ?? scene.From, Duration = edit.Duration ?? scene.Duration }, known);
        return plan with { Clips = clips };
    }

    private static AutoEditPlan Arrange(AutoEditPlan plan, IReadOnlyDictionary<string, ClipInput> known, PlanEdit edit)
    {
        var clips = plan.Clips;
        if (edit.Scenes is { } scenes)
        {
            if (scenes.Count is 0 or > MaxScenes)
            {
                throw new ArgumentException($"Cần từ 1 đến {MaxScenes} cảnh.");
            }

            // Same rule as the first plan: clip sound only when there is no music or voiceover over it.
            var keepAudio = plan.Voiceover is null && plan.Music is null;
            clips = scenes.Select(s =>
            {
                var source = Source(known, s.Clip);
                var length = Math.Min(s.Duration ?? Math.Min(source.DurationSeconds, 4), source.DurationSeconds);
                var from = s.From ?? Math.Max(0, (source.DurationSeconds - length) * 0.4);
                return Trim(new PlanClip(source.Id, from, length, keepAudio && source.HasAudio), known);
            }).ToList();
        }

        var style = plan.Style;
        if (edit.Transition is { } transition)
        {
            style = style with { Transition = OneOf(transition, Transitions, "Kiểu chuyển cảnh") };
        }

        var seconds = edit.TransitionSeconds is { } t ? Math.Round(Math.Clamp(t, 0, 1.5), 3) : plan.TransitionSeconds;
        var shortest = clips.Min(c => c.Duration);
        if (seconds > 0 && shortest <= seconds * 1.5)
        {
            seconds = Math.Round(shortest / 2, 3);
        }

        return plan with { Clips = clips, Style = style, TransitionSeconds = seconds };
    }

    private static AutoEditPlan SetCaptions(AutoEditPlan plan, PlanEdit edit)
    {
        var style = edit.CaptionStyle is { } cs ? plan.Style with { CaptionStyle = OneOf(cs, CaptionStyles, "Kiểu caption") } : plan.Style;
        var captions = plan.Captions;
        if (edit.Captions is { } given)
        {
            if (given.Count > MaxScenes)
            {
                throw new ArgumentException($"Tối đa {MaxScenes} dòng caption.");
            }

            captions = given
                .Where(c => !string.IsNullOrWhiteSpace(c.Text))
                .Select(c =>
                {
                    if (c.Start < 0 || c.End <= c.Start)
                    {
                        throw new ArgumentException($"Caption \"{c.Text}\": thời gian không hợp lệ ({c.Start}–{c.End}).");
                    }

                    var text = c.Text.Trim();
                    return new PlanCaption(text.Length > 200 ? text[..200] : text, Math.Round(c.Start, 3), Math.Round(c.End, 3));
                })
                .OrderBy(c => c.Start)
                .ToList();
        }
        else if (edit.Script is not null)
        {
            var script = edit.Script.Trim();
            captions = EditPlanner.BuildCaptions(script.Length > 6000 ? script[..6000] : script,
                plan.Intro is null ? 0 : plan.Intro.Seconds - plan.TransitionSeconds,
                plan.TotalSeconds - (plan.Outro is null ? 0 : plan.Outro.Seconds - plan.TransitionSeconds));
        }

        return plan with { Captions = captions, Style = style };
    }

    private static AutoEditPlan SetTitles(AutoEditPlan plan, PlanEdit edit)
    {
        var intro = plan.Intro;
        if (edit.Title is not null || edit.Subtitle is not null)
        {
            var title = Limit(edit.Title ?? intro?.Title, 80);
            intro = string.IsNullOrEmpty(title)
                ? null
                : new PlanIntro(title, Limit(edit.Subtitle ?? intro?.Subtitle, 120) ?? string.Empty, intro?.Seconds ?? EditPlanner.IntroSeconds);
        }

        var outro = plan.Outro;
        if (edit.OutroText is not null)
        {
            var text = Limit(edit.OutroText, 120);
            outro = string.IsNullOrEmpty(text) ? null : new PlanOutro(text, outro?.Seconds ?? EditPlanner.OutroSeconds);
        }

        return plan with { Intro = intro, Outro = outro };
    }

    private static AutoEditPlan SetAudio(AutoEditPlan plan, IReadOnlyDictionary<string, ClipInput> known, PlanEdit edit)
    {
        var music = edit.Music switch
        {
            true => new PlanAudio("music", Volume(edit.MusicVolume ?? plan.Music?.Volume ?? 0.6)),
            false => null,
            null => plan.Music is null ? null : plan.Music with { Volume = Volume(edit.MusicVolume ?? plan.Music.Volume) }
        };
        var voice = edit.Voiceover switch
        {
            true => new PlanAudio("voiceover", Volume(edit.VoiceoverVolume ?? plan.Voiceover?.Volume ?? 1.0)),
            false => null,
            null => plan.Voiceover is null ? null : plan.Voiceover with { Volume = Volume(edit.VoiceoverVolume ?? plan.Voiceover.Volume) }
        };
        // Clip sound defaults to off under music/voice, on otherwise; an explicit choice wins.
        var keep = edit.KeepClipAudio ?? (music is null && voice is null);
        var clips = plan.Clips.Select(c => c with { KeepAudio = keep && (known.GetValueOrDefault(c.Src)?.HasAudio ?? false) }).ToList();
        return plan with { Music = music, Voiceover = voice, Clips = clips };
    }

    private static AutoEditPlan SetLook(AutoEditPlan plan, PlanEdit edit)
    {
        var style = plan.Style;
        var accent = plan.AccentColor;
        if (!string.IsNullOrWhiteSpace(edit.StyleId))
        {
            var picked = StyleCatalog.All.FirstOrDefault(s => string.Equals(s.Id, edit.StyleId.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Không có style \"{edit.StyleId}\". Có: {string.Join(", ", StyleCatalog.All.Select(s => s.Id))}.");
            style = picked;
            accent = picked.Accent;
        }

        if (!string.IsNullOrWhiteSpace(edit.AccentColor))
        {
            accent = System.Text.RegularExpressions.Regex.IsMatch(edit.AccentColor.Trim(), "^#[0-9A-Fa-f]{6}$")
                ? edit.AccentColor.Trim()
                : throw new ArgumentException("Màu nhấn phải ở dạng #RRGGBB.");
        }

        return plan with { Style = style, AccentColor = accent };
    }

    private static ClipInput Source(IReadOnlyDictionary<string, ClipInput> known, string id) =>
        known.TryGetValue(id ?? string.Empty, out var c) && c.HasVideo
            ? c
            : throw new ArgumentException($"Clip \"{id}\" không có trong danh sách tư liệu của video này.");

    private static PlanClip Trim(PlanClip clip, IReadOnlyDictionary<string, ClipInput> known)
    {
        var source = Source(known, clip.Src);
        if (clip.From < 0 || clip.From >= source.DurationSeconds)
        {
            throw new ArgumentException($"Điểm bắt đầu {clip.From:0.##}s nằm ngoài clip (dài {source.DurationSeconds:0.##}s).");
        }

        var duration = Math.Min(clip.Duration, source.DurationSeconds - clip.From);
        if (duration < MinSceneSeconds)
        {
            throw new ArgumentException($"Mỗi cảnh cần dài ít nhất {MinSceneSeconds}s.");
        }

        return clip with { From = Math.Round(clip.From, 3), Duration = Math.Round(duration, 3) };
    }

    private static string OneOf(string value, string[] allowed, string what) =>
        allowed.FirstOrDefault(a => string.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"{what} \"{value}\" không có. Dùng: {string.Join(", ", allowed)}.");

    private static double Volume(double v) => Math.Round(Math.Clamp(v, 0, 1.5), 2);

    private static string? Limit(string? value, int max) =>
        value is null ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
