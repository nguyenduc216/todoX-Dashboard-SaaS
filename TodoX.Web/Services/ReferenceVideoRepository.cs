using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class ReferenceVideoRepository
{
    private static readonly HashSet<string> Platforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiktok", "youtube", "facebook", "instagram", "unknown"
    };

    private readonly TodoXConnectionFactory _factory;

    public ReferenceVideoRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<ReferenceVideoDto>> GetAsync(ReferenceVideoFilter filter, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReferenceVideoDto>(
            """
            SELECT id AS Id,
                   customer_id AS CustomerId,
                   created_by_user_id AS CreatedByUserId,
                   platform AS Platform,
                   source_url AS SourceUrl,
                   normalized_url AS NormalizedUrl,
                   external_video_id AS ExternalVideoId,
                   channel_name AS ChannelName,
                   channel_url AS ChannelUrl,
                   author_handle AS AuthorHandle,
                   title AS Title,
                   description AS Description,
                   COALESCE(hashtags, ARRAY[]::text[]) AS Hashtags,
                   published_at AS PublishedAt,
                   thumbnail_url AS ThumbnailUrl,
                   status AS Status,
                   reup_job_id AS ReupJobId,
                   added_from AS AddedFrom,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
              FROM content.reference_videos
             WHERE customer_id = @customerId
               AND is_deleted = false
               AND (@platform IS NULL OR platform = @platform)
               AND (@status IS NULL OR status = @status)
               AND (
                    @search IS NULL
                    OR source_url ILIKE @searchLike
                    OR title ILIKE @searchLike
                    OR channel_name ILIKE @searchLike
                    OR author_handle ILIKE @searchLike
               )
             ORDER BY created_at DESC
             LIMIT @limit OFFSET @offset;
            """,
            new
            {
                customerId = filter.CustomerId,
                platform = string.IsNullOrWhiteSpace(filter.Platform) ? null : filter.Platform,
                status = string.IsNullOrWhiteSpace(filter.Status) ? null : filter.Status,
                search = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search,
                searchLike = $"%{filter.Search?.Trim()}%",
                limit = Math.Clamp(filter.Limit, 1, 500),
                offset = Math.Max(0, filter.Offset)
            });

        return rows.ToList();
    }

    public async Task<Guid> UpsertAsync(Guid customerId, Guid userId, ReferenceVideoCreateRequest request, CancellationToken ct = default)
    {
        var sourceUrl = NormalizeUrlInput(request.SourceUrl);
        var platform = NormalizePlatform(request.Platform);
        var normalizedUrl = NormalizeUrl(sourceUrl);
        var hashtags = (request.Hashtags ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rawJson = request.RawMetadata is null ? "{}" : JsonSerializer.Serialize(request.RawMetadata);

        using var conn = await _factory.OpenAsync(ct);
        var existingId = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT id
              FROM content.reference_videos
             WHERE customer_id = @customerId
               AND platform = @platform
               AND source_url = @sourceUrl
               AND is_deleted = false
             ORDER BY created_at DESC
             LIMIT 1;
            """,
            new { customerId, platform, sourceUrl });

        if (existingId is Guid id)
        {
            await conn.ExecuteAsync(
                """
                UPDATE content.reference_videos
                   SET normalized_url = @normalizedUrl,
                       external_video_id = COALESCE(@externalVideoId, external_video_id),
                       channel_name = COALESCE(@channelName, channel_name),
                       channel_url = COALESCE(@channelUrl, channel_url),
                       author_handle = COALESCE(@authorHandle, author_handle),
                       title = COALESCE(@title, title),
                       description = COALESCE(@description, description),
                       hashtags = @hashtags,
                       published_at = COALESCE(@publishedAt, published_at),
                       thumbnail_url = COALESCE(@thumbnailUrl, thumbnail_url),
                       raw_metadata = @rawJson::jsonb,
                       updated_at = now()
                 WHERE id = @id;
                """,
                new
                {
                    id,
                    normalizedUrl,
                    request.ExternalVideoId,
                    request.ChannelName,
                    request.ChannelUrl,
                    request.AuthorHandle,
                    request.Title,
                    request.Description,
                    hashtags,
                    request.PublishedAt,
                    request.ThumbnailUrl,
                    rawJson
                });
            return id;
        }

        var newId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO content.reference_videos
                (id, customer_id, created_by_user_id, platform, source_url, normalized_url,
                 external_video_id, channel_name, channel_url, author_handle, title, description,
                 hashtags, published_at, thumbnail_url, raw_metadata, status, added_from,
                 is_deleted, created_at, updated_at)
            VALUES
                (@id, @customerId, @userId, @platform, @sourceUrl, @normalizedUrl,
                 @externalVideoId, @channelName, @channelUrl, @authorHandle, @title, @description,
                 @hashtags, @publishedAt, @thumbnailUrl, @rawJson::jsonb, 'new', 'chrome_extension',
                 false, now(), now());
            """,
            new
            {
                id = newId,
                customerId,
                userId,
                platform,
                sourceUrl,
                normalizedUrl,
                request.ExternalVideoId,
                request.ChannelName,
                request.ChannelUrl,
                request.AuthorHandle,
                request.Title,
                request.Description,
                hashtags,
                request.PublishedAt,
                request.ThumbnailUrl,
                rawJson
            });

        return newId;
    }

    public async Task SoftDeleteAsync(Guid customerId, Guid id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE content.reference_videos
               SET is_deleted = true,
                   updated_at = now()
             WHERE id = @id
               AND customer_id = @customerId;
            """,
            new { id, customerId });
    }

    private static string NormalizePlatform(string? platform)
    {
        var value = string.IsNullOrWhiteSpace(platform) ? "unknown" : platform.Trim().ToLowerInvariant();
        return Platforms.Contains(value) ? value : "unknown";
    }

    private static string NormalizeUrlInput(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new InvalidOperationException("Thiếu link video.");
        }

        return sourceUrl.Trim();
    }

    private static string NormalizeUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
        {
            return sourceUrl.Trim();
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };
        return builder.Uri.ToString().TrimEnd('/');
    }
}
