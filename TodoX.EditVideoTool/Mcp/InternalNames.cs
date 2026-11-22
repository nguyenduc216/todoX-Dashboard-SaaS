using System.Text.RegularExpressions;

namespace TodoX.EditVideoTool.Mcp;

/// <summary>
/// TodoX keeps the programs it drives on this computer to itself: nothing the assistant (Claude / Codex) reads may
/// name them or point into their folders. Every tool result passes through <see cref="Scrub"/>; error text that
/// comes from those programs is reduced to "TodoX" wording.
/// </summary>
public static partial class InternalNames
{
    private const string Replacement = "TodoX";

    // Program names, their libraries and the render browser. "node" only in forms that cannot be ordinary words.
    [GeneratedRegex(
        @"@remotion/[\w\-/.]+|\b(?:ffmpeg|ffprobe|ffplay|remotion|whisper(?:-cli|\.cpp)?|ggml[\w\-.]*|chrome-headless-shell|headless[_ ]?shell|chromium|puppeteer|webpack|esbuild|libav\w*|libsw\w*|libx26[45]|libopenh264|lavfi|node\.exe|node_modules|node\.js|nodejs)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProgramNames();

    // Version banners and build lines those programs print ("ffmpeg version n8.1 Copyright ...", "built with gcc ...").
    [GeneratedRegex(@"^\s*(?:\S+ version \S+.*|built with .*|configuration: .*|lib\w+\s+\d+\.\s*\d+.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BannerLines();

    private static string? _componentsDir;

    /// <summary>The folder holding TodoX's components; any path inside it is shown as "TodoX".</summary>
    public static void UseComponentsDir(string? dir) => _componentsDir = string.IsNullOrWhiteSpace(dir) ? null : dir.TrimEnd('\\', '/');

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var s = text;
        if (_componentsDir is { } dir)
        {
            // Plain and JSON-escaped forms of the folder, plus whatever path follows it.
            foreach (var form in new[] { dir, dir.Replace("\\", "\\\\"), dir.Replace('\\', '/') })
            {
                s = Regex.Replace(s, Regex.Escape(form) + @"[^\s""'<>|,;)\]]*", Replacement, RegexOptions.IgnoreCase);
            }
        }

        s = BannerLines().Replace(s, string.Empty);
        return ProgramNames().Replace(s, Replacement);
    }
}
