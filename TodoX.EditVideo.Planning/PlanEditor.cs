namespace TodoX.EditVideo.Planning;

/// <summary>One step-by-step change to an existing plan, as asked by the assistant through a todox_* tool.</summary>
public sealed record PlanSceneInput(string Clip, double? From = null, double? Duration = null, double? Zoom = null, string? Fx = null);

/// <summary>A cutaway asked for by the assistant: clip id, when it appears in the video and for how long.</summary>
public sealed record PlanOverlayInput(string Clip, double At, double Seconds, double? From = null, double? Speed = null, string? Crop = null, double? Keep = null, string? Fx = null);

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
    string? AccentColor = null,
    // headlines (empty list removes them) and logo (true = the media "logo", false = none)
    IReadOnlyList<PlanHeadline>? Headlines = null,
    bool? Logo = null,
    string? LogoCorner = null,
    // cut: punch-in of that scene and the effect on the cut into it ("none" removes it)
    double? Zoom = null,
    string? Fx = null,
    // broll: the full list of cutaways (empty list removes them)
    IReadOnlyList<PlanOverlayInput>? Overlays = null);

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
    public static readonly string[] CaptionStyles = ["karaoke", "box", "pop", "plain", "tiktok", "sparkle"];
    public static readonly string[] LogoCorners = ["top-right", "top-left", "bottom-right", "bottom-left"];
    public const int MaxHeadlines = 60;
    public static readonly string[] Transitions = ["fade", "slide", "mix"];
    public static readonly string[] Crops = ["none", "top", "bottom", "center"];
    public const int MaxOverlays = 200;
    public const double MinOverlaySeconds = 0.3;

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
            "headlines" => SetHeadlines(plan, edit),
            "logo" => SetLogo(plan, edit),
            "broll" => SetOverlays(plan, known, edit),
            _ => throw new ArgumentException("Thao tác không hợp lệ. Dùng: cut, arrange, captions, titles, audio, look, headlines, logo, broll.")
        };

        if (result.TotalSeconds > MaxTotalSeconds)
        {
            throw new ArgumentException($"Video dài tối đa {MaxTotalSeconds / 60:0} phút.");
        }

        // Scenes moved or shortened: keep only captions and headlines that still fit inside the video.
        var end = result.TotalSeconds;
        return result with
        {
            Captions = result.Captions.Where(c => c.Start < end).Select(c => c with { End = Math.Min(c.End, end) }).ToList(),
            Headlines = result.Headlines?.Where(h => h.Start < end).Select(h => h with { End = Math.Min(h.End, end) }).ToList(),
            Overlays = result.Overlays?.Where(o => o.Start < end - MinOverlaySeconds)
                .Select(o => o with { Duration = Math.Round(Math.Min(o.Duration, end - o.Start), 3) }).ToList() is { Count: > 0 } kept ? kept : null
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
        clips[index] = Trim(scene with { From = edit.From ?? scene.From, Duration = edit.Duration ?? scene.Duration, Zoom = ZoomOf(edit.Zoom ?? scene.Zoom), Fx = edit.Fx is null ? scene.Fx : FxOf(edit.Fx) }, known);
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
                return Trim(new PlanClip(source.Id, from, length, keepAudio && source.HasAudio, ZoomOf(s.Zoom ?? 0), FxOf(s.Fx)), known);
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

    private static AutoEditPlan SetHeadlines(AutoEditPlan plan, PlanEdit edit)
    {
        var given = edit.Headlines ?? [];
        if (given.Count > MaxHeadlines)
        {
            throw new ArgumentException($"Tối đa {MaxHeadlines} tiêu đề lớn.");
        }

        var headlines = given
            .Where(h => !string.IsNullOrWhiteSpace(h.Text))
            .Select(h =>
            {
                if (h.Start < 0 || h.End <= h.Start || h.End - h.Start > 30)
                {
                    throw new ArgumentException($"Tiêu đề \"{h.Text}\": thời gian không hợp lệ ({h.Start}–{h.End}, tối đa 30 giây).");
                }

                return new PlanHeadline(Limit(h.Text, 60)!, Limit(h.Sub, 80) ?? string.Empty, Math.Round(h.Start, 3), Math.Round(h.End, 3));
            })
            .OrderBy(h => h.Start)
            .ToList();
        return plan with { Headlines = headlines.Count == 0 ? null : headlines };
    }

    private static AutoEditPlan SetOverlays(AutoEditPlan plan, IReadOnlyDictionary<string, ClipInput> known, PlanEdit edit)
    {
        var given = edit.Overlays ?? [];
        if (given.Count > MaxOverlays)
        {
            throw new ArgumentException($"Tối đa {MaxOverlays} cảnh chen.");
        }

        var overlays = given.Select(o =>
        {
            var source = Source(known, o.Clip);
            var speed = o.Speed ?? 1;
            if (speed is < 0.25 or > 4)
            {
                throw new ArgumentException("Tốc độ cảnh chen từ 0.25 đến 4.");
            }

            if (o.At < 0 || o.Seconds < MinOverlaySeconds)
            {
                throw new ArgumentException($"Cảnh chen ở giây {o.At:0.##}: thời điểm phải ≥ 0 và dài ít nhất {MinOverlaySeconds}s.");
            }

            var from = o.From ?? 0;
            var used = o.Seconds * speed;
            if (from < 0 || from + used > source.DurationSeconds + 0.05)
            {
                throw new ArgumentException($"Cảnh chen từ clip {o.Clip}: đoạn {from:0.##}s + {used:0.##}s vượt quá độ dài clip ({source.DurationSeconds:0.##}s).");
            }

            var keep = o.Keep ?? (o.Crop is null or "none" ? 1 : 0.75);
            if (keep is < 0.5 or > 1)
            {
                throw new ArgumentException("Phần giữ lại khi cắt khung (keep) từ 0.5 đến 1.");
            }

            var crop = OneOf(o.Crop ?? "none", Crops, "Cắt khung");
            return new PlanOverlay(source.Id, Math.Round(from, 3), Math.Round(o.At, 3), Math.Round(o.Seconds, 3), Math.Round(speed, 3),
                crop, crop == "none" ? 1 : Math.Round(keep, 3), FxOf(o.Fx));
        }).OrderBy(o => o.Start).ToList();

        for (var i = 1; i < overlays.Count; i++)
        {
            if (overlays[i].Start < overlays[i - 1].Start + overlays[i - 1].Duration - 0.01)
            {
                throw new ArgumentException($"Hai cảnh chen chồng lên nhau ở giây {overlays[i].Start:0.##}.");
            }
        }

        return plan with { Overlays = overlays.Count == 0 ? null : overlays };
    }

    private static AutoEditPlan SetLogo(AutoEditPlan plan, PlanEdit edit)
    {
        if (edit.Logo == false)
        {
            return plan with { Logo = null };
        }

        var corner = edit.LogoCorner is { } c ? OneOf(c, LogoCorners, "Góc logo") : plan.Logo?.Corner ?? "top-right";
        return plan with { Logo = new PlanLogo("logo", corner) };
    }

    /// <summary>Effects on the cut into a scene: white blink, fast swipe with blur, blurred zoom punch, camera shake.</summary>
    public static readonly string[] CutEffects = ["flash", "whip", "zoom", "shake"];

    private static string? FxOf(string? fx) =>
        string.IsNullOrWhiteSpace(fx) || fx.Equals("none", StringComparison.OrdinalIgnoreCase) ? null
        : CutEffects.FirstOrDefault(e => e.Equals(fx.Trim(), StringComparison.OrdinalIgnoreCase))
          ?? throw new ArgumentException("Hiệu ứng chuyển cảnh: flash, whip, zoom, shake hoặc none.");

    private static double ZoomOf(double zoom) =>
        zoom is < 0 or > 0.35 ? throw new ArgumentException("Zoom (phóng to) từ 0 đến 0.35.") : Math.Round(zoom, 3);

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
