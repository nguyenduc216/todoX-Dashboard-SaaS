using System.Text;
using System.Text.Json;
using TodoX.EditVideoTool.Server;
using TodoX.EditVideoTool.Status;

namespace TodoX.EditVideoTool.Setup;

/// <summary>What the customer picked in the setup wizard.</summary>
public sealed record InstallOptions(bool ConnectClaude = true, bool ConnectCodex = true, bool StartWithWindows = true, bool DesktopIcon = true);

public enum StepState { Pending, Running, Done, Skipped, Failed }

public sealed record InstallStep(string Id, string Title, StepState State, string? Detail);

public sealed record InstallLogEntry(DateTimeOffset At, string Level, string? Step, string Message);

/// <summary>One install or update, as saved on the machine (logs\install-*.json / .log) and sent to TodoX.</summary>
public sealed class InstallRecord
{
    public string Id { get; set; } = "";
    public string Mode { get; set; } = "install";
    public string? FromVersion { get; set; }
    public string Version { get; set; } = StatusService.Version;
    public string Root { get; set; } = "";
    public InstallOptions? Options { get; set; }
    public string Machine { get; set; } = Environment.MachineName;
    public string Windows { get; set; } = Environment.OSVersion.VersionString;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public bool? Ok { get; set; }
    public bool Uploaded { get; set; }
    public List<InstallLogEntry> Entries { get; set; } = [];
}

/// <summary>
/// The setup's history: each step (start, done, skipped, failed) and every progress line, with the time.
/// The wizard shows it live; it is saved next to the install so TodoX can check it remotely later.
/// </summary>
public sealed class InstallLog
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, InstallStep> _steps = [];
    private readonly TimeProvider _clock;

    public InstallLog(string mode, string? fromVersion, string root, InstallOptions options, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        Record = new InstallRecord
        {
            Id = _clock.GetLocalNow().ToString("yyyyMMdd-HHmmss"),
            Mode = mode,
            FromVersion = fromVersion,
            Root = root,
            Options = options,
            StartedAt = _clock.GetUtcNow()
        };
    }

    public InstallRecord Record { get; }

    public event Action<InstallStep>? StepChanged;

    public event Action<InstallLogEntry>? Line;

    public IReadOnlyCollection<InstallStep> Steps
    {
        get
        {
            lock (_gate)
            {
                return _steps.Values.ToList();
            }
        }
    }

    /// <summary>Progress lines from a running step (download %, file names) go into the log under that step.</summary>
    public IProgress<string> ProgressFor(string? step) => new LineProgress(this, step);

    public void Info(string message, string? step = null) => Add("info", step, message);

    public void Warn(string message, string? step = null) => Add("warn", step, message);

    public void Start(string id, string title) => Set(id, title, StepState.Running, null, "info", $"Bắt đầu: {title}");

    public void Done(string id, string? detail = null) => Set(id, null, StepState.Done, detail, "info", $"Xong{(detail is null ? "" : ": " + detail)}");

    public void Skip(string id, string title, string detail) => Set(id, title, StepState.Skipped, detail, "info", $"Bỏ qua {title}: {detail}");

    public void Fail(string id, string detail) => Set(id, null, StepState.Failed, detail, "error", $"LỖI: {detail}");

    public void Finish(bool ok)
    {
        Record.Ok = ok;
        Record.FinishedAt = _clock.GetUtcNow();
        Add(ok ? "info" : "error", null, ok ? "Cài đặt hoàn tất." : "Cài đặt chưa xong.");
    }

    /// <summary>Writes logs\install-{time}.json (for TodoX) and .log (to read by eye); safe to call again.</summary>
    public string Save(AgentPaths paths)
    {
        Directory.CreateDirectory(paths.LogsDir);
        var basePath = Path.Combine(paths.LogsDir, $"install-{Record.Id}");
        lock (_gate)
        {
            File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(Record, Json));
            File.WriteAllText(basePath + ".log", ToText(Record), Encoding.UTF8);
        }

        return basePath + ".log";
    }

    internal static string ToText(InstallRecord record)
    {
        var text = new StringBuilder();
        text.AppendLine($"TodoX EditVideo — {(record.Mode == "update" ? "cập nhật" : "cài đặt")} bản {record.Version}"
                        + (record.FromVersion is null ? "" : $" (từ bản {record.FromVersion})"));
        text.AppendLine($"Thư mục: {record.Root}");
        text.AppendLine($"Máy: {record.Machine} · {record.Windows}");
        if (record.Options is { } o)
        {
            text.AppendLine($"Tuỳ chọn: Claude={o.ConnectClaude} Codex={o.ConnectCodex} Khởi động cùng Windows={o.StartWithWindows} Biểu tượng Desktop={o.DesktopIcon}");
        }

        text.AppendLine($"Kết quả: {(record.Ok is null ? "đang chạy" : record.Ok.Value ? "thành công" : "lỗi")}");
        text.AppendLine(new string('-', 60));
        foreach (var e in record.Entries)
        {
            text.AppendLine($"{e.At.ToLocalTime():yyyy-MM-dd HH:mm:ss} {e.Level.ToUpperInvariant(),-5} {(e.Step is null ? "" : $"[{e.Step}] ")}{e.Message}");
        }

        return text.ToString();
    }

    private void Set(string id, string? title, StepState state, string? detail, string level, string message)
    {
        InstallStep step;
        lock (_gate)
        {
            var known = _steps.TryGetValue(id, out var existing) ? existing : null;
            step = new InstallStep(id, title ?? known?.Title ?? id, state, detail);
            _steps[id] = step;
        }

        Add(level, id, message);
        StepChanged?.Invoke(step);
    }

    private void Add(string level, string? step, string message)
    {
        var entry = new InstallLogEntry(_clock.GetUtcNow(), level, step, message);
        lock (_gate)
        {
            Record.Entries.Add(entry);
        }

        Line?.Invoke(entry);
    }

    private sealed class LineProgress(InstallLog log, string? step) : IProgress<string>
    {
        public void Report(string value) => log.Info(value.Trim(), step);
    }
}

