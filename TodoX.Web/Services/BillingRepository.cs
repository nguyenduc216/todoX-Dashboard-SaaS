using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed record WalletView(Guid Id, Guid CustomerId, string CustomerName,
    decimal Balance, decimal LockedBalance, string Status);

public sealed record TransactionView(Guid Id, string CustomerName, string TransactionType,
    decimal Amount, decimal BalanceAfter, string? Description, DateTime CreatedAt);

public sealed record TokenSummary(decimal TotalBalance, decimal TotalLocked, int WalletCount, decimal SoldTotal);

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
}
