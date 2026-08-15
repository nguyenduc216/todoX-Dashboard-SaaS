namespace TodoX.Web.Services.Platform;

public interface ICoreExecutionRouter
{
    bool CanHandle(string serviceCode);

    Task DispatchAsync(CoreJobDispatchContext context, CancellationToken ct = default);
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

    public bool CanHandle(string serviceCode)
        => !string.IsNullOrWhiteSpace(serviceCode) && _adapters.ContainsKey(serviceCode.Trim());

    public Task DispatchAsync(CoreJobDispatchContext context, CancellationToken ct = default)
    {
        if (!_adapters.TryGetValue(context.ServiceCode.Trim(), out var adapter))
        {
            throw new InvalidOperationException(
                $"No TodoX execution adapter is registered for service '{context.ServiceCode}'. " +
                "The core job must not be dispatched until its service adapter is available.");
        }

        return adapter.DispatchAsync(context, ct);
    }
}
