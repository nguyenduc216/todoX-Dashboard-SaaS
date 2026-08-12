using System.Data;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderModelRepository
{
    private readonly TodoXConnectionFactory _factory;

    public AiProviderModelRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<AiProviderModelListItemDto>> ListModelsAsync(
        string? providerCode = null,
        string? mediaType = null,
        string? status = null,
        bool? enabled = null,
        string? search = null,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderModelListItemDto>(
            """
            SELECT m.id AS Id,
                   m.provider_id AS ProviderId,
                   p.provider_code AS ProviderCode,
                   m.provider_model_code AS ProviderModelCode,
                   m.provider_model_id_base AS ProviderModelIdBase,
                   m.display_name AS DisplayName,
                   m.media_type AS MediaType,
                   m.server_code AS ServerCode,
                   m.provider_status AS ProviderStatus,
                   m.status_message AS StatusMessage,
                   m.rate_type AS RateType,
                   m.base_provider_price AS BaseProviderPrice,
                   m.provider_price_unit AS ProviderPriceUnit,
                   m.description AS Description,
                   m.enabled AS Enabled,
                   m.allow_user_select AS AllowUserSelect,
                   m.is_deprecated AS IsDeprecated,
                   m.source AS Source,
                   m.last_provider_sync_at AS LastProviderSyncAt,
                   m.last_health_check_at AS LastHealthCheckAt,
                   m.last_success_at AS LastSuccessAt,
                   m.last_failure_at AS LastFailureAt,
                   COALESCE(m.failure_count, 0) AS FailureCount
              FROM public.todox_ai_provider_model m
              JOIN public.todox_ai_provider p ON p.id = m.provider_id
             WHERE (@providerCode IS NULL OR p.provider_code = @providerCode)
               AND (@mediaType IS NULL OR m.media_type = @mediaType)
               AND (@status IS NULL OR COALESCE(m.provider_status, '') = @status)
               AND (@enabled IS NULL OR m.enabled = @enabled)
               AND (
                    @search IS NULL OR
                    m.display_name ILIKE '%' || @search || '%' OR
                    m.provider_model_code ILIKE '%' || @search || '%' OR
                    p.provider_code ILIKE '%' || @search || '%'
               )
             ORDER BY p.priority, p.provider_name, m.media_type, m.display_name;
            """,
            new { providerCode, mediaType, status, enabled, search });

        var list = rows.ToList();
        foreach (var model in list)
        {
            model.Capabilities = (await GetCapabilitiesAsync(model.Id, ct)).Select(x => x.CapabilityCode).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            model.PriceSummary = await BuildPriceSummaryAsync(conn, model.Id, ct);
            await PopulateOptionsAsync(conn, model, ct);
        }

        return list;
    }

    public async Task<AiProviderModelDetailDto?> GetModelAsync(long id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var model = await conn.QuerySingleOrDefaultAsync<AiProviderModelDetailDto>(
            """
            SELECT m.id AS Id,
                   m.provider_id AS ProviderId,
                   p.provider_code AS ProviderCode,
                   m.provider_model_code AS ProviderModelCode,
                   m.provider_model_id_base AS ProviderModelIdBase,
                   m.display_name AS DisplayName,
                   m.media_type AS MediaType,
                   m.server_code AS ServerCode,
                   m.provider_status AS ProviderStatus,
                   m.status_message AS StatusMessage,
                   m.rate_type AS RateType,
                   m.base_provider_price AS BaseProviderPrice,
                   m.provider_price_unit AS ProviderPriceUnit,
                   m.description AS Description,
                   m.enabled AS Enabled,
                   m.allow_user_select AS AllowUserSelect,
                   m.is_deprecated AS IsDeprecated,
                   m.source AS Source,
                   m.last_provider_sync_at AS LastProviderSyncAt,
                   m.last_health_check_at AS LastHealthCheckAt,
                   m.last_success_at AS LastSuccessAt,
                   m.last_failure_at AS LastFailureAt,
                   COALESCE(m.failure_count, 0) AS FailureCount,
                   m.raw_json AS RawJson
              FROM public.todox_ai_provider_model m
              JOIN public.todox_ai_provider p ON p.id = m.provider_id
             WHERE m.id = @id
             LIMIT 1;
            """, new { id });
        if (model is null) return null;

        model.ModelCapabilities = (await GetCapabilitiesAsync(model.Id, ct)).ToList();
        model.Prices = (await GetPricesAsync(model.Id, ct)).ToList();
        model.PricingPolicies = (await GetPricingPoliciesAsync(model.ProviderId, ct)).ToList();
        model.SyncHistory = (await GetSyncHistoryAsync(model.ProviderId, 20, ct)).ToList();
        if (model.SyncHistory.Count > 0)
        {
            model.SyncChanges = (await GetSyncChangesAsync(model.SyncHistory[0].Id, 200, ct)).ToList();
        }
        model.PriceSummary = await BuildPriceSummaryAsync(conn, model.Id, ct);
        var options = AiProviderModelOptionsNormalizer.Normalize(
            model.SupportedModes,
            model.SupportedDurations,
            model.SupportedResolutions,
            model.SupportedRatios,
            model.Prices,
            model.RawJson);
        model.SupportedModes = options.Modes;
        model.SupportedDurations = options.Durations;
        model.SupportedResolutions = options.Resolutions;
        model.SupportedRatios = options.Ratios;
        return model;
    }

    public async Task<AiProviderModelDetailDto?> GetModelByCodeAsync(long providerId, string providerModelCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiProviderModelDetailDto>(
            """
            SELECT m.id AS Id,
                   m.provider_id AS ProviderId,
                   p.provider_code AS ProviderCode,
                   m.provider_model_code AS ProviderModelCode,
                   m.provider_model_id_base AS ProviderModelIdBase,
                   m.display_name AS DisplayName,
                   m.media_type AS MediaType,
                   m.server_code AS ServerCode,
                   m.provider_status AS ProviderStatus,
                   m.status_message AS StatusMessage,
                   m.rate_type AS RateType,
                   m.base_provider_price AS BaseProviderPrice,
                   m.provider_price_unit AS ProviderPriceUnit,
                   m.description AS Description,
                   m.enabled AS Enabled,
                   m.allow_user_select AS AllowUserSelect,
                   m.is_deprecated AS IsDeprecated,
                   m.source AS Source,
                   m.last_provider_sync_at AS LastProviderSyncAt,
                   m.last_health_check_at AS LastHealthCheckAt,
                   m.last_success_at AS LastSuccessAt,
                   m.last_failure_at AS LastFailureAt,
                   COALESCE(m.failure_count, 0) AS FailureCount,
                   m.raw_json AS RawJson
              FROM public.todox_ai_provider_model m
              JOIN public.todox_ai_provider p ON p.id = m.provider_id
             WHERE m.provider_id = @providerId
               AND m.provider_model_code = @providerModelCode
             LIMIT 1;
            """, new { providerId, providerModelCode });
    }

    public async Task UpdateAdminFieldsAsync(
        long id,
        string displayName,
        bool enabled,
        bool allowUserSelect,
        string? description,
        string? userId,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_model
               SET display_name = @displayName,
                   enabled = @enabled,
                   allow_user_select = @allowUserSelect,
                   description = @description,
                   updated_by = @userId,
                   updated_at = now()
             WHERE id = @id;
            """,
            new { id, displayName, enabled, allowUserSelect, description, userId });
    }

    public async Task UpdateSyncFieldsAsync(
        long providerId,
        string providerModelCode,
        string? providerModelIdBase,
        string displayName,
        string mediaType,
        string? serverCode,
        string? providerStatus,
        string? statusMessage,
        string? rateType,
        decimal? baseProviderPrice,
        string? providerPriceUnit,
        string? source,
        DateTime? lastProviderSyncAt,
        DateTime? lastHealthCheckAt,
        DateTime? lastSuccessAt,
        DateTime? lastFailureAt,
        int failureCount,
        string? rawJson,
        string? userId,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO public.todox_ai_provider_model
                (provider_id, provider_model_code, provider_model_id_base, display_name, media_type, server_code,
                 provider_status, status_message, rate_type, base_provider_price, provider_price_unit, source,
                 last_provider_sync_at, last_health_check_at, last_success_at, last_failure_at, failure_count,
                 raw_json, enabled, allow_user_select, is_deprecated, created_by, updated_by, created_at, updated_at)
            VALUES
                (@providerId, @providerModelCode, @providerModelIdBase, @displayName, @mediaType, @serverCode,
                 @providerStatus, @statusMessage, @rateType, @baseProviderPrice, @providerPriceUnit, @source,
                 @lastProviderSyncAt, @lastHealthCheckAt, @lastSuccessAt, @lastFailureAt, @failureCount,
                 CAST(@rawJson AS jsonb), true, true, false, @userId, @userId, now(), now())
            ON CONFLICT (provider_id, provider_model_code)
            DO UPDATE SET
                provider_model_id_base = EXCLUDED.provider_model_id_base,
                display_name = EXCLUDED.display_name,
                media_type = EXCLUDED.media_type,
                server_code = EXCLUDED.server_code,
                provider_status = EXCLUDED.provider_status,
                status_message = EXCLUDED.status_message,
                rate_type = EXCLUDED.rate_type,
                base_provider_price = EXCLUDED.base_provider_price,
                provider_price_unit = EXCLUDED.provider_price_unit,
                source = EXCLUDED.source,
                last_provider_sync_at = EXCLUDED.last_provider_sync_at,
                last_health_check_at = EXCLUDED.last_health_check_at,
                last_success_at = EXCLUDED.last_success_at,
                last_failure_at = EXCLUDED.last_failure_at,
                failure_count = EXCLUDED.failure_count,
                raw_json = EXCLUDED.raw_json,
                updated_by = @userId,
                updated_at = now();
            """,
            new
            {
                providerId,
                providerModelCode,
                providerModelIdBase,
                displayName,
                mediaType,
                serverCode,
                providerStatus,
                statusMessage,
                rateType,
                baseProviderPrice,
                providerPriceUnit,
                source,
                lastProviderSyncAt,
                lastHealthCheckAt,
                lastSuccessAt,
                lastFailureAt,
                failureCount,
                rawJson,
                userId
            });
    }

    public async Task<IReadOnlyList<AiProviderModelCapabilityDto>> GetCapabilitiesAsync(long modelId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderModelCapabilityDto>(
            """
            SELECT id AS Id, model_id AS ModelId, capability_code AS CapabilityCode, enabled AS Enabled,
                   source AS Source, config_json AS ConfigJson, created_at AS CreatedAt, updated_at AS UpdatedAt
              FROM public.todox_ai_model_capability
             WHERE model_id = @modelId
             ORDER BY capability_code;
            """, new { modelId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AiModelPriceDto>> GetPricesAsync(long modelId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiModelPriceDto>(
            """
            SELECT id AS Id, model_id AS ModelId, mode AS Mode, resolution AS Resolution,
                   duration_seconds AS DurationSeconds, ratio AS Ratio, rate_type AS RateType,
                   unit_type AS UnitType, provider_price AS ProviderPrice,
                   provider_price_default AS ProviderPriceDefault, provider_price_unit AS ProviderPriceUnit,
                   internal_cost_points AS InternalCostPoints, sell_points AS SellPoints,
                   sell_price_mode AS SellPriceMode, markup_percent AS MarkupPercent,
                   minimum_points AS MinimumPoints, rounding_rule AS RoundingRule,
                   price_source AS PriceSource, effective_from AS EffectiveFrom,
                   effective_to AS EffectiveTo, active AS Active
              FROM public.todox_ai_model_price
             WHERE model_id = @modelId
             ORDER BY mode, resolution, duration_seconds, ratio, effective_from DESC;
            """, new { modelId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AiPricingPolicyDto>> GetPricingPoliciesAsync(long providerId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiPricingPolicyDto>(
            """
            SELECT id AS Id, provider_id AS ProviderId, policy_code AS PolicyCode, policy_name AS PolicyName,
                   provider_credit_per_internal_point AS ProviderCreditPerInternalPoint,
                   internal_point_value_vnd AS InternalPointValueVnd,
                   default_markup_percent AS DefaultMarkupPercent,
                   minimum_sell_points AS MinimumSellPoints, rounding_rule AS RoundingRule,
                   allow_auto_sell_update AS AllowAutoSellUpdate, enabled AS Enabled, is_default AS IsDefault
              FROM public.todox_ai_pricing_policy
             WHERE provider_id = @providerId
             ORDER BY is_default DESC, policy_name;
            """, new { providerId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AiProviderSyncHeaderDto>> GetSyncHistoryAsync(long providerId, int limit, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderSyncHeaderDto>(
            """
            SELECT id AS Id, provider_id AS ProviderId, provider_code AS ProviderCode, trigger_type AS Trigger,
                   status AS Status, triggered_by AS RequestedBy,
                   models_received AS ModelsReceived, models_inserted AS ModelInsertedCount,
                   models_updated AS ModelUpdatedCount, models_unavailable AS ModelUnavailableCount,
                   pricing_rows_received AS PricingRowsReceived, pricing_rows_changed AS PriceChangedCount,
                   capability_rows_changed AS CapabilityRowsChanged, error_message AS ErrorMessage,
                   started_at AS StartedAt, created_at AS CreatedAt, completed_at AS CompletedAt,
                   summary_json::text AS SummaryJson
              FROM public.todox_ai_provider_sync
             WHERE provider_id = @providerId
             ORDER BY created_at DESC
             LIMIT @limit;
            """, new { providerId, limit });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AiProviderSyncChangeDto>> GetSyncChangesAsync(Guid syncId, int limit, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderSyncChangeDto>(
            """
            SELECT id AS Id, sync_id AS SyncId, change_type AS ChangeType, entity_type AS EntityType,
                   entity_key AS EntityKey, model_id AS ModelId,
                   old_value_json::text AS BeforeJson, new_value_json::text AS AfterJson,
                   changed_fields AS ChangedFields, created_at AS CreatedAt
              FROM public.todox_ai_provider_sync_change
             WHERE sync_id = @syncId
             ORDER BY created_at ASC
             LIMIT @limit;
            """, new { syncId, limit });
        return rows.ToList();
    }

    public async Task<Guid> InsertSyncHeaderAsync(
        long providerId,
        string providerCode,
        string trigger,
        Guid? triggeredBy,
        string status,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO public.todox_ai_provider_sync
                (provider_id, provider_code, trigger_type, status, started_at,
                 models_received, models_inserted, models_updated, models_unavailable,
                 pricing_rows_received, pricing_rows_changed, capability_rows_changed,
                 triggered_by, error_message, summary_json, created_at)
            VALUES
                (@providerId, @providerCode, @trigger, @status, now(),
                 0, 0, 0, 0, 0, 0, 0,
                 @triggeredBy, NULL, '{}'::jsonb, now())
            RETURNING id;
            """,
            new { providerId, providerCode, trigger, status, triggeredBy });
    }

    public async Task CompleteSyncHeaderAsync(
        Guid syncId,
        string status,
        string? errorMessage,
        int modelsReceived,
        int inserted,
        int updated,
        int unavailable,
        int pricingRowsReceived,
        int priceChanged,
        int capabilityRowsChanged,
        string summaryJson,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_sync
               SET status = @status,
                   completed_at = now(),
                   models_received = @modelsReceived,
                   models_inserted = @inserted,
                   models_updated = @updated,
                   models_unavailable = @unavailable,
                   pricing_rows_received = @pricingRowsReceived,
                   pricing_rows_changed = @priceChanged,
                   capability_rows_changed = @capabilityRowsChanged,
                   error_message = @errorMessage,
                   summary_json = CAST(@summaryJson AS jsonb)
             WHERE id = @syncId;
            """,
            new { syncId, status, errorMessage, modelsReceived, inserted, updated, unavailable, pricingRowsReceived, priceChanged, capabilityRowsChanged, summaryJson });
    }

    public async Task InsertSyncChangeAsync(
        Guid syncId,
        string changeType,
        string entityType,
        string entityKey,
        string? beforeJson,
        string? afterJson,
        CancellationToken ct = default,
        long? modelId = null,
        IReadOnlyList<string>? changedFields = null)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO public.todox_ai_provider_sync_change
                (sync_id, entity_type, entity_key, change_type, model_id,
                 old_value_json, new_value_json, changed_fields, created_at)
            VALUES
                (@syncId, @entityType, @entityKey, @changeType, @modelId,
                 CASE WHEN @beforeJson IS NULL THEN NULL ELSE CAST(@beforeJson AS jsonb) END,
                 CASE WHEN @afterJson IS NULL THEN NULL ELSE CAST(@afterJson AS jsonb) END,
                 COALESCE(@changedFields, ARRAY[]::text[]), now());
            """,
            new { syncId, changeType, entityType, entityKey, beforeJson, afterJson, modelId, changedFields = changedFields?.ToArray() ?? Array.Empty<string>() });
    }

    public async Task<long> UpsertModelAsync(AiProviderModelDetailDto model, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var modelId = await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO public.todox_ai_provider_model
                (provider_id, provider_model_code, provider_model_id_base, display_name, media_type, server_code,
                 provider_status, status_message, rate_type, base_provider_price, provider_price_unit, description,
                 enabled, allow_user_select, is_deprecated, source, last_provider_sync_at, last_health_check_at,
                 last_success_at, last_failure_at, failure_count, raw_json, created_by, updated_by, created_at, updated_at)
            VALUES
                (@ProviderId, @ProviderModelCode, @ProviderModelIdBase, @DisplayName, @MediaType, @ServerCode,
                 @ProviderStatus, @StatusMessage, @RateType, @BaseProviderPrice, @ProviderPriceUnit, @Description,
                 @Enabled, @AllowUserSelect, @IsDeprecated, @Source, @LastProviderSyncAt, @LastHealthCheckAt,
                 @LastSuccessAt, @LastFailureAt, @FailureCount, CAST(@RawJson AS jsonb), @userId, @userId, now(), now())
            ON CONFLICT (provider_id, provider_model_code)
            DO UPDATE SET
                provider_model_id_base = EXCLUDED.provider_model_id_base,
                display_name = EXCLUDED.display_name,
                media_type = EXCLUDED.media_type,
                server_code = EXCLUDED.server_code,
                provider_status = EXCLUDED.provider_status,
                status_message = EXCLUDED.status_message,
                rate_type = EXCLUDED.rate_type,
                base_provider_price = EXCLUDED.base_provider_price,
                provider_price_unit = EXCLUDED.provider_price_unit,
                description = EXCLUDED.description,
                enabled = EXCLUDED.enabled,
                allow_user_select = EXCLUDED.allow_user_select,
                is_deprecated = EXCLUDED.is_deprecated,
                source = EXCLUDED.source,
                last_provider_sync_at = EXCLUDED.last_provider_sync_at,
                last_health_check_at = EXCLUDED.last_health_check_at,
                last_success_at = EXCLUDED.last_success_at,
                last_failure_at = EXCLUDED.last_failure_at,
                failure_count = EXCLUDED.failure_count,
                raw_json = EXCLUDED.raw_json,
                updated_by = @userId,
                updated_at = now()
            RETURNING id;
            """,
            new
            {
                model.ProviderId,
                model.ProviderModelCode,
                model.ProviderModelIdBase,
                model.DisplayName,
                model.MediaType,
                model.ServerCode,
                model.ProviderStatus,
                model.StatusMessage,
                model.RateType,
                model.BaseProviderPrice,
                model.ProviderPriceUnit,
                model.Description,
                model.Enabled,
                model.AllowUserSelect,
                model.IsDeprecated,
                model.Source,
                model.LastProviderSyncAt,
                model.LastHealthCheckAt,
                model.LastSuccessAt,
                model.LastFailureAt,
                model.FailureCount,
                model.RawJson,
                userId
            }, tx);

        foreach (var capability in model.ModelCapabilities)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.todox_ai_model_capability
                    (model_id, capability_code, enabled, source, config_json, created_by, updated_by, created_at, updated_at)
                VALUES
                    (@modelId, @CapabilityCode, @Enabled, @Source, CAST(@ConfigJson AS jsonb), @userId, @userId, now(), now())
                ON CONFLICT (model_id, capability_code)
                DO UPDATE SET enabled = EXCLUDED.enabled,
                              source = EXCLUDED.source,
                              config_json = EXCLUDED.config_json,
                              updated_by = @userId,
                              updated_at = now();
                """,
                new
                {
                    modelId,
                    capability.CapabilityCode,
                    capability.Enabled,
                    capability.Source,
                    capability.ConfigJson,
                    userId
                }, tx);
        }

        foreach (var price in model.Prices)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.todox_ai_model_price
                    (model_id, mode, resolution, duration_seconds, ratio, rate_type, unit_type, provider_price,
                     provider_price_default, provider_price_unit, internal_cost_points, sell_points, sell_price_mode,
                     markup_percent, minimum_points, rounding_rule, price_source, effective_from, effective_to, active,
                     created_by, updated_by, created_at, updated_at)
                VALUES
                    (@modelId, @Mode, @Resolution, @DurationSeconds, @Ratio, @RateType, @UnitType, @ProviderPrice,
                     @ProviderPriceDefault, @ProviderPriceUnit, @InternalCostPoints, @SellPoints, @SellPriceMode,
                     @MarkupPercent, @MinimumPoints, @RoundingRule, @PriceSource, @EffectiveFrom, @EffectiveTo, @Active,
                     @userId, @userId, now(), now())
                ON CONFLICT (model_id, mode, resolution, duration_seconds, ratio)
                DO UPDATE SET rate_type = EXCLUDED.rate_type,
                              unit_type = EXCLUDED.unit_type,
                              provider_price = EXCLUDED.provider_price,
                              provider_price_default = EXCLUDED.provider_price_default,
                              provider_price_unit = EXCLUDED.provider_price_unit,
                              internal_cost_points = EXCLUDED.internal_cost_points,
                              price_source = EXCLUDED.price_source,
                              effective_from = EXCLUDED.effective_from,
                              effective_to = EXCLUDED.effective_to,
                              active = EXCLUDED.active,
                              updated_by = @userId,
                              updated_at = now();
                """,
                new
                {
                    modelId,
                    price.Mode,
                    price.Resolution,
                    price.DurationSeconds,
                    price.Ratio,
                    price.RateType,
                    price.UnitType,
                    price.ProviderPrice,
                    price.ProviderPriceDefault,
                    price.ProviderPriceUnit,
                    price.InternalCostPoints,
                    price.SellPoints,
                    price.SellPriceMode,
                    price.MarkupPercent,
                    price.MinimumPoints,
                    price.RoundingRule,
                    price.PriceSource,
                    price.EffectiveFrom,
                    price.EffectiveTo,
                    price.Active,
                    userId
                }, tx);
        }

        tx.Commit();
        return modelId;
    }

    public async Task MarkMissingAsDeprecatedAsync(long providerId, IReadOnlyCollection<string> providerModelCodes, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_model
               SET is_deprecated = true,
                   provider_status = COALESCE(provider_status, 'DEPRECATED'),
                   status_message = COALESCE(status_message, 'Model missing from latest catalog sync.'),
                   updated_by = @userId,
                   updated_at = now()
             WHERE provider_id = @providerId
               AND NOT (provider_model_code = ANY(@providerModelCodes));
            """,
            new { providerId, providerModelCodes = providerModelCodes.ToArray(), userId });
    }

    private async Task<AiModelPriceSummaryDto> BuildPriceSummaryAsync(IDbConnection conn, long modelId, CancellationToken ct)
    {
        var prices = (await conn.QueryAsync<AiModelPriceDto>(
            """
            SELECT id AS Id, model_id AS ModelId, mode AS Mode, resolution AS Resolution,
                   duration_seconds AS DurationSeconds, ratio AS Ratio, rate_type AS RateType,
                   unit_type AS UnitType, provider_price AS ProviderPrice,
                   provider_price_default AS ProviderPriceDefault, provider_price_unit AS ProviderPriceUnit,
                   internal_cost_points AS InternalCostPoints, sell_points AS SellPoints,
                   sell_price_mode AS SellPriceMode, markup_percent AS MarkupPercent,
                   minimum_points AS MinimumPoints, rounding_rule AS RoundingRule,
                   price_source AS PriceSource, effective_from AS EffectiveFrom,
                   effective_to AS EffectiveTo, active AS Active
              FROM public.todox_ai_model_price
             WHERE model_id = @modelId
               AND active = true;
            """, new { modelId })).ToList();

        return new AiModelPriceSummaryDto
        {
            ActiveVariantCount = prices.Count,
            ProviderPrice = prices.Where(x => x.ProviderPrice.HasValue).Select(x => x.ProviderPrice).Min(),
            InternalCostPoints = prices.Where(x => x.InternalCostPoints.HasValue).Select(x => x.InternalCostPoints).Min(),
            SellPoints = prices.Where(x => x.SellPoints.HasValue).Select(x => x.SellPoints).Min(),
            SellPriceMode = prices.FirstOrDefault()?.SellPriceMode,
            StatusMessage = prices.Count == 0 ? "Chưa có bảng giá / cần đồng bộ" : null
        };
    }

    private static async Task PopulateOptionsAsync(IDbConnection conn, AiProviderModelListItemDto model, CancellationToken ct)
    {
        var rawJson = await conn.ExecuteScalarAsync<string?>(
            "SELECT raw_json::text FROM public.todox_ai_provider_model WHERE id = @modelId;",
            new { modelId = model.Id });
        var prices = (await conn.QueryAsync<AiModelPriceDto>(
            """
            SELECT mode AS Mode, resolution AS Resolution, duration_seconds AS DurationSeconds,
                   ratio AS Ratio, active AS Active
              FROM public.todox_ai_model_price
             WHERE model_id = @modelId
               AND active = true;
            """,
            new { modelId = model.Id })).ToList();
        var options = AiProviderModelOptionsNormalizer.Normalize(null, null, null, null, prices, rawJson);
        model.SupportedModes = options.Modes;
        model.SupportedDurations = options.Durations;
        model.SupportedResolutions = options.Resolutions;
        model.SupportedRatios = options.Ratios;
    }
}
