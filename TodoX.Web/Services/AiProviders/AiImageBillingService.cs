using System.Data;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiImageBillingOptions
{
    public decimal ExchangeRateVndPerUsd { get; set; } = 8000m;
    public decimal TodoXVndPerPoint { get; set; } = 10000m;
}

public sealed record AiImageBillingCost(
    decimal ProviderEstimatedCostUsd,
    decimal? ProviderActualCostUsd,
    string ProviderCostSource,
    decimal ExchangeRateVndPerUsd,
    decimal TodoXVndPerPoint,
    decimal ProviderCostPoints,
    decimal CustomerChargedPoints)
{
    public static AiImageBillingCost FromConfiguredPoints(decimal points, decimal exchangeRateVndPerUsd, decimal todoxVndPerPoint)
    {
        var estimatedUsd = ToUsd(points, exchangeRateVndPerUsd, todoxVndPerPoint);
        return new AiImageBillingCost(
            estimatedUsd,
            ProviderActualCostUsd: null,
            ProviderCostSource: "configured_tariff",
            exchangeRateVndPerUsd,
            todoxVndPerPoint,
            points,
            points);
    }

    public static decimal ToUsd(decimal points, decimal exchangeRateVndPerUsd, decimal todoxVndPerPoint)
    {
        if (exchangeRateVndPerUsd <= 0) throw new InvalidOperationException("YEScale exchange rate must be greater than zero.");
        if (todoxVndPerPoint <= 0) throw new InvalidOperationException("TodoX point value must be greater than zero.");
        return points * todoxVndPerPoint / exchangeRateVndPerUsd;
    }
}

public sealed class AiImageBillingReserveRequest
{
    public string LogicalRequestId { get; set; } = string.Empty;
    public string? RenderJobId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public long ProviderId { get; set; }
    public long ProviderCapabilityId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string FeatureCode { get; set; } = string.Empty;
    public string? RequestedModel { get; set; }
    public AiImageBillingCost Cost { get; set; } = AiImageBillingCost.FromConfiguredPoints(0, 8000, 10000);
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public string? TariffSnapshotJson { get; set; }
    public object? Metadata { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class AiImageBillingCompleteRequest
{
    public string LogicalRequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ActualModel { get; set; }
    public string? ProviderTaskId { get; set; }
    public decimal? ProviderActualCostUsd { get; set; }
    public string? ProviderUsageJson { get; set; }
    public string? TariffSnapshotJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class AiImageBillingPendingReconciliationRequest
{
    public string LogicalRequestId { get; set; } = string.Empty;
    public string? ActualModel { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ProviderUsageJson { get; set; }
    public string? TariffSnapshotJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed record AiImageBillingReservation(
    bool Ok,
    bool ShouldSubmitProvider,
    string PayerType,
    string Status,
    string LogicalRequestId,
    decimal ChargedPoints,
    Guid? BillingRecordId,
    Guid? WalletTransactionId,
    string? ErrorMessage)
{
    public static AiImageBillingReservation Failed(string logicalRequestId, string status, string message)
        => new(false, false, string.Empty, status, logicalRequestId, 0, null, null, message);
}

public sealed record AiImageBillingCompletion(
    bool Ok,
    string Status,
    Guid? WalletTransactionId,
    string? ErrorMessage);

public sealed class AiImageBillingReconciliationItem
{
    public Guid Id { get; init; }
    public string LogicalRequestId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? RequestedModel { get; init; }
    public string? ActualModel { get; init; }
    public string? ProviderTaskId { get; init; }
    public int ReconciliationAttemptCount { get; init; }
    public string? TariffSnapshotJson { get; init; }
}

public interface IAiImageBillingService
{
    AiImageBillingCost BuildConfiguredCost(decimal unitCostPoints, decimal quantity);
    Task<AiImageBillingReservation> ReserveAsync(AiImageBillingReserveRequest request, CancellationToken ct = default);
    Task<AiImageBillingCompletion> CompleteAsync(AiImageBillingCompleteRequest request, CancellationToken ct = default);
    Task<AiImageBillingCompletion> MarkPendingReconciliationAsync(AiImageBillingPendingReconciliationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AiImageBillingReconciliationItem>> ClaimReconciliationBatchAsync(string workerKey, int batchSize, TimeSpan lockFor, int maxAttempts, CancellationToken ct = default);
    Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default);
    Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default);
}

public sealed class AiImageBillingService : IAiImageBillingService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly IAiBillingPayerResolver _payerResolver;
    private readonly ILogger<AiImageBillingService> _logger;

    public AiImageBillingService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IConfiguration config,
        IAiBillingPayerResolver payerResolver,
        ILogger<AiImageBillingService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _config = config;
        _payerResolver = payerResolver;
        _logger = logger;
    }

