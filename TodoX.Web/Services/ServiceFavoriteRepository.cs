using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services;

public sealed class ServiceFavoriteRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public ServiceFavoriteRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<Guid>> GetFavoriteServiceIdsAsync(Guid userId, Guid customerId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<Guid>(
            """
            SELECT service_id
              FROM crm.customer_service_favorites
             WHERE tenant_id = @tenant
               AND user_id = @userId
               AND customer_id = @customerId
             ORDER BY created_at, service_id;
            """,
            new { tenant = _tenant.TenantId, userId, customerId });

        return rows.ToList();
    }

    public async Task<IReadOnlyList<CatalogServiceView>> GetFavoriteServicesAsync(Guid userId, Guid customerId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CatalogServiceView>(
            """
            SELECT s.id AS Id,
                   s.service_code AS ServiceCode,
                   s.service_name AS DisplayName,
                   COALESCE(NULLIF(s.short_description, ''), s.description) AS Description,
                   s.service_type AS ServiceType,
                   s.workflow_code AS WorkflowCode,
                   s.thumbnail_url AS ThumbnailUrl,
                   s.cover_image_url AS CoverImageUrl,
                   COALESCE(s.default_options->'job_defaults', '{}'::jsonb)::text AS JobDefaultsJson,
                   (
                       SELECT string_agg(summary_text, ' · ' ORDER BY sort_key)
                       FROM (
                           SELECT 1 AS sort_key, 'Từ ' || min(p.sell_points)::text || ' điểm / hình' AS summary_text
                             FROM catalog.service_sell_prices p
                            WHERE p.service_id = s.id
                              AND p.asset_type = 'image'
                              AND p.is_active = true
                           HAVING count(*) > 0
                           UNION ALL
                           SELECT 2 AS sort_key, 'Từ ' || min(p.sell_points)::text || ' điểm / scene' AS summary_text
                             FROM catalog.service_sell_prices p
                            WHERE p.service_id = s.id
                              AND p.asset_type = 'video_scene'
                              AND p.is_active = true
                           HAVING count(*) > 0
                       ) prices
                   ) AS StartingPriceSummary,
                   CASE WHEN lower(s.status) = 'active' THEN true ELSE false END AS Enabled,
                   s.sort_order AS SortOrder
              FROM crm.customer_service_favorites f
              JOIN catalog.services s ON s.id = f.service_id
             WHERE f.tenant_id = @tenant
               AND f.user_id = @userId
               AND f.customer_id = @customerId
               AND lower(s.status) = 'active'
             ORDER BY s.sort_order, s.service_name, s.service_code;
            """,
            new { tenant = _tenant.TenantId, userId, customerId });

        return rows.ToList();
    }

    public async Task<bool> IsFavoriteAsync(Guid userId, Guid customerId, Guid serviceId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS(
                SELECT 1
                  FROM crm.customer_service_favorites
                 WHERE tenant_id = @tenant
                   AND user_id = @userId
                   AND customer_id = @customerId
                   AND service_id = @serviceId
            );
            """,
            new { tenant = _tenant.TenantId, userId, customerId, serviceId });
    }

    public async Task ToggleFavoriteAsync(
        Guid userId,
        Guid customerId,
        Guid serviceId,
        string addedSource,
        Guid? createdByUserId = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            await EnsureCustomerUserScopeAsync(conn, tx, _tenant.TenantId, userId, customerId);
            await EnsureServiceExistsAsync(conn, tx, serviceId);

            var exists = await conn.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS(
                    SELECT 1
                      FROM crm.customer_service_favorites
                     WHERE tenant_id = @tenant
                       AND user_id = @userId
                       AND customer_id = @customerId
                       AND service_id = @serviceId
                );
                """,
                new { tenant = _tenant.TenantId, userId, customerId, serviceId }, tx);

            if (exists)
            {
                await conn.ExecuteAsync(
                    """
                    DELETE FROM crm.customer_service_favorites
                     WHERE tenant_id = @tenant
                       AND user_id = @userId
                       AND customer_id = @customerId
                       AND service_id = @serviceId;
                    """,
                    new { tenant = _tenant.TenantId, userId, customerId, serviceId }, tx);
            }
            else
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO crm.customer_service_favorites
                        (id, tenant_id, customer_id, user_id, service_id, added_source, created_by_user_id, created_at)
                    VALUES
                        (gen_random_uuid(), @tenant, @customerId, @userId, @serviceId, @addedSource, @createdByUserId, now())
                    ON CONFLICT (tenant_id, user_id, service_id) DO NOTHING;
                    """,
                    new
                    {
                        tenant = _tenant.TenantId,
                        customerId,
                        userId,
                        serviceId,
                        addedSource,
                        createdByUserId
                    }, tx);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task ReplaceFavoritesAsync(
        Guid userId,
        Guid customerId,
        IEnumerable<Guid> serviceIds,
        string addedSource,
        Guid? createdByUserId = null)
    {
        await _tenant.EnsureLoadedAsync();
        var selected = serviceIds.Where(x => x != Guid.Empty).Distinct().ToArray();

        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            await EnsureCustomerUserScopeAsync(conn, tx, _tenant.TenantId, userId, customerId);
            await EnsureServiceIdsExistAsync(conn, tx, selected);

            await conn.ExecuteAsync(
                """
                DELETE FROM crm.customer_service_favorites
                 WHERE tenant_id = @tenant
                   AND user_id = @userId
                   AND customer_id = @customerId
                   AND service_id <> ALL(@serviceIds);
                """,
                new { tenant = _tenant.TenantId, userId, customerId, serviceIds = selected }, tx);

            if (selected.Length > 0)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO crm.customer_service_favorites
                        (id, tenant_id, customer_id, user_id, service_id, added_source, created_by_user_id, created_at)
                    SELECT gen_random_uuid(), @tenant, @customerId, @userId, s.service_id, @addedSource, @createdByUserId, now()
                      FROM unnest(@serviceIds::uuid[]) AS s(service_id)
                    ON CONFLICT (tenant_id, user_id, service_id) DO NOTHING;
                    """,
                    new
                    {
                        tenant = _tenant.TenantId,
                        customerId,
                        userId,
                        serviceIds = selected,
                        addedSource,
                        createdByUserId
                    }, tx);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static async Task EnsureCustomerUserScopeAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid tenantId,
        Guid userId,
        Guid customerId)
    {
        var exists = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS(
                SELECT 1
                  FROM crm.customer_users cu
                  JOIN auth.app_users u ON u.id = cu.user_id
                 WHERE u.tenant_id = @tenant
                   AND cu.user_id = @userId
                   AND cu.customer_id = @customerId
            );
            """,
            new { tenant = tenantId, userId, customerId }, tx);

        if (!exists)
        {
            throw new InvalidOperationException("Customer account scope is invalid for the current tenant.");
        }
    }

    private static async Task EnsureServiceExistsAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid serviceId)
    {
        var exists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM catalog.services WHERE id = @serviceId);",
            new { serviceId }, tx);

        if (!exists)
        {
            throw new InvalidOperationException("Service does not exist.");
        }
    }

    private static async Task EnsureServiceIdsExistAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, IReadOnlyCollection<Guid> serviceIds)
    {
        if (serviceIds.Count == 0)
        {
            return;
        }

        var found = await conn.QueryAsync<Guid>(
            "SELECT id FROM catalog.services WHERE id = ANY(@serviceIds);",
            new { serviceIds }, tx);

        var foundSet = found.ToHashSet();
        if (foundSet.Count != serviceIds.Count || !serviceIds.All(foundSet.Contains))
        {
            throw new InvalidOperationException("One or more services do not exist.");
        }
    }
}
