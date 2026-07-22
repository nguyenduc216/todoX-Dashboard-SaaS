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
    Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default);
    Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default);
}

public sealed class AiBillingService : IAiBillingService
{
    private readonly IAiImageBillingService _adapter;

    public AiBillingService(IAiImageBillingService adapter)
    {
        _adapter = adapter;
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
