using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodoX.EditVideoTool;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Status;
using TodoX.EditVideoTool.Setup;

// No arguments: Claude/Codex start us with redirected stdio ("mcp" is passed explicitly when registered);
// a double-click has a real console, so it opens the window app (install, activate, health check).
var command = args.Length > 0 ? args[0].ToLowerInvariant() : Console.IsInputRedirected ? "mcp" : "app";
if (command is not "mcp")
{
    // CLI messages are Vietnamese; the default Windows console code page would mangle them.
    Console.OutputEncoding = System.Text.Encoding.UTF8;
}

switch (command)
{
    case "mcp":
        await RunMcpServerAsync(args);
        return 0;
    case "app":
    {
        using var services = BuildServices();
        return TodoX.EditVideoTool.App.MainWindow.Run(services);
    }
    case "version":
    case "--version":
        Console.WriteLine(StatusService.Version);
        return 0;
    case "doctor":
    case "status":
    {
        using var services = BuildServices();
        // Console check keeps retrying every 15 s while TodoX is unreachable (Ctrl+C to stop).
        var report = await services.GetRequiredService<HealthCheck>().RunAsync(
            serverAttempts: command == "doctor" ? 0 : 1, status: Console.WriteLine);
        foreach (var item in report.Items)
        {
            Console.WriteLine($"{(item.Ok ? "[OK]  " : "[LỖI]")} {item.Name}: {item.Detail}{(item.Fix is null ? "" : $"  → {item.Fix}")}");
        }

        return report.Ok ? 0 : 1;
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
    case "install":
    {
        using var services = BuildServices();
        var ok = await services.GetRequiredService<Installer>().InstallAsync(new Progress<string>(Console.WriteLine), CancellationToken.None);
        return ok ? 0 : 1;
    }
    case "uninstall":
    {
        using var services = BuildServices();
        services.GetRequiredService<Installer>().Uninstall(new Progress<string>(Console.WriteLine));
        return 0;
    }
    case "update":
    {
        using var services = BuildServices();
        var outcome = await services.GetRequiredService<Updater>().CheckAndApplyAsync(new Progress<string>(Console.WriteLine), CancellationToken.None);
        Console.WriteLine(outcome);
        return 0;
    }
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Usage: todox-editvideo [app|mcp|status|doctor|activate|deactivate|install|uninstall|update|version]");
        return 2;
}

static string? OptionValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static void AddAgentServices(IServiceCollection services)
{
    var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
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
    services.AddSingleton(sp => new TodoXServerClient(http, sp.GetRequiredService<LicenseStore>(), sp.GetRequiredService<IDeviceIdentity>()));
    services.AddSingleton<INotifier, WindowsNotifier>();
    services.AddSingleton(sp => new ServerConnectivity(sp.GetRequiredService<TodoXServerClient>(), sp.GetRequiredService<INotifier>()));
    services.AddSingleton<StatusService>();
    services.AddSingleton<MediaProbe>();
    services.AddSingleton<ClipNormalizer>();
    services.AddSingleton<RemotionRenderer>();
    services.AddSingleton<HealthCheck>();
    services.AddSingleton<Installer>();
    services.AddSingleton<Updater>();
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

    var host = builder.Build();
    // Pull a newer version in the background; it is switched in on the next start, never mid-session.
    _ = Task.Run(async () =>
    {
        try
        {
            await host.Services.GetRequiredService<Updater>().CheckAndStageAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Updates must never break the MCP session.
        }
    });
    await host.RunAsync();
}
