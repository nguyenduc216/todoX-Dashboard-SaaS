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
    public string? PosterUrl { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
}

public sealed class LandingVideoSampleRepository(TodoXConnectionFactory db, AuthStateService auth)
{
    public const string MediaArea = "video-samples";
    public const string Portrait = "9:16";
    public const string Landscape = "16:9";
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 2000;

    public async Task<IReadOnlyList<LandingVideoSample>> ListAsync(CancellationToken ct = default)
    {
        using var connection = await db.OpenAsync(ct);
        var rows = await connection.QueryAsync<LandingVideoSample>(new CommandDefinition("""
            select s.id as Id, s.title as Title, s.description as Description, s.aspect_ratio as AspectRatio,
                   s.video_url as VideoUrl, s.poster_url as PosterUrl, s.category_id as CategoryId, c.category_name as CategoryName
            from landing.video_samples s
            left join catalog.service_categories c on c.id = s.category_id
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
        await connection.ExecuteAsync(new CommandDefinition("""
            insert into landing.video_samples (id, title, description, aspect_ratio, video_url, poster_url, category_id, updated_by)
            values (@Id, @Title, @Description, @AspectRatio, @VideoUrl, @PosterUrl, @CategoryId, @UpdatedBy)
            on conflict (id) do update set title = excluded.title, description = excluded.description,
                aspect_ratio = excluded.aspect_ratio, video_url = excluded.video_url, poster_url = excluded.poster_url,
                category_id = excluded.category_id, updated_at = now(), updated_by = excluded.updated_by
            """, new
        {
            sample.Id,
            sample.Title,
            sample.Description,
            sample.AspectRatio,
            sample.VideoUrl,
            sample.PosterUrl,
            sample.CategoryId,
            UpdatedBy = updatedBy
        }, cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        LandingIntroVideoWriteAccess.Demand(auth.CurrentUser);
        using var connection = await db.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "delete from landing.video_samples where id = @id", new { id }, cancellationToken: ct));
    }

    public static void Normalize(LandingVideoSample sample)
    {
        sample.Title = sample.Title?.Trim() ?? "";
        sample.Description = sample.Description?.Trim() ?? "";
    }

    public static void Validate(LandingVideoSample sample)
    {
        if (sample.Title.Length is 0 or > MaxTitleLength)
            throw new ArgumentException($"Tiêu đề video phải có từ 1 đến {MaxTitleLength} ký tự.", nameof(sample));
        if (sample.Description.Length > MaxDescriptionLength)
            throw new ArgumentException($"Thông tin nội dung tối đa {MaxDescriptionLength} ký tự.", nameof(sample));
        if (sample.AspectRatio is not (Portrait or Landscape))
            throw new ArgumentException("Kích thước video chỉ được chọn 9:16 hoặc 16:9.", nameof(sample));
        if (sample.CategoryId is null || sample.CategoryId == Guid.Empty)
            throw new ArgumentException("Cần chọn nhóm dịch vụ video.", nameof(sample));
        if (string.IsNullOrWhiteSpace(sample.VideoUrl) || !IsMediaUrl(sample.VideoUrl, "videos"))
            throw new ArgumentException("Cần tải video lên.", nameof(sample));
        if (string.IsNullOrWhiteSpace(sample.PosterUrl) || !IsMediaUrl(sample.PosterUrl, "posters"))
            throw new ArgumentException("Cần tải ảnh đại diện lên.", nameof(sample));
    }

    public static bool IsMediaUrl(string url, string folder)
        => folder switch
        {
            "videos" => Regex.IsMatch(url, @"^/media/landing/video-samples/videos/[a-f0-9]{32}\.(mp4|webm)\z"),
            "posters" => Regex.IsMatch(url, @"^/media/landing/video-samples/posters/[a-f0-9]{32}\.(jpg|jpeg|png|webp)\z"),
            _ => false
        };
}
