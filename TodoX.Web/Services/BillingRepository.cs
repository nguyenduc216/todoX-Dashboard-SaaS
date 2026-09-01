using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services;

public sealed record WalletView(Guid Id, Guid CustomerId, string CustomerName,
    decimal Balance, decimal LockedBalance, string Status);

public sealed record TransactionView(Guid Id, string CustomerName, string TransactionType,
    decimal Amount, decimal BalanceAfter, string? Description, DateTime CreatedAt);

public sealed record TokenSummary(decimal TotalBalance, decimal TotalLocked, int WalletCount, decimal SoldTotal);
public sealed record PointRateConfigView(string ResourceType, string QualityTier, decimal Rate, string Unit, bool IsActive, string? Description);
public sealed record PointVoucherView(Guid Id, string VoucherCode, decimal PointAmount, string Status, int? MaxRedemptions, int RedeemedCount, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil);
public sealed record PointVoucherRedemptionView(Guid Id, string VoucherCode, string CustomerName, decimal Points, Guid TransactionId, DateTimeOffset RedeemedAt);
public sealed record ServicePointRateView(string ResourceType, string QualityTier, decimal? OverrideRate, decimal GlobalRate, decimal EffectiveRate, string Unit, string Source);

/// <summary>Read access to billing.token_wallets / billing.token_transactions (Foundation V2).</summary>
public sealed class BillingRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public BillingRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<TokenSummary> GetSummaryAsync(Guid? customerId = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var wallet = await conn.QuerySingleAsync<(decimal? bal, decimal? locked, int cnt)>(
            """
            SELECT COALESCE(sum(balance),0) AS bal, COALESCE(sum(locked_balance),0) AS locked, count(*) AS cnt
              FROM billing.token_wallets WHERE tenant_id=@tenant AND (@cid IS NULL OR customer_id=@cid);
            """, new { tenant = _tenant.TenantId, cid = customerId });

        var sold = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT COALESCE(sum(t.amount),0) FROM billing.token_transactions t
              JOIN billing.token_wallets w ON w.id = t.wallet_id
             WHERE t.tenant_id=@tenant AND t.transaction_type IN ('credit','purchase','topup')
               AND (@cid IS NULL OR w.customer_id=@cid);
            """, new { tenant = _tenant.TenantId, cid = customerId });

        return new TokenSummary(wallet.bal ?? 0, wallet.locked ?? 0, wallet.cnt, sold ?? 0);
    }

    public async Task<IReadOnlyList<WalletView>> GetWalletsAsync(Guid? customerId = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<WalletView>(
            """
            SELECT w.id AS Id, w.customer_id AS CustomerId,
                   COALESCE(NULLIF(c.company_name,''), c.full_name) AS CustomerName,
                   w.balance AS Balance, w.locked_balance AS LockedBalance, w.status AS Status
              FROM billing.token_wallets w
              JOIN crm.customers c ON c.id = w.customer_id
             WHERE w.tenant_id = @tenant AND (@cid IS NULL OR w.customer_id=@cid)
             ORDER BY w.balance DESC;
            """, new { tenant = _tenant.TenantId, cid = customerId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<TransactionView>> GetRecentTransactionsAsync(Guid? customerId = null, int limit = 20)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<TransactionView>(
            """
            SELECT t.id AS Id,
                   COALESCE(NULLIF(c.company_name,''), c.full_name) AS CustomerName,
                   t.transaction_type AS TransactionType, t.amount AS Amount,
                   t.balance_after AS BalanceAfter, t.description AS Description,
                   t.created_at AS CreatedAt
              FROM billing.token_transactions t
              JOIN billing.token_wallets w ON w.id = t.wallet_id
              JOIN crm.customers c ON c.id = w.customer_id
             WHERE t.tenant_id = @tenant AND (@cid IS NULL OR w.customer_id=@cid)
             ORDER BY t.created_at DESC
             LIMIT @limit;
            """, new { tenant = _tenant.TenantId, cid = customerId, limit });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PointRateConfigView>> GetPointRatesAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<PointRateConfigView>(
            """
            SELECT resource_type AS ResourceType,
                   quality_tier AS QualityTier,
                   rate AS Rate,
                   unit AS Unit,
                   is_active AS IsActive,
                   description AS Description
              FROM billing.point_rate_config
             WHERE tenant_id=@tenant
             ORDER BY resource_type, quality_tier;
            """,
            new { tenant = _tenant.TenantId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PointVoucherView>> GetPointVouchersAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<PointVoucherView>(
            """
            SELECT id AS Id,
                   voucher_code AS VoucherCode,
                   point_amount AS PointAmount,
                   status AS Status,
                   max_redemptions AS MaxRedemptions,
                   redeemed_count AS RedeemedCount,
                   valid_from AS ValidFrom,
                   valid_until AS ValidUntil
              FROM billing.point_vouchers
             WHERE tenant_id=@tenant
             ORDER BY created_at DESC;
            """,
            new { tenant = _tenant.TenantId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PointVoucherRedemptionView>> GetPointVoucherRedemptionsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<PointVoucherRedemptionView>(
            """
            SELECT r.id AS Id,
                   v.voucher_code AS VoucherCode,
                   COALESCE(NULLIF(c.company_name,''), c.full_name) AS CustomerName,
                   r.points AS Points,
                   r.transaction_id AS TransactionId,
                   r.redeemed_at AS RedeemedAt
              FROM billing.point_voucher_redemptions r
              JOIN billing.point_vouchers v ON v.id = r.voucher_id
              JOIN crm.customers c ON c.id = r.customer_id
             WHERE v.tenant_id = @tenant
             ORDER BY r.redeemed_at DESC;
            """,
            new { tenant = _tenant.TenantId });
        return rows.ToList();
    }

    public async Task UpsertPointRateAsync(string resourceType, string qualityTier, decimal rate, Guid? actorUserId)
    {
        if (rate < 0)
        {
            throw new InvalidOperationException("Point rate cannot be negative.");
        }

        var resource = NormalizeResource(resourceType);
        var quality = NormalizeQuality(qualityTier);
        var unit = UnitFor(resource);

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.point_rate_config
                (id, tenant_id, resource_type, quality_tier, rate, unit, is_active, created_at, updated_at, created_by, updated_by)
            VALUES
                (gen_random_uuid(), @tenant, @resource, @quality, @rate, @unit, true, now(), now(), @actor, @actor)
            ON CONFLICT (tenant_id, resource_type, quality_tier)
            DO UPDATE SET rate=EXCLUDED.rate,
                          unit=EXCLUDED.unit,
                          is_active=true,
                          updated_at=now(),
                          updated_by=EXCLUDED.updated_by;
            """,
            new { tenant = _tenant.TenantId, resource, quality, rate, unit, actor = actorUserId });
    }

    public async Task<IReadOnlyList<ServicePointRateView>> GetServicePointRatesAsync(Guid serviceId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<ServicePointRateView>(
            """
            WITH expected(resource_type, quality_tier, unit) AS (
                VALUES ('image','standard','per_render'),
                       ('image','premium','per_render'),
                       ('video','standard','per_second'),
                       ('video','premium','per_second'),
                       ('voice','standard','per_render'),
                       ('voice','premium','per_render')
            )
            SELECT e.resource_type AS ResourceType,
                   e.quality_tier AS QualityTier,
                   o.rate AS OverrideRate,
                   g.rate AS GlobalRate,
                   COALESCE(o.rate, g.rate) AS EffectiveRate,
                   e.unit AS Unit,
                   CASE WHEN o.id IS NULL THEN 'global' ELSE 'service_override' END AS Source
              FROM expected e
              JOIN billing.point_rate_config g
                ON g.tenant_id=@tenant
               AND g.resource_type=e.resource_type
               AND g.quality_tier=e.quality_tier
               AND g.is_active
              LEFT JOIN billing.service_point_rate_override o
                ON o.tenant_id=@tenant
               AND o.service_id=@serviceId
               AND o.resource_type=e.resource_type
               AND o.quality_tier=e.quality_tier
               AND o.is_active
             ORDER BY e.resource_type, e.quality_tier;
            """,
            new { tenant = _tenant.TenantId, serviceId });
        return rows.ToList();
    }

    public async Task UpsertServicePointOverrideAsync(Guid serviceId, string resourceType, string qualityTier, decimal rate, Guid? actorUserId)
    {
        if (rate < 0)
        {
            throw new InvalidOperationException("Point override cannot be negative.");
        }

        var resource = NormalizeResource(resourceType);
        var quality = NormalizeQuality(qualityTier);
        var unit = UnitFor(resource);

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.service_point_rate_override
                (id, tenant_id, service_id, resource_type, quality_tier, rate, unit, is_active, created_at, updated_at, created_by, updated_by)
            VALUES
                (gen_random_uuid(), @tenant, @serviceId, @resource, @quality, @rate, @unit, true, now(), now(), @actor, @actor)
            ON CONFLICT (tenant_id, service_id, resource_type, quality_tier)
            DO UPDATE SET rate=EXCLUDED.rate,
                          unit=EXCLUDED.unit,
                          is_active=true,
                          updated_at=now(),
                          updated_by=EXCLUDED.updated_by;
            """,
            new { tenant = _tenant.TenantId, serviceId, resource, quality, rate, unit, actor = actorUserId });
    }

    public async Task RemoveServicePointOverrideAsync(Guid serviceId, string resourceType, string qualityTier, Guid? actorUserId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE billing.service_point_rate_override
               SET is_active=false,
                   updated_at=now(),
                   updated_by=@actor
             WHERE tenant_id=@tenant
               AND service_id=@serviceId
               AND resource_type=@resource
               AND quality_tier=@quality;
            """,
            new
            {
                tenant = _tenant.TenantId,
                serviceId,
                resource = NormalizeResource(resourceType),
                quality = NormalizeQuality(qualityTier),
                actor = actorUserId
            });
    }

    private static string NormalizeResource(string value)
        => PointPricingResourceTypes.IsValid(value)
            ? value.Trim().ToLowerInvariant()
            : throw new InvalidOperationException("Unsupported point resource.");

    private static string NormalizeQuality(string value)
        => ServiceSellPriceQualityTiers.IsValid(value)
            ? value.Trim().ToLowerInvariant()
            : throw new InvalidOperationException("Unsupported point quality tier.");

    private static string UnitFor(string resource)
        => string.Equals(resource, PointPricingResourceTypes.Video, StringComparison.OrdinalIgnoreCase)
            ? "per_second"
            : "per_render";
}
