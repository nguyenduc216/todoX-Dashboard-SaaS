using TodoX.ReupVideo.Storage;

namespace TodoX.ReupVideo.Core;

/// <summary>
/// Which fanpages a TikTok video goes to: the ticked fanpages that are mapped to its TikTok channel
/// (Cài đặt → Kênh → Fanpage). A channel with no fanpage chosen is not posted anywhere.
/// A link added by hand whose channel is not in the list goes to every ticked fanpage.
/// </summary>
public static class ReupTargets
{
    public static TikTokSource? SourceOf(VideoItem video, AppSettings settings) =>
        (video.SourceId is { } id ? settings.TikTokSources.FirstOrDefault(s => s.Id == id) : null)
        ?? settings.TikTokSources.FirstOrDefault(s => s.Username.Equals(video.Author, StringComparison.OrdinalIgnoreCase));

    public static List<FacebookPage> For(VideoItem video, AppSettings settings, IReadOnlyCollection<FacebookPage> enabledPages)
    {
        var source = SourceOf(video, settings);
        return source is null
            ? enabledPages.ToList()
            : enabledPages.Where(p => source.PageIds.Contains(p.PageId)).ToList();
    }

    /// <summary>Ticked channels that have no ticked fanpage to post to.</summary>
    public static List<TikTokSource> Unmapped(AppSettings settings) =>
        settings.TikTokSources
            .Where(s => s.Enabled && !settings.FacebookPages.Any(p => p.Enabled && s.PageIds.Contains(p.PageId)))
            .ToList();
}

public enum PageReupState { Posted, Waiting, Failed, GaveUp, NotMapped }

/// <summary>Where one link stands on one fanpage (Trang chính → chi tiết link, Lịch sử link).</summary>
public sealed record PageReupStatus(string PageId, string PageName, PageReupState State, PostRecord? Post);

public static class PageReupStatuses
{
    /// <summary>
    /// One row per fanpage the link goes to (posted / waiting / failed, will retry / failed, retries used up),
    /// plus fanpages it was posted to earlier that are no longer ticked or mapped (NotMapped).
    /// </summary>
    public static List<PageReupStatus> For(VideoItem video, AppSettings settings, IReadOnlyCollection<FacebookPage> enabledPages)
    {
        var rows = ReupTargets.For(video, settings, enabledPages).Select(p =>
        {
            var post = video.PostFor(p.PageId);
            var state = post?.State switch
            {
                PostState.Posted => PageReupState.Posted,
                PostState.Failed => post.Attempts < settings.Post.MaxAttempts ? PageReupState.Failed : PageReupState.GaveUp,
                _ => PageReupState.Waiting
            };
            return new PageReupStatus(p.PageId, p.Name, state, post);
        }).ToList();

        foreach (var post in video.Posts.Where(p => p.State == PostState.Posted && rows.All(r => r.PageId != p.PageId)))
        {
            var name = settings.FacebookPages.FirstOrDefault(p => p.PageId == post.PageId)?.Name ?? post.PageId;
            rows.Add(new PageReupStatus(post.PageId, name, PageReupState.NotMapped, post));
        }

        return rows;
    }

    /// <summary>Posted to every fanpage it goes to (and at least one).</summary>
    public static bool IsComplete(IReadOnlyCollection<PageReupStatus> rows) =>
        rows.Any(r => r.Post?.State == PostState.Posted) && rows.All(r => r.State is PageReupState.Posted or PageReupState.NotMapped);
}
