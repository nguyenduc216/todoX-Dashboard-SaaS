using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.EditVideoTool;

public sealed record EditVideoLogEntry(string? Level, string? Code, string? Message, JsonElement? Data);

public sealed class EditVideoSessionRow
{
    public Guid Id { get; init; }
    public string Tool { get; init; } = string.Empty;
    public string? AppVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? DeviceName { get; init; }
    /// <summary>Raw MCP client name the tool reported (e.g. claude-ai, claude-code, codex-mcp-client).</summary>
    public string? Client { get; init; }
    public string? Summary { get; init; }
    /// <summary>Skill and version the video was made with (todox_auto_edit's skill / skillVersion), when given.</summary>
    public string? Skill { get; init; }
    public string? SkillVersion { get; init; }
    public string? SkillVersionName { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public int ErrorCount { get; init; }
}

public sealed class EditVideoUsageLogRow
{
    public string Level { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string? Data { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class EditVideoAlertRow
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Per-use telemetry from the customer's tool: one session per piece of work, technical log lines only
/// (never footage or narration), plus admin alerts for problems TodoX itself must fix.
/// </summary>
public sealed class EditVideoUsageService
{
    private static readonly HashSet<string> Levels = new(StringComparer.OrdinalIgnoreCase) { "info", "warn", "error" };
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase) { "ok", "failed" };

    // A session still "running" after this long never reported its end: the tool was closed or the machine went
    // off mid-work. Shown as interrupted; the row itself is left as recorded.
    private const int StaleRunningHours = 3;

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<EditVideoUsageService> _logger;

    public EditVideoUsageService(TodoXConnectionFactory factory, TenantContext tenant, ILogger<EditVideoUsageService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<Guid> StartSessionAsync(EditVideoLicenseRow license, EditVideoDeviceRow device, string? tool, string? appVersion, string? client, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        // The calling app (Claude Desktop, Claude Code, Codex, the TodoX window) is kept in summary; the end summary
        // is merged into it.
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO editvideo.usage_sessions (tenant_id, license_id, device_id, customer_id, tool, app_version, summary)
            VALUES (@tenantId, @licenseId, @deviceId, @customerId, @tool, @appVersion,
                    CASE WHEN @client::text IS NULL THEN NULL ELSE jsonb_build_object('client', @client::text) END)
            RETURNING id;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                licenseId = license.Id,
                deviceId = device.Id,
                customerId = license.CustomerId,
                tool = Clip(tool, 40) ?? "unknown",
                appVersion = Clip(appVersion, 40),
                client = Clip(client, 80)
            });
    }

    /// <summary>Appends log lines; ignores sessions that do not belong to the calling license.</summary>
    public async Task<int> AppendLogsAsync(Guid sessionId, Guid licenseId, IReadOnlyList<EditVideoLogEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0)
        {
            return 0;
        }

        using var conn = await _factory.OpenAsync(ct);
        var owned = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM editvideo.usage_sessions WHERE id = @sessionId AND license_id = @licenseId);",
            new { sessionId, licenseId });
        if (!owned)
        {
            return 0;
        }

