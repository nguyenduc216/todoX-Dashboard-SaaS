using System.Globalization;
using System.Text;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

/// <summary>
/// Builds readable share URLs (/video/{slug}) from video titles without storing anything in the DB.
/// </summary>
public static class VideoSlug
{
    public const string RoutePrefix = "/video/";
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
    /// Assigns Slug/SharePath to every video. A title slug is used as-is when unique;
    /// duplicates (or empty titles) get a short id suffix so each URL stays stable.
    /// </summary>
    public static void Assign(IEnumerable<IndustrySolution> industries)
    {
        var videos = industries.SelectMany(i => i.Videos.Select(v => (Video: v, Industry: i))).ToList();
        var baseSlugs = videos.ToDictionary(
            x => x.Video,
            x => Slugify(string.IsNullOrWhiteSpace(x.Video.Title) ? x.Industry.Title : x.Video.Title));
        var counts = baseSlugs.Values.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        foreach (var (video, _) in videos)
        {
            var baseSlug = baseSlugs[video];
            var slug = baseSlug.Length > 0 && counts[baseSlug] == 1
                ? baseSlug
                : JoinSuffix(baseSlug, IdSuffix(video.Id));
            video.Slug = slug;
            video.SharePath = RoutePrefix + slug;
        }
    }

    /// <summary>
    /// Resolves a requested slug, tolerating links shared before a duplicate title appeared
    /// (base slug) or after a title changed (id suffix).
    /// </summary>
    public static (IndustrySolutionVideo Video, IndustrySolution Industry)? Find(
        IEnumerable<IndustrySolution> industries, string? requested)
    {
        // Normalizing also accepts hand-typed links such as /video/Review-Mỹ-Phẩm.
        var slug = Slugify(requested);
        if (slug.Length == 0)
            return null;

        var videos = industries.SelectMany(i => i.Videos.Select(v => (Video: v, Industry: i))).ToList();

        foreach (var item in videos)
            if (item.Video.Slug == slug)
                return item;

        var dash = slug.LastIndexOf('-');
        var suffix = dash >= 0 ? slug[(dash + 1)..] : slug;
        if (suffix.Length == IdSuffixLength)
            foreach (var item in videos)
                if (IdSuffix(item.Video.Id) == suffix)
                    return item;

        foreach (var item in videos)
            if (Slugify(string.IsNullOrWhiteSpace(item.Video.Title) ? item.Industry.Title : item.Video.Title) == slug)
                return item;

        return null;
    }

    private static string IdSuffix(Guid id) => id.ToString("N")[..IdSuffixLength];

    private static string JoinSuffix(string slug, string suffix) =>
        slug.Length == 0 ? "video-" + suffix : slug + "-" + suffix;
}
