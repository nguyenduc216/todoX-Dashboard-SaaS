using System.Text;
using System.Text.RegularExpressions;

namespace TodoX.Web.Services.PromptAssistant;

/// <summary>
/// Turns Vietnamese spoken punctuation in a speech-to-text transcript into the marks themselves
/// ("dấu phẩy" → ",", "chấm hỏi" → "?", "xuống dòng" → new line), tidies the spacing around them and
/// capitalizes the first letter of each sentence. Bare words that are also ordinary Vietnamese
/// ("chấm", "hỏi") are only converted in unambiguous phrases, so normal speech is left alone.
/// </summary>
public static class SpokenPunctuation
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Longest phrases first so "dấu chấm phẩy" never becomes ". phẩy".
    private static readonly (Regex Pattern, string Mark)[] Rules =
    {
        (Word("dấu chấm phẩy|chấm phẩy"), ";"),
        (Word("dấu chấm hỏi|chấm hỏi|dấu hỏi chấm|dấu hỏi"), "?"),
        (Word("dấu chấm than|chấm than|dấu than"), "!"),
        (Word("dấu hai chấm"), ":"),
        (Word("dấu ba chấm|ba chấm"), "..."),
        (Word("mở ngoặc kép|đóng ngoặc kép"), "\""),
        (Word("mở ngoặc"), "("),
        (Word("đóng ngoặc"), ")"),
        (Word("dấu gạch ngang"), "-"),
        (Word("xuống dòng|xuống hàng"), "\n"),
        (Word("dấu phẩy|phẩy"), ","),
        (Word("dấu chấm"), "."),
        // A lone "chấm" only counts as a full stop at the very end of what was said.
        (new Regex(@"(?<![\p{L}\p{N}])chấm\s*$", Options), "."),
    };

    private static readonly Regex SpaceBeforeMark = new(@"[ \t]+([,.;:!?)…]|\.\.\.)", Options);
    private static readonly Regex SpaceAfterOpen = new(@"\([ \t]+", Options);
    private static readonly Regex MissingSpaceAfterMark = new(@"([,;:!?)])(?=[\p{L}(])", Options);
    private static readonly Regex MissingSpaceAfterStop = new(@"(?<!\.)\.(?=\p{L})", Options);
    private static readonly Regex RepeatedMarks = new(@"([,;:])(\s*[,;:])+", Options);
    private static readonly Regex SpacesAroundNewLine = new(@"[ \t]*\n[ \t]*", Options);
    private static readonly Regex MultiSpace = new(@"[ \t]{2,}", Options);

    private static Regex Word(string alternatives)
        => new($@"(?<![\p{{L}}\p{{N}}])(?:{alternatives})(?![\p{{L}}\p{{N}}])", Options);

    public static string Apply(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return transcript ?? string.Empty;
        var text = transcript;
        foreach (var (pattern, mark) in Rules) text = pattern.Replace(text, mark);
        text = SpacesAroundNewLine.Replace(text, "\n");
        text = SpaceBeforeMark.Replace(text, "$1");
        text = SpaceAfterOpen.Replace(text, "(");
        text = RepeatedMarks.Replace(text, "$1");
        text = MissingSpaceAfterMark.Replace(text, "$1 ");
        text = MissingSpaceAfterStop.Replace(text, ". ");
        text = MultiSpace.Replace(text, " ");
        return CapitalizeSentences(text.Trim(' ', '\t'));
    }

    /// <summary>True when text ends a sentence, so the next spoken piece starts with a capital.</summary>
    public static bool EndsSentence(string? text)
    {
        var t = text?.TrimEnd(' ', '\t');
        return string.IsNullOrEmpty(t) || t[^1] is '.' or '?' or '!' or '\n' or '…';
    }

    public static string CapitalizeFirst(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLetter(text[i])) continue;
            return string.Concat(text.AsSpan(0, i), char.ToUpperInvariant(text[i]).ToString(), text.AsSpan(i + 1));
        }
        return text;
    }

    private static string CapitalizeSentences(string text)
    {
        var sb = new StringBuilder(text.Length);
        var capitalizeNext = false;
        foreach (var c in text)
        {
            if (capitalizeNext && char.IsLetter(c))
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
                continue;
            }
            if (c is '.' or '?' or '!' or '\n') capitalizeNext = true;
            else if (!char.IsWhiteSpace(c) && c != '"' && c != '(') capitalizeNext = false;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
