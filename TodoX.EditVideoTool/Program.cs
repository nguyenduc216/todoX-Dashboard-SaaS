using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodoX.EditVideoTool;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Status;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "mcp";
if (command != "mcp")
{
    // CLI messages are Vietnamese; the default Windows console code page would mangle them.
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}

switch (command)
{
    case "mcp":
        await RunMcpServerAsync(args);
        return 0;
    case "version":
    case "--version":
        Console.WriteLine(StatusService.Version);
        return 0;
    case "doctor":
    case "status":
    {
        using var services = BuildServices();
        var status = await services.GetRequiredService<StatusService>().GetAsync();
        Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(StatusTools.Json) { WriteIndented = true }));
        return status.LicenseState == "valid" ? 0 : 1;
    }
    case "activate":
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: todox-editvideo activate <LICENSE-KEY> [--server <url>]");
            return 2;
        }

        using var services = BuildServices();
        var result = await services.GetRequiredService<LicenseGuard>().ActivateAsync(args[1], serverUrl: OptionValue(args, "--server"));
        Console.WriteLine(result.Message);
        return result.IsUsable ? 0 : 1;
    }
    case "deactivate":
    {
        using var services = BuildServices();
        var (ok, message) = await services.GetRequiredService<LicenseGuard>().DeactivateAsync();
        Console.WriteLine(message);
        return ok ? 0 : 1;
    }
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Usage: todox-editvideo [mcp|status|doctor|activate|deactivate|version]");
        return 2;
}

static string? OptionValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void AddAgentServices(IServiceCollection services)
{
    services.AddSingleton(AgentPaths.Default());
    services.AddSingleton<ComponentLocator>();
    services.AddSingleton<LicenseStore>();
    services.AddSingleton(_ => LicenseVerifier.Default());
    services.AddSingleton<IDeviceIdentity, WindowsDeviceIdentity>();
    services.AddSingleton(_ => new LicenseApiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }));
    services.AddSingleton<LicenseGuard>(sp => new LicenseGuard(
        sp.GetRequiredService<LicenseStore>(),
        sp.GetRequiredService<LicenseVerifier>(),
        sp.GetRequiredService<IDeviceIdentity>(),
        sp.GetRequiredService<LicenseApiClient>()));
    services.AddSingleton<StatusService>();
}

static ServiceProvider BuildServices()
{
    var services = new ServiceCollection();
    AddAgentServices(services);
    return services.BuildServiceProvider();
}

static async Task RunMcpServerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // stdout is the MCP channel; every log line must go to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AddAgentServices(builder.Services);

    builder.Services
        .AddMcpServer(o => o.ServerInfo = new() { Name = "todox-editvideo", Version = StatusService.Version })
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
