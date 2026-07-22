using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellCompletionService
{
    Task<DanceSellCompletionResult> CompleteAsync(DanceSellCompletionRequest request, CancellationToken ct = default);
    Task<DanceSellCompletionResult> FailAsync(DanceSellFailureRequest request, CancellationToken ct = default);
}

public sealed class DanceSellCompletionService : IDanceSellCompletionService
{
    private readonly IDanceSellRepository _repo;
    private readonly IRenderJobService _renderJobs;
    private readonly IAiProviderService _providers;
    private readonly IDanceSellOperationRepository _operations;

    public DanceSellCompletionService(
        IDanceSellRepository repo,
        IRenderJobService renderJobs,
        IAiProviderService providers,
        IDanceSellOperationRepository operations)
    {
        _repo = repo;
        _renderJobs = renderJobs;
        _providers = providers;
        _operations = operations;
    }

    public async Task<DanceSellCompletionResult> CompleteAsync(DanceSellCompletionRequest request, CancellationToken ct = default)
    {
        var danceJob = request.DanceJob
            ?? (request.ProviderTaskId is null ? null : await _repo.GetByProviderTaskIdAsync(request.ProviderTaskId, ct));
        if (danceJob is null)
        {
            return DanceSellCompletionResult.NotFound();
        }

        if (danceJob.Status is DanceSellJobStatuses.Completed)
        {
            return DanceSellCompletionResult.AlreadyCompleted(danceJob);
        }

        var resultUrl = request.ResultVideoUrl?.Trim();
        if (string.IsNullOrWhiteSpace(resultUrl))
        {
            throw new KieProviderException("KIE resultUrls is empty.", KieErrorCodes.ResultUrlMissing, transient: false);
        }

        var responseJson = KieJsonRedactor.Redact(request.ResponseJson) ?? "{}";
        var providerStatus = string.IsNullOrWhiteSpace(request.ProviderStatus)
            ? KieTaskStatuses.Completed
            : request.ProviderStatus.Trim();

        var changed = await _repo.UpdateCompletedAsync(danceJob.Id, providerStatus, responseJson, resultUrl, ct);
        if (!changed)
        {
            var current = request.ProviderTaskId is null
                ? await _repo.GetByIdAsync(danceJob.Id, ct)
                : await _repo.GetByProviderTaskIdAsync(request.ProviderTaskId, ct);
            return DanceSellCompletionResult.AlreadyCompleted(current ?? danceJob);
        }

        if (danceJob.RenderJobId is Guid renderJobId)
        {
            await _renderJobs.MarkStatusAsync(renderJobId, RenderJobStatuses.Completed, new
            {
                resultVideoUrl = resultUrl,
                providerTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
                source = request.Source
            }, ct: ct);

            await _renderJobs.AddEventAsync(renderJobId, "KIE_TASK_COMPLETED", "KIE Motion Control task completed.",
                new
                {
                    danceSellJobId = danceJob.Id,
                    providerTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
                    resultUrlCount = request.ResultUrlCount,
                    source = request.Source
                },
                ct: ct);
        }

        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerGuid = danceJob.CustomerId,
            ProviderCode = danceJob.MotionProviderCode ?? danceJob.ProviderCode,
            CapabilityCode = DanceSellConstants.CapabilityCode,
            FeatureCode = DanceSellConstants.FeatureCode,
            OperationType = DanceSellOperationTypes.MotionVideo,
            ModelName = danceJob.MotionProviderModel ?? danceJob.ProviderModel,
            ProviderTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
            RequestId = danceJob.LogicalRequestId,
            RenderJobId = danceJob.RenderJobId,
            JobId = danceJob.RenderJobId?.ToString("N"),
            Quantity = 1,
            UnitType = request.CreditsConsumed is null ? "request" : "credits",
            UnitCostPoints = 0,
            TotalPoints = 0,
            ProviderRawCost = null,
            Status = "completed",
            ErrorMessage = null,
            MetadataJson = JsonSerializer.Serialize(new
            {
                danceSellJobId = danceJob.Id,
                providerTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
                providerStatus,
                resultUrlCount = request.ResultUrlCount,
                source = request.Source,
                creditsConsumed = request.CreditsConsumed,
                usageUnit = request.CreditsConsumed is null ? "request" : "credits",
                billingStatus = danceJob.BillingStatus
            }, KieJson.Options)
        }, ct);

        return DanceSellCompletionResult.Completed(danceJob);
    }

    public async Task<DanceSellCompletionResult> FailAsync(DanceSellFailureRequest request, CancellationToken ct = default)
    {
        var danceJob = request.DanceJob
            ?? (request.ProviderTaskId is null ? null : await _repo.GetByProviderTaskIdAsync(request.ProviderTaskId, ct));
        if (danceJob is null)
        {
            return DanceSellCompletionResult.NotFound();
        }

        if (danceJob.Status is DanceSellJobStatuses.Completed or DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout)
        {
            return DanceSellCompletionResult.AlreadyTerminal(danceJob);
        }

        var responseJson = KieJsonRedactor.Redact(request.ResponseJson);
        var providerStatus = string.IsNullOrWhiteSpace(request.ProviderStatus)
            ? KieTaskStatuses.Failed
            : request.ProviderStatus.Trim();
        var status = string.IsNullOrWhiteSpace(request.Status)
            ? DanceSellJobStatuses.Failed
            : request.Status.Trim();
        var errorCode = string.IsNullOrWhiteSpace(request.ErrorCode)
            ? KieErrorCodes.TaskFailed
            : request.ErrorCode.Trim();
        var errorMessage = string.IsNullOrWhiteSpace(request.ErrorMessage)
            ? "KIE task failed."
            : request.ErrorMessage.Trim();

        var changed = await _repo.UpdateFailedAsync(danceJob.Id, status, providerStatus, responseJson, errorCode, errorMessage, ct);
        if (!changed)
        {
            var current = request.ProviderTaskId is null
                ? await _repo.GetByIdAsync(danceJob.Id, ct)
                : await _repo.GetByProviderTaskIdAsync(request.ProviderTaskId, ct);
            return DanceSellCompletionResult.AlreadyTerminal(current ?? danceJob);
        }

        if (danceJob.RenderJobId is Guid renderJobId)
        {
            await _renderJobs.MarkStatusAsync(renderJobId, RenderJobStatuses.Failed,
                errorCode: errorCode,
                errorMessage: errorMessage,
                ct: ct);

            await _renderJobs.AddEventAsync(renderJobId, "KIE_TASK_FAILED", errorMessage,
                new
                {
                    danceSellJobId = danceJob.Id,
                    providerTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
                    providerStatus,
                    errorCode,
                    source = request.Source,
                    permanent = request.Permanent
                },
                "error",
                ct);
        }

        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerGuid = danceJob.CustomerId,
            ProviderCode = danceJob.MotionProviderCode ?? danceJob.ProviderCode,
            CapabilityCode = DanceSellConstants.CapabilityCode,
            FeatureCode = DanceSellConstants.FeatureCode,
            OperationType = DanceSellOperationTypes.MotionVideo,
            ModelName = danceJob.MotionProviderModel ?? danceJob.ProviderModel,
            ProviderTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
            RequestId = danceJob.LogicalRequestId,
            RenderJobId = danceJob.RenderJobId,
            JobId = danceJob.RenderJobId?.ToString("N"),
            Quantity = 1,
            UnitType = "request",
            UnitCostPoints = 0,
            TotalPoints = 0,
            ProviderRawCost = null,
            Status = "failed",
            ErrorMessage = errorMessage,
            MetadataJson = JsonSerializer.Serialize(new
            {
                danceSellJobId = danceJob.Id,
                providerTaskId = request.ProviderTaskId ?? danceJob.ProviderTaskId,
                providerStatus,
                errorCode,
                source = request.Source,
                permanent = request.Permanent,
                billingStatus = danceJob.BillingStatus
            }, KieJson.Options)
        }, ct);

        return DanceSellCompletionResult.Failed(danceJob);
    }

    public static long? ToBigIntCustomerId(Guid? id)
    {
        if (id is null) return null;
        var bytes = id.Value.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0);
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }
}

