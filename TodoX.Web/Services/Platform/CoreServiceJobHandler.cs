using System.Text.Json;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

/// <summary>
/// Single RenderJobWorker entry point for all platform service jobs. The envelope identifies the
/// catalog service and caller context; the router delegates execution to a service-specific adapter.
/// </summary>
public sealed class CoreServiceJobHandler : IRenderJobHandler
{
    private readonly ICoreExecutionRouter _router;

    public CoreServiceJobHandler(ICoreExecutionRouter router)
    {
        _router = router;
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

        await _router.DispatchAsync(new CoreJobDispatchContext(
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
    }
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
