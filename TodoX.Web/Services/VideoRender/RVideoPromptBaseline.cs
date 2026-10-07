using System.Text.RegularExpressions;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Pure presentation helpers for the RVideo V2 Info prompt textarea.
/// These only compare user-typed source input against an active-generation baseline.
/// They do NOT inspect, reconstruct or classify generated JSON payloads.
/// </summary>
public static class RVideoPromptBaseline
{
    /// <summary>Normalize line endings to LF for deterministic comparison. Never trims meaningful content.</summary>
    public static string Normalize(string? value)
        => Regex.Replace(value ?? string.Empty, "\r\n|\r", "\n");

    /// <summary>Whitespace-only (or null/empty) input is treated as empty for the Generate guard.</summary>
    public static bool IsEmpty(string? value)
        => string.IsNullOrWhiteSpace(value);

    /// <summary>Deterministic ordinal comparison after CRLF/CR -> LF normalization.</summary>
    public static bool AreEqual(string? a, string? b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>
    /// True when the working input differs from an existing active baseline and is not empty.
    /// When there is no baseline yet (first generation for a brand new job) a non-empty working
    /// input is considered dirty so the very first Generate is allowed.
    /// </summary>
    public static bool IsDirty(string? working, string? baseline, bool hasBaseline)
    {
        if (IsEmpty(working)) return false;
        if (!hasBaseline) return true;
        return !AreEqual(working, baseline);
    }
}