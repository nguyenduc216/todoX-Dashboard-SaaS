using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// New keys are long tokens: <c>tdxv_</c> + 48 lowercase base32 characters (240 random bits), meant to be
/// copied rather than typed. Keys issued earlier in the short form TDXV-ABCDE-FGHJK-MNPQR-STVWX stay valid.
/// Only the SHA-256 of the normalized key is stored; the full key is shown once at issue time.
/// </summary>
public static partial class EditVideoLicenseKeys
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string Prefix = "TDXV";
    private const string TokenPrefix = "tdxv_";
    private const int TokenLength = 48;

    public static string Generate()
    {
        var chars = new char[TokenLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = char.ToLowerInvariant(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }

        return TokenPrefix + new string(chars);
    }

    /// <summary>
    /// Token keys: trimmed and lowercased. Legacy short keys: uppercased, spaces/dashes dropped, look-alike
    /// characters mapped (O→0, I/L→1) and canonical dashes re-inserted. Returns null when the shape is wrong.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = trimmed.ToLowerInvariant();
            return TokenPattern().IsMatch(token) ? token : null;
        }

        var compact = new StringBuilder(raw.Length);
        foreach (var ch in raw.ToUpperInvariant())
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                continue;
            }

            compact.Append(ch);
        }

        var value = compact.ToString();
        if (value.Length != 24 || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = new StringBuilder(20);
        foreach (var ch in value[Prefix.Length..])
        {
            body.Append(ch switch { 'O' => '0', 'I' or 'L' => '1', _ => ch });
        }

        var b = body.ToString();
        var formatted = $"{Prefix}-{b[..5]}-{b[5..10]}-{b[10..15]}-{b[15..]}";
        return KeyPattern().IsMatch(formatted) ? formatted : null;
    }

    public static string Hash(string normalizedKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedKey))).ToLowerInvariant();

    /// <summary>Displayable prefix, e.g. "tdxv_3k9qm2" or "TDXV-ABCDE".</summary>
    public static string DisplayPrefix(string normalizedKey) =>
        normalizedKey.StartsWith(TokenPrefix, StringComparison.Ordinal) ? normalizedKey[..11] : normalizedKey[..10];

    public static bool IsValidDeviceHash(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DeviceHashPattern().IsMatch(value);

    [GeneratedRegex("^TDXV(-[0-9A-HJKMNP-TV-Z]{5}){4}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^tdxv_[0-9a-hjkmnp-tv-z]{48}$")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex DeviceHashPattern();
}
