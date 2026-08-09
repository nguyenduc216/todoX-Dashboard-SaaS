using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Landing;

namespace TodoX.Web.Services.Landing;

public sealed class LandingIndustrySolutionRepository
{
    private readonly TodoXConnectionFactory _db;

    public LandingIndustrySolutionRepository(TodoXConnectionFactory db)
    {
        _db = db;
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);
        const string sql = "select to_regclass('landing.industry_solutions') is not null";
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<LandingIndustrySolution>> ListAsync(CancellationToken ct = default)
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
                display_order as DisplayOrder,
                is_active as IsActive,
                created_at as CreatedAt,
                updated_at as UpdatedAt
            from landing.industry_solutions
            order by display_order, title;
            """;
        var rows = await connection.QueryAsync<LandingIndustrySolution>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<LandingIndustrySolution?> GetAsync(Guid id, CancellationToken ct = default)
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
                display_order as DisplayOrder,
                is_active as IsActive,
                created_at as CreatedAt,
                updated_at as UpdatedAt
            from landing.industry_solutions
            where id = @id;
            """;
        return await connection.QuerySingleOrDefaultAsync<LandingIndustrySolution>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<Guid> UpsertAsync(LandingIndustrySolutionEdit model, Guid actorUserId, CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);

        if (model.Id is null || model.Id == Guid.Empty)
        {
            const string insertSql = """
                insert into landing.industry_solutions
                (
                    slug,
                    title,
                    short_description,
                    description,
                    thumbnail_url,
                    video_url,
                    aspect_ratio,
                    display_order,
                    is_active,
                    created_by,
                    updated_by
                )
                values
                (
                    @Slug,
                    @Title,
                    nullif(@ShortDescription, ''),
                    nullif(@Description, ''),
                    nullif(@ThumbnailUrl, ''),
                    nullif(@VideoUrl, ''),
                    @AspectRatio,
                    @DisplayOrder,
                    @IsActive,
                    @ActorUserId,
                    @ActorUserId
                )
                returning id;
                """;

            return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(insertSql, new
            {
                model.Slug,
                model.Title,
                model.ShortDescription,
                model.Description,
                model.ThumbnailUrl,
                model.VideoUrl,
                model.AspectRatio,
                model.DisplayOrder,
                model.IsActive,
                ActorUserId = actorUserId
            }, cancellationToken: ct));
        }

        const string updateSql = """
            update landing.industry_solutions
            set slug = @Slug,
                title = @Title,
                short_description = nullif(@ShortDescription, ''),
                description = nullif(@Description, ''),
                thumbnail_url = nullif(@ThumbnailUrl, ''),
                video_url = nullif(@VideoUrl, ''),
                aspect_ratio = @AspectRatio,
                display_order = @DisplayOrder,
                is_active = @IsActive,
                updated_by = @ActorUserId,
                updated_at = now()
            where id = @Id;
            """;

        await connection.ExecuteAsync(new CommandDefinition(updateSql, new
        {
            model.Id,
            model.Slug,
            model.Title,
            model.ShortDescription,
            model.Description,
            model.ThumbnailUrl,
            model.VideoUrl,
            model.AspectRatio,
            model.DisplayOrder,
            model.IsActive,
            ActorUserId = actorUserId
        }, cancellationToken: ct));

        return model.Id.Value;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);
        const string sql = "delete from landing.industry_solutions where id = @id";
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
