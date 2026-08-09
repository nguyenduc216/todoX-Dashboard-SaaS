using Dapper;
using Npgsql;
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
                         and column_name in ('deleted_at','deleted_by','format_note','goal_note','capability_note')
                       having count(*) = 5
                   );
                """;
            return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(sql, cancellationToken: ct));
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<LandingIndustrySolution>> ListAsync(bool includeInactive = true, bool includeDeleted = true, CancellationToken ct = default)
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
                display_order as DisplayOrder,
                is_active as IsActive,
                created_at as CreatedAt,
                updated_at as UpdatedAt,
                deleted_at as DeletedAt,
                deleted_by as DeletedBy
            from landing.industry_solutions
            where (@includeInactive = true or is_active = true)
              and (@includeDeleted = true or deleted_at is null)
            order by display_order, title;
            """;
        var rows = await connection.QueryAsync<LandingIndustrySolution>(new CommandDefinition(sql, new
        {
            includeInactive,
            includeDeleted
        }, cancellationToken: ct));
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
                format_note as FormatNote,
                goal_note as GoalNote,
                capability_note as CapabilityNote,
                display_order as DisplayOrder,
                is_active as IsActive,
                created_at as CreatedAt,
                updated_at as UpdatedAt,
                deleted_at as DeletedAt,
                deleted_by as DeletedBy
            from landing.industry_solutions
            where id = @id;
            """;
        return await connection.QuerySingleOrDefaultAsync<LandingIndustrySolution>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }

    public async Task<Guid> CreateAsync(LandingIndustrySolutionEdit model, Guid actorUserId, CancellationToken ct = default)
    {
        Validate(model);
        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            insert into landing.industry_solutions
            (
                slug,
                title,
                short_description,
                description,
                thumbnail_url,
                video_url,
                aspect_ratio,
                format_note,
                goal_note,
                capability_note,
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
                nullif(@FormatNote, ''),
                nullif(@GoalNote, ''),
                nullif(@CapabilityNote, ''),
                @DisplayOrder,
                @IsActive,
                @ActorUserId,
                @ActorUserId
            )
            returning id;
            """;

        try
        {
            return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, ToParams(model, actorUserId), cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new LandingIndustrySolutionDuplicateSlugException(model.Slug);
        }
    }

    public async Task UpdateAsync(Guid id, LandingIndustrySolutionEdit model, Guid actorUserId, CancellationToken ct = default)
    {
        Validate(model);
        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            update landing.industry_solutions
            set slug = @Slug,
                title = @Title,
                short_description = nullif(@ShortDescription, ''),
                description = nullif(@Description, ''),
                thumbnail_url = nullif(@ThumbnailUrl, ''),
                video_url = nullif(@VideoUrl, ''),
                aspect_ratio = @AspectRatio,
                format_note = nullif(@FormatNote, ''),
                goal_note = nullif(@GoalNote, ''),
                capability_note = nullif(@CapabilityNote, ''),
                display_order = @DisplayOrder,
                is_active = @IsActive,
                updated_by = @ActorUserId,
                updated_at = now()
            where id = @Id;
            """;

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, ToParams(model, actorUserId, id), cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new LandingIndustrySolutionDuplicateSlugException(model.Slug);
        }
    }

    public async Task<Guid> UpsertAsync(LandingIndustrySolutionEdit model, Guid actorUserId, CancellationToken ct = default)
    {
        if (model.Id is null || model.Id == Guid.Empty)
        {
            return await CreateAsync(model, actorUserId, ct);
        }

        await UpdateAsync(model.Id.Value, model, actorUserId, ct);
        return model.Id.Value;
    }

    public async Task UpdateMediaAsync(Guid id, string? thumbnailUrl, string? videoUrl, Guid actorUserId, CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            update landing.industry_solutions
            set thumbnail_url = coalesce(nullif(@thumbnailUrl, ''), thumbnail_url),
                video_url = coalesce(nullif(@videoUrl, ''), video_url),
                updated_by = @actorUserId,
                updated_at = now()
            where id = @id;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            id,
            thumbnailUrl,
            videoUrl,
            actorUserId
        }, cancellationToken: ct));
    }

    public async Task SetActiveAsync(Guid id, bool active, Guid actorUserId, CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            update landing.industry_solutions
            set is_active = @active,
                updated_by = @actorUserId,
                updated_at = now()
            where id = @id;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id, active, actorUserId }, cancellationToken: ct));
    }

    public async Task DeleteAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            update landing.industry_solutions
            set is_active = false,
                deleted_at = now(),
                deleted_by = @actorUserId,
                updated_by = @actorUserId,
                updated_at = now()
            where id = @id;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id, actorUserId }, cancellationToken: ct));
    }

    public async Task RestoreAsync(Guid id, Guid actorUserId, CancellationToken ct = default)
    {
        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            update landing.industry_solutions
            set deleted_at = null,
                deleted_by = null,
                updated_by = @actorUserId,
                updated_at = now()
            where id = @id;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id, actorUserId }, cancellationToken: ct));
    }

    public async Task ReorderAsync(Guid id, int displayOrder, Guid actorUserId, CancellationToken ct = default)
    {
        if (displayOrder < 0)
        {
            throw new LandingIndustrySolutionValidationException("Thứ tự hiển thị phải >= 0.");
        }

        using var connection = await _db.OpenAsync(ct);
        const string sql = """
            update landing.industry_solutions
            set display_order = @displayOrder,
                updated_by = @actorUserId,
                updated_at = now()
            where id = @id;
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new { id, displayOrder, actorUserId }, cancellationToken: ct));
    }

    private static void Validate(LandingIndustrySolutionEdit model)
    {
        model.Title = (model.Title ?? string.Empty).Trim();
        model.Slug = (model.Slug ?? string.Empty).Trim().ToLowerInvariant();
        model.AspectRatio = (model.AspectRatio ?? LandingIndustryAspectRatios.Portrait).Trim();

        if (string.IsNullOrWhiteSpace(model.Title))
        {
            throw new LandingIndustrySolutionValidationException("Tên ngành là bắt buộc.");
        }

        if (string.IsNullOrWhiteSpace(model.Slug))
        {
            throw new LandingIndustrySolutionValidationException("Slug là bắt buộc.");
        }

        if (!LandingIndustryAspectRatios.IsValid(model.AspectRatio))
        {
            throw new LandingIndustrySolutionValidationException("Tỷ lệ video chỉ được là 9:16 hoặc 16:9.");
        }

        if (model.DisplayOrder < 0)
        {
            throw new LandingIndustrySolutionValidationException("Thứ tự hiển thị phải >= 0.");
        }
    }

    private static object ToParams(LandingIndustrySolutionEdit model, Guid actorUserId, Guid? id = null) => new
    {
        Id = id,
        model.Slug,
        model.Title,
        model.ShortDescription,
        model.Description,
        model.ThumbnailUrl,
        model.VideoUrl,
        model.AspectRatio,
        model.FormatNote,
        model.GoalNote,
        model.CapabilityNote,
        model.DisplayOrder,
        model.IsActive,
        ActorUserId = actorUserId
    };
}
