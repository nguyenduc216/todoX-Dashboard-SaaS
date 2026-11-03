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

    private readonly StatusService _status;

    public StatusTools(StatusService status)
    {
        _status = status;
    }

    [McpServerTool(Name = "todox_status", ReadOnly = true, Idempotent = true)]
    [Description("Show TodoX EditVideoTool version, license state and which video components are installed.")]
    public async Task<string> Status(CancellationToken ct) => JsonSerializer.Serialize(await _status.GetAsync(ct), Json);
}
