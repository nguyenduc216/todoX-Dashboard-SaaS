using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed record AuditLogView(DateTime OccurredAt, string? ActorDisplayName, string? ActorEmail,
    string Module, string? Feature, string Action, string? EntityDisplay, string Result,
    string? Severity, string? Message);

public sealed record AuditLogDetail(DateTime OccurredAt, string Module, string? Feature, string Action,
    string? EntityDisplay, string Result, string? Severity, string? Message);

/// <summary>Read/write access to audit.audit_logs (Foundation V2). Tenant + optional actor scoped.</summary>
public sealed class AuditRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AuditRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    /// <summary>Write an audit entry. Safe to call fire-and-forget; never throws to the caller path.</summary>
    public async Task LogAsync(CurrentUserSession? actor, string module, string action,
        string? entityDisplay = null, string result = "success", string? message = null,
        string? feature = null, string severity = "info")
    {
        try
        {
            await _tenant.EnsureLoadedAsync();
            using var conn = await _factory.OpenAsync();
            await conn.ExecuteAsync(
                """
                INSERT INTO audit.audit_logs
                    (id, tenant_id, occurred_at, actor_user_id, actor_user_type, actor_display_name,
                     actor_email, module, feature, action, entity_display, result, severity, message)
                VALUES
                    (gen_random_uuid(), @tenant, now(), @uid, @utype, @name, @email,
                     @module, @feature, @action, @entity, @result, @severity, @message);
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    uid = actor?.UserId,
                    utype = actor?.IsCustomer == true ? "customer" : "admin",
                    name = actor?.DisplayName,
                    email = actor?.Email,
                    module, feature, action, entity = entityDisplay,
                    result, severity, message
                });
        }
        catch
        {
            // Audit logging must never break the primary operation.
        }
    }

    /// <summary>Recent audit entries. Admins see all tenant logs; customers see only their own actions.</summary>
    public async Task<IReadOnlyList<AuditLogView>> GetRecentAsync(CurrentUserSession user, int limit = 200)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AuditLogView>(
            """
            SELECT occurred_at AS OccurredAt, actor_display_name AS ActorDisplayName, actor_email AS ActorEmail,
                   module AS Module, feature AS Feature, action AS Action, entity_display AS EntityDisplay,
                   result AS Result, severity AS Severity, message AS Message
              FROM audit.audit_logs
             WHERE tenant_id = @tenant
               AND (@all OR actor_user_id = @uid)
             ORDER BY occurred_at DESC
             LIMIT @limit;
            """,
            new { tenant = _tenant.TenantId, all = !user.IsCustomer, uid = user.UserId, limit });
        return rows.ToList();
    }

    public async Task<AuditLogDetail?> GetByEntityDisplayAsync(CurrentUserSession user, string entityDisplay)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.QueryFirstOrDefaultAsync<AuditLogDetail>(
            """
            SELECT occurred_at AS OccurredAt, module AS Module, feature AS Feature, action AS Action,
                   entity_display AS EntityDisplay, result AS Result, severity AS Severity, message AS Message
              FROM audit.audit_logs
             WHERE tenant_id = @tenant
               AND entity_display = @entity
               AND (@all OR actor_user_id = @uid)
             ORDER BY occurred_at DESC
             LIMIT 1;
            """,
            new { tenant = _tenant.TenantId, entity = entityDisplay, all = !user.IsCustomer, uid = user.UserId });
    }
}
