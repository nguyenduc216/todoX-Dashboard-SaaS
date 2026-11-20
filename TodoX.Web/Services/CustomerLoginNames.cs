using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TodoX.Web.Services;

/// <summary>
/// Login of a customer added by staff: the username follows the brand name ("Kesa Spa" → "kesaspa", "Chè Yến Nha Trang"
/// → "cheyennhatrang"), with a number added when it is taken, and a random first password the staff hands over.
/// </summary>
public static class CustomerLoginNames
{
    public const int MaxLength = 30;

    public static string FromBrand(string? brand, string? fallback = null)
    {
        var name = Slug(brand);
        if (name.Length == 0) name = Slug(fallback);
        return name.Length == 0 ? "khachhang" : name;
    }

    /// <summary>First free name: base, base2, base3 … (kept within <see cref="MaxLength"/>).</summary>
    public static async Task<string> FirstFreeAsync(string baseName, Func<string, Task<bool>> taken)
    {
        if (!await taken(baseName)) return baseName;
        for (var n = 2; n < 1000; n++)
        {
            var suffix = n.ToString(CultureInfo.InvariantCulture);
            var candidate = baseName[..Math.Min(baseName.Length, MaxLength - suffix.Length)] + suffix;
            if (!await taken(candidate)) return candidate;
        }
        return baseName + RandomNumberGenerator.GetInt32(1000, 9999);
    }

    public static string NewPassword(int length = 10)
    {
        const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++) sb.Append(chars[RandomNumberGenerator.GetInt32(chars.Length)]);
        return sb.ToString();
    }

    private static string Slug(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(lower);
            if (sb.Length == MaxLength) break;
        }
        return sb.ToString();
    }
}
