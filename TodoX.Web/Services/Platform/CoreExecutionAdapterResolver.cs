using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services.Platform;

/// <summary>
/// Maps catalog service families to their execution adapter identities without changing the
/// catalog service identity stored on Core jobs.
/// </summary>
public sealed class CoreExecutionAdapterResolver : ICoreExecutionAdapterResolver
{
    public CoreExecutionAdapterResolution Resolve(string catalogServiceCode, string catalogServiceType)
    {
        var serviceCode = catalogServiceCode?.Trim() ?? string.Empty;
        var serviceType = catalogServiceType?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(serviceCode))
        {
            return new(serviceCode, serviceType, null, "Catalog service code is missing.");
        }

        if (string.Equals(serviceType, TodoXServiceEngineTypes.RVideo, StringComparison.OrdinalIgnoreCase))
        {
            return new(serviceCode, serviceType, FixedTodoXServiceCatalog.RenderVideo, null);
        }

        if (string.Equals(serviceType, TodoXServiceEngineTypes.Timelapse, StringComparison.OrdinalIgnoreCase))
        {
            return new(serviceCode, serviceType, serviceCode, null);
        }

        return new(
            serviceCode,
            serviceType,
            null,
            $"Catalog service type '{serviceType}' has no execution-adapter mapping.");
    }
}
