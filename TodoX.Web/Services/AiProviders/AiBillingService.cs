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
    public Guid? ProviderAccountId { get; set; }
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
    public Guid? ProviderAccountId { get; init; }
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
    private readonly IAiBillingRepository _repository;
    private readonly IConfiguration _config;

    public AiBillingService(IAiBillingRepository repository, IConfiguration config)
    {
        _repository = repository;
        _config = config;
    }

    public AiBillingEstimateResult Estimate(AiBillingEstimateRequest request)
    {
        var exchangeRate = _config.GetValue("AiBilling:ExchangeRateVndPerUsd", _config.GetValue("AiImageBilling:ExchangeRateVndPerUsd", 8000m));
        var pointValue = _config.GetValue("AiBilling:TodoXVndPerPoint", _config.GetValue("AiImageBilling:TodoXVndPerPoint", 10000m));
        if (exchangeRate <= 0) throw new InvalidOperationException("AI billing exchange rate must be greater than zero.");
        if (pointValue <= 0) throw new InvalidOperationException("TodoX point value must be greater than zero.");
        var points = Math.Max(0, request.UnitCostPoints * request.Quantity);
        var estimatedUsd = points * pointValue / exchangeRate;
        return new AiBillingEstimateResult(
            points,
            estimatedUsd,
            exchangeRate,
            pointValue);
    }

    public Task<AiBillingReservation> ReserveAsync(AiBillingReservationRequest request, CancellationToken ct = default)
    {
        var estimate = Estimate(new AiBillingEstimateRequest { UnitCostPoints = request.UnitCostPoints, Quantity = request.Quantity });
        return _repository.GetOrCreateReservationAsync(request, estimate, ct);
    }

    public Task<AiBillingCompletion> CompleteAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
        => request.Success
            ? _repository.CompleteBillingAsync(request, ct)
            : _repository.ReleaseReservationAsync(request, ct);

    public Task<AiBillingCompletion> MarkPendingReconciliationAsync(AiBillingCompletionRequest request, CancellationToken ct = default)
        => _repository.MarkPendingReconciliationAsync(request, ct);

    public Task<IReadOnlyList<AiBillingReconciliationItem>> ClaimReconciliationBatchAsync(
        string workerKey,
        int batchSize,
        TimeSpan lockFor,
        int maxAttempts,
        CancellationToken ct = default)
        => _repository.ClaimReconciliationBatchAsync(workerKey, batchSize, lockFor, maxAttempts, ct);

    public Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default)
        => _repository.MarkManualReviewAsync(logicalRequestId, errorMessage, ct);

    public Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default)
        => _repository.RescheduleReconciliationAsync(logicalRequestId, errorMessage, delay, ct);

    public Task<AiBillingRefundResult> RefundAsync(AiBillingRefundRequest request, CancellationToken ct = default)
        => _repository.RefundAsync(request, ct);
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
