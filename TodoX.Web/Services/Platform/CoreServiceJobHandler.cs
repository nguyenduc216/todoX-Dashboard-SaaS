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
        var envelope = DeserializeEnvelope(job.InputJson);

        await _completion.MarkProgressAsync(
            Authority,
            new CoreJobProgressRequest(job.Id, "core_execution", 1, "Core service execution started."),
            ct);

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

    internal static CoreServiceJobEnvelope DeserializeEnvelope(string inputJson)
    {
        try
        {
            using var document = JsonDocument.Parse(inputJson);
            var root = document.RootElement;
            var envelope = JsonSerializer.Deserialize<CoreServiceJobEnvelope>(root.GetRawText(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (envelope is not null
                && envelope.ServiceId != Guid.Empty
                && !string.IsNullOrWhiteSpace(envelope.ServiceCode)
                && envelope.Payload.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                return envelope;
            }

            if (TryReadString(root, "engine")?.Equals("RVIDEO", StringComparison.OrdinalIgnoreCase) == true)
            {
                var serviceCode = TryReadString(root, "serviceCode")
                    ?? throw new InvalidOperationException("RVIDEO Core job is missing serviceCode.");
                var serviceIdText = TryReadString(root, "serviceId");
                if (!Guid.TryParse(serviceIdText, out var serviceId) || serviceId == Guid.Empty)
                {
                    throw new InvalidOperationException("RVIDEO Core job is missing a valid serviceId.");
                }

                var payload = root.Clone();
                var prompt = root.TryGetProperty("prompt", out var promptValue)
                    ? promptValue.Clone()
                    : (JsonElement?)null;
                return new CoreServiceJobEnvelope
                {
                    ServiceId = serviceId,
                    ServiceCode = serviceCode,
                    Channel = CoreChannelCodes.Dashboard,
                    Payload = payload,
                    Prompt = prompt
                };
            }

            throw new InvalidOperationException("Core service job is missing service identity.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Core service job input is not valid JSON.", ex);
        }
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            _ => value.ToString()
        };
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