    public AiImageBillingCost BuildConfiguredCost(decimal unitCostPoints, decimal quantity)
    {
        var exchangeRate = _config.GetValue("AiImageBilling:ExchangeRateVndPerUsd", 8000m);
        var pointValue = _config.GetValue("AiImageBilling:TodoXVndPerPoint", 10000m);
        return AiImageBillingCost.FromConfiguredPoints(Math.Max(0, unitCostPoints * quantity), exchangeRate, pointValue);
    }

    public async Task<AiImageBillingReservation> ReserveAsync(AiImageBillingReserveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LogicalRequestId))
        {
            return AiImageBillingReservation.Failed(string.Empty, "invalid", "Missing logical_request_id for image billing.");
        }

        AiBillingPayerContext payer;
        try
        {
            payer = _payerResolver.Resolve(new AiBillingPayerResolveRequest(
                request.CustomerId,
                request.UserId,
                request.FeatureCode,
                request.CapabilityCode,
                request.Metadata,
                request.TrustedPayerContext));
        }
        catch (Exception ex)
        {
            return AiImageBillingReservation.Failed(request.LogicalRequestId, "missing_payer", ex.Message);
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await LockLogicalRequestAsync(conn, tx, _tenant.TenantId, request.LogicalRequestId);

        var existing = await GetRecordForUpdateAsync(conn, tx, request.LogicalRequestId);
        if (existing is not null)
        {
            var decision = HandleExistingReservation(existing);
            tx.Commit();
            return decision;
        }

        var chargePoints = request.Cost.CustomerChargedPoints;
        var wallet = payer.PayerType == AiBillingPayerTypes.Customer
            ? await EnsureCustomerWalletForUpdateAsync(conn, tx, payer.PayerCustomerId!.Value)
            : await GetSystemWalletForUpdateAsync(conn, tx, payer.SystemWalletCode!);

        var available = wallet.Balance + (payer.PayerType == AiBillingPayerTypes.System ? wallet.OverdraftLimit : 0);
        if (available < chargePoints)
        {
            var recordId = await InsertRecordAsync(conn, tx, request, payer, wallet.Id, "insufficient", walletTransactionId: null);
            tx.Commit();
            return new AiImageBillingReservation(false, false, payer.PayerType, "insufficient", request.LogicalRequestId, chargePoints,
                recordId, null, $"Insufficient TodoX image points. Required {chargePoints:0.####}, available {available:0.####}.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.token_wallets
               SET balance = balance - @amount,
                   locked_balance = locked_balance + @amount,
                   updated_at = now()
             WHERE id = @walletId;
            """,
            new { amount = chargePoints, walletId = wallet.Id }, tx);

        var reservedId = await InsertRecordAsync(conn, tx, request, payer, wallet.Id, "reserved", walletTransactionId: null);
        tx.Commit();

        return new AiImageBillingReservation(true, true, payer.PayerType, "reserved", request.LogicalRequestId, chargePoints, reservedId, null, null);
    }

    public async Task<AiImageBillingCompletion> CompleteAsync(AiImageBillingCompleteRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await LockLogicalRequestAsync(conn, tx, _tenant.TenantId, request.LogicalRequestId);

        var record = await GetRecordForUpdateAsync(conn, tx, request.LogicalRequestId);
        if (record is null)
        {
            tx.Commit();
            return new AiImageBillingCompletion(false, "missing_record", null, "Image billing reservation was not found.");
        }

        if (record.Status is "completed" or "failed" or "released")
        {
            await InsertAttemptsAsync(conn, tx, record.Id, request);
            tx.Commit();
            return new AiImageBillingCompletion(true, record.Status, record.WalletTransactionId, null);
        }

        await InsertAttemptsAsync(conn, tx, record.Id, request);

        if (!request.Success)
        {
            await ReleaseReservationAsync(conn, tx, record, request, status: "released");
            tx.Commit();
            return new AiImageBillingCompletion(true, "released", null, null);
        }

        var reservedPoints = record.ReservedPoints;
        if (reservedPoints <= 0)
        {
            await CompleteRecordWithoutDebitAsync(conn, tx, record, request);
            tx.Commit();
            return new AiImageBillingCompletion(true, "completed", null, null);
        }

        if (record.PayerWalletId is not Guid walletId)
        {
            await ReleaseReservationAsync(conn, tx, record, new AiImageBillingCompleteRequest
            {
                LogicalRequestId = request.LogicalRequestId,
                Success = false,
                ActualModel = request.ActualModel,
                ProviderTaskId = request.ProviderTaskId,
                ProviderActualCostUsd = request.ProviderActualCostUsd,
                ProviderUsageJson = request.ProviderUsageJson,
                ErrorMessage = "Missing payer wallet id on reserved billing record."
            }, status: "released");
            tx.Commit();
            return new AiImageBillingCompletion(false, "released", null, "Missing payer wallet id on reserved billing record.");
        }

        var wallet = await conn.QuerySingleAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
              FROM billing.token_wallets
             WHERE id = @walletId
             FOR UPDATE;
            """,
            new { walletId }, tx);

        var lockedAfter = Math.Max(0, wallet.LockedBalance - reservedPoints);
        await conn.ExecuteAsync(
            "UPDATE billing.token_wallets SET locked_balance = @lockedAfter, updated_at = now() WHERE id = @walletId;",
            new { lockedAfter, walletId }, tx);

        var txId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_transactions
                (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                 reference_type, reference_id, description, created_at, created_by)
            VALUES
                (@txId, @tenant, @walletId, 'debit', @amount, @before, @after,
                 'ai_image_render', @recordId, @description, now(), @createdBy);
            """,
            new
            {
                txId,
                tenant = _tenant.TenantId,
                walletId,
                amount = reservedPoints,
                before = wallet.Balance + reservedPoints,
                after = wallet.Balance,
                recordId = record.Id,
                description = $"AI image render {record.LogicalRequestId} ({record.PayerType})",
                createdBy = record.CreatedBy
            }, tx);

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status = 'completed',
                   actual_model = @model,
                   provider_task_id = @taskId,
                   provider_actual_cost_usd = @actualUsd,
                   provider_cost_source = CASE WHEN @actualUsd IS NULL THEN provider_cost_source ELSE 'provider_actual' END,
                   wallet_transaction_id = @txId,
                   completed_at = now(),
                   updated_at = now()
             WHERE id = @id;
            """,
            new { id = record.Id, model = request.ActualModel, taskId = request.ProviderTaskId, actualUsd = request.ProviderActualCostUsd, txId }, tx);

        tx.Commit();
        return new AiImageBillingCompletion(true, "completed", txId, null);
    }

