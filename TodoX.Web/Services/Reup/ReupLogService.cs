using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Reup;

public sealed class ReupLogService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public ReupLogService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task WriteAsync(Guid? campaignId, Guid? taskId, string step, string message, string level = "info", object? data = null, CancellationToken ct = default)
    {
        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            var json = data is null ? null : JsonSerializer.Serialize(data);
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(
                """
                INSERT INTO reup.publish_logs (id, tenant_id, campaign_id, task_id, level, step, message, data, created_at)
                VALUES (gen_random_uuid(), @tenant, @campaignId, @taskId, @level, @step, @message, @data::jsonb, now());
                """,
                new { tenant = _tenant.TenantId, campaignId, taskId, level, step, message, data = json });
        }
        catch
        {
            // Reup logs must not break campaign processing.
        }
    }
}
