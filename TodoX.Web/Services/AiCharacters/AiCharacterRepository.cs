using System.Text;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiCharacters;

public sealed class AiCharacterRepository
{
    private readonly TodoXConnectionFactory _factory;

    public AiCharacterRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<CharacterListItemDto>> ListAsync(CharacterScope scope, string? keyword, string? status, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var where = new StringBuilder($"WHERE {OwnerPredicate(schema)}");
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            where.Append(" AND (character_name ILIKE @kw OR character_code ILIKE @kw OR description ILIKE @kw)");
        }
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            where.Append(" AND status = @status");
        }

        var rows = await conn.QueryAsync<CharacterListItemDto>(
            $"""
            SELECT id AS Id,
                   {CustomerSelect(schema)} AS CustomerId,
                   {TenantSelect(schema)} AS TenantId,
                   character_code AS CharacterCode,
                   character_name AS CharacterName,
                   description AS Description,
                   style_preset AS StylePreset,
                   gender AS Gender,
                   aspect_ratio AS AspectRatio,
                   master_image_url AS MasterImageUrl,
                   status AS Status,
                   model_name AS ModelName,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
              FROM todox_ai_character
              {where}
             ORDER BY sort_order, created_at DESC;
            """, BuildParams(scope, keyword, status));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ActiveCharacterDto>> ListActiveAsync(CharacterScope scope, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ActiveCharacterDto>(
            $"""
            SELECT id AS Id,
                   character_name AS CharacterName,
                   description AS Description,
                   style_preset AS StylePreset,
                   gender AS Gender,
                   master_image_url AS MasterImageUrl,
                   master_image_object_key AS MasterImageObjectKey,
                   normalized_prompt AS NormalizedPrompt
              FROM todox_ai_character
             WHERE {OwnerPredicate(schema)} AND status = 'active'
             ORDER BY sort_order, created_at DESC;
            """, BuildParams(scope));
        return rows.ToList();
    }

    public async Task<CharacterDetailDto?> GetAsync(CharacterScope scope, Guid id, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var item = await conn.QuerySingleOrDefaultAsync<CharacterDetailDto>(
            $"""
            SELECT id AS Id,
                   {CustomerSelect(schema)} AS CustomerId,
                   {TenantSelect(schema)} AS TenantId,
                   character_code AS CharacterCode,
                   character_name AS CharacterName,
                   description AS Description,
                   style_preset AS StylePreset,
                   gender AS Gender,
                   aspect_ratio AS AspectRatio,
                   master_prompt AS MasterPrompt,
                   normalized_prompt AS NormalizedPrompt,
                   negative_prompt AS NegativePrompt,
                   master_image_url AS MasterImageUrl,
                   master_image_object_key AS MasterImageObjectKey,
                   provider_code AS ProviderCode,
                   model_name AS ModelName,
                   seed AS Seed,
                   status AS Status,
                   sort_order AS SortOrder,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
              FROM todox_ai_character
             WHERE id=@id AND {OwnerPredicate(schema)}
             LIMIT 1;
            """, BuildParams(scope, id: id));
        if (item is null) return null;

        item.Renders = (await conn.QueryAsync<AiCharacterRenderDto>(
            """
            SELECT id AS Id,
                   character_id AS CharacterId,
                   render_code AS RenderCode,
                   provider_code AS ProviderCode,
                   model_name AS ModelName,
                   prompt AS Prompt,
                   output_image_url AS OutputImageUrl,
                   output_object_key AS OutputObjectKey,
                   aspect_ratio AS AspectRatio,
                   output_format AS OutputFormat,
                   quality AS Quality,
                   resolution AS Resolution,
                   seed AS Seed,
                   usage_cost AS UsageCost,
                   status AS Status,
                   error_message AS ErrorMessage,
                   created_at AS CreatedAt
              FROM todox_ai_character_render
             WHERE character_id=@id
             ORDER BY created_at DESC
             LIMIT 50;
            """, new { id })).ToList();

        item.References = (await conn.QueryAsync<AiCharacterReferenceDto>(
            """
            SELECT id AS Id,
                   character_id AS CharacterId,
                   image_url AS ImageUrl,
                   object_key AS ObjectKey,
                   reference_type AS ReferenceType,
                   note AS Note,
                   created_at AS CreatedAt
              FROM todox_ai_character_reference
             WHERE character_id=@id
             ORDER BY created_at DESC;
            """, new { id })).ToList();
        return item;
    }

    public async Task<Guid> InsertCharacterAsync(CharacterScope scope, AiCharacter character, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var ownerColumn = OwnerColumn(schema);
        var ownerValue = schema.HasCustomerId ? scope.CustomerId : scope.TenantId;
        await conn.ExecuteAsync(
            $"""
            INSERT INTO todox_ai_character
                (id, {ownerColumn}, character_code, character_name, description, style_preset, gender, aspect_ratio,
                 master_prompt, normalized_prompt, negative_prompt, master_image_url, master_image_object_key,
                 provider_code, model_name, seed, status, sort_order, created_by, updated_by, created_at, updated_at)
            VALUES
                (@Id, @owner, @CharacterCode, @CharacterName, @Description, @StylePreset, @Gender, @AspectRatio,
                 @MasterPrompt, @NormalizedPrompt, @NegativePrompt, @MasterImageUrl, @MasterImageObjectKey,
                 @ProviderCode, @ModelName, @Seed, @Status, @SortOrder, @CreatedBy, @UpdatedBy, now(), now());
            """, new
            {
                character.Id,
                owner = ownerValue,
                character.CharacterCode,
                character.CharacterName,
                character.Description,
                character.StylePreset,
                character.Gender,
                character.AspectRatio,
                character.MasterPrompt,
                character.NormalizedPrompt,
                character.NegativePrompt,
                character.MasterImageUrl,
                character.MasterImageObjectKey,
                character.ProviderCode,
                character.ModelName,
                character.Seed,
                character.Status,
                character.SortOrder,
                character.CreatedBy,
                character.UpdatedBy
            });
        return character.Id;
    }

    public async Task UpdateCharacterAsync(CharacterScope scope, Guid id, UpdateCharacterRequest request,
        string normalizedPrompt, string negativePrompt, Guid userId, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE todox_ai_character
               SET character_name=@name,
                   description=@description,
                   style_preset=@style,
                   gender=@gender,
                   aspect_ratio=@aspect,
                   master_prompt=@masterPrompt,
                   normalized_prompt=@normalized,
                   negative_prompt=@negative,
                   status=@status,
                   sort_order=@sortOrder,
                   updated_by=@userId,
                   updated_at=now()
             WHERE id=@id AND {OwnerPredicate(schema)};
            """, BuildParams(scope, id: id, extra: new
            {
                name = request.CharacterName.Trim(),
                description = request.Description.Trim(),
                masterPrompt = string.IsNullOrWhiteSpace(request.RenderPrompt) ? request.Description.Trim() : request.RenderPrompt.Trim(),
                style = request.StylePreset,
                gender = request.Gender,
                aspect = request.AspectRatio,
                normalized = normalizedPrompt,
                negative = negativePrompt,
                status = request.Status,
                sortOrder = request.SortOrder,
                userId
            }));
    }

    public async Task UpdateMasterImageAsync(CharacterScope scope, Guid characterId, string? imageUrl, string? objectKey, Guid userId, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE todox_ai_character
               SET master_image_url=@imageUrl,
                   master_image_object_key=@objectKey,
                   updated_by=@userId,
                   updated_at=now()
             WHERE id=@characterId AND {OwnerPredicate(schema)};
            """, BuildParams(scope, id: characterId, extra: new
            {
                characterId,
                imageUrl,
                objectKey,
                userId
            }));
    }

    public async Task DisableAsync(CharacterScope scope, Guid id, Guid userId, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE todox_ai_character
               SET status='inactive', updated_by=@userId, updated_at=now()
             WHERE id=@id AND {OwnerPredicate(schema)};
            """, BuildParams(scope, id: id, extra: new { userId }));
    }

    public async Task<Guid> InsertRenderAsync(CharacterScope scope, AiCharacterRender render, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var ownerColumn = OwnerColumn(schema);
        var ownerValue = schema.HasCustomerId ? scope.CustomerId : scope.TenantId;
        await conn.ExecuteAsync(
            $"""
            INSERT INTO todox_ai_character_render
                (id, character_id, {ownerColumn}, render_code, provider_code, model_name, prompt, request_json, response_json,
                 output_image_url, output_object_key, aspect_ratio, output_format, quality, resolution, seed,
                 usage_cost, usage_json, status, error_message, created_by, created_at)
            VALUES
                (@Id, @CharacterId, @owner, @RenderCode, @ProviderCode, @ModelName, @Prompt, @RequestJson, @ResponseJson,
                 @OutputImageUrl, @OutputObjectKey, @AspectRatio, @OutputFormat, @Quality, @Resolution, @Seed,
                 @UsageCost, @UsageJson, @Status, @ErrorMessage, @CreatedBy, now());
            """, new
            {
                render.Id,
                render.CharacterId,
                owner = ownerValue,
                render.RenderCode,
                render.ProviderCode,
                render.ModelName,
                render.Prompt,
                render.RequestJson,
                render.ResponseJson,
                render.OutputImageUrl,
                render.OutputObjectKey,
                render.AspectRatio,
                render.OutputFormat,
                render.Quality,
                render.Resolution,
                render.Seed,
                render.UsageCost,
                render.UsageJson,
                render.Status,
                render.ErrorMessage,
                render.CreatedBy
            });
        return render.Id;
    }

    public async Task SetMasterAsync(CharacterScope scope, Guid characterId, Guid renderId, Guid userId, CancellationToken ct = default)
    {
        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE todox_ai_character c
               SET master_image_url = r.output_image_url,
                   master_image_object_key = r.output_object_key,
                   updated_by = @userId,
                   updated_at = now()
              FROM todox_ai_character_render r
             WHERE c.id = @characterId
               AND r.id = @renderId
               AND r.character_id = c.id
               AND r.status = 'completed'
               AND c.{OwnerColumn(schema)} = @owner;
            """, new
            {
                characterId,
                renderId,
                userId,
                owner = schema.HasCustomerId ? scope.CustomerId : scope.TenantId
            });
    }

    public async Task AddReferencesAsync(CharacterScope scope, Guid characterId, IEnumerable<string> urls, Guid userId, CancellationToken ct = default)
    {
        var urlList = urls.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (urlList.Count == 0) return;

        var schema = await GetSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var ownerColumn = OwnerColumn(schema);
        var ownerValue = schema.HasCustomerId ? scope.CustomerId : scope.TenantId;
        foreach (var url in urlList)
        {
            await conn.ExecuteAsync(
                $"""
                INSERT INTO todox_ai_character_reference
                    (id, character_id, {ownerColumn}, image_url, object_key, reference_type, note, created_by, created_at)
                VALUES
                    (@id, @characterId, @owner, @url, NULL, 'image_url', NULL, @userId, now());
                """, new { id = Guid.NewGuid(), characterId, owner = ownerValue, url, userId });
        }
    }

    private async Task<TableSchema> GetSchemaAsync(CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        var columns = (await conn.QueryAsync<string>(
            """
            SELECT column_name
              FROM information_schema.columns
             WHERE table_name = 'todox_ai_character'
               AND table_schema = ANY(current_schemas(false));
            """)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (columns.Count == 0)
        {
            throw new InvalidOperationException("Chua co bang todox_ai_character. Vui long chay SQL schema rieng truoc khi dung chuc nang AI Characters.");
        }

        return new TableSchema(columns.Contains("customer_id"), columns.Contains("tenant_id"));
    }

    private static string OwnerColumn(TableSchema schema)
        => schema.HasCustomerId ? "customer_id" : schema.HasTenantId ? "tenant_id" : throw new InvalidOperationException("Bang todox_ai_character can co customer_id hoac tenant_id.");

    private static string OwnerPredicate(TableSchema schema)
        => $"{OwnerColumn(schema)} = @owner";

    private static string CustomerSelect(TableSchema schema) => schema.HasCustomerId ? "customer_id" : "NULL::uuid";
    private static string TenantSelect(TableSchema schema) => schema.HasTenantId ? "tenant_id" : "NULL::uuid";

    private static object BuildParams(CharacterScope scope, string? keyword = null, string? status = null, Guid? id = null, object? extra = null)
    {
        var p = new DynamicParameters(extra);
        p.Add("owner", scope.CustomerId ?? scope.TenantId);
        p.Add("kw", string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword.Trim()}%");
        p.Add("status", status);
        p.Add("id", id);
        return p;
    }

    private sealed record TableSchema(bool HasCustomerId, bool HasTenantId);
}

public sealed record CharacterScope(Guid? CustomerId, Guid? TenantId)
{
    public bool IsValid => CustomerId is not null || TenantId is not null;
}
