using System.Text.RegularExpressions;

namespace TodoX.ReupVideo.TikTok;

/// <summary>Parsing of what users paste: channel names/links and single video links.</summary>
public static partial class TikTokLinks
{
    [GeneratedRegex(@"tiktok\.com/@(?<user>[A-Za-z0-9._]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileInUrl();

    [GeneratedRegex(@"^@?(?<user>[A-Za-z0-9._]{2,24})$")]
    private static partial Regex BareUsername();

    [GeneratedRegex(@"tiktok\.com/@(?<user>[A-Za-z0-9._]+)/(video|photo)/(?<id>\d{8,})", RegexOptions.IgnoreCase)]
    private static partial Regex VideoUrl();

    [GeneratedRegex(@"(?<![\w&])#(?<tag>[\p{L}\p{N}_]+)")]
    private static partial Regex Hashtag();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex Spaces();

    /// <summary>"@shop", "shop", "https://www.tiktok.com/@shop?lang=vi" → "shop"; null when it is not a channel.</summary>
    public static string? ParseUsername(string? input)
    {
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
        {
            return null;
        }

        var match = ProfileInUrl().Match(text);
        if (match.Success)
        {
            return match.Groups["user"].Value.TrimEnd('.');
        }

        match = BareUsername().Match(text);
        return match.Success ? match.Groups["user"].Value : null;
    }

    /// <summary>Full video link → (username, video id); null for short links (vt.tiktok.com) or anything else.</summary>
    public static (string User, string VideoId)? ParseVideoUrl(string? input)
    {
        var match = VideoUrl().Match(input ?? "");
        return match.Success ? (match.Groups["user"].Value, match.Groups["id"].Value) : null;
    }

    public static string VideoUrlOf(string user, string videoId) => $"https://www.tiktok.com/@{user}/video/{videoId}";

    /// <summary>Hashtags in the text, in order, without "#", without duplicates (case-insensitive).</summary>
    public static List<string> ExtractHashtags(string? text)
    {
        var tags = new List<string>();
        foreach (Match match in Hashtag().Matches(text ?? ""))
        {
            var tag = match.Groups["tag"].Value;
            if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    /// <summary>The text with every hashtag removed and spaces tidied.</summary>
    public static string StripHashtags(string? text)
    {
        var stripped = Hashtag().Replace(text ?? "", "");
        var lines = stripped.Split('\n').Select(l => Spaces().Replace(l, " ").TrimEnd());
        return string.Join('\n', lines).Trim();
    }
}
