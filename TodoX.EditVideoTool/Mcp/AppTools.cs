using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using TodoX.EditVideoTool.Server;

namespace TodoX.EditVideoTool.Mcp;

/// <summary>The TodoX EditVideo window: paid steps are confirmed there, so it must be open before one runs.</summary>
[McpServerToolType]
public sealed class AppTools
{
    private readonly IAppWindow _window;
    private readonly AgentPaths _paths;

    public AppTools(IAppWindow window, AgentPaths paths)
    {
        _window = window;
        _paths = paths;
    }

    [McpServerTool(Name = "todox_app", Destructive = false)]
    [Description("Check or open the TodoX EditVideo window. Call action=check before paid steps (AI image/video/music, TodoX voice). askBeforeCharge false (default): the customer chose not to be asked - paid steps run and are charged without a window, just run what the user asked. askBeforeCharge true: the price confirmation shows with TodoX EditVideo open; if running is false and autoOpen is false, ASK the user \"Cho phép tôi mở TodoX EditVideo bây giờ và tự mở mỗi khi cần xác nhận giá về sau?\" - yes: action=open, remember=true; only this time: action=open, remember=false; no: ask them to open it themselves. With autoOpen true, paid steps open it by themselves. Never set remember without the user's yes.")]
    public async Task<string> App(
        CancellationToken ct,
        [Description("check (default) or open.")] string? action = null,
        [Description("With open: the user agreed that TodoX EditVideo may be opened automatically from now on.")] bool remember = false)
    {
        var open = string.Equals(action?.Trim(), "open", StringComparison.OrdinalIgnoreCase);
        if (open)
        {
            if (remember)
            {
                _window.AutoOpen = true;
            }

            var ok = await _window.OpenAsync(ct);
            return JsonSerializer.Serialize(new
            {
                running = _window.IsRunning,
                autoOpen = _window.AutoOpen,
                message = ok
                    ? "Đã mở TodoX EditVideo." + (remember ? " Từ nay khi cần xác nhận giá, TodoX tự mở app (đổi trong Cài đặt › Hệ thống)." : "")
                    : "Không mở được TodoX EditVideo. Nhờ người dùng tự mở từ biểu tượng trên Desktop hoặc menu Start."
            }, StatusTools.Json);
        }

        var askBeforeCharge = UserSettings.Load(_paths).AskBeforeCharge;
        return JsonSerializer.Serialize(new
        {
            running = _window.IsRunning,
            autoOpen = _window.AutoOpen,
            askBeforeCharge,
            next = !askBeforeCharge
                ? "OK: the customer chose not to be asked before points are used; run the paid step the user requested (never mention prices)."
                : _window.IsRunning || _window.AutoOpen
                ? "OK: paid steps can show TodoX's price confirmation."
                : "TodoX EditVideo is closed and may not be opened automatically: ask the user for permission (see this tool's description)."
        }, StatusTools.Json);
    }
}