    public async Task<AiImageBillingCompletion> MarkPendingReconciliationAsync(AiImageBillingPendingReconciliationRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(CancellationToken.None);
        using var conn = await _factory.OpenAsync(CancellationToken.None);
        using var tx = conn.BeginTransaction();

        await LockLogicalRequestAsync(conn, tx, _tenant.TenantId, request.LogicalRequestId);

        var record = await GetRecordForUpdateAsync(conn, tx, request.LogicalRequestId);
        if (record is null)
        {
            tx.Commit();
            return new AiImageBillingCompletion(false, "missing_record", null, "Image billing reservation was not found.");
        }

        await InsertAttemptsAsync(conn, tx, record.Id, new AiImageBillingCompleteRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            Success = false,
            ActualModel = request.ActualModel,
            ProviderTaskId = request.ProviderTaskId,
            ProviderUsageJson = request.ProviderUsageJson,
            TariffSnapshotJson = request.TariffSnapshotJson,
            ErrorMessage = request.ErrorMessage
        });

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status = 'pending_reconciliation',
                   actual_model = COALESCE(@model, actual_model),
                   provider_task_id = COALESCE(@taskId, provider_task_id),
                   error_message = @errorMessage,
                   pending_reconciliation_at = now(),
                   updated_at = now()
             WHERE id = @id
               AND status IN ('reserved','pending_reconciliation');
            """,
            new { id = record.Id, model = request.ActualModel, taskId = request.ProviderTaskId, errorMessage = request.ErrorMessage }, tx);

        tx.Commit();
        return new AiImageBillingCompletion(true, "pending_reconciliation", record.WalletTransactionId, null);
    }

    public async Task<IReadOnlyList<AiImageBillingReconciliationItem>> ClaimReconciliationBatchAsync(
        string workerKey,
        int batchSize,
        TimeSpan lockFor,
        int maxAttempts,
        CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var rows = (await conn.QueryAsync<AiImageBillingReconciliationItem>(
            """
            WITH claimed AS (
                SELECT id
                  FROM billing.ai_billing_records
                 WHERE status IN ('reserved','pending_reconciliation')
                   AND COALESCE(reconciliation_attempt_count, 0) < @maxAttempts
                   AND (reconciliation_lock_until IS NULL OR reconciliation_lock_until < now())
                   AND (
                        status = 'pending_reconciliation'
                        OR (status = 'reserved' AND reserved_until < now())
                   )
                 ORDER BY COALESCE(pending_reconciliation_at, reserved_until, created_at), id
                 FOR UPDATE SKIP LOCKED
                 LIMIT @batchSize
            )
            UPDATE billing.ai_billing_records r
               SET reconciliation_lock_owner = @workerKey,
                   reconciliation_lock_until = now() + (@lockSeconds || ' seconds')::interval,
                   reconciliation_attempt_count = COALESCE(reconciliation_attempt_count, 0) + 1,
                   pending_reconciliation_at = COALESCE(pending_reconciliation_at, now()),
                   status = CASE WHEN status = 'reserved' THEN 'pending_reconciliation' ELSE status END,
                   updated_at = now()
              FROM claimed
             WHERE r.id = claimed.id
             RETURNING r.id AS Id,
                       r.logical_request_id AS LogicalRequestId,
                       r.status AS Status,
                       r.requested_model AS RequestedModel,
                       r.actual_model AS ActualModel,
                       r.provider_task_id AS ProviderTaskId,
                       COALESCE(r.reconciliation_attempt_count, 0) AS ReconciliationAttemptCount,
                       r.tariff_snapshot_json::text AS TariffSnapshotJson;
            """,
            new
            {
                workerKey,
                batchSize = Math.Max(1, batchSize),
                lockSeconds = Math.Max(1, (int)lockFor.TotalSeconds),
                maxAttempts = Math.Max(1, maxAttempts)
            }, tx)).ToList();

        tx.Commit();
        return rows;
    }

    public async Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status = 'manual_review',
                   error_message = @errorMessage,
                   reconciliation_lock_owner = NULL,
                   reconciliation_lock_until = NULL,
                   updated_at = now()
             WHERE logical_request_id = @logicalRequestId
               AND status IN ('reserved','pending_reconciliation','manual_review');
            """,
            new { logicalRequestId, errorMessage });
    }

    public async Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status = 'pending_reconciliation',
                   error_message = @errorMessage,
                   pending_reconciliation_at = now() + (@delaySeconds || ' seconds')::interval,
                   reconciliation_lock_owner = NULL,
                   reconciliation_lock_until = NULL,
                   updated_at = now()
             WHERE logical_request_id = @logicalRequestId
               AND status = 'pending_reconciliation';
            """,
            new { logicalRequestId, errorMessage, delaySeconds = Math.Max(1, (int)delay.TotalSeconds) });
    }

    private static async Task LockLogicalRequestAsync(IDbConnection conn, IDbTransaction tx, Guid tenantId, string logicalRequestId)
    {
        await conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = $"{tenantId:N}:{logicalRequestId}" }, tx);
    }

    private static AiImageBillingReservation HandleExistingReservation(BillingRecord existing)
    {
        if (existing.Status is "completed")
        {
            return new AiImageBillingReservation(true, false, existing.PayerType, "completed", existing.LogicalRequestId,
                existing.ReservedPoints, existing.Id, existing.WalletTransactionId, "Image render request was already completed.");
        }

        if (existing.Status is "reserved" or "pending_reconciliation")
        {
            return new AiImageBillingReservation(true, false, existing.PayerType, existing.Status, existing.LogicalRequestId,
                existing.ReservedPoints, existing.Id, existing.WalletTransactionId, "Image render request is already in progress.");
        }

        if (existing.Status is "insufficient" or "missing_payer" or "missing_customer")
        {
            return new AiImageBillingReservation(false, false, existing.PayerType, existing.Status, existing.LogicalRequestId,
                existing.ReservedPoints, existing.Id, existing.WalletTransactionId, $"Image billing record is already '{existing.Status}'. Create a new logical_request_id after fixing payer/balance.");
        }

        return new AiImageBillingReservation(false, false, existing.PayerType, existing.Status, existing.LogicalRequestId,
            existing.ReservedPoints, existing.Id, existing.WalletTransactionId, $"Image billing record is in status '{existing.Status}'.");
    }

    private async Task<WalletRow> EnsureCustomerWalletForUpdateAsync(IDbConnection conn, IDbTransaction tx, Guid customerId)
    {
        var wallet = await conn.QuerySingleOrDefaultAsync<WalletRow?>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
              FROM billing.token_wallets
             WHERE tenant_id = @tenant
               AND wallet_scope = 'customer'
               AND customer_id = @customerId
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, customerId }, tx);
        if (wallet is not null)
        {
            return wallet;
        }

        var seed = await conn.ExecuteScalarAsync<decimal?>(
            """
            SELECT setting_value::numeric
              FROM system.app_settings
             WHERE setting_key = 'token.default_wallet_balance' AND is_active
             LIMIT 1;
            """,
            transaction: tx) ?? 100m;

        var walletId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_wallets
                (id, tenant_id, customer_id, wallet_scope, wallet_code, balance, locked_balance, overdraft_limit, status, created_at, updated_at)
            VALUES
                (@walletId, @tenant, @customerId, 'customer', NULL, @seed, 0, 0, 'active', now(), now())
            ON CONFLICT DO NOTHING;
            """,
            new { walletId, tenant = _tenant.TenantId, customerId, seed }, tx);

        return await conn.QuerySingleAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
              FROM billing.token_wallets
             WHERE tenant_id = @tenant
               AND wallet_scope = 'customer'
               AND customer_id = @customerId
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, customerId }, tx);
    }

    private async Task<WalletRow> GetSystemWalletForUpdateAsync(IDbConnection conn, IDbTransaction tx, string walletCode)
    {
        var wallet = await conn.QuerySingleOrDefaultAsync<WalletRow?>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
              FROM billing.token_wallets
             WHERE tenant_id = @tenant
               AND wallet_scope = 'system'
               AND wallet_code = @walletCode
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, walletCode }, tx);

        return wallet ?? throw new InvalidOperationException(
            $"System image wallet '{walletCode}' is missing. Run the manual YEScale billing SQL before enabling production image render.");
    }

    private async Task<Guid> InsertRecordAsync(
        IDbConnection conn,
        IDbTransaction tx,
        AiImageBillingReserveRequest request,
        AiBillingPayerContext payer,
        Guid? walletId,
        string status,
        Guid? walletTransactionId)
    {
        var customerPoints = payer.PayerType == AiBillingPayerTypes.Customer ? request.Cost.CustomerChargedPoints : 0m;
        var systemPoints = payer.PayerType == AiBillingPayerTypes.System ? request.Cost.CustomerChargedPoints : 0m;

        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO billing.ai_billing_records
                (id, tenant_id, logical_request_id, render_job_id, customer_id, user_id, wallet_id,
                 payer_type, payer_customer_id, payer_wallet_id, system_charged_points,
                 provider_id, provider_capability_id, provider_code, capability_code, feature_code,
                 requested_model, provider_estimated_cost_usd, total_provider_estimated_cost_usd,
                 provider_actual_cost_usd, total_provider_actual_cost_usd, provider_cost_source,
                 exchange_rate_vnd_per_usd, todox_vnd_per_point, provider_cost_points, customer_charged_points,
                 billing_exempt, exemption_reason, wallet_transaction_id, status, tariff_snapshot_json, metadata_json, created_by,
                 reserved_until, created_at, updated_at)
            VALUES
                (gen_random_uuid(), @tenant, @LogicalRequestId, @RenderJobId, @CustomerId, @UserId, @walletId,
                 @PayerType, @PayerCustomerId, @walletId, @SystemChargedPoints,
                 @ProviderId, @ProviderCapabilityId, @ProviderCode, @CapabilityCode, @FeatureCode,
                 @RequestedModel, @ProviderEstimatedCostUsd, @ProviderEstimatedCostUsd,
                 @ProviderActualCostUsd, @ProviderActualCostUsd, @ProviderCostSource,
                 @ExchangeRateVndPerUsd, @TodoXVndPerPoint, @ProviderCostPoints, @CustomerChargedPoints,
                 false, NULL, @walletTransactionId, @status, CAST(@TariffSnapshotJson AS jsonb), CAST(@MetadataJson AS jsonb), @CreatedBy,
                 now() + interval '30 minutes', now(), now())
            RETURNING id;
            """,
            new
            {
                tenant = _tenant.TenantId,
                request.LogicalRequestId,
                request.RenderJobId,
                CustomerId = payer.PayerCustomerId ?? request.CustomerId,
                request.UserId,
                walletId,
                payer.PayerType,
                PayerCustomerId = payer.PayerCustomerId,
                SystemChargedPoints = systemPoints,
                request.ProviderId,
                request.ProviderCapabilityId,
                request.ProviderCode,
                request.CapabilityCode,
                request.FeatureCode,
                request.RequestedModel,
                request.Cost.ProviderEstimatedCostUsd,
                request.Cost.ProviderActualCostUsd,
                request.Cost.ProviderCostSource,
                request.Cost.ExchangeRateVndPerUsd,
                request.Cost.TodoXVndPerPoint,
                request.Cost.ProviderCostPoints,
                CustomerChargedPoints = customerPoints,
                walletTransactionId,
                status,
                request.TariffSnapshotJson,
                MetadataJson = SerializeMetadata(new
                {
                    request = request.Metadata,
                    payer = new { payer.PayerType, payer.ResolutionSource, payer.SystemWalletCode }
                }),
                request.CreatedBy
            }, tx);
    }

    private static async Task<BillingRecord?> GetRecordForUpdateAsync(IDbConnection conn, IDbTransaction tx, string logicalRequestId)
        => await conn.QuerySingleOrDefaultAsync<BillingRecord>(
            """
            SELECT id AS Id,
                   logical_request_id AS LogicalRequestId,
                   payer_type AS PayerType,
                   COALESCE(payer_wallet_id, wallet_id) AS PayerWalletId,
                   wallet_transaction_id AS WalletTransactionId,
                   customer_charged_points AS CustomerChargedPoints,
                   system_charged_points AS SystemChargedPoints,
                   status AS Status,
                   created_by AS CreatedBy
              FROM billing.ai_billing_records
             WHERE logical_request_id = @logicalRequestId
             FOR UPDATE;
            """,
            new { logicalRequestId }, tx);

    private static async Task CompleteRecordWithoutDebitAsync(IDbConnection conn, IDbTransaction tx, BillingRecord record, AiImageBillingCompleteRequest request)
    {
        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status = 'completed',
                   actual_model = @model,
                   provider_task_id = @taskId,
                   provider_actual_cost_usd = @actualUsd,
                   provider_cost_source = CASE WHEN @actualUsd IS NULL THEN provider_cost_source ELSE 'provider_actual' END,
                   completed_at = now(),
                   updated_at = now()
             WHERE id = @id;
            """,
            new { id = record.Id, model = request.ActualModel, taskId = request.ProviderTaskId, actualUsd = request.ProviderActualCostUsd }, tx);
    }

    private static async Task ReleaseReservationAsync(
        IDbConnection conn,
        IDbTransaction tx,
        BillingRecord record,
        AiImageBillingCompleteRequest request,
        string status)
    {
        var reservedPoints = record.ReservedPoints;
        if (record.PayerWalletId is Guid walletId && reservedPoints > 0)
        {
            var wallet = await conn.QuerySingleAsync<WalletRow>(
                """
                SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
                  FROM billing.token_wallets
                 WHERE id = @walletId
                 FOR UPDATE;
                """,
                new { walletId }, tx);
            var release = Math.Min(wallet.LockedBalance, reservedPoints);
            await conn.ExecuteAsync(
                """
                UPDATE billing.token_wallets
                   SET balance = balance + @release,
                       locked_balance = locked_balance - @release,
                       updated_at = now()
                 WHERE id = @walletId;
                """,
                new { release, walletId }, tx);
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status = @status,
                   actual_model = @model,
                   provider_task_id = @taskId,
                   provider_actual_cost_usd = @actualUsd,
                   provider_cost_source = CASE WHEN @actualUsd IS NULL THEN provider_cost_source ELSE 'provider_actual' END,
                   error_message = @errorMessage,
                   failed_at = CASE WHEN @status = 'released' THEN now() ELSE failed_at END,
                   updated_at = now()
             WHERE id = @id;
            """,
            new
            {
                id = record.Id,
                status,
                model = request.ActualModel,
                taskId = request.ProviderTaskId,
                actualUsd = request.ProviderActualCostUsd,
                errorMessage = request.ErrorMessage
            }, tx);
    }

    private static async Task InsertAttemptsAsync(IDbConnection conn, IDbTransaction tx, Guid billingRecordId, AiImageBillingCompleteRequest request)
    {
        var attempts = AiImageBillingAttemptParser.Parse(request).ToList();
        var totalEstimatedUsd = attempts.Sum(a => a.ProviderEstimatedCostUsd ?? 0);
        var actualValues = attempts.Where(a => a.ProviderActualCostUsd.HasValue).Select(a => a.ProviderActualCostUsd!.Value).ToList();
        var totalActualUsd = actualValues.Count == 0 ? (decimal?)null : actualValues.Sum();
        var actualIncomplete = attempts.Any(a => !a.ProviderActualCostUsd.HasValue);
        foreach (var attempt in attempts)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO billing.ai_provider_attempts
                    (id, billing_record_id, attempt_number, model_name, provider_task_id, success,
                     provider_estimated_cost_usd, provider_actual_cost_usd, cost_source, error_code, error_message,
                     raw_usage_json, started_at, completed_at, created_at)
                VALUES
                    (gen_random_uuid(), @billingRecordId, @AttemptNumber, @ModelName, @ProviderTaskId, @Success,
                     @ProviderEstimatedCostUsd, @ProviderActualCostUsd, @CostSource, @ErrorCode, @ErrorMessage,
                     CAST(@RawUsageJson AS jsonb), @StartedAt, @CompletedAt, now())
                ON CONFLICT (billing_record_id, attempt_number) DO UPDATE
                    SET model_name = EXCLUDED.model_name,
                        provider_task_id = COALESCE(EXCLUDED.provider_task_id, billing.ai_provider_attempts.provider_task_id),
                        success = EXCLUDED.success,
                        provider_estimated_cost_usd = EXCLUDED.provider_estimated_cost_usd,
                        provider_actual_cost_usd = EXCLUDED.provider_actual_cost_usd,
                        cost_source = EXCLUDED.cost_source,
                        error_code = EXCLUDED.error_code,
                        error_message = EXCLUDED.error_message,
                        raw_usage_json = EXCLUDED.raw_usage_json,
                        completed_at = EXCLUDED.completed_at;
                """,
                new
                {
                    billingRecordId,
                    attempt.AttemptNumber,
                    attempt.ModelName,
                    attempt.ProviderTaskId,
                    attempt.Success,
                    attempt.ProviderEstimatedCostUsd,
                    attempt.ProviderActualCostUsd,
                    attempt.CostSource,
                    attempt.ErrorCode,
                    attempt.ErrorMessage,
                    RawUsageJson = request.ProviderUsageJson,
                    attempt.StartedAt,
                    attempt.CompletedAt
                }, tx);
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET total_provider_estimated_cost_usd = @totalEstimatedUsd,
                   total_provider_actual_cost_usd = @totalActualUsd,
                   actual_cost_incomplete = @actualIncomplete,
                   updated_at = now()
             WHERE id = @billingRecordId;
            """,
            new { billingRecordId, totalEstimatedUsd, totalActualUsd, actualIncomplete }, tx);
    }

    private static string? SerializeMetadata(object? metadata)
    {
        if (metadata is null) return null;
        try
        {
            return JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return null;
        }
    }

    private sealed class WalletRow
    {
        public Guid Id { get; init; }
        public decimal Balance { get; init; }
        public decimal LockedBalance { get; init; }
        public decimal OverdraftLimit { get; init; }
    }

    private sealed class BillingRecord
    {
        public Guid Id { get; init; }
        public string LogicalRequestId { get; init; } = string.Empty;
        public string PayerType { get; init; } = AiBillingPayerTypes.Customer;
        public Guid? PayerWalletId { get; init; }
        public Guid? WalletTransactionId { get; init; }
        public decimal CustomerChargedPoints { get; init; }
        public decimal SystemChargedPoints { get; init; }
        public decimal ReservedPoints => CustomerChargedPoints + SystemChargedPoints;
        public string Status { get; init; } = string.Empty;
        public string? CreatedBy { get; init; }
    }
}

public sealed record AiImageProviderAttempt(
    int AttemptNumber,
    string? ModelName,
    string? ProviderTaskId,
    bool Success,
    decimal? ProviderEstimatedCostUsd,
    decimal? ProviderActualCostUsd,
    string? CostSource,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public static class AiImageBillingAttemptParser
{
    public static IReadOnlyList<AiImageProviderAttempt> Parse(AiImageBillingCompleteRequest request)
    {
        var attempts = new List<AiImageProviderAttempt>();
        var attemptNumber = 1;
        var tariffs = AiImageTariffSnapshot.Parse(request.TariffSnapshotJson);

        if (!string.IsNullOrWhiteSpace(request.ProviderUsageJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(request.ProviderUsageJson);
                if (doc.RootElement.TryGetProperty("fallbackTrail", out var trail) && trail.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in trail.EnumerateArray())
                    {
                        var model = ReadString(item, "from");
                        var errorCode = ReadString(item, "errorCode");
                        var tariff = tariffs.Find(model);
                        attempts.Add(new AiImageProviderAttempt(
                            attemptNumber++,
                            model,
                            ReadString(item, "taskId") ?? ReadString(item, "task_id"),
                            Success: false,
                            ProviderEstimatedCostUsd: tariff?.ProviderEstimatedCostUsd,
                            ProviderActualCostUsd: null,
                            CostSource: tariff?.CostSource,
                            ErrorCode: errorCode,
                            ErrorMessage: ReadString(item, "reason") ?? errorCode,
                            StartedAt: ReadDate(item, "startedAt"),
                            CompletedAt: ReadDate(item, "completedAt")));
                    }
                }
            }
            catch
            {
                attempts.Clear();
                attemptNumber = 1;
            }
        }

        attempts.Add(new AiImageProviderAttempt(
            attemptNumber,
            request.ActualModel,
            request.ProviderTaskId,
            request.Success,
            tariffs.Find(request.ActualModel)?.ProviderEstimatedCostUsd,
            request.ProviderActualCostUsd,
            request.ProviderActualCostUsd is null ? tariffs.Find(request.ActualModel)?.CostSource : "provider_actual",
            ErrorCode: null,
            request.ErrorMessage,
            StartedAt: null,
            CompletedAt: DateTimeOffset.UtcNow));

        return attempts;
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement element, string propertyName)
        => DateTimeOffset.TryParse(ReadString(element, propertyName), out var value) ? value : null;
}

public sealed class AiImageTariffSnapshot
{
    public List<AiImageTariffSnapshotItem> Tariffs { get; set; } = new();

    public AiImageTariffSnapshotItem? Find(string? model)
        => string.IsNullOrWhiteSpace(model)
            ? null
            : Tariffs.FirstOrDefault(t => string.Equals(t.Model, model, StringComparison.OrdinalIgnoreCase));

    public static AiImageTariffSnapshot Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new AiImageTariffSnapshot();
        try
        {
            return JsonSerializer.Deserialize<AiImageTariffSnapshot>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                   ?? new AiImageTariffSnapshot();
        }
        catch
        {
            return new AiImageTariffSnapshot();
        }
    }
}

public sealed class AiImageTariffSnapshotItem
{
    public string? Model { get; set; }
    public long? ProviderCapabilityId { get; set; }
    public decimal? UnitCostPoints { get; set; }
    public decimal? ProviderEstimatedCostUsd { get; set; }
    public string? CostSource { get; set; }
}
