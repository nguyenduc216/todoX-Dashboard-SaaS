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
            const string sql = """
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

            var rows = await connection.QueryAsync<IndustrySolution>(new CommandDefinition(sql, cancellationToken: ct));
            return rows.AsList();
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
