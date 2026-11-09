using System.Text.RegularExpressions;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services;

namespace TodoX.Web.Services.Landing;

public sealed class LandingVideoSample
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AspectRatio { get; set; } = LandingVideoSampleRepository.Portrait;
    public string? VideoUrl { get; set; }
    // Thumbnail matching AspectRatio; kept in sync from the two thumbnails below on save.
    public string? PosterUrl { get; set; }
    public string? ThumbnailPortraitUrl { get; set; }
    public string? ThumbnailLandscapeUrl { get; set; }
    // ~1.91:1 image for Facebook / Zalo / Messenger link previews.
    public string? ShareImageUrl { get; set; }
    // Null means a built-in group (see SampleGroup).
    public Guid? ServiceId { get; set; }
    public string? ServiceName { get; set; }
    // Built-in group for samples without a service: null = "Video truyền thông", "tvc" = "Video TVC doanh nghiệp".
    public string? SampleGroup { get; set; }
    public string GroupName => ServiceName ?? LandingVideoSampleRepository.BuiltInGroupName(SampleGroup);
}

public sealed class LandingVideoSampleRepository(TodoXConnectionFactory db, AuthStateService auth)
{
    public const string MediaArea = "video-samples";
    public const string Portrait = "9:16";
    public const string Landscape = "16:9";
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const string OtherServiceChoice = "other";
    public const string OtherServiceName = "Video truyền thông";
    public const string TvcGroup = "tvc";
    public const string TvcServiceChoice = "group:tvc";
    public const string TvcGroupName = "Video TVC doanh nghiệp";
    public const string GroupScript = "database/manual/landing/20261109_landing_video_samples_group.sql";

    public static string BuiltInGroupName(string? group) => group == TvcGroup ? TvcGroupName : OtherServiceName;

    // Built-in groups offered after the services in the "Nhóm dịch vụ video" choice.
    public static IReadOnlyList<(string Choice, string Name)> BuiltInChoices { get; } =
        [(OtherServiceChoice, OtherServiceName), (TvcServiceChoice, TvcGroupName)];

    public static string ServiceChoiceOf(LandingVideoSample sample)
        => sample.ServiceId?.ToString() ?? (sample.SampleGroup == TvcGroup ? TvcServiceChoice : OtherServiceChoice);

    private static volatile bool _groupColumnReady;

