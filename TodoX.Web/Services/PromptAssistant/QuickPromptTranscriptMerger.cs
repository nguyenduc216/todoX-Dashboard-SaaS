namespace TodoX.Web.Services.PromptAssistant;

/// <summary>
/// Append semantics for browser speech-to-text transcripts (RVID-UI-002 4.G):
/// transcripts are appended to existing user input instead of overwriting it.
/// Pure logic so it can be unit tested without a browser.
/// </summary>
public static class QuickPromptTranscriptMerger
{
    /// <summary>
    /// Appends <paramref name="transcript"/> to <paramref name="existing"/>.
    /// Empty existing content receives the transcript as-is; otherwise a
    /// single space separates the existing text and the transcript.
    /// </summary>
    public static string AppendTranscript(string? existing, string transcript)
    {
        // Spoken punctuation ("dấu phẩy", "chấm hỏi"...) becomes the mark itself.
        var trimmedTranscript = SpokenPunctuation.Apply(transcript).Trim(' ', '\t');
        if (string.IsNullOrEmpty(trimmedTranscript))
        {
            return existing ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return SpokenPunctuation.CapitalizeFirst(trimmedTranscript);
        }

        var trimmedExisting = existing.TrimEnd(' ', '\t');
        if (SpokenPunctuation.EndsSentence(trimmedExisting))
        {
            trimmedTranscript = SpokenPunctuation.CapitalizeFirst(trimmedTranscript);
        }
        // A piece that starts with a mark (",", "?", new line...) attaches without a space.
        var startsWithMark = trimmedTranscript[0] is ',' or '.' or ';' or ':' or '!' or '?' or ')' or '\n' or '…';
        var needsSeparator = trimmedExisting.Length > 0
            && !char.IsWhiteSpace(trimmedExisting[^1])
            && !startsWithMark;
        return needsSeparator
            ? $"{trimmedExisting} {trimmedTranscript}"
            : $"{trimmedExisting}{trimmedTranscript}";
    }
}