/// <summary>
/// Sends saved install logs to TodoX (as a usage session "install"/"update" with its lines) once the machine has a
/// license; logs written before activation wait in logs\ and go up on the next try.
/// </summary>
public sealed class InstallLogUploader
{
    private const int MaxPerRun = 5;
    private const int LinesPerCall = 200;

    private readonly TodoXServerClient _server;
    private readonly AgentPaths _paths;

    public InstallLogUploader(TodoXServerClient server, AgentPaths paths)
    {
        _server = server;
        _paths = paths;
    }

    /// <summary>Returns how many logs were sent. Never throws.</summary>
    public async Task<int> UploadPendingAsync(CancellationToken ct)
    {
        var sent = 0;
        try
        {
            if (!File.Exists(_paths.LicenseFile) || !Directory.Exists(_paths.LogsDir))
            {
                return 0;
            }

            foreach (var file in Directory.EnumerateFiles(_paths.LogsDir, "install-*.json").Order().TakeLast(MaxPerRun))
            {
                var record = JsonSerializer.Deserialize<InstallRecord>(await File.ReadAllTextAsync(file, ct), InstallLog.Json);
                if (record is null || record.Uploaded || record.Ok is null)
                {
                    continue;
                }

                if (!await SendAsync(record, ct))
                {
                    break;
                }

                record.Uploaded = true;
                await File.WriteAllTextAsync(file, JsonSerializer.Serialize(record, InstallLog.Json), ct);
                sent++;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Logs stay on the machine; they are tried again next time.
        }

        return sent;
    }

    private async Task<bool> SendAsync(InstallRecord record, CancellationToken ct)
    {
        var started = await _server.StartSessionAsync(record.Mode, record.Version, "todox-setup", ct);
        if (!started.Ok || !started.Value.TryGetProperty("sessionId", out var idProp) || !Guid.TryParse(idProp.GetString(), out var sessionId))
        {
            return false;
        }

        foreach (var chunk in record.Entries.Chunk(LinesPerCall))
        {
            var lines = chunk.Select(e => (object)new
            {
                level = e.Level,
                code = e.Step ?? record.Mode,
                message = $"[{e.At.ToLocalTime():dd/MM HH:mm:ss}] {e.Message}"
            });
            if (!(await _server.SendLogsAsync(sessionId, lines, ct)).Ok)
            {
                return false;
            }
        }

        var summary = new
        {
            mode = record.Mode,
            fromVersion = record.FromVersion,
            version = record.Version,
            root = record.Root,
            machine = record.Machine,
            windows = record.Windows,
            options = record.Options,
            installedAt = record.StartedAt
        };
        return (await _server.EndSessionAsync(sessionId, record.Ok == true ? "ok" : "failed", summary, ct)).Ok;
    }
}
