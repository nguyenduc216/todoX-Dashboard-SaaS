using System.Text.Json;
using TodoX.EditVideoTool.Licensing;

namespace TodoX.EditVideoTool.Media;

// Shape of the edit plan TodoX's server returns (TodoX.EditVideo.Planning). The exe only reads it: deciding the
// cuts, lengths and caption timing happens on the server.
public sealed record PlanClip(string Src, double From, double Duration, bool KeepAudio);
public sealed record PlanCaption(string Text, double Start, double End);
public sealed record PlanIntro(string Title, string Subtitle, double Seconds);
public sealed record PlanOutro(string Text, double Seconds);
public sealed record PlanAudio(string Src, double Volume);
public sealed record PlanStyle(string Id, string Name, string Accent, string Background, string Text, string FontFamily, string CaptionStyle, string Transition, double ZoomAmount);

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
    PlanStyle Style,
    double TotalSeconds);

public static class PlanDocument
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Verifies TodoX's signature and swaps the media ids for local paths. Returns null for anything not signed
    /// by TodoX, so a tampered or home-made plan is never rendered.
    /// </summary>
    public static AutoEditPlan? Open(string document, LicenseVerifier verifier, IReadOnlyDictionary<string, string> media)
    {
        var body = verifier.VerifyBody(document);
        if (body is null)
        {
            return null;
        }

        var plan = JsonSerializer.Deserialize<AutoEditPlan>(body, Json);
        if (plan is null)
        {
            return null;
        }

        string Local(string id) => media.TryGetValue(id, out var path) ? path : throw new InvalidOperationException($"Kế hoạch dựng tham chiếu media không có: {id}");

        return plan with
        {
            Clips = plan.Clips.Select(c => c with { Src = Local(c.Src) }).ToList(),
            Voiceover = plan.Voiceover is null ? null : plan.Voiceover with { Src = Local(plan.Voiceover.Src) },
            Music = plan.Music is null ? null : plan.Music with { Src = Local(plan.Music.Src) }
        };
    }
}
