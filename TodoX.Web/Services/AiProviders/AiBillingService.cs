using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public enum AiBillingStatus
{
    Estimated,
    Reserved,
    PendingProvider,
    PendingReconciliation,
    Completed,
    Released,
    Failed,
    ManualReview,
    Cancelled
}

public enum AiRefundStatus
{
    None,
    Pending,
    Completed,
    Failed,
    ManualReview
}

public sealed record AiUsageAmount(decimal Quantity, string Unit);
public sealed record AiProviderMoney(decimal Amount, string Currency);
public sealed record TodoXPointAmount(decimal Points);

public sealed class AiPricingSnapshot
{
    public decimal ExchangeRateVndPerUsd { get; set; }
    public decimal TodoXVndPerPoint { get; set; }
    public string? Source { get; set; }
}

public sealed class AiBillingPayer
{
    public string PayerType { get; set; } = AiBillingPayerTypes.Customer;
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string? SystemWalletCode { get; set; }
}

public sealed class AiBillingEstimateRequest
{
    public decimal UnitCostPoints { get; set; }
    public decimal Quantity { get; set; } = 1;
}

public sealed record AiBillingEstimateResult(
    decimal EstimatedPoints,
    decimal ProviderEstimatedCostUsd,
    decimal ExchangeRateVndPerUsd,
    decimal TodoXVndPerPoint);

