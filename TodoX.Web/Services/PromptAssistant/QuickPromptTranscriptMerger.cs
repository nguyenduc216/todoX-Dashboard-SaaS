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
        var trimmedTranscript = transcript.Trim();
        if (string.IsNullOrEmpty(trimmedTranscript))
        {
            return existing ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return trimmedTranscript;
        }

        var trimmedExisting = existing.TrimEnd();
        var needsSeparator = trimmedExisting.Length > 0
            && !char.IsWhiteSpace(trimmedExisting[^1]);
        return needsSeparator
            ? $"{trimmedExisting} {trimmedTranscript}"
            : $"{trimmedExisting}{trimmedTranscript}";
    }
}