        var rows = entries.Take(200).Select(e => new
        {
            tenantId = _tenant.TenantId,
            sessionId,
            level = Levels.Contains(e.Level ?? "") ? e.Level!.ToLowerInvariant() : "info",
            code = Clip(e.Code, 80) ?? "log",
            message = Clip(e.Message, 1000),
            data = e.Data is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } d && d.GetRawText().Length <= 8000 ? d.GetRawText() : null
        }).ToList();

        await conn.ExecuteAsync(
            """
            INSERT INTO editvideo.usage_logs (tenant_id, session_id, level, code, message, data)
            VALUES (@tenantId, @sessionId, @level, @code, @message, CAST(@data AS jsonb));
            """,
            rows);
        return rows.Count;
    }

    public async Task EndSessionAsync(Guid sessionId, Guid licenseId, string? status, JsonElement? summary, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE editvideo.usage_sessions
               SET status = @status,
                   summary = CASE WHEN @summary::text IS NULL THEN summary ELSE COALESCE(summary, '{}'::jsonb) || CAST(@summary AS jsonb) END,
                   ended_at = now()
             WHERE id = @sessionId AND license_id = @licenseId AND ended_at IS NULL;
            """,
            new
            {
                sessionId,
                licenseId,
                status = Statuses.Contains(status ?? "") ? status!.ToLowerInvariant() : "ok",
                summary = summary is { ValueKind: JsonValueKind.Object } s && s.GetRawText().Length <= 8000 ? s.GetRawText() : null
            });
    }

    public async Task RaiseAlertAsync(string code, string message, object? data, CancellationToken ct = default)
    {
        _logger.LogError("EDITVIDEO_ADMIN_ALERT code={Code} message={Message}", code, message);
        using var conn = await _factory.OpenAsync(ct);
        // One open alert per code is enough; repeated failures just keep it open.
        await conn.ExecuteAsync(
            """
            INSERT INTO editvideo.admin_alerts (tenant_id, code, message, data)
            SELECT @tenantId, @code, @message, CAST(@data AS jsonb)
             WHERE NOT EXISTS (SELECT 1 FROM editvideo.admin_alerts
                                WHERE tenant_id = @tenantId AND code = @code AND resolved_at IS NULL);
            """,
            new { tenantId = _tenant.TenantId, code, message, data = data is null ? null : JsonSerializer.Serialize(data) });
    }

    public async Task<IReadOnlyList<EditVideoAlertRow>> ListOpenAlertsAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return (await conn.QueryAsync<EditVideoAlertRow>(
            """
            SELECT id AS Id, code AS Code, message AS Message, created_at AS CreatedAt
              FROM editvideo.admin_alerts
             WHERE tenant_id = @tenantId AND resolved_at IS NULL
             ORDER BY created_at DESC;
            """,
            new { tenantId = _tenant.TenantId })).ToList();
    }

    public async Task ResolveAlertAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE editvideo.admin_alerts SET resolved_at = now() WHERE id = @id AND tenant_id = @tenantId;",
            new { id, tenantId = _tenant.TenantId });
    }

    public async Task<IReadOnlyList<EditVideoSessionRow>> ListSessionsAsync(Guid licenseId, int limit = 30, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return (await conn.QueryAsync<EditVideoSessionRow>(
            """
            SELECT s.id AS Id, s.tool AS Tool, s.app_version AS AppVersion,
                   CASE WHEN s.status = 'running' AND s.started_at < now() - make_interval(hours => @staleHours)
                        THEN 'interrupted' ELSE s.status END AS Status,
                   d.device_name AS DeviceName, s.summary->>'client' AS Client,
                   s.summary->>'skill' AS Skill, s.summary->>'skillVersion' AS SkillVersion, s.summary->>'skillVersionName' AS SkillVersionName,
                   s.summary::text AS Summary, s.started_at AS StartedAt, s.ended_at AS EndedAt,
                   (SELECT count(*) FROM editvideo.usage_logs l WHERE l.session_id = s.id AND l.level = 'error')::int AS ErrorCount
              FROM editvideo.usage_sessions s
              LEFT JOIN editvideo.devices d ON d.id = s.device_id
             WHERE s.license_id = @licenseId AND s.tenant_id = @tenantId
             ORDER BY s.started_at DESC
             LIMIT @limit;
            """,
            new { licenseId, tenantId = _tenant.TenantId, limit = Math.Clamp(limit, 1, 200), staleHours = StaleRunningHours })).ToList();
    }

    public async Task<IReadOnlyList<EditVideoUsageLogRow>> ListLogsAsync(Guid sessionId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return (await conn.QueryAsync<EditVideoUsageLogRow>(
            """
            SELECT level AS Level, code AS Code, message AS Message, data::text AS Data, created_at AS CreatedAt
              FROM editvideo.usage_logs
             WHERE session_id = @sessionId AND tenant_id = @tenantId
             ORDER BY created_at
             LIMIT 500;
            """,
            new { sessionId, tenantId = _tenant.TenantId })).ToList();
    }

    private static string? Clip(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }
}