public sealed class DanceSellCompletionRequest
{
    public DanceSellJobDto? DanceJob { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ResponseJson { get; set; }
    public string? ResultVideoUrl { get; set; }
    public int ResultUrlCount { get; set; }
    public decimal? CreditsConsumed { get; set; }
    public string Source { get; set; } = "poll";
}

public sealed class DanceSellFailureRequest
{
    public DanceSellJobDto? DanceJob { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ResponseJson { get; set; }
    public string Status { get; set; } = DanceSellJobStatuses.Failed;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Permanent { get; set; } = true;
    public string Source { get; set; } = "poll";
}

public sealed record DanceSellCompletionResult(bool Found, bool CompletedNow, bool FailedNow, DanceSellJobDto? Job)
{
    public bool TerminalNow => CompletedNow || FailedNow;

    public static DanceSellCompletionResult NotFound() => new(false, false, false, null);
    public static DanceSellCompletionResult AlreadyCompleted(DanceSellJobDto job) => new(true, false, false, job);
    public static DanceSellCompletionResult AlreadyTerminal(DanceSellJobDto job) => new(true, false, false, job);
    public static DanceSellCompletionResult Completed(DanceSellJobDto job) => new(true, true, false, job);
    public static DanceSellCompletionResult Failed(DanceSellJobDto job) => new(true, false, true, job);
}
