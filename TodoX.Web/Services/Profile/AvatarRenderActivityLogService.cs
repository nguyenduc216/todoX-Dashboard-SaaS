using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Profile;

public sealed class AvatarRenderActivityLogService
{
    private const string Charset = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<AvatarRenderActivityLogService> _logger;

    public AvatarRenderActivityLogService(TodoXConnectionFactory factory, TenantContext tenant,
        ILogger<AvatarRenderActivityLogService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public static string GenerateLogCode(int length = 10)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        return new string(bytes.ToArray().Select(b => Charset[b % Charset.Length]).ToArray());
    }

    public async Task WriteAsync(Guid userId, Guid? customerId, string logCode, string taskType, string status,
        object input, string finalPrompt, IReadOnlyList<ReferenceImage> references,
        IReadOnlyList<ChibiImage> outputs, IReadOnlyList<RenderLogEntry> timeline, string? error = null,
        CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            var payload = new
            {
                logCode,
                taskType,
                status,
                createdAt = timeline.FirstOrDefault()?.Timestamp,
                completedAt = DateTime.UtcNow,
                input,
                finalPrompt,
                references = references.Select(r => new
                {
                    r.Role,
                    r.SourceType,
                    r.SourceUrl,
                    r.MediaId,
                    r.Url,
                    r.FileName,
                    r.MimeType,
                    r.SizeBytes,
                    r.Width,
                    r.Height,
                    r.HasAlpha
                }),
                outputs = outputs.Select(o => new
                {
                    o.Index,
                    o.RenderId,
                    o.MediaId,
                    o.Url,
                    o.Status,
                    o.Error
                }),
                timeline,
                error
            };

            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO audit.audit_logs
                    (id, tenant_id, occurred_at, actor_user_id, actor_user_type, module, feature, action,
                     entity_display, result, severity, message)
                VALUES
                    (gen_random_uuid(), @tenant, now(), @uid, @utype, 'avatar_render', @feature, @action,
                     @logCode, @result, @severity, @message);
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    uid = userId,
                    utype = customerId is null ? "admin" : "customer",
                    feature = taskType,
                    action = taskType == "avatar-rerender" ? "rerender" : "render",
                    logCode,
                    result = status == "completed" ? "success" : "failed",
                    severity = status == "completed" ? "info" : "error",
                    message = JsonSerializer.Serialize(payload)
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write avatar render activity log {LogCode}", logCode);
        }
    }
}
