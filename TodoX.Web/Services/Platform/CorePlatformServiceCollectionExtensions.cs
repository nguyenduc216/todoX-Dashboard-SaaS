using Microsoft.Extensions.DependencyInjection;

namespace TodoX.Web.Services.Platform;

public static class CorePlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers transport-neutral TodoX Core Platform services. Service-specific execution adapters
    /// are registered separately so legacy runtimes can be migrated one service at a time.
    /// </summary>
    public static IServiceCollection AddTodoXCorePlatform(this IServiceCollection services)
    {
        services.AddScoped<ICoreServiceCatalogService, CoreServiceCatalogService>();
        services.AddScoped<ICoreExecutionRouter, CoreExecutionRouter>();
        services.AddScoped<CoreServiceJobHandler>();
        return services;
    }
}
