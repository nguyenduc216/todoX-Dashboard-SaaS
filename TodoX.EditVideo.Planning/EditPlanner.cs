using System.Text.RegularExpressions;

namespace TodoX.EditVideo.Planning;

/// <summary>
/// Turns raw clips + a short brief into an edit decision list. This is TodoX's editing know-how and lives in
/// server code on purpose: the customer's tool only sends clip metadata and receives the finished plan.
/// </summary>
public static partial class EditPlanner
{
    public const int Fps = 30;
    public const double Transition = 0.5;
    public const double IntroSeconds = 2.5;
    public const double OutroSeconds = 2.0;
    public const double MinClipSeconds = 1.6;
    public const double MaxClipSeconds = 7.0;

    public static (int Width, int Height) Resolution(string aspect) => aspect.Replace(" ", "") switch
    {
        "16:9" => (1920, 1080),
        "1:1" => (1080, 1080),
        "4:5" => (1080, 1350),
        _ => (1080, 1920)
    };

    public static AutoEditPlan Plan(PlanRequest req)
    {
        var usable = req.Clips.Where(c => c.HasVideo && c.DurationSeconds >= 1.0).ToList();
        if (usable.Count == 0)
        {
            throw new ArgumentException("Không có clip video nào dùng được (cần ít nhất 1 clip dài trên 1 giây).");
        }

        var (width, height) = Resolution(req.Aspect ?? "9:16");
        var wanted = req.TargetSeconds ?? 30;
        var target = Math.Clamp(req.VoiceoverSeconds is > 0 ? Math.Max(wanted, req.VoiceoverSeconds.Value + 1) : wanted, 5, 600);
        var intro = string.IsNullOrWhiteSpace(req.Title) ? null : new PlanIntro(req.Title.Trim(), req.Subtitle?.Trim() ?? string.Empty, IntroSeconds);
        var outro = string.IsNullOrWhiteSpace(req.OutroText) ? null : new PlanOutro(req.OutroText.Trim(), OutroSeconds);
        double BudgetFor(int clipCount) =>
            target - (intro?.Seconds ?? 0) - (outro?.Seconds ?? 0)
            + Math.Max(0, clipCount + (intro is null ? 0 : 1) + (outro is null ? 0 : 1) - 1) * Transition;

        // Too many clips for the target: keep an evenly spaced subset so every shot stays watchable.
        var maxClips = Math.Max(1, (int)Math.Floor((target + Transition) / (MinClipSeconds + 0.0001)));
        if (usable.Count > maxClips)
        {
            usable = Enumerable.Range(0, maxClips).Select(i => usable[(int)Math.Round(i * (usable.Count - 1) / (double)Math.Max(1, maxClips - 1))]).Distinct().ToList();
        }

        // Not enough footage for the target (typically a long voiceover): replay the clips rather than cut the voice.
        var source = usable.ToList();
        while (usable.Sum(c => c.DurationSeconds) < BudgetFor(usable.Count) && usable.Count < source.Count * 4)
        {
            usable.AddRange(source);
        }

        var sceneCount = usable.Count + (intro is null ? 0 : 1) + (outro is null ? 0 : 1);
        var clipBudget = BudgetFor(usable.Count);
        var lengths = Allocate(usable.Select(c => c.DurationSeconds).ToList(), Math.Max(clipBudget, usable.Count * MinClipSeconds));

        var hasBedAudio = req.HasMusic || req.HasVoiceover;
        var clips = usable.Select((c, i) =>
        {
            var len = lengths[i];
            // Skip the shaky first moments; centre the cut slightly early in the shot.
            var from = Math.Max(0, Math.Min(c.DurationSeconds - len, (c.DurationSeconds - len) * 0.4));
            return new PlanClip(c.Id, Math.Round(from, 3), Math.Round(len, 3), KeepAudio: !hasBedAudio && c.HasAudio);
        }).ToList();

        var style = StyleCatalog.Resolve(req.StyleId);
        var accent = IsHexColor(req.AccentColor) ? req.AccentColor!.Trim() : style.Accent;
        var plan = new AutoEditPlan(width, height, Fps, clips, Transition, intro, outro, [], null, null, accent, style);
        var captions = BuildCaptions(req.Script, intro is null ? 0 : intro.Seconds - Transition, plan.TotalSeconds - (outro is null ? 0 : outro.Seconds - Transition));

        return plan with
        {
            Captions = captions,
            Voiceover = req.HasVoiceover ? new PlanAudio("voiceover", 1.0) : null,
            Music = req.HasMusic ? new PlanAudio("music", 0.6) : null
        };
    }

    /// <summary>
    /// Shares the budget across clips: first capped at a per-shot maximum so no single shot drags, then — only
    /// if the budget still is not met — letting shots run up to their full length.
    /// </summary>
    internal static List<double> Allocate(IReadOnlyList<double> available, double budget)
    {
        var first = Fill(available.Select(a => Math.Min(a, MaxClipSeconds)).ToList(), new double[available.Count], budget);
        var short_ = budget - first.Sum();
        return short_ > 0.01
            ? Fill(available.ToList(), first, budget).Select(r => Math.Round(r, 3)).ToList()
            : first.Select(r => Math.Round(r, 3)).ToList();
    }

    private static double[] Fill(IReadOnlyList<double> caps, double[] start, double budget)
    {
        var result = (double[])start.Clone();
        var open = Enumerable.Range(0, caps.Count).ToList();
        var remaining = budget - result.Sum();
        while (open.Count > 0 && remaining > 0.001)
        {
            var share = remaining / open.Count;
            var capped = open.Where(i => caps[i] - result[i] <= share).ToList();
            if (capped.Count == 0)
            {
                foreach (var i in open)
                {
                    result[i] += share;
                }

                break;
            }

            foreach (var i in capped)
            {
                remaining -= caps[i] - result[i];
                result[i] = caps[i];
                open.Remove(i);
            }
        }

        return result;
    }

    /// <summary>Splits the script into short on-screen lines timed by their length across [start, end).</summary>
    internal static IReadOnlyList<PlanCaption> BuildCaptions(string? script, double start, double end)
    {
        if (string.IsNullOrWhiteSpace(script) || end - start < 1)
        {
            return [];
        }

        var lines = new List<string>();
        foreach (var sentence in SentenceSplit().Split(script.Trim()).Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i += 8)
            {
                var chunk = words.Skip(i).Take(8).ToList();
                // Avoid a dangling one- or two-word last line: fold it into the previous one.
                if (chunk.Count <= 2 && i > 0 && lines.Count > 0)
                {
                    lines[^1] += " " + string.Join(' ', chunk);
                }
                else
                {
                    lines.Add(string.Join(' ', chunk));
                }
            }
        }

        var totalChars = lines.Sum(l => l.Length);
        var span = end - start;
        var t = start;
        var result = new List<PlanCaption>();
        foreach (var line in lines)
        {
            var len = span * line.Length / totalChars;
            result.Add(new PlanCaption(line, Math.Round(t, 3), Math.Round(t + len, 3)));
            t += len;
        }

        return result;
    }

    private static bool IsHexColor(string? v) => v is not null && HexColor().IsMatch(v.Trim());

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    [GeneratedRegex(@"(?<=[\.!\?…;])\s+|\r?\n+")]
    private static partial Regex SentenceSplit();
}
