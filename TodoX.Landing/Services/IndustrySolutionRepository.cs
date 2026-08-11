using Dapper;
using Npgsql;
using TodoX.Landing.Data;
using TodoX.Landing.Models;

namespace TodoX.Landing.Services;

public sealed class IndustrySolutionRepository
{
    private readonly LandingConnectionFactory _db;
    private readonly ILogger<IndustrySolutionRepository> _logger;

    public IndustrySolutionRepository(LandingConnectionFactory db, ILogger<IndustrySolutionRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IndustrySolution>> ListPublicAsync(CancellationToken ct = default)
    {
        try
        {
            using var connection = await _db.OpenAsync(ct);
            const string industrySql = """
                select
                    id,
                    slug,
                    title,
                    short_description as ShortDescription,
                    description,
                    thumbnail_url as ThumbnailUrl,
                    video_url as VideoUrl,
                    aspect_ratio as AspectRatio,
                    format_note as FormatNote,
                    goal_note as GoalNote,
                    capability_note as CapabilityNote,
                    display_order as DisplayOrder
                from landing.industry_solutions
                where is_active = true
                  and deleted_at is null
                order by display_order, title;
                """;

            var industries = (await connection.QueryAsync<IndustrySolution>(
                new CommandDefinition(industrySql, cancellationToken: ct))).AsList();

            if (industries.Count == 0)
                return industries;

            var videoTableReady = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select to_regclass('landing.industry_solution_videos') is not null;",
                cancellationToken: ct));

            if (!videoTableReady)
                return industries;

            const string videoSql = """
                select
                    id,
                    industry_solution_id as IndustrySolutionId,
                    title,
                    short_description as ShortDescription,
                    description,
                    thumbnail_url as ThumbnailUrl,
                    video_url as VideoUrl,
                    aspect_ratio as AspectRatio,
                    format_note as FormatNote,
                    goal_note as GoalNote,
                    capability_note as CapabilityNote,
                    display_order as DisplayOrder,
                    is_primary as IsPrimary
                from landing.industry_solution_videos
                where is_active = true
                  and deleted_at is null
                  and industry_solution_id = any(@ids)
                order by industry_solution_id, is_primary desc, display_order, created_at;
                """;

            var ids = industries.Select(x => x.Id).ToArray();
            var videos = (await connection.QueryAsync<IndustrySolutionVideo>(
                new CommandDefinition(videoSql, new { ids }, cancellationToken: ct))).AsList();
            var grouped = videos.GroupBy(x => x.IndustrySolutionId)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<IndustrySolutionVideo>)x.ToList());

            foreach (var industry in industries)
            {
                if (!grouped.TryGetValue(industry.Id, out var industryVideos))
                    continue;

                industry.Videos = industryVideos;
                var primary = industryVideos.FirstOrDefault(x => x.IsPrimary)
                              ?? industryVideos.FirstOrDefault();
                if (primary is null)
                    continue;

                // Keep the existing top-level contract for cards/homepage.
                // It now mirrors the selected representative video while Videos contains all clips.
                industry.ThumbnailUrl = primary.ThumbnailUrl ?? industry.ThumbnailUrl;
                industry.VideoUrl = primary.VideoUrl ?? industry.VideoUrl;
                industry.AspectRatio = string.IsNullOrWhiteSpace(primary.AspectRatio)
                    ? industry.AspectRatio
                    : primary.AspectRatio;
            }

            return industries;
        }
        catch (LandingSchemaUnavailableException ex)
        {
            _logger.LogWarning(ex, "Landing industry solution database is not configured.");
            return Array.Empty<IndustrySolution>();
        }
        catch (PostgresException ex) when (ex.SqlState is "42P01" or "42703" or "3F000")
        {
            _logger.LogWarning(ex, "Landing industry solution schema is not ready.");
            return Array.Empty<IndustrySolution>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load public landing industry solutions.");
            return Array.Empty<IndustrySolution>();
        }
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        try
        {
            using var connection = await _db.OpenAsync(ct);
            const string sql = """
                select to_regclass('landing.industry_solutions') is not null
                   and exists (
                       select 1
                       from information_schema.columns
                       where table_schema = 'landing'
                         and table_name = 'industry_solutions'
                         and column_name in ('deleted_at','format_note','goal_note','capability_note')
                       having count(*) = 4
                   );
                """;
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: ct));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Landing industry solution readiness check failed.");
            return false;
        }
    }
}
