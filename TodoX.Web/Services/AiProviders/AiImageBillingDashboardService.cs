using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiImageBillingDashboardRequest
{
    public DateTimeOffset FromUtc { get; set; } = DateTimeOffset.UtcNow.AddDays(-30);
    public DateTimeOffset ToUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AiImageBillingDashboardSnapshot
{
    public decimal EstimatedUsd { get; set; }
    public decimal ActualUsd { get; set; }
    public bool ActualCostIncomplete { get; set; }
    public decimal CustomerChargedPoints { get; set; }
    public decimal SystemChargedPoints { get; set; }
    public decimal SystemWalletBalance { get; set; }
    public decimal SystemWalletLockedBalance { get; set; }
    public decimal SystemWalletOverdraftLimit { get; set; }
    public decimal SystemWalletLowBalanceThreshold { get; set; }
    public int PendingReconciliationCount { get; set; }
    public int ManualReviewCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount { get; set; }
    public DateTimeOffset? LastSuccessfulYEScaleTaskAt { get; set; }
    public IReadOnlyList<AiImageBillingModelAggregate> ByModel { get; set; } = Array.Empty<AiImageBillingModelAggregate>();
}

public sealed class AiImageBillingModelAggregate
{
    public string ProviderCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public int TotalCount { get; set; }
    public decimal EstimatedUsd { get; set; }
    public decimal ActualUsd { get; set; }
}

public interface IAiImageBillingDashboardService
{
    Task<AiImageBillingDashboardSnapshot> GetSnapshotAsync(AiImageBillingDashboardRequest request, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class AiImageBillingDashboardService : IAiImageBillingDashboardService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AiImageBillingDashboardService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<AiImageBillingDashboardSnapshot> GetSnapshotAsync(AiImageBillingDashboardRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        if (user.IsAuthenticated != true || (!user.IsRoot && !user.Can(AiBillingPermissions.ViewBillingDashboard)))
        {
            throw new UnauthorizedAccessException("User is not allowed to view AI image billing dashboard.");
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var summary = await conn.QuerySingleAsync<AiImageBillingDashboardSnapshot>(
            """
            SELECT COALESCE(sum(total_provider_estimated_cost_usd),0) AS EstimatedUsd,
                   COALESCE(sum(total_provider_actual_cost_usd),0) AS ActualUsd,
                   COALESCE(bool_or(actual_cost_incomplete), false) AS ActualCostIncomplete,
                   COALESCE(sum(customer_charged_points),0) AS CustomerChargedPoints,
                   COALESCE(sum(system_charged_points),0) AS SystemChargedPoints,
                   count(*) FILTER (WHERE status='pending_reconciliation')::int AS PendingReconciliationCount,
                   count(*) FILTER (WHERE status='manual_review')::int AS ManualReviewCount,
                   count(*) FILTER (WHERE status IN ('failed','released'))::int AS FailedCount,
                   count(*)::int AS TotalCount,
                   max(completed_at) FILTER (WHERE provider_code='yescale_task_image' AND status='completed') AS LastSuccessfulYEScaleTaskAt
              FROM billing.ai_billing_records
             WHERE tenant_id=@tenant
               AND created_at >= @fromUtc
               AND created_at < @toUtc;
            """,
            new { tenant = _tenant.TenantId, fromUtc = request.FromUtc.UtcDateTime, toUtc = request.ToUtc.UtcDateTime });

        var wallet = await conn.QuerySingleOrDefaultAsync<SystemWalletRow>(
            """
            SELECT balance AS Balance,
                   locked_balance AS LockedBalance,
                   COALESCE(overdraft_limit,0) AS OverdraftLimit,
                   COALESCE(low_balance_threshold,0) AS LowBalanceThreshold
              FROM billing.token_wallets
             WHERE tenant_id=@tenant
               AND wallet_scope='system'
               AND wallet_code=@walletCode
             LIMIT 1;
            """,
            new { tenant = _tenant.TenantId, walletCode = AiBillingPayerResolver.SystemImageWalletCode });

        if (wallet is not null)
        {
            summary.SystemWalletBalance = wallet.Balance;
            summary.SystemWalletLockedBalance = wallet.LockedBalance;
            summary.SystemWalletOverdraftLimit = wallet.OverdraftLimit;
            summary.SystemWalletLowBalanceThreshold = wallet.LowBalanceThreshold;
        }

        var byModel = await conn.QueryAsync<AiImageBillingModelAggregate>(
            """
            SELECT provider_code AS ProviderCode,
                   capability_code AS CapabilityCode,
                   COALESCE(actual_model, requested_model) AS ModelName,
                   count(*)::int AS TotalCount,
                   COALESCE(sum(total_provider_estimated_cost_usd),0) AS EstimatedUsd,
                   COALESCE(sum(total_provider_actual_cost_usd),0) AS ActualUsd
              FROM billing.ai_billing_records
             WHERE tenant_id=@tenant
               AND created_at >= @fromUtc
               AND created_at < @toUtc
             GROUP BY provider_code, capability_code, COALESCE(actual_model, requested_model)
             ORDER BY TotalCount DESC, EstimatedUsd DESC
             LIMIT 50;
            """,
            new { tenant = _tenant.TenantId, fromUtc = request.FromUtc.UtcDateTime, toUtc = request.ToUtc.UtcDateTime });

        summary.ByModel = byModel.ToList();
        return summary;
    }

    private sealed class SystemWalletRow
    {
        public decimal Balance { get; init; }
        public decimal LockedBalance { get; init; }
        public decimal OverdraftLimit { get; init; }
        public decimal LowBalanceThreshold { get; init; }
    }
}
