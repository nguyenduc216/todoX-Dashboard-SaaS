using System.Text.Json;

namespace TodoX.Web.Services.AiProviders;

[Obsolete("Use AiBillingService/AiBillingRepository. This type is a compatibility adapter only.")]
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
        if (exchangeRateVndPerUsd <= 0) throw new InvalidOperationException("AI billing exchange rate must be greater than zero.");
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
    public Guid? ProviderAccountId { get; set; }
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
    public Guid? ProviderAccountId { get; init; }
    public string LogicalRequestId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? RequestedModel { get; init; }
    public string? ActualModel { get; init; }
    public string? ProviderTaskId { get; init; }
    public int ReconciliationAttemptCount { get; init; }
    public string? TariffSnapshotJson { get; init; }
}

[Obsolete("Use IAiBillingService. This interface remains for existing image/video callers only.")]
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

[Obsolete("Use AiBillingService/AiBillingRepository. This adapter contains no SQL or wallet mutation.")]
public sealed class AiImageBillingService : IAiImageBillingService
{
    private readonly IAiBillingService _billing;

    public AiImageBillingService(IAiBillingService billing)
    {
        _billing = billing;
    }

    public AiImageBillingCost BuildConfiguredCost(decimal unitCostPoints, decimal quantity)
    {
        var estimate = _billing.Estimate(new AiBillingEstimateRequest
        {
            UnitCostPoints = unitCostPoints,
            Quantity = quantity
        });
        return AiImageBillingCost.FromConfiguredPoints(estimate.EstimatedPoints, estimate.ExchangeRateVndPerUsd, estimate.TodoXVndPerPoint);
    }

    public async Task<AiImageBillingReservation> ReserveAsync(AiImageBillingReserveRequest request, CancellationToken ct = default)
    {
        var result = await _billing.ReserveAsync(new AiBillingReservationRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            RenderJobId = request.RenderJobId,
            CustomerId = request.CustomerId,
            UserId = request.UserId,
            ProviderId = request.ProviderId,
            ProviderCapabilityId = request.ProviderCapabilityId,
            ProviderAccountId = request.ProviderAccountId,
            ProviderCode = request.ProviderCode,
            CapabilityCode = request.CapabilityCode,
            FeatureCode = request.FeatureCode,
            RequestedModel = request.RequestedModel,
            UnitCostPoints = request.Cost.CustomerChargedPoints,
            Quantity = 1,
            TrustedPayerContext = request.TrustedPayerContext,
            PricingSnapshotJson = request.TariffSnapshotJson,
            Metadata = request.Metadata,
            CreatedBy = request.CreatedBy
        }, ct);
        return new AiImageBillingReservation(result.Ok, result.ShouldSubmitProvider, result.PayerType, result.Status,
            result.LogicalRequestId, result.ReservedPoints, result.BillingRecordId, result.WalletTransactionId, result.ErrorMessage);
    }

    public async Task<AiImageBillingCompletion> CompleteAsync(AiImageBillingCompleteRequest request, CancellationToken ct = default)
    {
        var result = await _billing.CompleteAsync(new AiBillingCompletionRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            Success = request.Success,
            ActualModel = request.ActualModel,
            ProviderTaskId = request.ProviderTaskId,
            ProviderActualCost = request.ProviderActualCostUsd,
            ProviderCurrency = "usd",
            ProviderUsageJson = request.ProviderUsageJson,
            PricingSnapshotJson = request.TariffSnapshotJson,
            ErrorMessage = request.ErrorMessage
        }, ct);
        return new AiImageBillingCompletion(result.Ok, result.Status, result.WalletTransactionId, result.ErrorMessage);
    }

    public async Task<AiImageBillingCompletion> MarkPendingReconciliationAsync(AiImageBillingPendingReconciliationRequest request, CancellationToken ct = default)
    {
        var result = await _billing.MarkPendingReconciliationAsync(new AiBillingCompletionRequest
        {
            LogicalRequestId = request.LogicalRequestId,
            Success = false,
            ActualModel = request.ActualModel,
            ProviderTaskId = request.ProviderTaskId,
            ProviderUsageJson = request.ProviderUsageJson,
            PricingSnapshotJson = request.TariffSnapshotJson,
            ErrorMessage = request.ErrorMessage
        }, ct);
        return new AiImageBillingCompletion(result.Ok, result.Status, result.WalletTransactionId, result.ErrorMessage);
    }

    public async Task<IReadOnlyList<AiImageBillingReconciliationItem>> ClaimReconciliationBatchAsync(string workerKey, int batchSize, TimeSpan lockFor, int maxAttempts, CancellationToken ct = default)
    {
        var rows = await _billing.ClaimReconciliationBatchAsync(workerKey, batchSize, lockFor, maxAttempts, ct);
        return rows.Select(r => new AiImageBillingReconciliationItem
        {
            Id = r.Id,
            ProviderAccountId = r.ProviderAccountId,
            LogicalRequestId = r.LogicalRequestId,
            Status = r.Status,
            RequestedModel = r.RequestedModel,
            ActualModel = r.ActualModel,
            ProviderTaskId = r.ProviderTaskId,
            ReconciliationAttemptCount = r.ReconciliationAttemptCount,
            TariffSnapshotJson = r.PricingSnapshotJson
        }).ToList();
    }

    public Task MarkManualReviewAsync(string logicalRequestId, string errorMessage, CancellationToken ct = default)
        => _billing.MarkManualReviewAsync(logicalRequestId, errorMessage, ct);

    public Task RescheduleReconciliationAsync(string logicalRequestId, string? errorMessage, TimeSpan delay, CancellationToken ct = default)
        => _billing.RescheduleReconciliationAsync(logicalRequestId, errorMessage, delay, ct);
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
            catch (JsonException)
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
        catch (JsonException)
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
