using System.Text.RegularExpressions;

namespace TodoX.Web.Services.VideoRender;

public sealed record RVideoVoiceFit(int Words, double SpeechSeconds, int NeededSeconds, bool Fits);

/// <summary>
/// Rough speaking time of a scene's dialogue, to warn when it cannot fit the scene: Vbee voices are cut to the scene
/// length when muxed, and Native speech must fit the clip. Vietnamese TTS reads about 2.5 words per second at rate 1.0.
/// </summary>
public static partial class RVideoVoiceTiming
{
    public const double WordsPerSecondAtRate1 = 2.5;
    /// <summary>Breathing room after the last word.</summary>
    public const double TailSeconds = 0.5;

    public static RVideoVoiceFit Estimate(string? text, decimal? ttsRate, int sceneSeconds)
    {
        var words = string.IsNullOrWhiteSpace(text) ? 0 : Words().Matches(text).Count;
        var rate = ttsRate is > 0 ? (double)ttsRate.Value : 1.0;
        var speech = words == 0 ? 0 : words / (WordsPerSecondAtRate1 * rate);
        var needed = words == 0 ? 0 : (int)Math.Ceiling(speech + TailSeconds);
        return new RVideoVoiceFit(words, Math.Round(speech, 1), needed, words == 0 || needed <= sceneSeconds);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Words();
}
