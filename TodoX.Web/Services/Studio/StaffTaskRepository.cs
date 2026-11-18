using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Studio;

/// <summary>
/// studio.staff_tasks — database/manual/studio/20261119_staff_tasks.sql.
/// Every read and write checks the caller's staff_tasks permission here, not only in the page.
/// </summary>
public sealed class StaffTaskRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly AuthStateService _auth;
    private readonly ILogger<StaffTaskRepository> _logger;

    public StaffTaskRepository(TodoXConnectionFactory factory, TenantContext tenant, AuthStateService auth, ILogger<StaffTaskRepository> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _auth = auth;
        _logger = logger;
    }

    public const string Script = "database/manual/studio/20261119_staff_tasks.sql";

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            return await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('studio.staff_tasks') IS NOT NULL;");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staff task schema check failed.");
            return false;
        }
    }

    private CurrentUserSession Require(string permission)
    {
        var user = _auth.CurrentUser;
        if (user?.IsAuthenticated != true || user.IsCustomer
            || !(user.Can(permission) || AdminEndpointAuthorization.IsAdmin(user)))
            throw new UnauthorizedAccessException("Bạn không có quyền thực hiện thao tác này.");
        return user;
    }

    private const string Select =
        """
        SELECT t.id AS Id, t.title AS Title, t.note AS Note,
               t.customer_id AS CustomerId, COALESCE(NULLIF(c.company_name, ''), c.full_name) AS CustomerName,
               t.assignee_user_id AS AssigneeUserId, COALESCE(a.full_name, a.display_name, a.username) AS AssigneeName,
               t.start_at AS StartAt, t.completed_at AS CompletedAt, t.status AS Status,
               COALESCE(cb.full_name, cb.display_name, cb.username) AS CreatedByName,
               t.created_at AS CreatedAt, t.updated_at AS UpdatedAt
          FROM studio.staff_tasks t
          LEFT JOIN crm.customers c ON c.id = t.customer_id
          LEFT JOIN auth.app_users a ON a.id = t.assignee_user_id
          LEFT JOIN auth.app_users cb ON cb.id = t.created_by
        """;

    public async Task<IReadOnlyList<StaffTask>> ListAsync()
    {
        Require("staff_tasks.view");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = (await conn.QueryAsync<StaffTask>(
            Select + """
                      WHERE t.tenant_id = @tenant
                      ORDER BY CASE WHEN t.status IN ('done', 'cancelled') THEN 1 ELSE 0 END,
                               t.start_at NULLS LAST, t.created_at DESC;
                     """,
            new { tenant = _tenant.TenantId })).ToList();
        foreach (var row in rows) ToLocal(row);
        return rows;
    }

    public async Task<(bool Ok, string? Error)> SaveAsync(StaffTask task)
    {
        var isNew = task.Id == Guid.Empty;
        var user = Require(isNew ? "staff_tasks.create" : "staff_tasks.update");
        task.Title = task.Title?.Trim() ?? string.Empty;
        task.CompletedAt = StaffTaskRules.CompletedAtFor(task.Status, task.CompletedAt, VietnamTime.Now);
        var error = StaffTaskRules.Validate(task);
        if (error is not null) return (false, error);

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        if (task.CustomerId is { } customerId && !await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM crm.customers WHERE id = @id AND tenant_id = @tenant);",
                new { id = customerId, tenant = _tenant.TenantId }))
            return (false, "Khách hàng không tồn tại.");
        if (task.AssigneeUserId is { } assigneeId && !await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM auth.app_users WHERE id = @id AND tenant_id = @tenant);",
                new { id = assigneeId, tenant = _tenant.TenantId }))
            return (false, "Người thực hiện không tồn tại.");

        var args = new
        {
            id = isNew ? Guid.NewGuid() : task.Id,
            tenant = _tenant.TenantId,
            title = task.Title,
            note = string.IsNullOrWhiteSpace(task.Note) ? null : task.Note.Trim(),
            customerId = task.CustomerId,
            assignee = task.AssigneeUserId,
            startAt = VietnamTime.ToUtc(task.StartAt),
            completedAt = VietnamTime.ToUtc(task.CompletedAt),
            status = task.Status,
            by = user.UserId
        };
        if (isNew)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO studio.staff_tasks
                    (id, tenant_id, title, note, customer_id, assignee_user_id, start_at, completed_at, status,
                     created_at, created_by, updated_at, updated_by)
                VALUES (@id, @tenant, @title, @note, @customerId, @assignee, @startAt, @completedAt, @status,
                        now(), @by, now(), @by);
                """, args);
            task.Id = args.id;
        }
        else
        {
            var updated = await conn.ExecuteAsync(
                """
                UPDATE studio.staff_tasks
                   SET title = @title, note = @note, customer_id = @customerId, assignee_user_id = @assignee,
                       start_at = @startAt, completed_at = @completedAt, status = @status,
                       updated_at = now(), updated_by = @by
                 WHERE id = @id AND tenant_id = @tenant;
                """, args);
            if (updated == 0) return (false, "Công việc không còn tồn tại.");
        }
        return (true, null);
    }

    /// <summary>Quick status change from the list; "Đã hoàn thành" stamps the completion time when it is empty.</summary>
    public async Task SetStatusAsync(Guid id, string status)
    {
        var user = Require("staff_tasks.update");
        if (!StaffTaskStatuses.All.Contains(status)) throw new ArgumentException("Trạng thái không hợp lệ.", nameof(status));
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE studio.staff_tasks
               SET status = @status,
                   completed_at = CASE WHEN @status = 'done' THEN COALESCE(completed_at, now()) ELSE completed_at END,
                   updated_at = now(), updated_by = @by
             WHERE id = @id AND tenant_id = @tenant;
            """, new { id, status, by = user.UserId, tenant = _tenant.TenantId });
    }

    public async Task DeleteAsync(Guid id)
    {
        Require("staff_tasks.delete");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM studio.staff_tasks WHERE id = @id AND tenant_id = @tenant;",
            new { id, tenant = _tenant.TenantId });
    }

    private static void ToLocal(StaffTask task)
    {
        task.StartAt = VietnamTime.FromUtc(task.StartAt);
        task.CompletedAt = VietnamTime.FromUtc(task.CompletedAt);
        task.CreatedAt = VietnamTime.FromUtc(task.CreatedAt);
        task.UpdatedAt = VietnamTime.FromUtc(task.UpdatedAt);
    }
}
