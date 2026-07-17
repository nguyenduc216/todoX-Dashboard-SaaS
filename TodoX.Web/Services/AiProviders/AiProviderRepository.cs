using System.Data;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

/// <summary>
/// Dapper access to the pre-existing public.todox_ai_provider* tables.
/// No schema changes — columns are read/written exactly as they exist.
/// </summary>
public sealed class AiProviderRepository
{
    private readonly TodoXConnectionFactory _factory;

    public AiProviderRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    private const string ProviderColumns = """
        p.id AS Id,
        p.provider_code AS ProviderCode,
        p.provider_name AS ProviderName,
        p.provider_type AS ProviderType,
        p.base_url AS BaseUrl,
        p.api_key_config_name AS ApiKeyConfigName,
        p.enabled AS Enabled,
        p.is_system AS IsSystem,
        p.priority AS Priority,
        p.description AS Description,
        p.config_json AS ConfigJson,
        p.created_at AS CreatedAt,
        p.updated_at AS UpdatedAt
        """;

    private const string CapabilityColumns = """
        c.id AS Id,
        c.provider_id AS ProviderId,
        c.provider_code AS ProviderCode,
        c.capability_code AS CapabilityCode,
        c.display_name AS DisplayName,
        c.model_name AS ModelName,
        c.endpoint_path AS EndpointPath,
        c.unit_type AS UnitType,
        c.unit_cost_points AS UnitCostPoints,
        c.is_default AS IsDefault,
        c.enabled AS Enabled,
        c.allow_user_select AS AllowUserSelect,
        c.config_json AS ConfigJson,
        c.created_at AS CreatedAt,
        c.updated_at AS UpdatedAt
        """;

