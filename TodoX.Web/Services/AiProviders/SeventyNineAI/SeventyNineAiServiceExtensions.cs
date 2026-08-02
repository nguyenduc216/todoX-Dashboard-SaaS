using TodoX.Web.Services.AiProviders.SeventyNineAI;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Service registration extensions for 79AI provider integration.
/// </summary>
public static class SeventyNineAiServiceExtensions
{
    /// <summary>
    /// Registers 79AI services into the dependency injection container.
    /// Call this from Program.cs or Startup.cs after other AI provider services.
    /// </summary>
    public static IServiceCollection AddSeventyNineAiProvider(this IServiceCollection services)
    {
        // Register the HTTP client for 79AI API
        services.AddHttpClient<ISeventyNineAiClient, SeventyNineAiClient>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var baseUrl = config["AiProviders:SeventyNineAI:BaseUrl"]
                ?? SeventyNineAiConstants.DefaultBaseUrl;

            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.Add("User-Agent", "TodoX/1.0");
        });

        // Register the video render provider client
        services.AddSingleton<IAiVideoRenderProviderClient, SeventyNineAiVideoRenderProviderClient>();

        return services;
    }
}
