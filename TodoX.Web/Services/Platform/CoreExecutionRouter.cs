namespace TodoX.Web.Services.Platform;

public interface ICoreExecutionRouter
{
    bool CanHandle(CoreExecutionAdapterResolution resolution);

    Task<CoreExecutionResult> DispatchAsync(
        CoreJobDispatchContext context,
        CoreExecutionAdapterResolution resolution,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves service-specific execution adapters without exposing workflow details to callers.
/// Adding a new service requires registering an ICoreJobExecutionAdapter; Dashboard/API/Zalo code
/// remains unchanged.
/// </summary>
public sealed class CoreExecutionRouter : ICoreExecutionRouter
{
    private readonly IReadOnlyDictionary<string, ICoreJobExecutionAdapter> _adapters;

    public CoreExecutionRouter(IEnumerable<ICoreJobExecutionAdapter> adapters)
    {
        var map = new Dictionary<string, ICoreJobExecutionAdapter>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            if (string.IsNullOrWhiteSpace(adapter.ServiceCode))
            {
                throw new InvalidOperationException($"Execution adapter {adapter.GetType().Name} has an empty service code.");
            }

            if (!map.TryAdd(adapter.ServiceCode.Trim(), adapter))
            {
                throw new InvalidOperationException($"Multiple execution adapters are registered for service '{adapter.ServiceCode}'.");
            }
        }

        _adapters = map;
    }

    public bool CanHandle(CoreExecutionAdapterResolution resolution)
        => resolution.IsResolved
           && _adapters.ContainsKey(resolution.ExecutionAdapterCode!);

    public Task<CoreExecutionResult> DispatchAsync(
        CoreJobDispatchContext context,
        CoreExecutionAdapterResolution resolution,
        CancellationToken ct = default)
    {
        if (!resolution.IsResolved)
        {
            throw new InvalidOperationException(
                $"Catalog service '{resolution.CatalogServiceCode}' with type '{resolution.CatalogServiceType}' " +
                $"could not resolve an execution adapter. Reason: {resolution.FailureReason ?? "unknown"}");
        }

        if (!_adapters.TryGetValue(resolution.ExecutionAdapterCode!, out var adapter))
        {
            throw new InvalidOperationException(
                $"Catalog service '{resolution.CatalogServiceCode}' with type '{resolution.CatalogServiceType}' " +
                $"resolved execution adapter '{resolution.ExecutionAdapterCode}', but it is not registered.");
        }

        return adapter.DispatchAsync(context with { ServiceCode = resolution.ExecutionAdapterCode! }, ct);
    }
}
