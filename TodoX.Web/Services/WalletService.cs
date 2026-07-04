using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed record WalletBalance(Guid WalletId, decimal Balance, decimal Locked);

public sealed record ChargeResult(bool Ok, decimal Charged, decimal BalanceAfter, string? Error);

/// <summary>
/// Point wallet operations: balance lookups, deductions with ledger entries, and per-provider-call
/// usage logging (billing.token_usage_logs). Admin/root accounts are never charged.
/// </summary>
public sealed class WalletService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly TokenSettingsService _tokenSettings;
    private readonly ILogger<WalletService> _logger;

    public WalletService(TodoXConnectionFactory factory, TenantContext tenant,
        TokenSettingsService tokenSettings, ILogger<WalletService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _tokenSettings = tokenSettings;
        _logger = logger;
    }

    public async Task<decimal> GetBalanceAsync(Guid customerId)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<decimal?>(
            "SELECT balance FROM billing.token_wallets WHERE customer_id=@cid LIMIT 1;",
            new { cid = customerId }) ?? 0m;
    }

    /// <summary>Ensure a point wallet exists for the customer, seeded with the default balance.</summary>
    public async Task<Guid> EnsureWalletAsync(Guid customerId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var existing = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM billing.token_wallets WHERE customer_id=@cid LIMIT 1;", new { cid = customerId });
        if (existing is Guid id) return id;

        var seed = await _tokenSettings.GetDefaultWalletBalanceAsync();
        var newId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_wallets (id, tenant_id, customer_id, balance, locked_balance, status, created_at)
            VALUES (@id, @tenant, @cid, @seed, 0, 'active', now())
            ON CONFLICT DO NOTHING;
            """, new { id = newId, tenant = _tenant.TenantId, cid = customerId, seed });
        return newId;
    }

    /// <summary>
    /// Deduct points for an operation and write ledger + usage-log rows.
    /// If customerId is null (admin/operator) no deduction happens but usage is still logged (charged=false).
    /// </summary>
    public async Task<ChargeResult> ChargeAsync(Guid? customerId, Guid? userId, decimal amount, int quantity,
        string operation, string providerCode, string modelCode, string endpointCode,
        string unit = "image", Guid? referenceId = null, string? referenceType = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();

        // Admin path: no wallet, no charge, but log the usage for auditing.
        if (customerId is null)
        {
            await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
                unit, amount, charged: false, referenceType, referenceId, endpointCode, "success");
            return new ChargeResult(true, 0, 0, null);
        }

        var walletId = await EnsureWalletAsync(customerId.Value);
        var balance = await conn.ExecuteScalarAsync<decimal>(
            "SELECT balance FROM billing.token_wallets WHERE id=@id FOR UPDATE;", new { id = walletId });

        if (balance < amount)
        {
            await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
                unit, amount, charged: false, referenceType, referenceId, endpointCode, "insufficient");
            return new ChargeResult(false, 0, balance, $"Không đủ điểm (cần {amount:0}, còn {balance:0}).");
        }

        var after = balance - amount;
        await conn.ExecuteAsync(
            "UPDATE billing.token_wallets SET balance=@after, updated_at=now() WHERE id=@id;",
            new { after, id = walletId });

        // Wallet ledger entry (debit).
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_transactions
                (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                 reference_type, reference_id, description, created_at, created_by)
            VALUES
                (gen_random_uuid(), @tenant, @wallet, 'debit', @amount, @before, @after,
                 @reftype, @refid, @desc, now(), @user);
            """,
            new
            {
                tenant = _tenant.TenantId, wallet = walletId, amount, before = balance, after,
                reftype = referenceType ?? operation, refid = referenceId,
                desc = $"Trừ {amount:0} điểm cho {operation} ({quantity} {unit})", user = userId
            });

        await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
            unit, amount, charged: true, referenceType, referenceId, endpointCode, "success");

        _logger.LogInformation("Charged {Amount} points to customer {Cid} for {Op}; balance {After}", amount, customerId, operation, after);
        return new ChargeResult(true, amount, after, null);
    }

    /// <summary>Log a usage record without charging (e.g. for admin, or secondary calls like Gemini).</summary>
    public async Task LogUsageOnlyAsync(Guid? customerId, Guid? userId, string providerCode, string modelCode,
        string operation, int quantity, decimal tokenCost, string endpointCode, string unit = "call",
        Guid? referenceId = null, string? referenceType = null, string status = "success")
    {
        using var conn = await _factory.OpenAsync();
        await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
            unit, tokenCost, charged: false, referenceType, referenceId, endpointCode, status);
    }

    private async Task LogUsageAsync(System.Data.IDbConnection conn, Guid? customerId, Guid? userId,
        string providerCode, string modelCode, string operation, int quantity, string unit,
        decimal tokenCost, bool charged, string? referenceType, Guid? referenceId, string endpointCode, string status)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_usage_logs
                (id, tenant_id, user_id, customer_id, provider_code, model_code, operation, quantity,
                 unit, token_cost, charged, reference_type, reference_id, endpoint_code, status, created_at)
            VALUES
                (gen_random_uuid(), @tenant, @user, @customer, @provider, @model, @op, @qty,
                 @unit, @cost, @charged, @reftype, @refid, @endpoint, @status, now());
            """,
            new
            {
                tenant = _tenant.TenantId, user = userId, customer = customerId, provider = providerCode,
                model = modelCode, op = operation, qty = quantity, unit, cost = tokenCost, charged,
                reftype = referenceType, refid = referenceId, endpoint = endpointCode, status
            });
    }

    /// <summary>Startup: create point wallets for customers without one, seeded with the default balance.</summary>
    public async Task SeedCustomerWalletsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        var seed = await _tokenSettings.GetDefaultWalletBalanceAsync();
        using var conn = await _factory.OpenAsync();
        var created = await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_wallets (id, tenant_id, customer_id, balance, locked_balance, status, created_at)
            SELECT gen_random_uuid(), c.tenant_id, c.id, @seed, 0, 'active', now()
              FROM crm.customers c
             WHERE NOT EXISTS (SELECT 1 FROM billing.token_wallets w WHERE w.customer_id = c.id);
            """, new { seed });
        if (created > 0)
        {
            _logger.LogInformation("Seeded {N} customer point wallets with {Seed} points.", created, seed);
        }
    }
}
