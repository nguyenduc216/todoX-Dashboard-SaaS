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
    public string PosterUrl { get; set; } = "";
    public string GroupCode { get; set; } = "";
    public string GroupName { get; set; } = "";
}

public sealed record VideoSampleGroup(string Code, string Name, int Count);

public sealed class VideoSampleCatalog
{
    // Only groups that have at least one published video, in the service catalog's order.
    public List<VideoSampleGroup> Groups { get; set; } = [];
    public List<VideoSample> Videos { get; set; } = [];
}

public sealed partial class VideoSampleRepository(LandingConnectionFactory db, ILogger<VideoSampleRepository> logger, IOptions<SharedMediaOptions> media)
{
    public async Task<VideoSampleCatalog> GetPublicAsync(CancellationToken ct = default)
    {
        try
        {
            var root = media.Value.StorageRoot;
            if (string.IsNullOrWhiteSpace(root)) return new();
            using var connection = await db.OpenAsync(ct);
            var rows = await connection.QueryAsync<VideoSample>(new CommandDefinition("""
                select s.id as Id, s.title as Title, s.description as Description, s.aspect_ratio as AspectRatio,
                       s.video_url as VideoUrl, s.poster_url as PosterUrl,
                       c.category_code as GroupCode, c.category_name as GroupName
                from landing.video_samples s
                join catalog.service_categories c on c.id = s.category_id
                where c.is_active
                order by c.sort_order, c.category_name, s.created_at desc
                """, cancellationToken: ct));
            var videos = rows.Where(video => IsPublishable(video, root)).ToList();
            return new VideoSampleCatalog
            {
                Videos = videos,
                Groups = videos.GroupBy(video => video.GroupCode)
                    .Select(group => new VideoSampleGroup(group.Key, group.First().GroupName, group.Count())).ToList()
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Landing video samples unavailable.");
            return new();
        }
    }

    private static bool IsPublishable(VideoSample video, string root)
        => video.AspectRatio is "9:16" or "16:9"
            && VideoUrl().IsMatch(video.VideoUrl) && PosterUrl().IsMatch(video.PosterUrl)
            && File.Exists(Path.Combine(root, "landing", "video-samples", "videos", Path.GetFileName(video.VideoUrl)))
            && File.Exists(Path.Combine(root, "landing", "video-samples", "posters", Path.GetFileName(video.PosterUrl)));

    [GeneratedRegex(@"^/media/landing/video-samples/videos/[a-f0-9]{32}\.(mp4|webm)\z")]
    private static partial Regex VideoUrl();

    [GeneratedRegex(@"^/media/landing/video-samples/posters/[a-f0-9]{32}\.(jpg|jpeg|png|webp)\z")]
    private static partial Regex PosterUrl();
}
