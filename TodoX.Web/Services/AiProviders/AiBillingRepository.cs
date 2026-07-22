using System.Data;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public interface IAiBillingRepository
{
    Task<AiBillingReservation> GetOrCreateReservationAsync(AiBillingReservationRequest request, AiBillingEstimateResult estimate, CancellationToken ct = default);
    Task<AiBillingCompletion> CompleteBillingAsync(AiBillingCompletionRequest request, CancellationToken ct = default);
    Task<AiBillingCompletion> ReleaseReservationAsync(AiBillingCompletionRequest request, CancellationToken ct = default);
    Task<AiBillingCompletion> MarkPendingReconciliationAsync(AiBillingCompletionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AiBillingReconciliationItem>> ClaimReconciliationBatchAsync(string workerKey, int batchSize, TimeSpan lockFor, int maxAttempts, CancellationToken ct = default);
    Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default);
    Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default);
    Task<AiBillingRefundResult> RefundAsync(AiBillingRefundRequest request, CancellationToken ct = default);
    Task UpsertProviderAttemptAsync(Guid billingRecordId, AiBillingCompletionRequest request, IDbConnection conn, IDbTransaction tx);
}

public sealed class AiBillingRepository : IAiBillingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IAiBillingPayerResolver _payerResolver;

    public AiBillingRepository(TodoXConnectionFactory factory, TenantContext tenant, IAiBillingPayerResolver payerResolver)
    {
        _factory = factory;
        _tenant = tenant;
        _payerResolver = payerResolver;
    }

    public async Task<AiBillingReservation> GetOrCreateReservationAsync(AiBillingReservationRequest request, AiBillingEstimateResult estimate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LogicalRequestId))
        {
            return new AiBillingReservation(false, false, string.Empty, "invalid", string.Empty, 0, null, null, "Missing logical_request_id.");
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
            return new AiBillingReservation(false, false, string.Empty, "missing_payer", request.LogicalRequestId, 0, null, null, ex.Message);
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockLogicalRequestAsync(conn, tx, request.LogicalRequestId);

        var existing = await LockBillingRecordAsync(conn, tx, request.LogicalRequestId);
        if (existing is not null)
        {
            tx.Commit();
            return ExistingReservation(existing);
        }

        var reservedPoints = Math.Max(0, estimate.EstimatedPoints);
        var wallet = payer.PayerType == AiBillingPayerTypes.Customer
            ? await EnsureCustomerWalletForUpdateAsync(conn, tx, payer.PayerCustomerId!.Value)
            : await GetSystemWalletForUpdateAsync(conn, tx, payer.SystemWalletCode!);

        var available = wallet.Balance + (payer.PayerType == AiBillingPayerTypes.System ? wallet.OverdraftLimit : 0);
        if (available < reservedPoints)
        {
            var insufficientId = await InsertBillingRecordAsync(conn, tx, request, estimate, payer, wallet.Id, "insufficient", reservedPoints, null);
            tx.Commit();
            return new AiBillingReservation(false, false, payer.PayerType, "insufficient", request.LogicalRequestId, reservedPoints, insufficientId, null,
                $"Insufficient TodoX points. Required {reservedPoints:0.####}, available {available:0.####}.");
        }

        var reservationTransactionId = reservedPoints > 0 ? Guid.NewGuid() : (Guid?)null;
        var recordId = await InsertBillingRecordAsync(conn, tx, request, estimate, payer, wallet.Id, "reserved", reservedPoints, reservationTransactionId);

        if (reservedPoints > 0)
        {
            await conn.ExecuteAsync(
                """
                UPDATE billing.token_wallets
                   SET balance = balance - @reservedPoints,
                       locked_balance = locked_balance + @reservedPoints,
                       updated_at = now()
                 WHERE id=@walletId;
                """,
                new { reservedPoints, walletId = wallet.Id },
                tx);

            await InsertTokenTransactionAsync(conn, tx, reservationTransactionId!.Value, wallet.Id, "reserve", reservedPoints, wallet.Balance, wallet.Balance - reservedPoints,
                "ai_billing_reservation", recordId, $"AI billing reserve {request.LogicalRequestId}", request.CreatedBy);
        }

        tx.Commit();
        return new AiBillingReservation(true, true, payer.PayerType, "reserved", request.LogicalRequestId, reservedPoints, recordId, reservationTransactionId, null);
    }

    public async Task<AiBillingCompletion> CompleteBillingAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockLogicalRequestAsync(conn, tx, request.LogicalRequestId);

        var record = await LockBillingRecordAsync(conn, tx, request.LogicalRequestId);
        if (record is null)
        {
            tx.Commit();
            return new AiBillingCompletion(false, "missing_record", null, "Billing reservation was not found.");
        }

        await UpsertProviderAttemptAsync(record.Id, request, conn, tx);
        if (record.Status is "completed" or "released" or "failed" or "cancelled")
        {
            tx.Commit();
            return new AiBillingCompletion(true, record.Status, record.ChargeTransactionId ?? record.WalletTransactionId, null);
        }

        if (!request.Success)
        {
            await ReleaseReservationCoreAsync(conn, tx, record, request, "released");
            tx.Commit();
            return new AiBillingCompletion(true, "released", null, null);
        }

        var reservedPoints = record.ReservedPoints;
        Guid? txId = record.ChargeTransactionId;
        if (reservedPoints > 0 && record.WalletId is Guid walletId)
        {
            var wallet = await LockWalletAsync(conn, tx, walletId);
            var release = Math.Min(wallet.LockedBalance, reservedPoints);
            if (release > 0)
            {
                await conn.ExecuteAsync(
                    "UPDATE billing.token_wallets SET locked_balance = locked_balance - @release, updated_at=now() WHERE id=@walletId;",
                    new { release, walletId },
                    tx);
            }

            txId ??= Guid.NewGuid();
            await InsertTokenTransactionAsync(conn, tx, txId.Value, walletId, "charge", reservedPoints, wallet.Balance, wallet.Balance,
                "ai_billing_charge", record.Id, $"AI billing charge {record.LogicalRequestId}", record.CreatedBy);
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status='completed',
                   billing_status='completed',
                   charged_points=GREATEST(charged_points, reserved_points),
                   provider_task_id=COALESCE(@ProviderTaskId, provider_task_id),
                   actual_model=COALESCE(@ActualModel, actual_model),
                   provider_usage_json=COALESCE(CAST(@ProviderUsageJson AS jsonb), provider_usage_json),
                   provider_actual_cost=@ProviderActualCost,
                   provider_actual_cost_usd=CASE WHEN lower(COALESCE(@ProviderCurrency, 'usd'))='usd' THEN @ProviderActualCost ELSE provider_actual_cost_usd END,
                   provider_currency=COALESCE(@ProviderCurrency, provider_currency),
                   provider_cost_source=CASE WHEN @ProviderActualCost IS NULL THEN provider_cost_source ELSE 'provider_actual' END,
                   charge_transaction_id=COALESCE(charge_transaction_id, @txId),
                   wallet_transaction_id=COALESCE(wallet_transaction_id, @txId),
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE id=@id;
            """,
            new
            {
                id = record.Id,
                request.ProviderTaskId,
                request.ActualModel,
                request.ProviderUsageJson,
                request.ProviderActualCost,
                request.ProviderCurrency,
                txId
            },
            tx);

        tx.Commit();
        return new AiBillingCompletion(true, "completed", txId, null);
    }

    public async Task<AiBillingCompletion> ReleaseReservationAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockLogicalRequestAsync(conn, tx, request.LogicalRequestId);
        var record = await LockBillingRecordAsync(conn, tx, request.LogicalRequestId);
        if (record is null)
        {
            tx.Commit();
            return new AiBillingCompletion(false, "missing_record", null, "Billing reservation was not found.");
        }

        await UpsertProviderAttemptAsync(record.Id, request, conn, tx);
        if (record.Status is "completed" or "released" or "cancelled")
        {
            tx.Commit();
            return new AiBillingCompletion(true, record.Status, record.ChargeTransactionId ?? record.WalletTransactionId, null);
        }

        await ReleaseReservationCoreAsync(conn, tx, record, request, "released");
        tx.Commit();
        return new AiBillingCompletion(true, "released", null, null);
    }

    public async Task<AiBillingCompletion> MarkPendingReconciliationAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockLogicalRequestAsync(conn, tx, request.LogicalRequestId);
        var record = await LockBillingRecordAsync(conn, tx, request.LogicalRequestId);
        if (record is null)
        {
            tx.Commit();
            return new AiBillingCompletion(false, "missing_record", null, "Billing reservation was not found.");
        }

        await UpsertProviderAttemptAsync(record.Id, request, conn, tx);
        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status='pending_reconciliation',
                   billing_status='pending_reconciliation',
                   provider_task_id=COALESCE(@ProviderTaskId, provider_task_id),
                   actual_model=COALESCE(@ActualModel, actual_model),
                   provider_usage_json=COALESCE(CAST(@ProviderUsageJson AS jsonb), provider_usage_json),
                   error_message=@ErrorMessage,
                   pending_reconciliation_at=now(),
                   next_reconciliation_at=now(),
                   updated_at=now()
             WHERE id=@id
               AND status IN ('reserved','pending_provider','pending_reconciliation');
            """,
            new { id = record.Id, request.ProviderTaskId, request.ActualModel, request.ProviderUsageJson, request.ErrorMessage },
            tx);
        tx.Commit();
        return new AiBillingCompletion(true, "pending_reconciliation", record.ChargeTransactionId ?? record.WalletTransactionId, null);
    }

    public async Task<IReadOnlyList<AiBillingReconciliationItem>> ClaimReconciliationBatchAsync(string workerKey, int batchSize, TimeSpan lockFor, int maxAttempts, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var rows = (await conn.QueryAsync<AiBillingReconciliationItem>(
            """
            WITH claimed AS (
                SELECT id
                  FROM billing.ai_billing_records
                 WHERE status IN ('reserved','pending_reconciliation')
                   AND COALESCE(reconciliation_attempt_count, 0) < @maxAttempts
                   AND (reconciliation_lock_until IS NULL OR reconciliation_lock_until < now())
                   AND (status='pending_reconciliation' OR (status='reserved' AND reserved_until < now()))
                 ORDER BY COALESCE(pending_reconciliation_at, reserved_until, created_at), id
                 FOR UPDATE SKIP LOCKED
                 LIMIT @batchSize
            )
            UPDATE billing.ai_billing_records b
               SET reconciliation_lock_owner=@workerKey,
                   reconciliation_lock_until=now() + (@lockSeconds || ' seconds')::interval,
                   reconciliation_attempt_count=COALESCE(reconciliation_attempt_count, 0) + 1,
                   pending_reconciliation_at=COALESCE(pending_reconciliation_at, now()),
                   status=CASE WHEN status='reserved' THEN 'pending_reconciliation' ELSE status END,
                   billing_status='pending_reconciliation',
                   updated_at=now()
              FROM claimed
             WHERE b.id=claimed.id
             RETURNING b.id AS Id,
                       b.provider_account_id AS ProviderAccountId,
                       b.logical_request_id AS LogicalRequestId,
                       b.status AS Status,
                       b.requested_model AS RequestedModel,
                       b.actual_model AS ActualModel,
                       b.provider_task_id AS ProviderTaskId,
                       COALESCE(b.reconciliation_attempt_count, 0) AS ReconciliationAttemptCount,
                       b.pricing_snapshot_json::text AS PricingSnapshotJson;
            """,
            new
            {
                workerKey,
                batchSize = Math.Max(1, batchSize),
                lockSeconds = Math.Max(1, (int)lockFor.TotalSeconds),
                maxAttempts = Math.Max(1, maxAttempts)
            },
            tx)).ToList();
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
               SET status='manual_review',
                   billing_status='manual_review',
                   error_message=@errorMessage,
                   reconciliation_lock_owner=NULL,
                   reconciliation_lock_until=NULL,
                   updated_at=now()
             WHERE logical_request_id=@logicalRequestId
               AND status IN ('reserved','pending_provider','pending_reconciliation','manual_review');
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
               SET status='pending_reconciliation',
                   billing_status='pending_reconciliation',
                   error_message=@errorMessage,
                   pending_reconciliation_at=now() + (@delaySeconds || ' seconds')::interval,
                   next_reconciliation_at=now() + (@delaySeconds || ' seconds')::interval,
                   reconciliation_lock_owner=NULL,
                   reconciliation_lock_until=NULL,
                   updated_at=now()
             WHERE logical_request_id=@logicalRequestId
               AND status='pending_reconciliation';
            """,
            new { logicalRequestId, errorMessage, delaySeconds = Math.Max(1, (int)delay.TotalSeconds) });
    }

    public async Task<AiBillingRefundResult> RefundAsync(AiBillingRefundRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));", new { lockName = $"{_tenant.TenantId:N}:{request.LogicalRequestId}:refund" }, tx);
        var record = await LockBillingRecordAsync(conn, tx, request.LogicalRequestId);
        if (record is null)
        {
            tx.Commit();
            return new AiBillingRefundResult(false, "missing_record", 0, "Billing record was not found.");
        }

        var refundable = Math.Max(0, record.ChargedPoints - record.RefundedPoints);
        var refund = Math.Min(Math.Max(0, request.Points), refundable);
        if (refund <= 0)
        {
            tx.Commit();
            return new AiBillingRefundResult(true, record.RefundStatus ?? "none", record.RefundedPoints, null);
        }

        if (record.WalletId is not Guid walletId)
        {
            await conn.ExecuteAsync("UPDATE billing.ai_billing_records SET refund_status='manual_review', updated_at=now() WHERE id=@id;", new { id = record.Id }, tx);
            tx.Commit();
            return new AiBillingRefundResult(false, "manual_review", record.RefundedPoints, "Missing wallet id for refund.");
        }

        var wallet = await LockWalletAsync(conn, tx, walletId);
        var txId = record.RefundTransactionId ?? Guid.NewGuid();
        await InsertTokenTransactionAsync(conn, tx, txId, walletId, "refund", refund, wallet.Balance, wallet.Balance + refund, "ai_billing_refund", record.Id, request.Reason ?? $"AI billing refund {record.LogicalRequestId}", record.CreatedBy);
        await conn.ExecuteAsync("UPDATE billing.token_wallets SET balance=balance + @refund, updated_at=now() WHERE id=@walletId;", new { refund, walletId }, tx);
        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET refunded_points=COALESCE(refunded_points, 0) + @refund,
                   refund_status=CASE WHEN COALESCE(refunded_points, 0) + @refund >= charged_points THEN 'completed' ELSE 'pending' END,
                   refund_transaction_id=COALESCE(refund_transaction_id, @txId),
                   updated_at=now()
             WHERE id=@id;
            """,
            new { id = record.Id, refund, txId },
            tx);
        tx.Commit();
        return new AiBillingRefundResult(true, "completed", record.RefundedPoints + refund, null);
    }

    public async Task UpsertProviderAttemptAsync(Guid billingRecordId, AiBillingCompletionRequest request, IDbConnection conn, IDbTransaction tx)
    {
        var attempts = AiImageBillingAttemptParser.Parse(new AiImageBillingCompleteRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            Success = request.Success,
            ActualModel = request.ActualModel,
            ProviderTaskId = request.ProviderTaskId,
            ProviderActualCostUsd = string.Equals(request.ProviderCurrency, "usd", StringComparison.OrdinalIgnoreCase) ? request.ProviderActualCost : null,
            ProviderUsageJson = request.ProviderUsageJson,
            TariffSnapshotJson = request.PricingSnapshotJson,
            ErrorMessage = request.ErrorMessage
        });

        var record = await conn.QuerySingleAsync<BillingRecord>("SELECT render_job_id AS RenderJobId, provider_account_id AS ProviderAccountId FROM billing.ai_billing_records WHERE id=@billingRecordId;", new { billingRecordId }, tx);
        foreach (var attempt in attempts)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO billing.ai_provider_attempts
                    (id, billing_record_id, render_job_id, provider_account_id, provider_task_id, attempt_no, attempt_number,
                     model_name, status, success, usage_quantity, usage_unit, provider_cost, provider_currency,
                     provider_estimated_cost_usd, provider_actual_cost_usd, cost_source, raw_usage_json,
                     error_code, error_message, started_at, completed_at, created_at)
                VALUES
                    (gen_random_uuid(), @billingRecordId, @RenderJobId, @ProviderAccountId, @ProviderTaskId, @AttemptNumber, @AttemptNumber,
                     @ModelName, @Status, @Success, @UsageQuantity, @UsageUnit, @ProviderCost, @ProviderCurrency,
                     @ProviderEstimatedCostUsd, @ProviderActualCostUsd, @CostSource, CAST(@RawUsageJson AS jsonb),
                     @ErrorCode, @ErrorMessage, @StartedAt, @CompletedAt, now())
                ON CONFLICT (billing_record_id, attempt_number) DO UPDATE
                    SET provider_task_id=COALESCE(EXCLUDED.provider_task_id, billing.ai_provider_attempts.provider_task_id),
                        status=EXCLUDED.status,
                        success=EXCLUDED.success,
                        usage_quantity=EXCLUDED.usage_quantity,
                        usage_unit=EXCLUDED.usage_unit,
                        provider_cost=EXCLUDED.provider_cost,
                        provider_currency=EXCLUDED.provider_currency,
                        provider_actual_cost_usd=EXCLUDED.provider_actual_cost_usd,
                        raw_usage_json=EXCLUDED.raw_usage_json,
                        error_code=EXCLUDED.error_code,
                        error_message=EXCLUDED.error_message,
                        completed_at=EXCLUDED.completed_at;
                """,
                new
                {
                    billingRecordId,
                    record.RenderJobId,
                    record.ProviderAccountId,
                    attempt.ProviderTaskId,
                    attempt.AttemptNumber,
                    attempt.ModelName,
                    Status = attempt.Success ? "completed" : "failed",
                    attempt.Success,
                    UsageQuantity = ReadUsageQuantity(request.ProviderUsageJson),
                    UsageUnit = ReadUsageUnit(request.ProviderUsageJson),
                    ProviderCost = request.ProviderActualCost,
                    ProviderCurrency = request.ProviderCurrency,
                    attempt.ProviderEstimatedCostUsd,
                    attempt.ProviderActualCostUsd,
                    attempt.CostSource,
                    RawUsageJson = string.IsNullOrWhiteSpace(request.ProviderUsageJson) ? "{}" : request.ProviderUsageJson,
                    attempt.ErrorCode,
                    attempt.ErrorMessage,
                    attempt.StartedAt,
                    attempt.CompletedAt
                },
                tx);
        }
    }

    private async Task ReleaseReservationCoreAsync(IDbConnection conn, IDbTransaction tx, BillingRecord record, AiBillingCompletionRequest request, string status)
    {
        if (record.WalletId is Guid walletId && record.ReservedPoints > 0)
        {
            var wallet = await LockWalletAsync(conn, tx, walletId);
            var release = Math.Min(wallet.LockedBalance, record.ReservedPoints);
            if (release > 0)
            {
                await conn.ExecuteAsync(
                    "UPDATE billing.token_wallets SET balance=balance + @release, locked_balance=locked_balance - @release, updated_at=now() WHERE id=@walletId;",
                    new { release, walletId },
                    tx);

                await InsertTokenTransactionAsync(conn, tx, Guid.NewGuid(), walletId, "release", release, wallet.Balance, wallet.Balance + release,
                    "ai_billing_release", record.Id, $"AI billing release {record.LogicalRequestId}", record.CreatedBy);
            }
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET status=@status,
                   billing_status='released',
                   provider_task_id=COALESCE(@ProviderTaskId, provider_task_id),
                   actual_model=COALESCE(@ActualModel, actual_model),
                   provider_usage_json=COALESCE(CAST(@ProviderUsageJson AS jsonb), provider_usage_json),
                   provider_actual_cost=@ProviderActualCost,
                   provider_currency=COALESCE(@ProviderCurrency, provider_currency),
                   error_message=@ErrorMessage,
                   failed_at=CASE WHEN @status IN ('released','failed') THEN COALESCE(failed_at, now()) ELSE failed_at END,
                   updated_at=now()
             WHERE id=@id;
            """,
            new { id = record.Id, status, request.ProviderTaskId, request.ActualModel, request.ProviderUsageJson, request.ProviderActualCost, request.ProviderCurrency, request.ErrorMessage },
            tx);
    }

    private async Task<Guid> InsertBillingRecordAsync(IDbConnection conn, IDbTransaction tx, AiBillingReservationRequest request, AiBillingEstimateResult estimate, AiBillingPayerContext payer, Guid walletId, string status, decimal reservedPoints, Guid? reservationTransactionId)
    {
        var renderJobId = Guid.TryParse(request.RenderJobId, out var parsedRenderJobId) ? parsedRenderJobId : (Guid?)null;
        var customerPoints = payer.PayerType == AiBillingPayerTypes.Customer ? reservedPoints : 0m;
        var systemPoints = payer.PayerType == AiBillingPayerTypes.System ? reservedPoints : 0m;
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO billing.ai_billing_records
                (id, tenant_id, logical_request_id, render_job_id, customer_id, user_id, wallet_id,
                 payer_type, payer_customer_id, payer_wallet_id, provider_id, provider_capability_id, provider_account_id,
                 provider_code, capability_code, feature_code, requested_model, status, billing_status, refund_status,
                 estimated_points, reserved_points, charged_points, refunded_points,
                 customer_charged_points, system_charged_points, provider_estimated_cost, provider_estimated_cost_usd,
                 provider_currency, provider_cost_source, exchange_rate_vnd_per_usd, todox_vnd_per_point,
                 provider_cost_points, pricing_snapshot_json, tariff_snapshot_json, metadata_json, created_by,
                 reservation_transaction_id, wallet_transaction_id, reserved_until, created_at, updated_at)
            VALUES
                (gen_random_uuid(), @tenant, @LogicalRequestId, @renderJobId, @CustomerId, @UserId, @walletId,
                 @PayerType, @PayerCustomerId, @walletId, @ProviderId, @ProviderCapabilityId, @ProviderAccountId,
                 @ProviderCode, @CapabilityCode, @FeatureCode, @RequestedModel, @status, @billingStatus, 'none',
                 @reservedPoints, @reservedPoints, 0, 0,
                 @customerPoints, @systemPoints, @ProviderEstimatedCostUsd, @ProviderEstimatedCostUsd,
                 'usd', 'configured_tariff', @ExchangeRateVndPerUsd, @TodoXVndPerPoint,
                 @reservedPoints, COALESCE(CAST(@PricingSnapshotJson AS jsonb), '{}'::jsonb), COALESCE(CAST(@PricingSnapshotJson AS jsonb), '{}'::jsonb),
                 CAST(@MetadataJson AS jsonb), @CreatedBy, @reservationTransactionId, @reservationTransactionId, now() + interval '30 minutes', now(), now())
            RETURNING id;
            """,
            new
            {
                tenant = _tenant.TenantId,
                request.LogicalRequestId,
                renderJobId,
                CustomerId = payer.PayerCustomerId ?? request.CustomerId,
                request.UserId,
                walletId,
                payer.PayerType,
                PayerCustomerId = payer.PayerCustomerId,
                request.ProviderId,
                request.ProviderCapabilityId,
                request.ProviderAccountId,
                request.ProviderCode,
                request.CapabilityCode,
                request.FeatureCode,
                request.RequestedModel,
                status,
                billingStatus = status == "reserved" ? "reserved" : status,
                reservedPoints,
                customerPoints,
                systemPoints,
                estimate.ProviderEstimatedCostUsd,
                estimate.ExchangeRateVndPerUsd,
                estimate.TodoXVndPerPoint,
                reservationTransactionId,
                PricingSnapshotJson = request.PricingSnapshotJson,
                MetadataJson = JsonSerializer.Serialize(new { request = request.Metadata, payer = new { payer.PayerType, payer.ResolutionSource, payer.SystemWalletCode } }, JsonOptions),
                request.CreatedBy
            },
            tx);
    }

    private async Task<WalletRow> EnsureCustomerWalletForUpdateAsync(IDbConnection conn, IDbTransaction tx, Guid customerId)
    {
        var wallet = await conn.QuerySingleOrDefaultAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
              FROM billing.token_wallets
             WHERE tenant_id=@tenant AND wallet_scope='customer' AND customer_id=@customerId
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, customerId },
            tx);
        if (wallet is not null) return wallet;

        var walletId = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_wallets
                (id, tenant_id, customer_id, wallet_scope, balance, locked_balance, overdraft_limit, status, created_at, updated_at)
            VALUES
                (@walletId, @tenant, @customerId, 'customer', 100, 0, 0, 'active', now(), now())
            ON CONFLICT DO NOTHING;
            """,
            new { walletId, tenant = _tenant.TenantId, customerId },
            tx);
        return await EnsureCustomerWalletForUpdateAsync(conn, tx, customerId);
    }

    private async Task<WalletRow> GetSystemWalletForUpdateAsync(IDbConnection conn, IDbTransaction tx, string walletCode)
        => await conn.QuerySingleOrDefaultAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit
              FROM billing.token_wallets
             WHERE tenant_id=@tenant AND wallet_scope='system' AND wallet_code=@walletCode
             LIMIT 1
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, walletCode },
            tx)
           ?? throw new InvalidOperationException($"System wallet '{walletCode}' is missing.");

    private static async Task<BillingRecord?> LockBillingRecordAsync(IDbConnection conn, IDbTransaction tx, string logicalRequestId)
        => await conn.QuerySingleOrDefaultAsync<BillingRecord>(
            """
            SELECT id AS Id, logical_request_id AS LogicalRequestId, render_job_id AS RenderJobId,
                   provider_account_id AS ProviderAccountId, payer_type AS PayerType,
                   COALESCE(payer_wallet_id, wallet_id) AS WalletId,
                   wallet_transaction_id AS WalletTransactionId,
                   charge_transaction_id AS ChargeTransactionId,
                   refund_transaction_id AS RefundTransactionId,
                   COALESCE(reserved_points, customer_charged_points + system_charged_points, 0) AS ReservedPoints,
                   COALESCE(charged_points, 0) AS ChargedPoints,
                   COALESCE(refunded_points, 0) AS RefundedPoints,
                   status AS Status, refund_status AS RefundStatus, created_by AS CreatedBy
              FROM billing.ai_billing_records
             WHERE logical_request_id=@logicalRequestId
             FOR UPDATE;
            """,
            new { logicalRequestId },
            tx);

    private static async Task<WalletRow> LockWalletAsync(IDbConnection conn, IDbTransaction tx, Guid walletId)
        => await conn.QuerySingleAsync<WalletRow>(
            "SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance, COALESCE(overdraft_limit, 0) AS OverdraftLimit FROM billing.token_wallets WHERE id=@walletId FOR UPDATE;",
            new { walletId },
            tx);

    private async Task LockLogicalRequestAsync(IDbConnection conn, IDbTransaction tx, string logicalRequestId)
        => await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));", new { lockName = $"{_tenant.TenantId:N}:{logicalRequestId}" }, tx);

    private async Task InsertTokenTransactionAsync(IDbConnection conn, IDbTransaction tx, Guid txId, Guid walletId, string type, decimal amount, decimal before, decimal after, string referenceType, Guid recordId, string description, string? createdBy)
        => await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_transactions
                (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                 reference_type, reference_id, description, created_at, created_by)
            VALUES
                (@txId, @tenant, @walletId, @type, @amount, @before, @after,
                 @referenceType, @recordId, @description, now(), @createdBy)
            ON CONFLICT DO NOTHING;
            """,
            new { txId, tenant = _tenant.TenantId, walletId, type, amount, before, after, referenceType, recordId, description, createdBy },
            tx);

    private static AiBillingReservation ExistingReservation(BillingRecord existing)
        => existing.Status switch
        {
            "completed" => new AiBillingReservation(true, false, existing.PayerType, existing.Status, existing.LogicalRequestId, existing.ReservedPoints, existing.Id, existing.ChargeTransactionId ?? existing.WalletTransactionId, "Billing request was already completed."),
            "reserved" or "pending_provider" or "pending_reconciliation" => new AiBillingReservation(true, false, existing.PayerType, existing.Status, existing.LogicalRequestId, existing.ReservedPoints, existing.Id, existing.ChargeTransactionId ?? existing.WalletTransactionId, "Billing request is already active."),
            _ => new AiBillingReservation(false, false, existing.PayerType, existing.Status, existing.LogicalRequestId, existing.ReservedPoints, existing.Id, existing.ChargeTransactionId ?? existing.WalletTransactionId, $"Billing record is in status '{existing.Status}'.")
        };

    private static decimal? ReadUsageQuantity(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("creditsConsumed", out var credits) && credits.TryGetDecimal(out var value)) return value;
            if (doc.RootElement.TryGetProperty("usageQuantity", out var usage) && usage.TryGetDecimal(out var usageValue)) return usageValue;
        }
        catch (JsonException) { }
        return null;
    }

    private static string? ReadUsageUnit(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("creditsConsumed", out _)) return "credits";
            if (doc.RootElement.TryGetProperty("usageUnit", out var unit) && unit.ValueKind == JsonValueKind.String) return unit.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private sealed class BillingRecord
    {
        public Guid Id { get; init; }
        public string LogicalRequestId { get; init; } = string.Empty;
        public Guid? RenderJobId { get; init; }
        public Guid? ProviderAccountId { get; init; }
        public string PayerType { get; init; } = AiBillingPayerTypes.Customer;
        public Guid? WalletId { get; init; }
        public Guid? WalletTransactionId { get; init; }
        public Guid? ChargeTransactionId { get; init; }
        public Guid? RefundTransactionId { get; init; }
        public decimal ReservedPoints { get; init; }
        public decimal ChargedPoints { get; init; }
        public decimal RefundedPoints { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? RefundStatus { get; init; }
        public string? CreatedBy { get; init; }
    }

    private sealed class WalletRow
    {
        public Guid Id { get; init; }
        public decimal Balance { get; init; }
        public decimal LockedBalance { get; init; }
        public decimal OverdraftLimit { get; init; }
    }
}
