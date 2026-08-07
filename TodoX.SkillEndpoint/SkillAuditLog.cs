using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TodoX.SkillEndpoint;

public sealed class SkillAuditLog
{
    private readonly SkillEndpointOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SkillAuditLog(IOptions<SkillEndpointOptions> options, IWebHostEnvironment environment)
    {
        _options = options.Value;
        _environment = environment;
    }

    public async Task WriteAsync(HttpContext http, string action, long jobId, object body, int statusCode, CancellationToken ct)
    {
        var relativePath = string.IsNullOrWhiteSpace(_options.AuditLogPath)
            ? "logs/skill-audit.ndjson"
            : _options.AuditLogPath;
        var path = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_environment.ContentRootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var entry = JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow,
            request_id = http.TraceIdentifier,
            action,
            job_id = jobId,
            status_code = statusCode,
            idempotency_key = http.Request.Headers["X-Idempotency-Key"].FirstOrDefault(),
            remote_ip = http.Connection.RemoteIpAddress?.ToString(),
            body
        });

        await _gate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(path, entry + Environment.NewLine, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