    public async Task<IReadOnlyList<AiProviderListItemDto>> ListProvidersAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AiProviderListItemDto>(
            $"""
            SELECT p.id AS Id,
                   p.provider_code AS ProviderCode,
                   p.provider_name AS ProviderName,
                   p.provider_type AS ProviderType,
                   p.base_url AS BaseUrl,
                   p.api_key_config_name AS ApiKeyConfigName,
                   p.enabled AS Enabled,
                   p.is_system AS IsSystem,
                   p.priority AS Priority,
                   p.description AS Description,
                   p.created_at AS CreatedAt,
                   p.updated_at AS UpdatedAt,
                   COALESCE(cap.total, 0) AS CapabilityCount,
                   COALESCE(cap.enabled_total, 0) AS EnabledCapabilityCount
              FROM public.todox_ai_provider p
              LEFT JOIN (
                    SELECT provider_id,
                           COUNT(*) AS total,
                           COUNT(*) FILTER (WHERE enabled) AS enabled_total
                      FROM public.todox_ai_provider_capability
                     GROUP BY provider_id
              ) cap ON cap.provider_id = p.id
             ORDER BY p.priority, p.provider_name;
            """);
        return rows.ToList();
    }

    public async Task<AiProviderDetailDto?> GetProviderAsync(long id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var provider = await conn.QuerySingleOrDefaultAsync<AiProviderDetailDto>(
            $"SELECT {ProviderColumns} FROM public.todox_ai_provider p WHERE p.id = @id LIMIT 1;", new { id });
        if (provider is null) return null;
        provider.Capabilities = (await LoadCapabilitiesAsync(conn, provider.Id, null)).ToList();
        return provider;
    }

    public async Task<AiProviderDetailDto?> GetProviderByCodeAsync(string providerCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var provider = await conn.QuerySingleOrDefaultAsync<AiProviderDetailDto>(
            $"SELECT {ProviderColumns} FROM public.todox_ai_provider p WHERE p.provider_code = @providerCode LIMIT 1;",
            new { providerCode });
        if (provider is null) return null;
        provider.Capabilities = (await LoadCapabilitiesAsync(conn, provider.Id, null)).ToList();
        return provider;
    }

    public async Task UpdateProviderAsync(long id, UpdateAiProviderRequest request, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider
               SET provider_name = @ProviderName,
                   base_url = @BaseUrl,
                   api_key_config_name = @ApiKeyConfigName,
                   enabled = @Enabled,
                   priority = @Priority,
                   description = @Description,
                   config_json = CAST(@ConfigJson AS jsonb),
                   updated_by = @userId,
                   updated_at = now()
             WHERE id = @id;
            """,
            new
            {
                id,
                request.ProviderName,
                request.BaseUrl,
                request.ApiKeyConfigName,
                request.Enabled,
                request.Priority,
                request.Description,
                ConfigJson = NullIfBlank(request.ConfigJson),
                userId
            });
    }

    public async Task<IReadOnlyList<AiProviderCapabilityDto>> GetCapabilitiesAsync(long? providerId, string? capabilityCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return (await LoadCapabilitiesAsync(conn, providerId, capabilityCode)).ToList();
    }

    public async Task<AiProviderCapabilityDto?> GetCapabilityAsync(long id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiProviderCapabilityDto>(
            $"SELECT {CapabilityColumns} FROM public.todox_ai_provider_capability c WHERE c.id = @id LIMIT 1;", new { id });
    }

    public async Task UpdateCapabilityAsync(long id, UpdateAiProviderCapabilityRequest request, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_capability
               SET display_name = @DisplayName,
                   model_name = @ModelName,
                   endpoint_path = @EndpointPath,
                   unit_type = @UnitType,
                   unit_cost_points = @UnitCostPoints,
                   enabled = @Enabled,
                   allow_user_select = @AllowUserSelect,
                   config_json = CAST(@ConfigJson AS jsonb),
                   updated_by = @userId,
                   updated_at = now()
             WHERE id = @id;
            """,
            new
            {
                id,
                request.DisplayName,
                request.ModelName,
                request.EndpointPath,
                request.UnitType,
                request.UnitCostPoints,
                request.Enabled,
                request.AllowUserSelect,
                ConfigJson = NullIfBlank(request.ConfigJson),
                userId
            });
    }

    /// <summary>
    /// Marks one capability as default for its capability_code. Enforces exactly one default per
    /// capability_code (unsets siblings). Only allowed when provider + capability are both enabled.
    /// </summary>
    public async Task SetDefaultCapabilityAsync(long capabilityId, string? userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var target = await conn.QuerySingleOrDefaultAsync<(string CapabilityCode, bool Enabled, bool ProviderEnabled)?>(
            """
            SELECT c.capability_code AS CapabilityCode, c.enabled AS Enabled, p.enabled AS ProviderEnabled
              FROM public.todox_ai_provider_capability c
              JOIN public.todox_ai_provider p ON p.id = c.provider_id
             WHERE c.id = @capabilityId
             LIMIT 1;
            """, new { capabilityId });

        if (target is null)
        {
            throw new InvalidOperationException("Không tìm thấy capability để đặt mặc định.");
        }

        if (!target.Value.Enabled || !target.Value.ProviderEnabled)
        {
            throw new InvalidOperationException("Chỉ đặt mặc định khi provider và capability đều đang được bật.");
        }

        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_capability
               SET is_default = false, updated_by = @userId, updated_at = now()
             WHERE capability_code = @code AND is_default = true AND id <> @capabilityId;
            """, new { code = target.Value.CapabilityCode, capabilityId, userId }, tx);

        await conn.ExecuteAsync(
            """
            UPDATE public.todox_ai_provider_capability
               SET is_default = true, updated_by = @userId, updated_at = now()
             WHERE id = @capabilityId;
            """, new { capabilityId, userId }, tx);

        tx.Commit();
    }

    public async Task SetDefaultCapabilitiesAsync(IReadOnlyList<long> capabilityIds, string? userId, CancellationToken ct = default)
    {
        if (capabilityIds.Count == 0)
        {
            return;
        }

        using var conn = await _factory.OpenAsync(ct);
        var targets = (await conn.QueryAsync<(long Id, string CapabilityCode, bool Enabled, bool ProviderEnabled)>(
            """
            SELECT c.id AS Id,
                   c.capability_code AS CapabilityCode,
                   c.enabled AS Enabled,
                   p.enabled AS ProviderEnabled
              FROM public.todox_ai_provider_capability c
              JOIN public.todox_ai_provider p ON p.id = c.provider_id
             WHERE c.id = ANY(@capabilityIds);
            """, new { capabilityIds = capabilityIds.ToArray() })).ToList();

        if (targets.Count != capabilityIds.Distinct().Count())
        {
            throw new InvalidOperationException("Một hoặc nhiều model đã chọn không còn tồn tại.");
        }

        var disabled = targets.FirstOrDefault(x => !x.Enabled || !x.ProviderEnabled);
        if (disabled != default)
        {
            throw new InvalidOperationException("Chỉ đặt mặc định khi provider và capability đều đang được bật.");
        }

        var duplicateCapability = targets
            .GroupBy(x => x.CapabilityCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateCapability is not null)
        {
            throw new InvalidOperationException($"Không thể lưu nhiều model mặc định cho cùng capability {duplicateCapability.Key}.");
        }

        using var tx = conn.BeginTransaction();
        foreach (var target in targets)
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.todox_ai_provider_capability
                   SET is_default = false, updated_by = @userId, updated_at = now()
                 WHERE capability_code = @code AND is_default = true AND id <> @capabilityId;
                """, new { code = target.CapabilityCode, capabilityId = target.Id, userId }, tx);

            await conn.ExecuteAsync(
                """
                UPDATE public.todox_ai_provider_capability
                   SET is_default = true, updated_by = @userId, updated_at = now()
                 WHERE id = @capabilityId;
                """, new { capabilityId = target.Id, userId }, tx);
        }

        tx.Commit();
    }

    public async Task<IReadOnlyList<ProviderOptionDto>> GetSelectableAsync(string capabilityCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ProviderOptionDto>(
            $"""
            {ProviderOptionSelect}
             WHERE c.capability_code = @capabilityCode
               AND c.enabled = true
               AND c.allow_user_select = true
               AND p.enabled = true
             ORDER BY c.is_default DESC, p.priority, p.provider_name;
            """, new { capabilityCode });
        return rows.ToList();
    }

    public async Task<ProviderOptionDto?> GetDefaultAsync(string capabilityCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderOptionDto>(
            $"""
            {ProviderOptionSelect}
             WHERE c.capability_code = @capabilityCode
               AND c.enabled = true
               AND c.is_default = true
               AND p.enabled = true
             ORDER BY p.priority
             LIMIT 1;
            """, new { capabilityCode });
    }

    public async Task<ProviderOptionDto?> GetFirstByPriorityAsync(string capabilityCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderOptionDto>(
            $"""
            {ProviderOptionSelect}
             WHERE c.capability_code = @capabilityCode
               AND c.enabled = true
               AND p.enabled = true
             ORDER BY p.priority, p.provider_name
             LIMIT 1;
            """, new { capabilityCode });
    }

    public async Task<ProviderOptionDto?> GetOptionByCapabilityIdAsync(long capabilityId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<ProviderOptionDto>(
            $"""
            {ProviderOptionSelect}
             WHERE c.id = @capabilityId
             LIMIT 1;
            """, new { capabilityId });
    }

    public async Task<long> InsertUsageLogAsync(AiProviderUsageLog log, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO public.todox_ai_provider_usage_log
                (customer_id, provider_id, provider_capability_id, provider_code, capability_code, feature_code,
                 model_name, request_id, job_id, quantity, unit_type, unit_cost_points, total_points, provider_raw_cost,
                 status, error_message, metadata_json, created_by, created_at)
            VALUES
                (@CustomerId, @ProviderId, @ProviderCapabilityId, @ProviderCode, @CapabilityCode, @FeatureCode,
                 @ModelName, @RequestId, @JobId, @Quantity, @UnitType, @UnitCostPoints, @TotalPoints, @ProviderRawCost,
                 @Status, @ErrorMessage, CAST(@MetadataJson AS jsonb), @CreatedBy, now())
            RETURNING id;
            """,
            new
            {
                log.CustomerId,
                log.ProviderId,
                log.ProviderCapabilityId,
                log.ProviderCode,
                log.CapabilityCode,
                log.FeatureCode,
                log.ModelName,
                log.RequestId,
                log.JobId,
                log.Quantity,
                log.UnitType,
                log.UnitCostPoints,
                log.TotalPoints,
                log.ProviderRawCost,
                log.Status,
                log.ErrorMessage,
                MetadataJson = NullIfBlank(log.MetadataJson),
                log.CreatedBy
            });
    }

    private static async Task<IEnumerable<AiProviderCapabilityDto>> LoadCapabilitiesAsync(IDbConnection conn, long? providerId, string? capabilityCode)
    {
        var where = "WHERE 1=1";
        if (providerId is not null) where += " AND c.provider_id = @providerId";
        if (!string.IsNullOrWhiteSpace(capabilityCode)) where += " AND c.capability_code = @capabilityCode";
        return await conn.QueryAsync<AiProviderCapabilityDto>(
            $"""
            SELECT {CapabilityColumns}
              FROM public.todox_ai_provider_capability c
              {where}
             ORDER BY c.capability_code, c.is_default DESC, c.display_name;
            """, new { providerId, capabilityCode });
    }

    private const string ProviderOptionSelect = """
        SELECT c.id AS ProviderCapabilityId,
               p.id AS ProviderId,
               p.provider_code AS ProviderCode,
               p.provider_name AS ProviderName,
               c.capability_code AS CapabilityCode,
               c.display_name AS DisplayName,
               c.model_name AS ModelName,
               c.unit_type AS UnitType,
               c.unit_cost_points AS UnitCostPoints,
               c.is_default AS IsDefault,
               c.enabled AS Enabled,
               c.allow_user_select AS AllowUserSelect
          FROM public.todox_ai_provider_capability c
          JOIN public.todox_ai_provider p ON p.id = c.provider_id
        """;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
