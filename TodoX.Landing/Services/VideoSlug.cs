using System.Globalization;
using System.Text;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

/// <summary>
/// Builds readable share URLs from video titles without storing anything in the DB.
/// </summary>
public static class VideoSlug
{
    public const string IndustryRoutePrefix = "/video/";
    public const string SampleRoutePrefix = "/mau-video/";
    private const int MaxLength = 80;
    private const int IdSuffixLength = 8;

    public static string Slugify(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value.Trim().ToLowerInvariant()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder(normalized.Length);
        var pendingDash = false;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(ch);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = builder.ToString();
        if (slug.Length > MaxLength)
            slug = slug[..MaxLength].TrimEnd('-');
        return slug;
    }

    /// <summary>
    /// Returns a slug per item. A title slug is used as-is when unique;
    /// duplicates (or empty titles) get a short id suffix so each URL stays stable.
    /// </summary>
    public static Dictionary<Guid, string> Assign<T>(IReadOnlyList<T> items, Func<T, Guid> id, Func<T, string?> title)
    {
        var baseSlugs = items.Select(x => (Id: id(x), Slug: Slugify(title(x)))).ToList();
        var counts = baseSlugs.GroupBy(x => x.Slug).ToDictionary(g => g.Key, g => g.Count());
        var result = new Dictionary<Guid, string>();
        foreach (var (itemId, baseSlug) in baseSlugs)
        {
            result[itemId] = baseSlug.Length > 0 && counts[baseSlug] == 1
                ? baseSlug
                : JoinSuffix(baseSlug, IdSuffix(itemId));
        }
        return result;
    }

    /// <summary>
    /// Resolves a requested slug, tolerating links shared before a duplicate title appeared
    /// (base slug), after a title changed (id suffix), or typed with accents/upper case.
    /// </summary>
    public static T? Find<T>(IReadOnlyList<T> items, string? requested, Func<T, Guid> id, Func<T, string?> title, Func<T, string> slug)
        where T : class
    {
        var normalized = Slugify(requested);
        if (normalized.Length == 0)
            return null;

        var exact = items.FirstOrDefault(x => slug(x) == normalized);
        if (exact is not null)
            return exact;

        var dash = normalized.LastIndexOf('-');
        var suffix = dash >= 0 ? normalized[(dash + 1)..] : normalized;
        if (suffix.Length == IdSuffixLength)
        {
            var byId = items.FirstOrDefault(x => IdSuffix(id(x)) == suffix);
            if (byId is not null)
                return byId;
        }

        return items.FirstOrDefault(x => Slugify(title(x)) == normalized);
    }

    public static void AssignIndustryVideos(IEnumerable<IndustrySolution> industries)
    {
        var videos = IndustryVideos(industries);
        var slugs = Assign(videos, x => x.Video.Id, x => x.Title);
        foreach (var item in videos)
        {
            item.Video.Slug = slugs[item.Video.Id];
            item.Video.SharePath = IndustryRoutePrefix + item.Video.Slug;
        }
    }

    public static (IndustrySolutionVideo Video, IndustrySolution Industry)? FindIndustryVideo(
        IEnumerable<IndustrySolution> industries, string? requested)
    {
        var match = Find(IndustryVideos(industries), requested, x => x.Video.Id, x => x.Title, x => x.Video.Slug);
        return match is null ? null : (match.Video, match.Industry);
    }

    private static List<IndustryVideoRef> IndustryVideos(IEnumerable<IndustrySolution> industries) =>
        industries.SelectMany(i => i.Videos.Select(v => new IndustryVideoRef(v, i))).ToList();

    private sealed record IndustryVideoRef(IndustrySolutionVideo Video, IndustrySolution Industry)
    {
        public string Title => string.IsNullOrWhiteSpace(Video.Title) ? Industry.Title : Video.Title;
    }

    private static string IdSuffix(Guid id) => id.ToString("N")[..IdSuffixLength];

    private static string JoinSuffix(string slug, string suffix) =>
        slug.Length == 0 ? "video-" + suffix : slug + "-" + suffix;
}
