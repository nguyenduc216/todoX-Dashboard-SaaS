using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Options;
using TodoX.Landing.Services;

namespace TodoX.Landing.Data;

public sealed class VideoSample
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AspectRatio { get; set; } = "9:16";
    public string VideoUrl { get; set; } = "";
    // Thumbnail matching AspectRatio (used by cards and the player).
    public string PosterUrl { get; set; } = "";
    public string? ThumbnailPortraitUrl { get; set; }
    public string? ThumbnailLandscapeUrl { get; set; }
    // ~1.91:1 image for Facebook / Zalo / Messenger link previews.
    public string? ShareImageUrl { get; set; }
    public string? GroupCode { get; set; }
    public string? GroupName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    // Computed from the title (not stored): readable share link that carries Open Graph previews.
    public string Slug { get; set; } = "";
    public string SharePath { get; set; } = "";
}

public sealed record VideoSampleGroup(string Code, string Name, int Count);

public sealed class VideoSampleCatalog
{
    // Only groups that have at least one published video, in the service list's order, then "Video TVC doanh nghiệp", "Video truyền thông" last.
    // Videos are newest first.
    public List<VideoSampleGroup> Groups { get; set; } = [];
    public List<VideoSample> Videos { get; set; } = [];
}

public sealed partial class VideoSampleRepository(LandingConnectionFactory db, ILogger<VideoSampleRepository> logger, IOptions<SharedMediaOptions> media)
{
    // Samples without a service: built-in groups (landing.video_samples.sample_group). The code stays
    // "dich-vu-khac" so links shared before the rename keep working.
    private const string OtherGroupCode = "dich-vu-khac";
    private const string OtherGroupName = "Video truyền thông";
    private const string TvcGroupCode = "video-tvc-doanh-nghiep";
    private const string TvcGroupName = "Video TVC doanh nghiệp";

    public async Task<VideoSampleCatalog> GetPublicAsync(CancellationToken ct = default)
    {
        try
        {
            var root = media.Value.StorageRoot;
            if (string.IsNullOrWhiteSpace(root)) return new();
            using var connection = await db.OpenAsync(ct);
            // Extra image columns come from database/manual/todox_landing_video_samples_thumbnails.sql; work without them.
            var thumbnailColumns = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                select count(*) from information_schema.columns
                where table_schema = 'landing' and table_name = 'video_samples'
                  and column_name in ('thumbnail_portrait_url', 'thumbnail_landscape_url', 'share_image_url')
                """, cancellationToken: ct)) == 3;
            var thumbnails = thumbnailColumns
                ? "s.thumbnail_portrait_url as ThumbnailPortraitUrl, s.thumbnail_landscape_url as ThumbnailLandscapeUrl, s.share_image_url as ShareImageUrl,"
                : "";
            // sample_group comes from database/manual/landing/20261109_landing_video_samples_group.sql; work without it.
            var groupColumn = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                select count(*) from information_schema.columns
                where table_schema = 'landing' and table_name = 'video_samples' and column_name = 'sample_group'
                """, cancellationToken: ct)) == 1;
            var sampleGroup = groupColumn ? "s.sample_group" : "null::text";
            // is_visible comes from database/manual/landing/20261111_landing_video_samples_visible.sql; work without it.
            var visibleColumn = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                select count(*) from information_schema.columns
                where table_schema = 'landing' and table_name = 'video_samples' and column_name = 'is_visible'
                """, cancellationToken: ct)) == 1;
            var visibleFilter = visibleColumn ? "and s.is_visible" : "";
            // Rows come in group order (services, then TVC, then "Video truyền thông"); videos are re-sorted newest first below.
            var rows = await connection.QueryAsync<VideoSample>(new CommandDefinition($"""
                select s.id as Id, s.title as Title, s.description as Description, s.aspect_ratio as AspectRatio,
                       s.video_url as VideoUrl, s.poster_url as PosterUrl, {thumbnails}
                       coalesce(sv.service_code, case when {sampleGroup} = 'tvc' then '{TvcGroupCode}' end) as GroupCode,
                       coalesce(sv.service_name, case when {sampleGroup} = 'tvc' then '{TvcGroupName}' end) as GroupName,
                       s.created_at as CreatedAt
                from landing.video_samples s
                left join catalog.services sv on sv.id = s.service_id
                where (sv.id is null or sv.status = 'active') {visibleFilter}
                order by sv.id is null, {sampleGroup} is null, sv.sort_order, sv.service_name, s.created_at desc
                """, cancellationToken: ct));
            var videos = rows.Where(video => IsPublishable(video, root)).ToList();
            foreach (var video in videos)
            {
                video.ThumbnailPortraitUrl = PublishableImage(video.ThumbnailPortraitUrl, root);
                video.ThumbnailLandscapeUrl = PublishableImage(video.ThumbnailLandscapeUrl, root);
                video.ShareImageUrl = PublishableImage(video.ShareImageUrl, root);
            }
            foreach (var video in videos.Where(video => string.IsNullOrWhiteSpace(video.GroupCode)))
            {
                video.GroupCode = OtherGroupCode;
                video.GroupName = OtherGroupName;
            }
            var slugs = VideoSlug.Assign(videos, video => video.Id, video => video.Title);
            foreach (var video in videos)
            {
                video.Slug = slugs[video.Id];
                video.SharePath = VideoSlug.SampleRoutePrefix + video.Slug;
            }
            // Group chips keep the service order; the videos themselves show newest first.
            var groups = videos.GroupBy(video => video.GroupCode)
                .Select(group => new VideoSampleGroup(group.Key!, group.First().GroupName!, group.Count())).ToList();
            return new VideoSampleCatalog
            {
                Videos = videos.OrderByDescending(video => video.CreatedAt ?? DateTimeOffset.MinValue).ToList(),
                Groups = groups
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Landing video samples unavailable.");
            return new();
        }
    }

    public async Task<(VideoSample Sample, VideoSampleCatalog Catalog)?> FindBySlugAsync(string slug, CancellationToken ct = default)
    {
        var catalog = await GetPublicAsync(ct);
        var sample = VideoSlug.Find(catalog.Videos, slug, video => video.Id, video => video.Title, video => video.Slug);
        return sample is null ? null : (sample, catalog);
    }

    private static bool IsPublishable(VideoSample video, string root)
        => video.AspectRatio is "9:16" or "16:9"
            && VideoUrl().IsMatch(video.VideoUrl) && PosterUrl().IsMatch(video.PosterUrl)
            && File.Exists(Path.Combine(root, "landing", "video-samples", "videos", Path.GetFileName(video.VideoUrl)))
            && File.Exists(Path.Combine(root, "landing", "video-samples", "posters", Path.GetFileName(video.PosterUrl)));

    private static string? PublishableImage(string? url, string root)
        => url is not null && PosterUrl().IsMatch(url)
           && File.Exists(Path.Combine(root, "landing", "video-samples", "posters", Path.GetFileName(url)))
            ? url
            : null;

    [GeneratedRegex(@"^/media/landing/video-samples/videos/[a-f0-9]{32}\.(mp4|webm)\z")]
    private static partial Regex VideoUrl();

    [GeneratedRegex(@"^/media/landing/video-samples/posters/[a-f0-9]{32}\.(jpg|jpeg|png|webp)\z")]
    private static partial Regex PosterUrl();
}
