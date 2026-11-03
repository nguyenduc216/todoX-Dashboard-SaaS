using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Mcp;

[McpServerToolType]
public sealed class StatusTools
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly StatusService _status;

    public StatusTools(StatusService status)
    {
        _status = status;
    }

    [McpServerTool(Name = "todox_status", ReadOnly = true, Idempotent = true)]
    [Description("Show TodoX EditVideoTool version, license state and which video components are installed.")]
    public string Status() => JsonSerializer.Serialize(_status.Get(), Json);
}
