using TodoX.ReupVideo.TikTok;

namespace TodoX.ReupVideo.Core;

/// <summary>Builds the Facebook post text from the TikTok text and hashtags.</summary>
public static class CaptionBuilder
{
    public const int FacebookMaxLength = 5000;

    public static string Build(string template, TikTokVideoInfo info, string extraHashtags)
    {
        var description = info.Description.Trim();
        var inText = TikTokLinks.ExtractHashtags(description);

        // Tags yt-dlp found outside the text are added, so no hashtag from TikTok is lost.
        var missing = info.Tags.Where(t => !inText.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        var caption = missing.Count == 0 ? description : $"{description}\n\n{Join(missing)}".Trim();

        var allTags = inText.Concat(missing).ToList();
        var extras = TikTokLinks.ExtractHashtags(NormalizeExtra(extraHashtags))
            .Where(t => !allTags.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var text = (string.IsNullOrWhiteSpace(template) ? "{caption}" : template)
            .Replace("{caption}", caption)
            .Replace("{text}", TikTokLinks.StripHashtags(description))
            .Replace("{hashtags}", Join(allTags))
            .Replace("{author}", string.IsNullOrEmpty(info.Author) ? "" : "@" + info.Author)
            .Replace("{link}", info.Url)
            .Trim();

        if (extras.Count > 0)
        {
            text = $"{text}\n{Join(extras)}".Trim();
        }

        return text.Length > FacebookMaxLength ? text[..FacebookMaxLength] : text;
    }

    private static string Join(IEnumerable<string> tags) => string.Join(' ', tags.Select(t => "#" + t));

    /// <summary>"abc, #xyz def" → "#abc #xyz #def".</summary>
    private static string NormalizeExtra(string extra) =>
        string.Join(' ', (extra ?? "").Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => "#" + t.TrimStart('#')));
}
