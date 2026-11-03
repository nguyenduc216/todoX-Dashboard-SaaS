using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TodoX.Web.Services.EditVideoTool;

/// <summary>
/// License keys look like TDXV-ABCDE-FGHJK-MNPQR-STVWX (Crockford base32, no I/L/O/U).
/// Only the SHA-256 of the normalized key is stored; the full key is shown once at issue time.
/// </summary>
public static partial class EditVideoLicenseKeys
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string Prefix = "TDXV";

    public static string Generate()
    {
        var groups = new string[4];
        for (var g = 0; g < groups.Length; g++)
        {
            var chars = new char[5];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }

            groups[g] = new string(chars);
        }

        return $"{Prefix}-{string.Join('-', groups)}";
    }

    /// <summary>
    /// Uppercases, drops spaces/dashes and maps look-alike characters (O→0, I/L→1) so hand-typed keys
    /// still match, then re-inserts the canonical dashes. Returns null when the shape is wrong.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
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

    /// <summary>Displayable prefix, e.g. "TDXV-ABCDE".</summary>
    public static string DisplayPrefix(string normalizedKey) => normalizedKey[..10];

    public static bool IsValidDeviceHash(string? value) =>
        !string.IsNullOrWhiteSpace(value) && DeviceHashPattern().IsMatch(value);

    [GeneratedRegex("^TDXV(-[0-9A-HJKMNP-TV-Z]{5}){4}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex DeviceHashPattern();
}
