using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed record WalletBalance(Guid WalletId, decimal Balance, decimal Locked);
public sealed record ChargeResult(bool Ok, decimal Charged, decimal BalanceAfter, string? Error);
public sealed record WalletMutationResult(bool Ok, Guid? TransactionId, decimal BalanceBefore, decimal BalanceAfter, string? Error);
public sealed record VoucherCreateResult(bool Ok, Guid? VoucherId, string? VoucherCode, string? Error);

/// <summary>
/// Point wallet operations: usable balance, reserved balance, ledger mutations, and usage logging.
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

    public async Task<Guid> EnsureWalletAsync(Guid customerId)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        var id = await EnsureWalletAsync(conn, tx, customerId);
        tx.Commit();
        return id;
    }

    public async Task<WalletMutationResult> TopUpAsync(Guid customerId, decimal amount, string? description, string? note, Guid? actorUserId)
        => await MutateAsync(customerId, amount, "topup", description, note, RequireAdmin(actorUserId));

    public async Task<WalletMutationResult> AdjustPlusAsync(Guid customerId, decimal amount, string? description, string? note, Guid? actorUserId)
        => await MutateAsync(customerId, amount, "adjust_plus", description, note, RequireAdmin(actorUserId));

    public async Task<WalletMutationResult> AdjustMinusAsync(Guid customerId, decimal amount, string? description, string? note, Guid? actorUserId)
        => await MutateAsync(customerId, -Math.Abs(amount), "adjust_minus", description, note, RequireAdmin(actorUserId));

    public async Task<WalletMutationResult> RefundAsync(Guid customerId, decimal amount, string? description, string? note, Guid? actorUserId, Guid? referenceId = null)
        => await MutateAsync(customerId, amount, "refund", description, note, RequireAdmin(actorUserId), referenceId);

    public async Task<VoucherCreateResult> CreateVoucherAsync(
        string voucherCode,
        decimal pointAmount,
        int? maxRedemptions,
        DateTimeOffset? validFrom,
        DateTimeOffset? validUntil,
        string? description,
        Guid? actorUserId)
    {
        RequireAdmin(actorUserId);
        if (string.IsNullOrWhiteSpace(voucherCode))
        {
            return new(false, null, null, "Voucher code is required.");
        }

        if (pointAmount <= 0)
        {
            return new(false, null, null, "Voucher points must be greater than zero.");
        }

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();

        var normalized = voucherCode.Trim().ToUpperInvariant();
        var existing = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT id
              FROM billing.point_vouchers
             WHERE tenant_id=@tenant AND upper(voucher_code)=@code
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, code = normalized },
            tx);
        if (existing is Guid)
        {
            tx.Commit();
            return new(false, null, normalized, "Voucher code already exists.");
        }

        var voucherId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.point_vouchers
                (id, tenant_id, voucher_code, point_amount, status, max_redemptions, redeemed_count, valid_from, valid_until, created_by, created_at, updated_at)
            VALUES
                (@id, @tenant, @code, @points, 'active', @maxRedemptions, 0, @validFrom, @validUntil, @actorUserId, now(), now());
            """,
            new
            {
                id = voucherId,
                tenant = _tenant.TenantId,
                code = normalized,
                points = pointAmount,
                maxRedemptions,
                validFrom,
                validUntil,
                actorUserId
            },
            tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_usage_logs
                (id, tenant_id, user_id, customer_id, provider_code, model_code, operation, quantity, unit, token_cost, charged, reference_type, reference_id, endpoint_code, status, created_at)
            VALUES
                (gen_random_uuid(), @tenant, @actorUserId, NULL, 'admin', 'voucher', 'voucher_create', 1, 'call', @points, false, 'voucher', @voucherId, 'wallets', 'success', now());
            """,
            new { tenant = _tenant.TenantId, actorUserId, points = pointAmount, voucherId },
            tx);

        tx.Commit();
        return new(true, voucherId, normalized, null);
    }

    private static Guid RequireAdmin(Guid? actorUserId)
        => actorUserId ?? throw new UnauthorizedAccessException("Administrator authorization is required.");

    public async Task<WalletMutationResult> RedeemVoucherAsync(Guid customerId, string voucherCode, Guid? actorUserId)
    {
        if (string.IsNullOrWhiteSpace(voucherCode))
        {
            return new(false, null, 0, 0, "Voucher code is required.");
        }

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();

        var voucher = await conn.QuerySingleOrDefaultAsync<VoucherRow>(
            """
            SELECT id AS Id, point_amount AS PointAmount, status AS Status,
                   max_redemptions AS MaxRedemptions, redeemed_count AS RedeemedCount,
                   valid_from AS ValidFrom, valid_until AS ValidUntil
              FROM billing.point_vouchers
             WHERE tenant_id=@tenant AND upper(voucher_code)=@code
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, code = voucherCode.Trim().ToUpperInvariant() },
            tx);

        if (voucher is null)
        {
            tx.Commit();
            return new(false, null, 0, 0, "Invalid voucher.");
        }

        var now = DateTimeOffset.UtcNow;
        if (!string.Equals(voucher.Status, "active", StringComparison.OrdinalIgnoreCase)
            || (voucher.ValidFrom is not null && voucher.ValidFrom > now)
            || (voucher.ValidUntil is not null && voucher.ValidUntil < now)
            || (voucher.MaxRedemptions is not null && voucher.RedeemedCount >= voucher.MaxRedemptions))
        {
            tx.Commit();
            return new(false, null, 0, 0, "Voucher is not redeemable.");
        }

        var duplicate = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS(
                SELECT 1
                  FROM billing.point_voucher_redemptions
                 WHERE voucher_id=@voucherId AND customer_id=@customerId
            );
            """,
            new { voucherId = voucher.Id, customerId },
            tx);

        if (duplicate)
        {
            tx.Commit();
            return new(false, null, 0, 0, "Voucher was already redeemed.");
        }

        var result = await MutateAsync(conn, tx, customerId, voucher.PointAmount, "voucher", $"Voucher {voucherCode.Trim().ToUpperInvariant()}", null, actorUserId, voucher.Id);
        if (!result.Ok || result.TransactionId is null)
        {
            tx.Commit();
            return result;
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO billing.point_voucher_redemptions (voucher_id, customer_id, points, transaction_id, redeemed_at)
            VALUES (@voucherId, @customerId, @points, @transactionId, now());

            UPDATE billing.point_vouchers
               SET redeemed_count = redeemed_count + 1,
                   updated_at = now()
             WHERE id = @voucherId;
            """,
            new { voucherId = voucher.Id, customerId, points = voucher.PointAmount, transactionId = result.TransactionId },
            tx);

        tx.Commit();
        return result;
    }

    public async Task<ChargeResult> ChargeAsync(Guid? customerId, Guid? userId, decimal amount, int quantity,
        string operation, string providerCode, string modelCode, string endpointCode,
        string unit = "image", Guid? referenceId = null, string? referenceType = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();

        if (customerId is null)
        {
            await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
                unit, amount, charged: false, referenceType, referenceId, endpointCode, "success", tx);
            tx.Commit();
            return new ChargeResult(true, 0, 0, null);
        }

        var walletId = await EnsureWalletAsync(conn, tx, customerId.Value);

        if (referenceId is not null)
        {
            var existing = await conn.QuerySingleOrDefaultAsync<(decimal Amount, decimal BalanceAfter)>(
                """
                SELECT amount AS Amount, balance_after AS BalanceAfter
                  FROM billing.token_transactions
                 WHERE tenant_id=@tenant
                   AND wallet_id=@wallet
                   AND reference_id=@referenceId
                   AND reference_type=@referenceType
                   AND transaction_type IN ('debit','charge')
                 ORDER BY created_at DESC
                 LIMIT 1;
                """,
                new { tenant = _tenant.TenantId, wallet = walletId, referenceId, referenceType = referenceType ?? operation },
                tx);
            if (existing != default)
            {
                tx.Commit();
                return new ChargeResult(true, 0, existing.BalanceAfter, null);
            }
        }

        var balance = await conn.ExecuteScalarAsync<decimal>(
            "SELECT balance FROM billing.token_wallets WHERE id=@id FOR UPDATE;",
            new { id = walletId },
            tx);

        if (balance < amount)
        {
            await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
                unit, amount, charged: false, referenceType, referenceId, endpointCode, "insufficient", tx);
            tx.Commit();
            return new ChargeResult(false, 0, balance, $"KhÃ´ng Ä‘á»§ Ä‘iá»ƒm (cáº§n {amount:0}, cÃ²n {balance:0}).");
        }

        var after = balance - amount;
        await conn.ExecuteAsync(
            "UPDATE billing.token_wallets SET balance=@after, updated_at=now() WHERE id=@id;",
            new { after, id = walletId },
            tx);

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
                tenant = _tenant.TenantId,
                wallet = walletId,
                amount,
                before = balance,
                after,
                reftype = referenceType ?? operation,
                refid = referenceId,
                desc = $"Trá»« {amount:0} Ä‘iá»ƒm cho {operation} ({quantity} {unit})",
                user = userId
            },
            tx);

        await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
            unit, amount, charged: true, referenceType, referenceId, endpointCode, "success", tx);

        tx.Commit();
        _logger.LogInformation("Charged {Amount} points to customer {Cid} for {Op}; balance {After}", amount, customerId, operation, after);
        return new ChargeResult(true, amount, after, null);
    }

    public async Task LogUsageOnlyAsync(Guid? customerId, Guid? userId, string providerCode, string modelCode,
        string operation, int quantity, decimal tokenCost, string endpointCode, string unit = "call",
        Guid? referenceId = null, string? referenceType = null, string status = "success")
    {
        using var conn = await _factory.OpenAsync();
        await LogUsageAsync(conn, customerId, userId, providerCode, modelCode, operation, quantity,
            unit, tokenCost, charged: false, referenceType, referenceId, endpointCode, status);
    }

    private async Task<WalletMutationResult> MutateAsync(
        Guid customerId,
        decimal signedAmount,
        string transactionType,
        string? description,
        string? note,
        Guid? actorUserId,
        Guid? referenceId = null)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        var result = await MutateAsync(conn, tx, customerId, signedAmount, transactionType, description, note, actorUserId, referenceId);
        tx.Commit();
        return result;
    }

    private async Task<WalletMutationResult> MutateAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid customerId,
        decimal signedAmount,
        string transactionType,
        string? description,
        string? note,
        Guid? actorUserId,
        Guid? referenceId = null)
    {
        var amount = Math.Abs(signedAmount);
        if (amount <= 0)
        {
            return new(false, null, 0, 0, "Point amount must be greater than zero.");
        }

        var walletId = await EnsureWalletAsync(conn, tx, customerId);
        var before = await conn.ExecuteScalarAsync<decimal>(
            "SELECT balance FROM billing.token_wallets WHERE id=@walletId FOR UPDATE;",
            new { walletId },
            tx);
        var after = before + signedAmount;
        if (after < 0)
        {
            return new(false, null, before, before, "Insufficient usable points.");
        }

        await conn.ExecuteAsync(
            "UPDATE billing.token_wallets SET balance=@after, updated_at=now() WHERE id=@walletId;",
            new { after, walletId },
            tx);

        var transactionId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_transactions
                (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                 reference_type, reference_id, description, created_at, created_by)
            VALUES
                (@transactionId, @tenant, @walletId, @transactionType, @amount, @before, @after,
                 @referenceType, @referenceId, @description, now(), @actorUserId);
            """,
            new
            {
                transactionId,
                tenant = _tenant.TenantId,
                walletId,
                transactionType,
                amount,
                before,
                after,
                referenceType = transactionType,
                referenceId,
                description = string.Join(" ", new[] { description, note }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
                actorUserId
            },
            tx);

        return new(true, transactionId, before, after, null);
    }

    private async Task LogUsageAsync(
        System.Data.IDbConnection conn,
        Guid? customerId,
        Guid? userId,
        string providerCode,
        string modelCode,
        string operation,
        int quantity,
        string unit,
        decimal tokenCost,
        bool charged,
        string? referenceType,
        Guid? referenceId,
        string endpointCode,
        string status,
        System.Data.IDbTransaction? tx = null)
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
                tenant = _tenant.TenantId,
                user = userId,
                customer = customerId,
                provider = providerCode,
                model = modelCode,
                op = operation,
                qty = quantity,
                unit,
                cost = tokenCost,
                charged,
                reftype = referenceType,
                refid = referenceId,
                endpoint = endpointCode,
                status
            },
            tx);
    }

    private async Task<Guid> EnsureWalletAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid customerId)
    {
        var existing = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM billing.token_wallets WHERE customer_id=@cid LIMIT 1;",
            new { cid = customerId },
            tx);
        if (existing is Guid id)
        {
            return id;
        }

        var seed = await _tokenSettings.GetDefaultWalletBalanceAsync();
        var newId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_wallets (id, tenant_id, customer_id, balance, locked_balance, status, created_at)
            VALUES (@id, @tenant, @cid, @seed, 0, 'active', now())
            ON CONFLICT DO NOTHING;
            """,
            new { id = newId, tenant = _tenant.TenantId, cid = customerId, seed },
            tx);
        return newId;
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

    private sealed class VoucherRow
    {
        public Guid Id { get; set; }
        public decimal PointAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public int? MaxRedemptions { get; set; }
        public int RedeemedCount { get; set; }
        public DateTimeOffset? ValidFrom { get; set; }
        public DateTimeOffset? ValidUntil { get; set; }
    }
}
