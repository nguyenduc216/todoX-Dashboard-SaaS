using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellCompletionService
{
    Task<DanceSellCompletionResult> CompleteAsync(DanceSellCompletionRequest request, CancellationToken ct = default);
}

public sealed class DanceSellCompletionService : IDanceSellCompletionService
{
    private readonly IDanceSellRepository _repo;
    private readonly IRenderJobService _renderJobs;
    private readonly IAiProviderService _providers;

    public DanceSellCompletionService(
        IDanceSellRepository repo,
        IRenderJobService renderJobs,
        IAiProviderService providers)
    {
        _repo = repo;
        _renderJobs = renderJobs;
        _providers = providers;
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
            CustomerId = ToBigIntCustomerId(danceJob.CustomerId),
            ProviderCode = DanceSellConstants.ProviderCode,
            CapabilityCode = DanceSellConstants.CapabilityCode,
            FeatureCode = DanceSellConstants.FeatureCode,
            ModelName = DanceSellConstants.Model,
            RequestId = danceJob.LogicalRequestId,
            JobId = danceJob.RenderJobId?.ToString("N"),
            Quantity = 1,
            UnitType = "request",
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
                phase = "phase1_no_billing"
            }, KieJson.Options)
        }, ct);

        return DanceSellCompletionResult.Completed(danceJob);
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
    public string Source { get; set; } = "poll";
}

public sealed record DanceSellCompletionResult(bool Found, bool CompletedNow, DanceSellJobDto? Job)
{
    public static DanceSellCompletionResult NotFound() => new(false, false, null);
    public static DanceSellCompletionResult AlreadyCompleted(DanceSellJobDto job) => new(true, false, job);
    public static DanceSellCompletionResult Completed(DanceSellJobDto job) => new(true, true, job);
}