public sealed class AiBillingReservationRequest
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
    public decimal UnitCostPoints { get; set; }
    public decimal Quantity { get; set; } = 1;
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; set; }
    public string? PricingSnapshotJson { get; set; }
    public object? Metadata { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed record AiBillingReservation(
    bool Ok,
    bool ShouldSubmitProvider,
    string PayerType,
    string Status,
    string LogicalRequestId,
    decimal ReservedPoints,
    Guid? BillingRecordId,
    Guid? WalletTransactionId,
    string? ErrorMessage);

public sealed class AiBillingCompletionRequest
{
    public string LogicalRequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ActualModel { get; set; }
    public string? ProviderTaskId { get; set; }
    public decimal? ProviderActualCost { get; set; }
    public string? ProviderCurrency { get; set; } = "usd";
    public string? ProviderUsageJson { get; set; }
    public string? PricingSnapshotJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed record AiBillingCompletion(
    bool Ok,
    string Status,
    Guid? WalletTransactionId,
    string? ErrorMessage);

public sealed class AiBillingProviderAttempt
{
    public int AttemptNo { get; set; }
    public string? ModelName { get; set; }
    public string? ProviderTaskId { get; set; }
    public string Status { get; set; } = "submitted";
    public AiUsageAmount? Usage { get; set; }
    public AiProviderMoney? ProviderCost { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class AiBillingReconciliationItem
{
    public Guid Id { get; init; }
    public string LogicalRequestId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? RequestedModel { get; init; }
    public string? ActualModel { get; init; }
    public string? ProviderTaskId { get; init; }
    public int ReconciliationAttemptCount { get; init; }
    public string? PricingSnapshotJson { get; init; }
}

public sealed class AiBillingRefundRequest
{
    public string LogicalRequestId { get; set; } = string.Empty;
    public decimal Points { get; set; }
    public string? Reason { get; set; }
}

public sealed record AiBillingRefundResult(bool Ok, string Status, decimal RefundedPoints, string? ErrorMessage);

public interface IAiBillingService
{
    AiBillingEstimateResult Estimate(AiBillingEstimateRequest request);
    Task<AiBillingReservation> ReserveAsync(AiBillingReservationRequest request, CancellationToken ct = default);
    Task<AiBillingCompletion> CompleteAsync(AiBillingCompletionRequest request, CancellationToken ct = default);
    Task<AiBillingCompletion> MarkPendingReconciliationAsync(AiBillingCompletionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AiBillingReconciliationItem>> ClaimReconciliationBatchAsync(string workerKey, int batchSize, TimeSpan lockFor, int maxAttempts, CancellationToken ct = default);
    Task<AiBillingRefundResult> RefundAsync(AiBillingRefundRequest request, CancellationToken ct = default);
    Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default);
    Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default);
}

public sealed class AiBillingService : IAiBillingService
{
    private readonly IAiImageBillingService _adapter;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AiBillingService(IAiImageBillingService adapter, TodoXConnectionFactory factory, TenantContext tenant)
    {
        _adapter = adapter;
        _factory = factory;
        _tenant = tenant;
    }

    public AiBillingEstimateResult Estimate(AiBillingEstimateRequest request)
    {
        var cost = _adapter.BuildConfiguredCost(request.UnitCostPoints, request.Quantity);
        return new AiBillingEstimateResult(
            cost.CustomerChargedPoints,
            cost.ProviderEstimatedCostUsd,
            cost.ExchangeRateVndPerUsd,
            cost.TodoXVndPerPoint);
    }

    public async Task<AiBillingReservation> ReserveAsync(AiBillingReservationRequest request, CancellationToken ct = default)
    {
        var cost = _adapter.BuildConfiguredCost(request.UnitCostPoints, request.Quantity);
        var result = await _adapter.ReserveAsync(new AiImageBillingReserveRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            RenderJobId = request.RenderJobId,
            CustomerId = request.CustomerId,
            UserId = request.UserId,
            ProviderId = request.ProviderId,
            ProviderCapabilityId = request.ProviderCapabilityId,
            ProviderCode = request.ProviderCode,
            CapabilityCode = request.CapabilityCode,
            FeatureCode = request.FeatureCode,
            RequestedModel = request.RequestedModel,
            Cost = cost,
            TrustedPayerContext = request.TrustedPayerContext,
            TariffSnapshotJson = request.PricingSnapshotJson,
            Metadata = request.Metadata,
            CreatedBy = request.CreatedBy
        }, ct);

        return new AiBillingReservation(result.Ok, result.ShouldSubmitProvider, result.PayerType, result.Status,
            result.LogicalRequestId, result.ChargedPoints, result.BillingRecordId, result.WalletTransactionId, result.ErrorMessage);
    }

    public async Task<AiBillingCompletion> CompleteAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
    {
        var result = await _adapter.CompleteAsync(ToImageComplete(request), ct);
        return new AiBillingCompletion(result.Ok, result.Status, result.WalletTransactionId, result.ErrorMessage);
    }

    public async Task<AiBillingCompletion> MarkPendingReconciliationAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
    {
        var result = await _adapter.MarkPendingReconciliationAsync(new AiImageBillingPendingReconciliationRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            ActualModel = request.ActualModel,
            ProviderTaskId = request.ProviderTaskId,
            ProviderUsageJson = request.ProviderUsageJson,
            TariffSnapshotJson = request.PricingSnapshotJson,
            ErrorMessage = request.ErrorMessage
        }, ct);
        return new AiBillingCompletion(result.Ok, result.Status, result.WalletTransactionId, result.ErrorMessage);
    }

    public async Task<IReadOnlyList<AiBillingReconciliationItem>> ClaimReconciliationBatchAsync(
        string workerKey,
        int batchSize,
        TimeSpan lockFor,
        int maxAttempts,
        CancellationToken ct = default)
    {
        var rows = await _adapter.ClaimReconciliationBatchAsync(workerKey, batchSize, lockFor, maxAttempts, ct);
        return rows.Select(r => new AiBillingReconciliationItem
        {
            Id = r.Id,
            LogicalRequestId = r.LogicalRequestId,
            Status = r.Status,
            RequestedModel = r.RequestedModel,
            ActualModel = r.ActualModel,
            ProviderTaskId = r.ProviderTaskId,
            ReconciliationAttemptCount = r.ReconciliationAttemptCount,
            PricingSnapshotJson = r.TariffSnapshotJson
        }).ToList();
    }

    public Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default)
        => _adapter.MarkManualReviewAsync(logicalRequestId, errorMessage, ct);

    public Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default)
        => _adapter.RescheduleReconciliationAsync(logicalRequestId, errorMessage, delay, ct);

    public async Task<AiBillingRefundResult> RefundAsync(AiBillingRefundRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LogicalRequestId))
        {
            return new AiBillingRefundResult(false, "invalid", 0, "Missing logical_request_id.");
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = $"{_tenant.TenantId:N}:{request.LogicalRequestId}:refund" },
            tx);

        var record = await conn.QuerySingleOrDefaultAsync<RefundRecord>(
            """
            SELECT id AS Id,
                   logical_request_id AS LogicalRequestId,
                   COALESCE(payer_wallet_id, wallet_id) AS WalletId,
                   COALESCE(charged_points, customer_charged_points + system_charged_points, 0) AS ChargedPoints,
                   COALESCE(refunded_points, 0) AS RefundedPoints,
                   refund_status AS RefundStatus,
                   refund_transaction_id AS RefundTransactionId,
                   created_by AS CreatedBy
              FROM billing.ai_billing_records
             WHERE logical_request_id=@LogicalRequestId
             FOR UPDATE;
            """,
            new { request.LogicalRequestId },
            tx);
        if (record is null)
        {
            tx.Commit();
            return new AiBillingRefundResult(false, "missing_record", 0, "Billing record was not found.");
        }

        var refundable = Math.Max(0, record.ChargedPoints - record.RefundedPoints);
        var refund = Math.Min(Math.Max(0, request.Points), refundable);
        if (refund <= 0 || record.RefundStatus is "completed" or "refunded")
        {
            tx.Commit();
            return new AiBillingRefundResult(true, record.RefundStatus ?? "none", record.RefundedPoints, null);
        }

        if (record.WalletId is not Guid walletId)
        {
            await conn.ExecuteAsync(
                """
                UPDATE billing.ai_billing_records
                   SET refund_status='manual_review',
                       error_message=COALESCE(error_message, 'Missing wallet id for refund.'),
                       updated_at=now()
                 WHERE id=@id;
                """,
                new { id = record.Id },
                tx);
            tx.Commit();
            return new AiBillingRefundResult(false, "manual_review", record.RefundedPoints, "Missing wallet id for refund.");
        }

        var wallet = await conn.QuerySingleAsync<WalletSnapshot>(
            "SELECT id AS Id, balance AS Balance FROM billing.token_wallets WHERE id=@walletId FOR UPDATE;",
            new { walletId },
            tx);
        var txId = record.RefundTransactionId ?? Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO billing.token_transactions
                (id, tenant_id, wallet_id, transaction_type, amount, balance_before, balance_after,
                 reference_type, reference_id, description, created_at, created_by)
            VALUES
                (@txId, @tenant, @walletId, 'refund', @refund, @before, @after,
                 'ai_billing_refund', @recordId, @description, now(), @createdBy)
            ON CONFLICT DO NOTHING;
            """,
            new
            {
                txId,
                tenant = _tenant.TenantId,
                walletId,
                refund,
                before = wallet.Balance,
                after = wallet.Balance + refund,
                recordId = record.Id,
                description = request.Reason ?? $"AI billing refund {record.LogicalRequestId}",
                createdBy = record.CreatedBy
            },
            tx);
        await conn.ExecuteAsync(
            "UPDATE billing.token_wallets SET balance = balance + @refund, updated_at=now() WHERE id=@walletId;",
            new { refund, walletId },
            tx);
        await conn.ExecuteAsync(
            """
            UPDATE billing.ai_billing_records
               SET refunded_points = COALESCE(refunded_points, 0) + @refund,
                   refund_status = CASE WHEN COALESCE(refunded_points, 0) + @refund >= charged_points THEN 'completed' ELSE 'pending' END,
                   refund_transaction_id = @txId,
                   updated_at=now()
             WHERE id=@recordId;
            """,
            new { refund, txId, recordId = record.Id },
            tx);

        tx.Commit();
        return new AiBillingRefundResult(true, "completed", record.RefundedPoints + refund, null);
    }

    private sealed class RefundRecord
    {
        public Guid Id { get; init; }
        public string LogicalRequestId { get; init; } = string.Empty;
        public Guid? WalletId { get; init; }
        public decimal ChargedPoints { get; init; }
        public decimal RefundedPoints { get; init; }
        public string? RefundStatus { get; init; }
        public Guid? RefundTransactionId { get; init; }
        public string? CreatedBy { get; init; }
    }

    private sealed class WalletSnapshot
    {
        public Guid Id { get; init; }
        public decimal Balance { get; init; }
    }

    private static AiImageBillingCompleteRequest ToImageComplete(AiBillingCompletionRequest request)
        => new()
        {
            LogicalRequestId = request.LogicalRequestId,
            Success = request.Success,
            ActualModel = request.ActualModel,
            ProviderTaskId = request.ProviderTaskId,
            ProviderActualCostUsd = string.Equals(request.ProviderCurrency, "usd", StringComparison.OrdinalIgnoreCase)
                ? request.ProviderActualCost
                : null,
            ProviderUsageJson = request.ProviderUsageJson,
            TariffSnapshotJson = request.PricingSnapshotJson,
            ErrorMessage = request.ErrorMessage
        };
}

public interface IAiBillingDashboardService
{
    Task<AiBillingDashboardSnapshot> GetSnapshotAsync(AiBillingDashboardRequest request, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class AiBillingDashboardRequest
{
    public DateTimeOffset FromUtc { get; set; } = DateTimeOffset.UtcNow.AddDays(-30);
    public DateTimeOffset ToUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AiBillingDashboardSnapshot
{
    public decimal EstimatedProviderCostUsd { get; set; }
    public decimal ActualProviderCostUsd { get; set; }
    public bool ActualCostIncomplete { get; set; }
    public decimal CustomerChargedPoints { get; set; }
    public decimal SystemChargedPoints { get; set; }
    public int PendingReconciliationCount { get; set; }
    public int ManualReviewCount { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount { get; set; }
}

public sealed class AiBillingDashboardService : IAiBillingDashboardService
{
    private readonly IAiImageBillingDashboardService _adapter;

    public AiBillingDashboardService(IAiImageBillingDashboardService adapter)
    {
        _adapter = adapter;
    }

    public async Task<AiBillingDashboardSnapshot> GetSnapshotAsync(AiBillingDashboardRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        var snapshot = await _adapter.GetSnapshotAsync(new AiImageBillingDashboardRequest
        {
            FromUtc = request.FromUtc,
            ToUtc = request.ToUtc
        }, user, ct);

        return new AiBillingDashboardSnapshot
        {
            EstimatedProviderCostUsd = snapshot.EstimatedUsd,
            ActualProviderCostUsd = snapshot.ActualUsd,
            ActualCostIncomplete = snapshot.ActualCostIncomplete,
            CustomerChargedPoints = snapshot.CustomerChargedPoints,
            SystemChargedPoints = snapshot.SystemChargedPoints,
            PendingReconciliationCount = snapshot.PendingReconciliationCount,
            ManualReviewCount = snapshot.ManualReviewCount,
            FailedCount = snapshot.FailedCount,
            TotalCount = snapshot.TotalCount
        };
    }
}
