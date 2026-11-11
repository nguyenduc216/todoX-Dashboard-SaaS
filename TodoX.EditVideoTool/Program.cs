using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TodoX.EditVideoTool;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Licensing;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Media;
using TodoX.EditVideoTool.Projects;
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
    CallerApp.Set(command == "app" ? "todox-window" : "todox-cli");
}

switch (command)
{
    case "mcp":
        await RunMcpServerAsync(args);
        return 0;
    case "app":
    {
        // The downloaded setup (or any copy outside the install folder) opens the setup wizard; the installed exe
        // opens the TodoX EditVideo window.
        using var services = BuildServices();
        return services.GetRequiredService<Installer>().IsRunningInstalled
            ? TodoX.EditVideoTool.App.MainWindow.Run(services)
            : TodoX.EditVideoTool.App.SetupWizard.Run();
    }
    case "setup":
        return TodoX.EditVideoTool.App.SetupWizard.Run();
    case "window":
    {
        using var services = BuildServices();
        return TodoX.EditVideoTool.App.MainWindow.Run(services);
    }
    case "autostart":
    {
        // Runs at Windows sign-in (if chosen in setup): fetch a newer version and skills quietly, then exit.
        TodoX.EditVideoTool.App.MainWindow.HideConsole();
        using var services = BuildServices();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try
        {
            await services.GetRequiredService<InstallLogUploader>().UploadPendingAsync(cts.Token);
            await services.GetRequiredService<Updater>().CheckAndStageAsync(cts.Token, notify: true);
            if ((await services.GetRequiredService<LicenseGuard>().EnsureAsync(cts.Token)).IsUsable)
            {
                await services.GetRequiredService<SkillSync>().SyncAsync(notify: true, cts.Token);
            }
        }
        catch (Exception)
        {
            // Never bother the customer at sign-in; the next start tries again.
        }

        return 0;
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
        // Kept so old instructions get a clear answer: only TodoX moves a license to another machine.
        Console.WriteLine(LicenseMessages.OneMachineNote);
        return 1;
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
        Console.Error.WriteLine($"Unknown command '{command}'. Usage: todox-editvideo [app|setup|window|mcp|status|doctor|activate|install|uninstall|update|autostart|version]");
        return 2;
}

static string? OptionValue(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static ServiceProvider BuildServices() => AppServices.Build();

static async Task RunMcpServerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // stdout is the MCP channel; every log line must go to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AppServices.Add(builder.Services);

    builder.Services
        .AddMcpServer(o =>
        {
            o.ServerInfo = new() { Name = "todox-editvideo", Version = StatusService.Version };
            o.ServerInstructions = ServerGuide.Instructions;
        })
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithRequestFilters(f => f.AddCallToolFilter(next => async (context, ct) =>
        {
            // Claude Desktop, Claude Code and Codex each send their own name when they connect.
            CallerApp.Set(context.Server.ClientInfo?.Name ?? "mcp", context.Server.ClientInfo?.Version);
            var result = await next(context, ct);
            // Once per session: tell the assistant to let the customer know a newer TodoX EditVideo is ready.
            if (context.Services?.GetService<Updater>()?.TakeAssistantNotice() is { } notice)
            {
                result.Content.Add(new TextContentBlock { Text = notice });
            }

            return result;
        }));

    var host = builder.Build();
    // Pull a newer version in the background now and every few hours; it is switched in on the next start, never
    // mid-session. The customer gets a Windows notification when it is ready.
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
    _ = Task.Run(() => host.Services.GetRequiredService<Updater>().RunPeriodicallyAsync(lifetime.ApplicationStopping));
    // Skill library: install/refresh TodoX skills now and every few hours while Claude/Codex keeps us running.
    _ = Task.Run(() => host.Services.GetRequiredService<SkillSync>().RunPeriodicallyAsync(lifetime.ApplicationStopping));
    await host.RunAsync();
}
