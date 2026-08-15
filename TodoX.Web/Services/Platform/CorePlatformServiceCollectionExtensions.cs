using Microsoft.Extensions.DependencyInjection;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

public static class CorePlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers transport-neutral TodoX Core Platform services. Service-specific execution adapters
    /// and transport-specific authenticators are registered separately so each channel can evolve
    /// without changing the business layer.
    /// </summary>
    public static IServiceCollection AddTodoXCorePlatform(this IServiceCollection services)
    {
        services.AddScoped<ICoreServiceCatalogService, CoreServiceCatalogService>();
        services.AddScoped<ICoreJobApplicationService, CoreJobApplicationService>();
        services.AddScoped<ICoreExecutionRouter, CoreExecutionRouter>();
        services.AddScoped<ICoreApiCallerResolver, CoreApiCallerResolver>();
        services.AddScoped<IRenderJobHandler, CoreServiceJobHandler>();
        return services;
    }
}
