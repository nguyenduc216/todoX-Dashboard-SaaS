using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class StatusTools
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        // Keep Vietnamese messages readable instead of \uXXXX escapes; output is never embedded in HTML.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HealthCheck _health;

    public StatusTools(HealthCheck health)
    {
        _health = health;
    }

    [McpServerTool(Name = "todox_status", ReadOnly = true, Idempotent = true)]
    [Description("Check TodoX EditVideo before working: components, renderer, connection to TodoX, license and updates. Relay any problem and its fix to the user.")]
    public async Task<string> Status(CancellationToken ct) => HealthCheck.Describe(await _health.RunAsync(serverAttempts: 1, ct: ct));
}
