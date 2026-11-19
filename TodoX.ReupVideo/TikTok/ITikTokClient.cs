namespace TodoX.ReupVideo.TikTok;

/// <summary>A link found when listing a channel (cheap: no per-video request).</summary>
public sealed record TikTokLink(string VideoId, string Url, string Author, string? Description, DateTime? PublishedAt);

/// <summary>Full details of one video, read right before posting (also proves the link is still live).</summary>
public sealed record TikTokVideoInfo(
    string VideoId,
    string Url,
    string Author,
    string? AuthorName,
    string Description,
    IReadOnlyList<string> Tags,
    double? DurationSeconds,
    DateTime? PublishedAt);

public enum LinkCheck
{
    Active,
    /// <summary>Deleted, private, region/age blocked: TikTok says it cannot be watched.</summary>
    Unavailable,
    /// <summary>Network/tool problem — say nothing about the video, try again next run.</summary>
    Error
}

public sealed record TikTokCheckResult(LinkCheck State, TikTokVideoInfo? Info, string? Message);

public interface ITikTokClient
{
    Task<IReadOnlyList<TikTokLink>> ListChannelAsync(string username, int limit, CancellationToken ct);

    Task<TikTokCheckResult> CheckAsync(string url, CancellationToken ct);

    /// <summary>Downloads the video (mp4) into <paramref name="folder"/>; returns the file path.</summary>
    Task<string> DownloadAsync(string url, string videoId, string folder, CancellationToken ct);
}
