using System.Text.Json;

namespace TodoX.EditVideoTool.Server;

/// <summary>
/// One usage session per piece of work. Lines are kept locally (logs folder) and sent to TodoX when the session
/// ends; when TodoX cannot be reached the local file is the record. Never log footage or narration text.
/// </summary>
public sealed class SessionLogger : IAsyncDisposable
{
    private readonly TodoXServerClient _server;
    private readonly string _localFile;
    private readonly List<object> _pending = [];
    private Guid? _sessionId;
    private bool _ended;
    private bool _hadError;

    private SessionLogger(TodoXServerClient server, string localFile)
    {
        _server = server;
        _localFile = localFile;
    }

    public Guid? SessionId => _sessionId;

    public static async Task<SessionLogger> StartAsync(TodoXServerClient server, AgentPaths paths, string tool, CancellationToken ct = default)
    {
        Directory.CreateDirectory(paths.LogsDir);
        var logger = new SessionLogger(server, Path.Combine(paths.LogsDir, $"{DateTime.Now:yyyyMMdd}.log"));
        var started = await server.StartSessionAsync(tool, Status.StatusService.Version, CallerApp.Current, ct);
        if (started.Ok && started.Value.TryGetProperty("sessionId", out var id) && id.TryGetGuid(out var sessionId))
        {
            logger._sessionId = sessionId;
        }

        logger.Info("session_start", $"{tool} v{Status.StatusService.Version}",
            started.Ok ? new { client = CallerApp.Current } : new { client = CallerApp.Current, server = started.ErrorCode });
        return logger;
    }

    public void Info(string code, string? message = null, object? data = null) => Add("info", code, message, data);
    public void Warn(string code, string? message = null, object? data = null) => Add("warn", code, message, data);

    public void Error(string code, string? message = null, object? data = null)
    {
        _hadError = true;
        Add("error", code, message, data);
    }

    public async Task EndAsync(string status, object? summary = null, CancellationToken ct = default)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        Add("info", "session_end", status, summary);
        if (_sessionId is not { } sessionId)
        {
            return;
        }

        if (_pending.Count > 0 && (await _server.SendLogsAsync(sessionId, _pending.ToArray(), ct)).Ok)
        {
            _pending.Clear();
        }

        await _server.EndSessionAsync(sessionId, status, summary, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_ended)
        {
            await EndAsync(_hadError ? "failed" : "ok");
        }
    }

    private void Add(string level, string code, string? message, object? data)
    {
        var entry = new { level, code, message, data };
        _pending.Add(entry);
        try
        {
            File.AppendAllText(_localFile,
                $"{DateTime.Now:O}\t{_sessionId}\t{level}\t{code}\t{message}\t{(data is null ? "" : JsonSerializer.Serialize(data))}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Logging must never break the tool.
        }
    }
}
