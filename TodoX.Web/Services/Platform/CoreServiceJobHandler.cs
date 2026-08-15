using System.Text.Json;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

/// <summary>
/// Single RenderJobWorker entry point for all platform service jobs. The envelope identifies the
/// catalog service and caller context; the router delegates execution to a service-specific adapter.
/// </summary>
public sealed class CoreServiceJobHandler : IRenderJobHandler
{
    private static readonly CoreExecutionAuthority Authority =
        CoreExecutionAuthority.Trusted(nameof(CoreServiceJobHandler));

    private readonly ICoreExecutionRouter _router;
    private readonly ICoreJobCompletionService _completion;

    public CoreServiceJobHandler(
        ICoreExecutionRouter router,
        ICoreJobCompletionService completion)
    {
        _router = router;
        _completion = completion;
    }

    public string JobType => RenderJobTypes.CoreService;

    public async Task HandleAsync(RenderJobDto job, CancellationToken ct)
    {
        CoreServiceJobEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CoreServiceJobEnvelope>(job.InputJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Core service job input is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Core service job input is not valid JSON.", ex);
        }

        if (envelope.ServiceId == Guid.Empty || string.IsNullOrWhiteSpace(envelope.ServiceCode))
        {
            throw new InvalidOperationException("Core service job is missing service identity.");
        }

        var channel = CoreChannelCodes.Normalize(envelope.Channel);
        if (!_router.CanHandle(envelope.ServiceCode))
        {
            throw new InvalidOperationException(
                $"Service '{envelope.ServiceCode}' is registered in the catalog but has no execution adapter.");
        }

        var result = await _router.DispatchAsync(new CoreJobDispatchContext(
            job.Id,
            envelope.ServiceId,
            envelope.ServiceCode,
            new CoreRequestContext(
                job.CustomerId,
                job.UserId,
                channel,
                envelope.ClientId,
                envelope.ExternalRequestId),
            envelope.Payload,
            envelope.Prompt,
            envelope.References), ct);

        switch (result.Disposition)
        {
            case CoreExecutionDisposition.Completed:
                await _completion.CompleteAsync(Authority, new CoreJobCompleteRequest(
                    job.Id,
                    result.Output ?? JsonSerializer.SerializeToElement(new { }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    result.Message), ct);
                throw new RenderJobDeferredException("Core job completed by Core completion service.");

            case CoreExecutionDisposition.Deferred:
                await _completion.MarkDeferredAsync(
                    Authority,
                    job.Id,
                    new CoreExecutionCorrelation(
                        Required(result.ExecutionSystem, nameof(result.ExecutionSystem)),
                        Required(result.ExternalExecutionId, nameof(result.ExternalExecutionId)),
                        result.Adapter,
                        result.Metadata),
                    result.Message,
                    ct);
                throw new RenderJobDeferredException(result.Message ?? "Core job deferred to external execution runtime.");

            default:
                throw new InvalidOperationException($"Unsupported Core execution disposition '{result.Disposition}'.");
        }
    }

    private static string Required(string? value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Core execution result is missing {name}.")
            : value.Trim();
}

public sealed class CoreServiceJobEnvelope
{
    public Guid ServiceId { get; init; }
    public string ServiceCode { get; init; } = string.Empty;
    public string Channel { get; init; } = CoreChannelCodes.System;
    public string? ClientId { get; init; }
    public string? ExternalRequestId { get; init; }
    public JsonElement Payload { get; init; }
    public JsonElement? Prompt { get; init; }
    public JsonElement? References { get; init; }
}