    private static async Task<bool> SupportsGroupAsync(System.Data.IDbConnection connection, CancellationToken ct)
    {
        if (_groupColumnReady) return true;
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*) from information_schema.columns
            where table_schema = 'landing' and table_name = 'video_samples' and column_name = 'sample_group'
            """, cancellationToken: ct));
        return _groupColumnReady = count == 1;
    }
    public const string ThumbnailsScript = "database/manual/todox_landing_video_samples_thumbnails.sql";

    // Columns added by ThumbnailsScript; once present they stay, so a positive check is cached.
    private static volatile bool _thumbnailColumnsReady;

    public async Task<bool> SupportsThumbnailsAsync(CancellationToken ct = default)
    {
        if (_thumbnailColumnsReady) return true;
        using var connection = await db.OpenAsync(ct);
        return await SupportsThumbnailsAsync(connection, ct);
    }

    private static async Task<bool> SupportsThumbnailsAsync(System.Data.IDbConnection connection, CancellationToken ct)
    {
        if (_thumbnailColumnsReady) return true;
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            select count(*) from information_schema.columns
            where table_schema = 'landing' and table_name = 'video_samples'
              and column_name in ('thumbnail_portrait_url', 'thumbnail_landscape_url', 'share_image_url')
            """, cancellationToken: ct));
        return _thumbnailColumnsReady = count == 3;
    }

    public async Task<IReadOnlyList<LandingVideoSample>> ListAsync(CancellationToken ct = default)
    {
        using var connection = await db.OpenAsync(ct);
        var thumbnails = await SupportsThumbnailsAsync(connection, ct)
            ? "s.thumbnail_portrait_url as ThumbnailPortraitUrl, s.thumbnail_landscape_url as ThumbnailLandscapeUrl, s.share_image_url as ShareImageUrl"
            : "null as ThumbnailPortraitUrl, null as ThumbnailLandscapeUrl, null as ShareImageUrl";
        var group = await SupportsGroupAsync(connection, ct) ? "s.sample_group" : "null";
        var rows = await connection.QueryAsync<LandingVideoSample>(new CommandDefinition($"""
            select s.id as Id, s.title as Title, s.description as Description, s.aspect_ratio as AspectRatio,
                   s.video_url as VideoUrl, s.poster_url as PosterUrl, s.service_id as ServiceId, sv.service_name as ServiceName,
                   {group} as SampleGroup, {thumbnails}
            from landing.video_samples s
            left join catalog.services sv on sv.id = s.service_id
            order by s.created_at desc
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SaveAsync(LandingVideoSample sample, string updatedBy, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        if (updatedBy != auth.CurrentUser!.UserId.ToString()) throw new UnauthorizedAccessException("Invalid user identity.");
        Normalize(sample);
        Validate(sample);
        if (sample.Id == Guid.Empty) sample.Id = Guid.NewGuid();
        using var connection = await db.OpenAsync(ct);
        var sql = await SupportsThumbnailsAsync(connection, ct)
            ? """
            insert into landing.video_samples (id, title, description, aspect_ratio, video_url, poster_url,
                thumbnail_portrait_url, thumbnail_landscape_url, share_image_url, service_id, updated_by)
            values (@Id, @Title, @Description, @AspectRatio, @VideoUrl, @PosterUrl,
                @ThumbnailPortraitUrl, @ThumbnailLandscapeUrl, @ShareImageUrl, @ServiceId, @UpdatedBy)
            on conflict (id) do update set title = excluded.title, description = excluded.description,
                aspect_ratio = excluded.aspect_ratio, video_url = excluded.video_url, poster_url = excluded.poster_url,
                thumbnail_portrait_url = excluded.thumbnail_portrait_url, thumbnail_landscape_url = excluded.thumbnail_landscape_url,
                share_image_url = excluded.share_image_url,
                service_id = excluded.service_id, updated_at = now(), updated_by = excluded.updated_by
            """
            : """
            insert into landing.video_samples (id, title, description, aspect_ratio, video_url, poster_url, service_id, updated_by)
            values (@Id, @Title, @Description, @AspectRatio, @VideoUrl, @PosterUrl, @ServiceId, @UpdatedBy)
            on conflict (id) do update set title = excluded.title, description = excluded.description,
                aspect_ratio = excluded.aspect_ratio, video_url = excluded.video_url, poster_url = excluded.poster_url,
                service_id = excluded.service_id, updated_at = now(), updated_by = excluded.updated_by
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            sample.Id,
            sample.Title,
            sample.Description,
            sample.AspectRatio,
            sample.VideoUrl,
            sample.PosterUrl,
            sample.ThumbnailPortraitUrl,
            sample.ThumbnailLandscapeUrl,
            sample.ShareImageUrl,
            sample.ServiceId,
            UpdatedBy = updatedBy
        }, cancellationToken: ct));
        if (await SupportsGroupAsync(connection, ct))
            await connection.ExecuteAsync(new CommandDefinition(
                "update landing.video_samples set sample_group = @SampleGroup where id = @Id",
                new { sample.Id, sample.SampleGroup }, cancellationToken: ct));
        else if (sample.SampleGroup is not null)
            throw new InvalidOperationException($"Cần chạy script {GroupScript} trên database để dùng nhóm {TvcGroupName}.");
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        using var connection = await db.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from landing.video_samples where id = @id", new { id }, cancellationToken: ct));
    }

    /// <param name="choice">A service id, <see cref="OtherServiceChoice"/>, or blank when nothing was selected.</param>
    public static void ApplyServiceChoice(LandingVideoSample sample, string? choice)
    {
        if (choice == OtherServiceChoice) { sample.ServiceId = null; sample.SampleGroup = null; }
        else if (choice == TvcServiceChoice) { sample.ServiceId = null; sample.SampleGroup = TvcGroup; }
        else if (Guid.TryParse(choice, out var id) && id != Guid.Empty) { sample.ServiceId = id; sample.SampleGroup = null; }
        else throw new ArgumentException("Cần chọn nhóm dịch vụ video.", nameof(choice));
    }

    public static void Normalize(LandingVideoSample sample)
    {
        sample.Title = sample.Title?.Trim() ?? "";
        sample.Description = sample.Description?.Trim() ?? "";
        sample.ThumbnailPortraitUrl = Blank(sample.ThumbnailPortraitUrl);
        sample.ThumbnailLandscapeUrl = Blank(sample.ThumbnailLandscapeUrl);
        sample.ShareImageUrl = Blank(sample.ShareImageUrl);
        // The main poster follows the video's shape; fall back to the other thumbnail, then the existing poster.
        sample.PosterUrl = (sample.AspectRatio == Landscape
            ? sample.ThumbnailLandscapeUrl ?? sample.ThumbnailPortraitUrl
            : sample.ThumbnailPortraitUrl ?? sample.ThumbnailLandscapeUrl) ?? Blank(sample.PosterUrl);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static void Validate(LandingVideoSample sample)
    {
        if (sample.Title.Length is 0 or > MaxTitleLength)
            throw new ArgumentException($"Tiêu đề video phải có từ 1 đến {MaxTitleLength} ký tự.", nameof(sample));
        if (sample.Description.Length > MaxDescriptionLength)
            throw new ArgumentException($"Thông tin nội dung tối đa {MaxDescriptionLength} ký tự.", nameof(sample));
        if (sample.AspectRatio is not (Portrait or Landscape))
            throw new ArgumentException("Kích thước video chỉ được chọn 9:16 hoặc 16:9.", nameof(sample));
        if (sample.SampleGroup is not (null or TvcGroup) || (sample.SampleGroup is not null && sample.ServiceId is not null))
            throw new ArgumentException("Nhóm video không hợp lệ.", nameof(sample));
        if (sample.ServiceId == Guid.Empty)
            throw new ArgumentException("Cần chọn nhóm dịch vụ video.", nameof(sample));
        if (string.IsNullOrWhiteSpace(sample.VideoUrl) || !IsMediaUrl(sample.VideoUrl, "videos"))
            throw new ArgumentException("Cần tải video lên.", nameof(sample));
        if (string.IsNullOrWhiteSpace(sample.PosterUrl) || !IsMediaUrl(sample.PosterUrl, "posters"))
            throw new ArgumentException("Cần tải ít nhất một thumbnail (9:16 hoặc 16:9).", nameof(sample));
        foreach (var image in new[] { sample.ThumbnailPortraitUrl, sample.ThumbnailLandscapeUrl, sample.ShareImageUrl })
            if (image is not null && !IsMediaUrl(image, "posters"))
                throw new ArgumentException("Ảnh thumbnail không hợp lệ, vui lòng tải lại.", nameof(sample));
    }

    public static bool IsMediaUrl(string url, string folder)
        => folder switch
        {
            "videos" => Regex.IsMatch(url, @"^/media/landing/video-samples/videos/[a-f0-9]{32}\.(mp4|webm)\z"),
            "posters" => Regex.IsMatch(url, @"^/media/landing/video-samples/posters/[a-f0-9]{32}\.(jpg|jpeg|png|webp)\z"),
            _ => false
        };
}
