using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TodoX.EditVideoTool;
using TodoX.EditVideoTool.Components;
using TodoX.EditVideoTool.Mcp;
using TodoX.EditVideoTool.Status;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "mcp";

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
        var status = new StatusService(AgentPaths.Default(), new ComponentLocator(AgentPaths.Default())).Get();
        Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(StatusTools.Json) { WriteIndented = true }));
        return 0;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Usage: todox-editvideo [mcp|status|doctor|version]");
        return 2;
}

static async Task RunMcpServerAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // stdout is the MCP channel; every log line must go to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddSingleton(AgentPaths.Default());
    builder.Services.AddSingleton<ComponentLocator>();
    builder.Services.AddSingleton<StatusService>();

    builder.Services
        .AddMcpServer(o => o.ServerInfo = new() { Name = "todox-editvideo", Version = StatusService.Version })
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    await builder.Build().RunAsync();
}
