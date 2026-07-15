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
    public bool BillingExempt { get; set; }
    public string? ExemptionReason { get; set; }
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
    public string? ErrorMessage { get; set; }
}

public sealed record AiImageBillingReservation(
    bool Ok,
    bool ShouldSubmitProvider,
    bool BillingExempt,
    string Status,
    string LogicalRequestId,
    decimal CustomerChargedPoints,
    Guid? BillingRecordId,
    Guid? WalletTransactionId,
    string? ErrorMessage)
{
    public static AiImageBillingReservation Failed(string logicalRequestId, string status, string message)
        => new(false, false, false, status, logicalRequestId, 0, null, null, message);
}

public sealed record AiImageBillingCompletion(
    bool Ok,
    string Status,
    Guid? WalletTransactionId,
    string? ErrorMessage);

public interface IAiImageBillingService
{
    AiImageBillingCost BuildConfiguredCost(decimal unitCostPoints, decimal quantity);
    Task<AiImageBillingReservation> ReserveAsync(AiImageBillingReserveRequest request, CancellationToken ct = default);
    Task<AiImageBillingCompletion> CompleteAsync(AiImageBillingCompleteRequest request, CancellationToken ct = default);
}

public sealed class AiImageBillingService : IAiImageBillingService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly ILogger<AiImageBillingService> _logger;

    public AiImageBillingService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IConfiguration config,
        ILogger<AiImageBillingService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _config = config;
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

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var existing = await GetRecordForUpdateAsync(conn, tx, request.LogicalRequestId);
        if (existing is not null)
        {
            var decision = await HandleExistingReservationAsync(conn, tx, existing, request, ct);
            tx.Commit();
            return decision;
        }

        if (request.BillingExempt)
        {
            var recordId = await InsertRecordAsync(conn, tx, request, walletId: null, status: "reserved_exempt", walletTransactionId: null);
            tx.Commit();
            return new AiImageBillingReservation(true, true, true, "reserved_exempt", request.LogicalRequestId, 0, recordId, null, null);
        }

        if (request.CustomerId is null)
        {
            var recordId = await InsertRecordAsync(conn, tx, request, walletId: null, status: "missing_customer", walletTransactionId: null);
            tx.Commit();
            return new AiImageBillingReservation(false, false, false, "missing_customer", request.LogicalRequestId, request.Cost.CustomerChargedPoints,
                recordId, null, "Missing customer UUID for chargeable image render.");
        }

        var wallet = await EnsureWalletForUpdateAsync(conn, tx, request.CustomerId.Value);
        if (wallet.Balance < request.Cost.CustomerChargedPoints)
        {
            var recordId = await InsertRecordAsync(conn, tx, request, wallet.Id, "insufficient", walletTransactionId: null);
            tx.Commit();
            return new AiImageBillingReservation(false, false, false, "insufficient", request.LogicalRequestId, request.Cost.CustomerChargedPoints,
                recordId, null, $"Insufficient TodoX points. Required {request.Cost.CustomerChargedPoints:0.####}, available {wallet.Balance:0.####}.");
        }

        await conn.ExecuteAsync(
            """
            UPDATE billing.token_wallets
               SET balance = balance - @amount,
                   locked_balance = locked_balance + @amount,
                   updated_at = now()
             WHERE id = @walletId;
            """,
            new { amount = request.Cost.CustomerChargedPoints, walletId = wallet.Id }, tx);

        var reservedId = await InsertRecordAsync(conn, tx, request, wallet.Id, "reserved", walletTransactionId: null);
        tx.Commit();
        return new AiImageBillingReservation(true, true, false, "reserved", request.LogicalRequestId, request.Cost.CustomerChargedPoints, reservedId, null, null);
    }

    public async Task<AiImageBillingCompletion> CompleteAsync(AiImageBillingCompleteRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

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
            await ReleaseReservationAsync(conn, tx, record, request);
            tx.Commit();
            return new AiImageBillingCompletion(true, "released", null, null);
        }

        if (record.BillingExempt || record.CustomerChargedPoints <= 0)
        {
            await conn.ExecuteAsync(
                """
                UPDATE billing.ai_image_billing_records
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
            tx.Commit();
            return new AiImageBillingCompletion(true, "completed", null, null);
        }

        if (record.WalletId is not Guid walletId)
        {
            await ReleaseReservationAsync(conn, tx, record, new AiImageBillingCompleteRequest
            {
                LogicalRequestId = request.LogicalRequestId,
                Success = false,
                ActualModel = request.ActualModel,
                ProviderTaskId = request.ProviderTaskId,
                ProviderActualCostUsd = request.ProviderActualCostUsd,
                ProviderUsageJson = request.ProviderUsageJson,
                ErrorMessage = "Missing wallet id on reserved billing record."
            });
            tx.Commit();
            return new AiImageBillingCompletion(false, "released", null, "Missing wallet id on reserved billing record.");
        }

        var wallet = await conn.QuerySingleAsync<WalletRow>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance
              FROM billing.token_wallets
             WHERE id = @walletId
             FOR UPDATE;
            """,
            new { walletId }, tx);

        var lockedAfter = Math.Max(0, wallet.LockedBalance - record.CustomerChargedPoints);
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
                amount = record.CustomerChargedPoints,
                before = wallet.Balance + record.CustomerChargedPoints,
                after = wallet.Balance,
                recordId = record.Id,
                description = $"AI image render {record.LogicalRequestId}",
                createdBy = record.CreatedBy
            }, tx);

        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_image_billing_records
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

    private async Task<AiImageBillingReservation> HandleExistingReservationAsync(
        IDbConnection conn,
        IDbTransaction tx,
        BillingRecord existing,
        AiImageBillingReserveRequest request,
        CancellationToken ct)
    {
        if (existing.Status is "completed")
        {
            return new AiImageBillingReservation(true, false, existing.BillingExempt, "completed", existing.LogicalRequestId,
                existing.CustomerChargedPoints, existing.Id, existing.WalletTransactionId, "Image render request was already completed.");
        }

        if (existing.Status is "reserved" or "reserved_exempt")
        {
            return new AiImageBillingReservation(true, false, existing.BillingExempt, existing.Status, existing.LogicalRequestId,
                existing.CustomerChargedPoints, existing.Id, existing.WalletTransactionId, "Image render request is already in progress.");
        }

        if (existing.Status is "insufficient" or "missing_customer")
        {
            return new AiImageBillingReservation(false, false, existing.BillingExempt, existing.Status, existing.LogicalRequestId,
                existing.CustomerChargedPoints, existing.Id, existing.WalletTransactionId, $"Image billing record is already '{existing.Status}'. Create a new logical_request_id after fixing the customer balance/scope.");
        }

        return new AiImageBillingReservation(false, false, existing.BillingExempt, existing.Status, existing.LogicalRequestId,
            existing.CustomerChargedPoints, existing.Id, existing.WalletTransactionId, $"Image billing record is in status '{existing.Status}'.");
    }

    private async Task<WalletRow> EnsureWalletForUpdateAsync(IDbConnection conn, IDbTransaction tx, Guid customerId)
    {
        var wallet = await conn.QuerySingleOrDefaultAsync<WalletRow?>(
            """
            SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance
              FROM billing.token_wallets
             WHERE tenant_id = @tenant AND customer_id = @customerId
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
            INSERT INTO billing.token_wallets (id, tenant_id, customer_id, balance, locked_balance, status, created_at, updated_at)
            VALUES (@walletId, @tenant, @customerId, @seed, 0, 'active', now(), now());
            """,
            new { walletId, tenant = _tenant.TenantId, customerId, seed }, tx);

        return new WalletRow { Id = walletId, Balance = seed, LockedBalance = 0 };
    }

    private async Task<Guid> InsertRecordAsync(
        IDbConnection conn,
        IDbTransaction tx,
        AiImageBillingReserveRequest request,
        Guid? walletId,
        string status,
        Guid? walletTransactionId)
    {
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO billing.ai_image_billing_records
                (id, tenant_id, logical_request_id, render_job_id, customer_id, user_id, wallet_id,
                 provider_id, provider_capability_id, provider_code, capability_code, feature_code,
                 requested_model, provider_estimated_cost_usd, provider_actual_cost_usd, provider_cost_source,
                 exchange_rate_vnd_per_usd, todox_vnd_per_point, provider_cost_points, customer_charged_points,
                 billing_exempt, exemption_reason, wallet_transaction_id, status, metadata_json, created_by, created_at, updated_at)
            VALUES
                (gen_random_uuid(), @tenant, @LogicalRequestId, @RenderJobId, @CustomerId, @UserId, @walletId,
                 @ProviderId, @ProviderCapabilityId, @ProviderCode, @CapabilityCode, @FeatureCode,
                 @RequestedModel, @ProviderEstimatedCostUsd, @ProviderActualCostUsd, @ProviderCostSource,
                 @ExchangeRateVndPerUsd, @TodoXVndPerPoint, @ProviderCostPoints, @CustomerChargedPoints,
                 @BillingExempt, @ExemptionReason, @walletTransactionId, @status, CAST(@MetadataJson AS jsonb), @CreatedBy, now(), now())
            RETURNING id;
            """,
            new
            {
                tenant = _tenant.TenantId,
                request.LogicalRequestId,
                request.RenderJobId,
                request.CustomerId,
                request.UserId,
                walletId,
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
                request.Cost.CustomerChargedPoints,
                request.BillingExempt,
                request.ExemptionReason,
                walletTransactionId,
                status,
                MetadataJson = SerializeMetadata(request.Metadata),
                request.CreatedBy
            }, tx);
    }

    private static async Task<BillingRecord?> GetRecordForUpdateAsync(IDbConnection conn, IDbTransaction tx, string logicalRequestId)
        => await conn.QuerySingleOrDefaultAsync<BillingRecord>(
            """
            SELECT id AS Id,
                   logical_request_id AS LogicalRequestId,
                   wallet_id AS WalletId,
                   wallet_transaction_id AS WalletTransactionId,
                   customer_charged_points AS CustomerChargedPoints,
                   billing_exempt AS BillingExempt,
                   status AS Status,
                   created_by AS CreatedBy
              FROM billing.ai_image_billing_records
             WHERE logical_request_id = @logicalRequestId
             FOR UPDATE;
            """,
            new { logicalRequestId }, tx);

    private async Task ReleaseReservationAsync(IDbConnection conn, IDbTransaction tx, BillingRecord record, AiImageBillingCompleteRequest request)
    {
        if (!record.BillingExempt && record.WalletId is Guid walletId && record.CustomerChargedPoints > 0)
        {
            var wallet = await conn.QuerySingleAsync<WalletRow>(
                """
                SELECT id AS Id, balance AS Balance, locked_balance AS LockedBalance
                  FROM billing.token_wallets
                 WHERE id = @walletId
                 FOR UPDATE;
                """,
                new { walletId }, tx);
            var release = Math.Min(wallet.LockedBalance, record.CustomerChargedPoints);
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
            UPDATE billing.ai_image_billing_records
               SET status = 'released',
                   actual_model = @model,
                   provider_task_id = @taskId,
                   provider_actual_cost_usd = @actualUsd,
                   provider_cost_source = CASE WHEN @actualUsd IS NULL THEN provider_cost_source ELSE 'provider_actual' END,
                   error_message = @errorMessage,
                   failed_at = now(),
                   updated_at = now()
             WHERE id = @id;
            """,
            new
            {
                id = record.Id,
                model = request.ActualModel,
                taskId = request.ProviderTaskId,
                actualUsd = request.ProviderActualCostUsd,
                errorMessage = request.ErrorMessage
            }, tx);
    }

    private static async Task InsertAttemptsAsync(IDbConnection conn, IDbTransaction tx, Guid billingRecordId, AiImageBillingCompleteRequest request)
    {
        var attempts = ExtractAttempts(request).ToList();
        if (attempts.Count == 0)
        {
            attempts.Add(new ProviderAttempt(1, request.ActualModel, request.ProviderTaskId, request.Success, request.ErrorMessage));
        }

        foreach (var attempt in attempts)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO billing.ai_image_provider_attempts
                    (id, billing_record_id, attempt_number, model_name, provider_task_id, success,
                     provider_actual_cost_usd, error_message, raw_usage_json, created_at)
                VALUES
                    (gen_random_uuid(), @billingRecordId, @AttemptNumber, @ModelName, @ProviderTaskId, @Success,
                     @ProviderActualCostUsd, @ErrorMessage, CAST(@RawUsageJson AS jsonb), now())
                ON CONFLICT (billing_record_id, attempt_number) DO UPDATE
                    SET model_name = EXCLUDED.model_name,
                        provider_task_id = COALESCE(EXCLUDED.provider_task_id, billing.ai_image_provider_attempts.provider_task_id),
                        success = EXCLUDED.success,
                        provider_actual_cost_usd = EXCLUDED.provider_actual_cost_usd,
                        error_message = EXCLUDED.error_message,
                        raw_usage_json = EXCLUDED.raw_usage_json;
                """,
                new
                {
                    billingRecordId,
                    attempt.AttemptNumber,
                    attempt.ModelName,
                    attempt.ProviderTaskId,
                    attempt.Success,
                    ProviderActualCostUsd = request.ProviderActualCostUsd,
                    attempt.ErrorMessage,
                    RawUsageJson = request.ProviderUsageJson
                }, tx);
        }
    }

    private static IEnumerable<ProviderAttempt> ExtractAttempts(AiImageBillingCompleteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderUsageJson))
        {
            yield return new ProviderAttempt(1, request.ActualModel, request.ProviderTaskId, request.Success, request.ErrorMessage);
            yield break;
        }

        using var doc = JsonDocument.Parse(request.ProviderUsageJson);
        var attempt = 1;
        if (doc.RootElement.TryGetProperty("fallbackTrail", out var trail) && trail.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in trail.EnumerateArray())
            {
                yield return new ProviderAttempt(
                    attempt++,
                    ReadString(item, "from"),
                    ReadString(item, "taskId"),
                    Success: false,
                    ReadString(item, "reason") ?? ReadString(item, "errorCode"));
            }
        }

        yield return new ProviderAttempt(attempt, request.ActualModel, request.ProviderTaskId, request.Success, request.ErrorMessage);
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

    private static string? ReadString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed class WalletRow
    {
        public Guid Id { get; init; }
        public decimal Balance { get; init; }
        public decimal LockedBalance { get; init; }
    }

    private sealed class BillingRecord
    {
        public Guid Id { get; init; }
        public string LogicalRequestId { get; init; } = string.Empty;
        public Guid? WalletId { get; init; }
        public Guid? WalletTransactionId { get; init; }
        public decimal CustomerChargedPoints { get; init; }
        public bool BillingExempt { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? CreatedBy { get; init; }
    }

    private sealed record ProviderAttempt(int AttemptNumber, string? ModelName, string? ProviderTaskId, bool Success, string? ErrorMessage);
}
