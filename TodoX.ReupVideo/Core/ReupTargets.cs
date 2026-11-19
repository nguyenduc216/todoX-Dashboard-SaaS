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
