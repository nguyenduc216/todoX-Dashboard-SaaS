using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Studio;

/// <summary>
/// studio.customer_profiles, studio.staff_members, studio.video_plans (+ versions, notes)
/// — database/manual/studio/20261116_customer_segments_staff_video_plans.sql.
/// Every write checks the caller's permission here, not only in the page.
/// </summary>
public sealed class StudioRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly AuthStateService _auth;
    private readonly ILogger<StudioRepository> _logger;

    public StudioRepository(TodoXConnectionFactory factory, TenantContext tenant, AuthStateService auth, ILogger<StudioRepository> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _auth = auth;
        _logger = logger;
    }

    public async Task<bool> IsReadyAsync()
    {
        try
        {
            using var conn = await _factory.OpenAsync();
            return await conn.ExecuteScalarAsync<bool>(
                """
                SELECT to_regclass('studio.customer_profiles') IS NOT NULL
                   AND to_regclass('studio.staff_members') IS NOT NULL
                   AND to_regclass('studio.video_plans') IS NOT NULL
                   AND to_regclass('studio.video_plan_versions') IS NOT NULL
                   AND to_regclass('studio.video_plan_notes') IS NOT NULL;
                """);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Studio schema check failed.");
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

    // ===================================================== Loại khách + số video

    public async Task<IReadOnlyDictionary<Guid, string>> GetSegmentsAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<(Guid CustomerId, string Segment)>(
            "SELECT customer_id, segment FROM studio.customer_profiles;");
        return rows.ToDictionary(x => x.CustomerId, x => x.Segment);
    }

    public async Task SetSegmentAsync(Guid customerId, string segment)
    {
        var user = Require("customers.update");
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO studio.customer_profiles (customer_id, segment, updated_at, updated_by)
            VALUES (@customerId, @segment, now(), @by)
            ON CONFLICT (customer_id) DO UPDATE
               SET segment = EXCLUDED.segment, updated_at = now(), updated_by = EXCLUDED.updated_by;
            """,
            new { customerId, segment = CustomerSegments.Normalize(segment), by = user.UserId });
    }

    /// <summary>Video đã bàn giao (studio.customer_videos), kế hoạch đã release và đang làm, theo khách.</summary>
    public async Task<IReadOnlyDictionary<Guid, CustomerVideoCounts>> GetVideoCountsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var hasDelivered = await conn.ExecuteScalarAsync<bool>("SELECT to_regclass('studio.customer_videos') IS NOT NULL;");
        var delivered = hasDelivered
            ? "(SELECT count(*) FROM studio.customer_videos v WHERE v.customer_id = c.id)::int"
            : "0";
        var rows = await conn.QueryAsync<(Guid CustomerId, int Delivered, int Released, int InProgress)>(
            $"""
            SELECT c.id,
                   {delivered},
                   (SELECT count(*) FROM studio.video_plans p WHERE p.customer_id = c.id AND p.status = 'released')::int,
                   (SELECT count(*) FROM studio.video_plans p WHERE p.customer_id = c.id
                       AND p.status NOT IN ('released', 'cancelled'))::int
              FROM crm.customers c
             WHERE c.tenant_id = @tenant;
            """, new { tenant = _tenant.TenantId });
        return rows.ToDictionary(x => x.CustomerId, x => new CustomerVideoCounts(x.Delivered, x.Released, x.InProgress));
    }

    // ===================================================== Nhân viên TodoX

    public async Task<IReadOnlyList<StaffMember>> ListStaffAsync()
    {
        Require("staff_members.view");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<StaffMember>(
            """
            SELECT u.id AS UserId, COALESCE(u.username, '') AS Username,
                   COALESCE(u.full_name, u.display_name, '') AS FullName,
                   COALESCE(NULLIF(s.contact_email, ''), u.email, '') AS Email, COALESCE(NULLIF(s.phone, ''), u.phone, '') AS Phone,
                   u.is_active AS IsActive, u.created_at AS CreatedAt,
                   s.employee_code AS EmployeeCode, s.position AS Position, s.department AS Department,
                   s.joined_on AS JoinedOn, s.note AS Note,
                   (SELECT count(*) FROM studio.video_plans p
                     WHERE p.assignee_user_id = u.id AND p.status NOT IN ('released', 'cancelled'))::int AS OpenPlans
              FROM auth.app_users u
              JOIN auth.user_roles ur ON ur.user_id = u.id
              JOIN auth.roles r ON r.id = ur.role_id AND r.code = 'staff'
              LEFT JOIN studio.staff_members s ON s.user_id = u.id
             WHERE u.tenant_id = @tenant
             ORDER BY COALESCE(u.full_name, u.display_name, u.username);
            """, new { tenant = _tenant.TenantId });
        return rows.ToList();
    }

    /// <summary>Hồ sơ nhân viên; mã nhân viên trống được cấp NV-0001, NV-0002...</summary>
    public async Task<string?> SaveStaffProfileAsync(StaffMember staff, bool isNew)
    {
        var user = Require(isNew ? "staff_members.create" : "staff_members.update");
        if (staff.UserId == Guid.Empty) return "Chưa có tài khoản đăng nhập cho nhân viên.";
        using var conn = await _factory.OpenAsync();
        var code = string.IsNullOrWhiteSpace(staff.EmployeeCode) ? null : staff.EmployeeCode.Trim();
        if (code is null)
        {
            var existing = await conn.ExecuteScalarAsync<string?>(
                "SELECT employee_code FROM studio.staff_members WHERE user_id = @id;", new { id = staff.UserId });
            code = existing ?? await NextEmployeeCodeAsync(conn);
        }
        else if (await conn.ExecuteScalarAsync<bool>(
                     "SELECT EXISTS(SELECT 1 FROM studio.staff_members WHERE lower(employee_code) = lower(@code) AND user_id <> @id);",
                     new { code, id = staff.UserId }))
        {
            return $"Mã nhân viên {code} đã được dùng.";
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO studio.staff_members
                (user_id, employee_code, position, department, joined_on, note, phone, contact_email, created_at, created_by, updated_at, updated_by)
            VALUES (@id, @code, @position, @department, @joined::date, @note, @phone, @email, now(), @by, now(), @by)
            ON CONFLICT (user_id) DO UPDATE
               SET employee_code = EXCLUDED.employee_code, position = EXCLUDED.position,
                   department = EXCLUDED.department, joined_on = EXCLUDED.joined_on, note = EXCLUDED.note,
                   phone = EXCLUDED.phone, contact_email = EXCLUDED.contact_email,
                   updated_at = now(), updated_by = EXCLUDED.updated_by;
            """,
            new
            {
                id = staff.UserId,
                code,
                position = Blank(staff.Position),
                department = Blank(staff.Department),
                joined = staff.JoinedOn?.Date,
                note = Blank(staff.Note),
                phone = Blank(staff.Phone),
                email = Blank(staff.Email),
                by = user.UserId
            });
        staff.EmployeeCode = code;
        return null;
    }

    /// <summary>Checked before the login account is created, so a duplicate code never leaves an account without a profile.</summary>
    public async Task<bool> EmployeeCodeTakenAsync(string? code, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM studio.staff_members WHERE lower(employee_code) = lower(@code) AND user_id <> @userId);",
            new { code = code.Trim(), userId });
    }

    private static async Task<string> NextEmployeeCodeAsync(System.Data.IDbConnection conn)
    {
        var max = await conn.ExecuteScalarAsync<int?>(
            """
            SELECT max(substring(employee_code FROM '^NV-(\d+)$')::int)
              FROM studio.staff_members
             WHERE employee_code ~ '^NV-\d+$';
            """);
        return $"NV-{(max ?? 0) + 1:0000}";
    }

    /// <summary>Nhân viên và quản trị viên đang hoạt động — người có thể nhận làm một kế hoạch video.</summary>
    public async Task<IReadOnlyList<PlanAssignee>> ListAssigneesAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<(Guid Id, string Name, bool IsStaff)>(
            """
            SELECT u.id, COALESCE(u.full_name, u.display_name, u.username, u.email, '') AS name,
                   EXISTS(SELECT 1 FROM auth.user_roles ur JOIN auth.roles r ON r.id = ur.role_id
                           WHERE ur.user_id = u.id AND r.code = 'staff') AS is_staff
              FROM auth.app_users u
             WHERE u.tenant_id = @tenant AND u.user_type IN ('root', 'admin') AND u.is_active
             ORDER BY 3 DESC, 2;
            """, new { tenant = _tenant.TenantId });
        return rows.Select(x => new PlanAssignee(x.Id, x.Name, x.IsStaff)).ToList();
    }

    // ===================================================== Kế hoạch video

    private const string PlanSelect =
        """
        SELECT p.id AS Id, p.customer_id AS CustomerId,
               COALESCE(NULLIF(c.company_name, ''), c.full_name) AS CustomerName, c.customer_code AS CustomerCode,
               p.title AS Title, p.content AS Content, p.script AS Script,
               p.assignee_user_id AS AssigneeUserId,
               COALESCE(a.full_name, a.display_name, a.username) AS AssigneeName,
               p.release_date AS ReleaseDate, p.status AS Status,
               (SELECT count(*) FROM studio.video_plan_versions v WHERE v.plan_id = p.id)::int AS VersionCount,
               (SELECT count(*) FROM studio.video_plan_notes n WHERE n.plan_id = p.id)::int AS NoteCount,
               p.created_at AS CreatedAt, p.updated_at AS UpdatedAt
          FROM studio.video_plans p
          JOIN crm.customers c ON c.id = p.customer_id
          LEFT JOIN auth.app_users a ON a.id = p.assignee_user_id
        """;

    public async Task<IReadOnlyList<VideoPlan>> ListPlansAsync()
    {
        Require("video_plans.view");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<VideoPlan>(
            PlanSelect + " WHERE c.tenant_id = @tenant ORDER BY p.release_date NULLS LAST, p.created_at DESC;",
            new { tenant = _tenant.TenantId });
        return rows.ToList();
    }

    /// <summary>Open plans for the home page blocks (no script/content text needed there).</summary>
    public async Task<IReadOnlyList<VideoPlan>> ListOpenPlansForHomeAsync()
    {
        var user = _auth.CurrentUser;
        if (!HomeWidgets.CanSee(user, HomeWidgets.MyVideoPlans) && !HomeWidgets.CanSee(user, HomeWidgets.VideoPlans))
            throw new UnauthorizedAccessException("Bạn không có quyền xem kế hoạch video trên trang chủ.");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<VideoPlan>(
            PlanSelect + " WHERE c.tenant_id = @tenant AND p.status NOT IN ('released', 'cancelled') ORDER BY p.release_date NULLS LAST, p.created_at;",
            new { tenant = _tenant.TenantId });
        return rows.ToList();
    }

    public async Task<VideoPlan?> GetPlanAsync(Guid id)
    {
        Require("video_plans.view");
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<VideoPlan>(
            PlanSelect + " WHERE c.tenant_id = @tenant AND p.id = @id;", new { tenant = _tenant.TenantId, id });
    }

    public async Task<(bool Ok, string? Error)> SavePlanAsync(VideoPlan plan)
    {
        var isNew = plan.Id == Guid.Empty;
        var user = Require(isNew ? "video_plans.create" : "video_plans.update");
        plan.Title = plan.Title?.Trim() ?? string.Empty;
        var error = VideoPlanRules.Validate(plan);
        if (error is not null) return (false, error);

        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        if (!await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM crm.customers WHERE id = @id AND tenant_id = @tenant);",
                new { id = plan.CustomerId, tenant = _tenant.TenantId }))
            return (false, "Khách hàng không tồn tại.");

        var args = new
        {
            id = isNew ? Guid.NewGuid() : plan.Id,
            customerId = plan.CustomerId,
            title = plan.Title,
            content = Blank(plan.Content),
            script = Blank(plan.Script),
            assignee = plan.AssigneeUserId,
            release = plan.ReleaseDate?.Date,
            status = plan.Status,
            by = user.UserId
        };
        if (isNew)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO studio.video_plans
                    (id, customer_id, title, content, script, assignee_user_id, release_date, status,
                     created_at, created_by, updated_at, updated_by)
                VALUES (@id, @customerId, @title, @content, @script, @assignee, @release::date, @status,
                        now(), @by, now(), @by);
                """, args);
            plan.Id = args.id;
        }
        else
        {
            var updated = await conn.ExecuteAsync(
                """
                UPDATE studio.video_plans
                   SET customer_id = @customerId, title = @title, content = @content, script = @script,
                       assignee_user_id = @assignee, release_date = @release::date, status = @status,
                       updated_at = now(), updated_by = @by
                 WHERE id = @id;
                """, args);
            if (updated == 0) return (false, "Kế hoạch không còn tồn tại.");
        }
        return (true, null);
    }

    public async Task SetPlanStatusAsync(Guid id, string status)
    {
        var user = Require("video_plans.update");
        if (!VideoPlanStatuses.All.Contains(status)) throw new ArgumentException("Trạng thái không hợp lệ.", nameof(status));
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE studio.video_plans SET status = @status, updated_at = now(), updated_by = @by WHERE id = @id;",
            new { id, status, by = user.UserId });
    }

    public async Task DeletePlanAsync(Guid id)
    {
        Require("video_plans.delete");
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM studio.video_plans WHERE id = @id;", new { id });
    }

    // ----------------------------------------------------- Phiên bản

    public async Task<IReadOnlyList<VideoPlanVersion>> ListVersionsAsync(Guid planId)
    {
        Require("video_plans.view");
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<VideoPlanVersion>(
            """
            SELECT v.id AS Id, v.plan_id AS PlanId, v.version_no AS VersionNo, v.video_url AS VideoUrl,
                   v.change_note AS ChangeNote, v.status AS Status, v.created_at AS CreatedAt,
                   COALESCE(u.full_name, u.display_name, u.username) AS CreatedByName
              FROM studio.video_plan_versions v
              LEFT JOIN auth.app_users u ON u.id = v.created_by
             WHERE v.plan_id = @planId
             ORDER BY v.version_no DESC;
            """, new { planId });
        return rows.ToList();
    }

    /// <summary>Thêm phiên bản mới: số phiên bản tự tăng (v1, v2...).</summary>
    public async Task<(bool Ok, string? Error)> AddVersionAsync(VideoPlanVersion version)
    {
        var user = Require("video_plans.create");
        version.VideoUrl = Blank(version.VideoUrl);
        version.ChangeNote = Blank(version.ChangeNote);
        var error = VideoPlanRules.ValidateVersion(version);
        if (error is not null) return (false, error);

        using var conn = await _factory.OpenAsync();
        using var tx = conn.BeginTransaction();
        // Lock the plan row so two people adding a version at once get v2 and v3, not two v2.
        var exists = await conn.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM studio.video_plans WHERE id = @id FOR UPDATE;", new { id = version.PlanId }, tx);
        if (exists is null) return (false, "Kế hoạch không còn tồn tại.");
        var next = await conn.ExecuteScalarAsync<int>(
            "SELECT COALESCE(max(version_no), 0) + 1 FROM studio.video_plan_versions WHERE plan_id = @id;",
            new { id = version.PlanId }, tx);
        await conn.ExecuteAsync(
            """
            INSERT INTO studio.video_plan_versions (id, plan_id, version_no, video_url, change_note, status, created_at, created_by)
            VALUES (@id, @planId, @no, @url, @note, @status, now(), @by);
            UPDATE studio.video_plans SET updated_at = now(), updated_by = @by WHERE id = @planId;
            """,
            new { id = Guid.NewGuid(), planId = version.PlanId, no = next, url = version.VideoUrl, note = version.ChangeNote, status = version.Status, by = user.UserId },
            tx);
        tx.Commit();
        version.VersionNo = next;
        return (true, null);
    }

    public async Task SetVersionStatusAsync(Guid versionId, string status)
    {
        Require("video_plans.update");
        if (!VideoPlanVersionStatuses.All.Contains(status)) throw new ArgumentException("Trạng thái không hợp lệ.", nameof(status));
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("UPDATE studio.video_plan_versions SET status = @status WHERE id = @versionId;", new { versionId, status });
    }

    // ----------------------------------------------------- Ghi chú làm việc với khách

    public async Task<IReadOnlyList<VideoPlanNote>> ListNotesAsync(Guid planId)
    {
        Require("video_plans.view");
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<VideoPlanNote>(
            """
            SELECT n.id AS Id, n.plan_id AS PlanId, n.note AS Note, n.created_at AS CreatedAt,
                   COALESCE(u.full_name, u.display_name, u.username) AS CreatedByName
              FROM studio.video_plan_notes n
              LEFT JOIN auth.app_users u ON u.id = n.created_by
             WHERE n.plan_id = @planId
             ORDER BY n.created_at DESC;
            """, new { planId });
        return rows.ToList();
    }

    public async Task<(bool Ok, string? Error)> AddNoteAsync(Guid planId, string? note)
    {
        var user = Require("video_plans.create");
        if (string.IsNullOrWhiteSpace(note)) return (false, "Vui lòng nhập ghi chú.");
        using var conn = await _factory.OpenAsync();
        var inserted = await conn.ExecuteAsync(
            """
            INSERT INTO studio.video_plan_notes (id, plan_id, note, created_at, created_by)
            SELECT @id, p.id, @note, now(), @by FROM studio.video_plans p WHERE p.id = @planId;
            """,
            new { id = Guid.NewGuid(), planId, note = note.Trim(), by = user.UserId });
        return inserted == 0 ? (false, "Kế hoạch không còn tồn tại.") : (true, null);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
