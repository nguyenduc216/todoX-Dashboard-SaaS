using System.Globalization;
using System.Text;

namespace TodoX.Web.Services.Landing;

/// <summary>
/// Public page of each sample on todox.vn (/mau-video/{slug}), shown on the "Mẫu video" admin page so a sample can be
/// sent to a customer, hidden ones included: todox.vn plays a hidden sample from its link but does not list it.
/// Mirrors TodoX.Landing VideoSlug: the title slug when it is unique, otherwise slug + first 8 hex of the id
/// (todox.vn also resolves that form and redirects it to the canonical link).
/// </summary>
public static class LandingVideoSampleLinks
{
    public const string DefaultBaseUrl = "https://todox.vn";
    private const int MaxLength = 80;
    private const int IdSuffixLength = 8;

    public static string BaseUrl(IConfiguration? configuration)
    {
        var value = configuration?["Landing:PublicBaseUrl"];
        return (string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : value.Trim()).TrimEnd('/');
    }

    public static Dictionary<Guid, string> ShareUrls(IEnumerable<LandingVideoSample> samples, string baseUrl)
    {
        var list = samples.Select(x => (x.Id, Slug: Slugify(x.Title))).ToList();
        var counts = list.GroupBy(x => x.Slug).ToDictionary(g => g.Key, g => g.Count());
        return list.ToDictionary(x => x.Id, x =>
        {
            var slug = x.Slug.Length > 0 && counts[x.Slug] == 1
                ? x.Slug
                : (x.Slug.Length == 0 ? "video" : x.Slug) + "-" + x.Id.ToString("N")[..IdSuffixLength];
            return $"{baseUrl.TrimEnd('/')}/mau-video/{slug}";
        });
    }

    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim().ToLowerInvariant().Replace('đ', 'd').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0) builder.Append('-');
                builder.Append(ch);
                pendingDash = false;
            }
            else pendingDash = true;
        }
        var slug = builder.ToString();
        return slug.Length > MaxLength ? slug[..MaxLength].TrimEnd('-') : slug;
    }
}
