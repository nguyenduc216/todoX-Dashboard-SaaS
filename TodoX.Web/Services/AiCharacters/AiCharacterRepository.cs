using Dapper;
using Microsoft.Extensions.Logging;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiCharacters;

public sealed class AiCharacterRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly ILogger<AiCharacterRepository> _logger;

    public AiCharacterRepository(TodoXConnectionFactory factory, ILogger<AiCharacterRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CharacterListItemDto>> ListAsync(CharacterScope scope, string? keyword, string? status, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var where = "WHERE customer_id=@customerId";
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            where += " AND (character_name ILIKE @kw OR character_code ILIKE @kw OR description ILIKE @kw)";
        }
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            where += " AND status = @status";
        }

        var rows = await conn.QueryAsync<CharacterListItemDto>(
            $"""
            SELECT id AS Id,
                   customer_id AS CustomerId,
                   character_code AS CharacterCode,
                   character_name AS CharacterName,
                   description AS Description,
                   COALESCE(style_preset, '') AS StylePreset,
                   COALESCE(gender, '') AS Gender,
                   COALESCE(aspect_ratio, '1:1') AS AspectRatio,
                   master_image_url AS MasterImageUrl,
                   status AS Status,
                   COALESCE(model_name, '') AS ModelName,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
              FROM public.todox_ai_character
              {where}
             ORDER BY sort_order, created_at DESC;
            """, BuildParams(scope, keyword, status));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ActiveCharacterDto>> ListActiveAsync(CharacterScope scope, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ActiveCharacterDto>(
            """
            SELECT id AS Id,
                   character_name AS CharacterName,
                   description AS Description,
                   COALESCE(style_preset, '') AS StylePreset,
                   COALESCE(gender, '') AS Gender,
                   master_image_url AS MasterImageUrl,
                   master_image_object_key AS MasterImageObjectKey,
                   COALESCE(normalized_prompt, '') AS NormalizedPrompt
              FROM public.todox_ai_character
             WHERE customer_id=@customerId AND status = 'active'
             ORDER BY sort_order, created_at DESC;
            """, BuildParams(scope));
        return rows.ToList();
    }

    public async Task<CharacterDetailDto?> GetAsync(CharacterScope scope, long id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var item = await conn.QuerySingleOrDefaultAsync<CharacterDetailDto>(
            """
            SELECT id AS Id,
                   customer_id AS CustomerId,
                   character_code AS CharacterCode,
                   character_name AS CharacterName,
                   description AS Description,
                   COALESCE(style_preset, '') AS StylePreset,
                   COALESCE(gender, '') AS Gender,
                   COALESCE(aspect_ratio, '1:1') AS AspectRatio,
                   COALESCE(master_prompt, '') AS MasterPrompt,
                   COALESCE(normalized_prompt, '') AS NormalizedPrompt,
                   COALESCE(negative_prompt, '') AS NegativePrompt,
                   master_image_url AS MasterImageUrl,
                   master_image_object_key AS MasterImageObjectKey,
                   provider_code AS ProviderCode,
                   COALESCE(model_name, '') AS ModelName,
                   seed AS Seed,
                   status AS Status,
                   sort_order AS SortOrder,
                   created_at AS CreatedAt,
                   updated_at AS UpdatedAt
              FROM public.todox_ai_character
             WHERE id=@id AND customer_id=@customerId
             LIMIT 1;
            """, BuildParams(scope, id: id));
        if (item is null) return null;

        item.Renders = (await conn.QueryAsync<AiCharacterRenderDto>(
            """
            SELECT id AS Id,
                   character_id AS CharacterId,
                   render_code AS RenderCode,
                   provider_code AS ProviderCode,
                   COALESCE(model_name, '') AS ModelName,
                   prompt AS Prompt,
                   output_image_url AS OutputImageUrl,
                   output_object_key AS OutputObjectKey,
                   COALESCE(aspect_ratio, '') AS AspectRatio,
                   output_format AS OutputFormat,
                   COALESCE(quality, '') AS Quality,
                   COALESCE(resolution, '') AS Resolution,
                   seed AS Seed,
                   usage_cost AS UsageCost,
                   status AS Status,
                   error_message AS ErrorMessage,
                   created_at AS CreatedAt
              FROM public.todox_ai_character_render
             WHERE character_id=@id AND customer_id=@customerId
             ORDER BY created_at DESC
             LIMIT 50;
            """, BuildParams(scope, id: id))).ToList();

        item.References = (await conn.QueryAsync<AiCharacterReferenceDto>(
            """
            SELECT id AS Id,
                   character_id AS CharacterId,
                   image_url AS ImageUrl,
                   object_key AS ObjectKey,
                   reference_type AS ReferenceType,
                   note AS Note,
                   created_at AS CreatedAt
              FROM public.todox_ai_character_reference
             WHERE character_id=@id AND customer_id=@customerId
             ORDER BY created_at DESC;
            """, BuildParams(scope, id: id))).ToList();
        return item;
    }

    public async Task<long> InsertCharacterAsync(CharacterScope scope, AiCharacter character, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO public.todox_ai_character
                (customer_id, character_code, character_name, description, style_preset, gender, aspect_ratio,
                 master_prompt, normalized_prompt, negative_prompt, master_image_url, master_image_object_key,
                 provider_code, model_name, seed, status, sort_order, created_by, updated_by, created_at, updated_at)
            VALUES
                (@customerId, @CharacterCode, @CharacterName, @Description, @StylePreset, @Gender, @AspectRatio,
                 @MasterPrompt, @NormalizedPrompt, @NegativePrompt, @MasterImageUrl, @MasterImageObjectKey,
                 @ProviderCode, @ModelName, @Seed, @Status, @SortOrder, @CreatedBy, @UpdatedBy, now(), now())
            RETURNING id;
            """, new
            {
                customerId = scope.CustomerId,
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
    }

    public async Task UpdateCharacterAsync(CharacterScope scope, long id, UpdateCharacterRequest request,
        string normalizedPrompt, string negativePrompt, string status, string currentStatus, string userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_character
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
             WHERE id=@id AND customer_id=@customerId;
            """, BuildParams(scope, id: id, extra: new
            {
                name = request.CharacterName.Trim(),
                description = request.Description.Trim(),
                masterPrompt = string.IsNullOrWhiteSpace(request.RenderPrompt) ? request.Description.Trim() : request.RenderPrompt.Trim(),
                style = CharacterPresetOptions.NormalizeOptionalPreset(request.StylePreset),
                gender = CharacterPresetOptions.NormalizeOptionalPreset(request.Gender),
                aspect = request.AspectRatio,
                normalized = normalizedPrompt,
                negative = negativePrompt,
                status,
                sortOrder = request.SortOrder,
                userId
            }));
        _logger.LogInformation("AI_CHARACTER_UPDATE characterId={CharacterId} customerId={CustomerId} statusBefore={StatusBefore} statusRequest={StatusRequest} statusNormalized={StatusNormalized} affectedRows={AffectedRows} masterImageExists={MasterImageExists}",
            id, scope.CustomerId, currentStatus, request.Status, status, affected, false);
        if (affected == 0)
        {
            throw new InvalidOperationException("Không cập nhật được Character do sai ID hoặc customer scope.");
        }
    }

    public async Task UpdateMasterImageAsync(CharacterScope scope, long characterId, string? imageUrl, string? objectKey, string userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_character
               SET master_image_url=@imageUrl,
                   master_image_object_key=@objectKey,
                   updated_by=@userId,
                   updated_at=now()
             WHERE id=@characterId AND customer_id=@customerId;
            """, BuildParams(scope, id: characterId, extra: new
            {
                characterId,
                imageUrl,
                objectKey,
                userId
            }));
    }

    public async Task DisableAsync(CharacterScope scope, long id, string userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_character
               SET status='inactive', updated_by=@userId, updated_at=now()
             WHERE id=@id AND customer_id=@customerId;
            """, BuildParams(scope, id: id, extra: new { userId }));
    }

    public async Task<long> InsertRenderAsync(CharacterScope scope, AiCharacterRender render, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO public.todox_ai_character_render
                (character_id, customer_id, render_code, provider_code, model_name, prompt, request_json, response_json,
                 output_image_url, output_object_key, aspect_ratio, output_format, quality, resolution, seed,
                 usage_cost, usage_json, status, error_message, created_by, created_at)
            VALUES
                (@CharacterId, @customerId, @RenderCode, @ProviderCode, @ModelName, @Prompt, CAST(@RequestJson AS jsonb), CAST(@ResponseJson AS jsonb),
                 @OutputImageUrl, @OutputObjectKey, @AspectRatio, @OutputFormat, @Quality, @Resolution, @Seed,
                 @UsageCost, CAST(@UsageJson AS jsonb), @Status, @ErrorMessage, @CreatedBy, now())
            RETURNING id;
            """, new
            {
                render.CharacterId,
                customerId = scope.CustomerId,
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
    }

    public async Task SetMasterAsync(CharacterScope scope, long characterId, long renderId, string userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_character c
               SET master_image_url = r.output_image_url,
                   master_image_object_key = r.output_object_key,
                   updated_by = @userId,
                   updated_at = now()
              FROM public.todox_ai_character_render r
             WHERE c.id = @characterId
               AND r.id = @renderId
               AND r.character_id = c.id
               AND r.status = 'success'
               AND c.customer_id = @customerId
               AND r.customer_id = @customerId;
            """, new
            {
                characterId,
                renderId,
                userId,
                customerId = scope.CustomerId
            });
    }

    public async Task AddReferencesAsync(CharacterScope scope, long characterId, IEnumerable<string> urls, string userId, CancellationToken ct = default)
    {
        var urlList = urls.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (urlList.Count == 0) return;

        using var conn = await _factory.OpenAsync(ct);
        foreach (var url in urlList)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.todox_ai_character_reference
                    (character_id, customer_id, image_url, object_key, reference_type, note, created_by, created_at)
                VALUES
                    (@characterId, @customerId, @url, NULL, 'image_url', NULL, @userId, now());
                """, new { characterId, customerId = scope.CustomerId, url, userId });
        }
    }

    private static object BuildParams(CharacterScope scope, string? keyword = null, string? status = null, long? id = null, object? extra = null)
    {
        var p = new DynamicParameters(extra);
        p.Add("customerId", scope.CustomerId);
        p.Add("kw", string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword.Trim()}%");
        p.Add("status", status);
        p.Add("id", id);
        return p;
    }
}

public sealed record CharacterScope(long CustomerId);
