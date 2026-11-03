namespace TodoX.EditVideoTool.Server;

/// <summary>
/// "Can we reach TodoX?" with the agreed behaviour: on failure show a Windows notification right away, say we
/// retry in 15 seconds, and retry every 15 seconds.
/// </summary>
public sealed class ServerConnectivity
{
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    public const string OfflineTitle = "TodoX EditVideo";
    public const string OfflineText = "Không kết nối được máy chủ TodoX. Kiểm tra Internet — tool sẽ tự thử lại sau 15 giây.";

    private readonly TodoXServerClient _server;
    private readonly INotifier _notifier;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ServerConnectivity(TodoXServerClient server, INotifier notifier, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _server = server;
        _notifier = notifier;
        _delay = delay ?? Task.Delay;
    }

    /// <summary>
    /// Returns true once TodoX answers. <paramref name="maxAttempts"/> 0 = keep trying until cancelled
    /// (the window app); the MCP tools use a bounded number so the assistant gets an answer.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(int maxAttempts, Action<string>? status = null, CancellationToken ct = default)
    {
        for (var attempt = 1; maxAttempts == 0 || attempt <= maxAttempts; attempt++)
        {
            var ping = await _server.PingAsync(ct);
            if (ping.Ok)
            {
                return true;
            }

            if (attempt == 1)
            {
                _notifier.Show(OfflineTitle, OfflineText, isError: true);
            }

            if (maxAttempts != 0 && attempt == maxAttempts)
            {
                break;
            }

            status?.Invoke($"Không kết nối được máy chủ TodoX (lần {attempt}). Thử lại sau 15 giây...");
            await _delay(RetryDelay, ct);
        }

        return false;
    }
}
