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
    private readonly ICoreExecutionAdapterResolver _adapterResolver;
    private readonly ICoreServiceCatalogService _catalog;
    private readonly ICoreJobCompletionService _completion;
    private readonly ILogger<CoreServiceJobHandler> _logger;

    public CoreServiceJobHandler(
        ICoreExecutionRouter router,
        ICoreExecutionAdapterResolver adapterResolver,
        ICoreServiceCatalogService catalog,
        ICoreJobCompletionService completion,
        ILogger<CoreServiceJobHandler> logger)
    {
        _router = router;
        _adapterResolver = adapterResolver;
        _catalog = catalog;
        _completion = completion;
        _logger = logger;
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

        var catalogService = await _catalog.GetByIdAsync(envelope.ServiceId, ct)
            ?? throw new InvalidOperationException(
                $"Core job catalog service '{envelope.ServiceCode}' ({envelope.ServiceId}) no longer exists.");
        if (!string.Equals(catalogService.ServiceCode, envelope.ServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Core job catalog identity mismatch: snapshot service '{envelope.ServiceCode}' does not match " +
                $"catalog service '{catalogService.ServiceCode}' for id '{envelope.ServiceId}'.");
        }

        var resolution = _adapterResolver.Resolve(catalogService.ServiceCode, catalogService.ServiceType);
        _logger.LogInformation(
            "CORE_EXECUTION_ADAPTER_RESOLVED jobId={JobId} catalogServiceCode={CatalogServiceCode} catalogServiceType={CatalogServiceType} executionAdapter={ExecutionAdapter}",
            job.Id,
            resolution.CatalogServiceCode,
            resolution.CatalogServiceType,
            resolution.ExecutionAdapterCode);
        if (!resolution.IsResolved)
        {
            throw new InvalidOperationException(
                $"Catalog service '{resolution.CatalogServiceCode}' with type '{resolution.CatalogServiceType}' " +
                $"could not resolve an execution adapter. Reason: {resolution.FailureReason ?? "unknown"}");
        }

        var channel = CoreChannelCodes.Normalize(envelope.Channel);
        if (!_router.CanHandle(resolution))
        {
            throw new InvalidOperationException(
                $"Catalog service '{resolution.CatalogServiceCode}' with type '{resolution.CatalogServiceType}' " +
                $"resolved execution adapter '{resolution.ExecutionAdapterCode}', but it is not registered.");
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
            envelope.References), resolution, ct);

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
