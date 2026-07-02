using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

/// <summary>
/// Access to auth.app_users (admin + customer logins), auth.roles/user_roles,
/// mapped onto the dashboard's SystemUser / CustomerAccount models.
/// SQL is explicit and matches the todo_saas Foundation V2 contract; no schema changes.
/// </summary>
public sealed class AccountRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private Dictionary<string, Guid>? _roleIdByCode;

    public AccountRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    // ---- role code mapping between the dashboard enum and auth.roles.code ----
    public static string RoleCode(TodoXUserRole role) => role switch
    {
        TodoXUserRole.Admin => "admin",
        TodoXUserRole.SystemOperator => "support",
        TodoXUserRole.CustomerOwner => "customer",
        TodoXUserRole.CustomerUser => "customer",
        _ => "support"
    };

    private static TodoXUserRole SystemRoleFromCode(string? code) => code switch
    {
        "administrator_root" => TodoXUserRole.Admin,
        "admin" => TodoXUserRole.Admin,
        _ => TodoXUserRole.SystemOperator
    };

    private static TodoXAccountStatus StatusFromActive(bool isActive)
        => isActive ? TodoXAccountStatus.Active : TodoXAccountStatus.Locked;

    private async Task<Guid> RoleIdAsync(System.Data.IDbConnection conn, string code)
    {
        _roleIdByCode ??= (await conn.QueryAsync<(string Code, Guid Id)>(
                "SELECT code, id FROM auth.roles;"))
            .ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);

        return _roleIdByCode.TryGetValue(code, out var id)
            ? id
            : throw new InvalidOperationException($"Role '{code}' not found in auth.roles.");
    }

    // ===================== System (admin) users =====================

    public async Task<IReadOnlyList<SystemUser>> GetSystemUsersAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<SystemUserRow>(
            """
            SELECT u.id, u.username, u.full_name, u.display_name, u.email, u.phone,
                   u.is_active, u.created_at,
                   (SELECT r.code FROM auth.user_roles ur
                      JOIN auth.roles r ON r.id = ur.role_id
                     WHERE ur.user_id = u.id
                     ORDER BY r.sort_order LIMIT 1) AS role_code
              FROM auth.app_users u
             WHERE u.tenant_id = @tenant AND u.user_type IN ('root','admin')
             ORDER BY u.full_name;
            """, new { tenant = _tenant.TenantId });

        return rows.Select(r => new SystemUser
        {
            Id = r.id,
            Username = r.username ?? string.Empty,
            FullName = r.full_name ?? r.display_name ?? string.Empty,
            Email = r.email ?? string.Empty,
            Phone = r.phone ?? string.Empty,
            Role = SystemRoleFromCode(r.role_code),
            Status = StatusFromActive(r.is_active),
            CreatedAt = r.created_at.LocalDateTime
        }).ToList();
    }

    private sealed class SystemUserRow
    {
        public Guid id { get; set; }
        public string? username { get; set; }
        public string? full_name { get; set; }
        public string? display_name { get; set; }
        public string? email { get; set; }
        public string? phone { get; set; }
        public bool is_active { get; set; }
        public DateTimeOffset created_at { get; set; }
        public string? role_code { get; set; }
    }

    public async Task<bool> UsernameExistsAsync(string username, Guid? excludeId = null)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM auth.app_users WHERE lower(username)=lower(@u) AND (@id IS NULL OR id<>@id));",
            new { u = username, id = excludeId });
    }

    public async Task<bool> EmailExistsAsync(string email, Guid? excludeId = null)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM auth.app_users WHERE lower(email)=lower(@e) AND (@id IS NULL OR id<>@id));",
            new { e = email, id = excludeId });
    }

    /// <summary>Insert an admin/operator user and assign the matching role. Returns new id.</summary>
    public async Task<Guid> InsertSystemUserAsync(SystemUser user, string passwordHash)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var roleId = await RoleIdAsync(conn, RoleCode(user.Role));
        var id = Guid.NewGuid();

        await conn.ExecuteAsync(
            """
            INSERT INTO auth.app_users
                (id, tenant_id, user_type, username, email, phone, password_hash,
                 display_name, full_name, is_root, is_active, created_at)
            VALUES
                (@id, @tenant, 'admin', @username, @email, @phone, @hash,
                 @full, @full, false, @active, now());
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                username = user.Username,
                email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
                phone = string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone,
                hash = passwordHash,
                full = user.FullName,
                active = user.Status == TodoXAccountStatus.Active
            });

        await conn.ExecuteAsync(
            "INSERT INTO auth.user_roles (user_id, role_id) VALUES (@uid, @rid) ON CONFLICT DO NOTHING;",
            new { uid = id, rid = roleId });

        return id;
    }

    /// <summary>Update an admin/operator user; only replaces password when a new hash is provided.</summary>
    public async Task UpdateSystemUserAsync(SystemUser user, string? newPasswordHash)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE auth.app_users
               SET username = @username,
                   email = @email,
                   phone = @phone,
                   full_name = @full,
                   display_name = @full,
                   is_active = @active,
                   password_hash = COALESCE(@hash, password_hash),
                   updated_at = now()
             WHERE id = @id;
            """,
            new
            {
                id = user.Id,
                username = user.Username,
                email = string.IsNullOrWhiteSpace(user.Email) ? null : user.Email,
                phone = string.IsNullOrWhiteSpace(user.Phone) ? null : user.Phone,
                full = user.FullName,
                active = user.Status == TodoXAccountStatus.Active,
                hash = newPasswordHash
            });

        // Sync the single role assignment to the chosen role code.
        var roleId = await RoleIdAsync(conn, RoleCode(user.Role));
        await conn.ExecuteAsync("DELETE FROM auth.user_roles WHERE user_id = @uid;", new { uid = user.Id });
        await conn.ExecuteAsync(
            "INSERT INTO auth.user_roles (user_id, role_id) VALUES (@uid, @rid) ON CONFLICT DO NOTHING;",
            new { uid = user.Id, rid = roleId });
    }

    public async Task SetSystemUserActiveAsync(Guid id, bool active)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE auth.app_users SET is_active=@active, updated_at=now() WHERE id=@id;",
            new { id, active });
    }

    public async Task<int> CountActiveAdminsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            """
            SELECT count(*) FROM auth.app_users u
             WHERE u.tenant_id=@tenant AND u.user_type IN ('root','admin') AND u.is_active;
            """, new { tenant = _tenant.TenantId });
    }

    public async Task<bool> IsRootAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT COALESCE(is_root,false) FROM auth.app_users WHERE id=@id;", new { id });
    }

    public async Task DeleteUserAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        // auth.user_roles / crm.customer_users cascade on user delete.
        await conn.ExecuteAsync("DELETE FROM auth.app_users WHERE id=@id;", new { id });
    }

    // ===================== Login =====================

    public sealed class LoginRow
    {
        public Guid Id { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? FullName { get; set; }
        public string? DisplayName { get; set; }
        public string UserType { get; set; } = string.Empty;
        public string? PasswordHash { get; set; }
        public bool IsActive { get; set; }
        public string? RoleCode { get; set; }
        public bool IsRoot { get; set; }
    }

    public async Task<LoginRow?> FindForLoginAsync(string usernameOrEmail)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<LoginRow>(
            """
            SELECT u.id AS Id, u.username AS Username, u.email AS Email,
                   u.full_name AS FullName, u.display_name AS DisplayName,
                   u.user_type AS UserType, u.password_hash AS PasswordHash, u.is_active AS IsActive,
                   COALESCE(u.is_root,false) AS IsRoot,
                   (SELECT r.code FROM auth.user_roles ur
                      JOIN auth.roles r ON r.id = ur.role_id
                     WHERE ur.user_id = u.id ORDER BY r.sort_order LIMIT 1) AS RoleCode
              FROM auth.app_users u
             WHERE u.tenant_id = @tenant
               AND (lower(u.username) = lower(@key) OR lower(u.email) = lower(@key))
             LIMIT 1;
            """, new { tenant = _tenant.TenantId, key = usernameOrEmail });
    }

    public async Task TouchLastLoginAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("UPDATE auth.app_users SET last_login_at=now() WHERE id=@id;", new { id });
    }

    /// <summary>Load a login row by user id (used to re-hydrate a persisted session).</summary>
    public async Task<LoginRow?> FindByIdAsync(Guid id)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<LoginRow>(
            """
            SELECT u.id AS Id, u.username AS Username, u.email AS Email,
                   u.full_name AS FullName, u.display_name AS DisplayName,
                   u.user_type AS UserType, u.password_hash AS PasswordHash, u.is_active AS IsActive,
                   COALESCE(u.is_root,false) AS IsRoot,
                   (SELECT r.code FROM auth.user_roles ur
                      JOIN auth.roles r ON r.id = ur.role_id
                     WHERE ur.user_id = u.id ORDER BY r.sort_order LIMIT 1) AS RoleCode
              FROM auth.app_users u
             WHERE u.tenant_id = @tenant AND u.id = @id
             LIMIT 1;
            """, new { tenant = _tenant.TenantId, id });
    }

    public async Task<bool> SetPasswordByEmailAsync(string email, string passwordHash)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var affected = await conn.ExecuteAsync(
            """
            UPDATE auth.app_users
               SET password_hash=@hash, password_changed_at=now(), updated_at=now()
             WHERE tenant_id=@tenant AND lower(email)=lower(@email) AND is_active;
            """, new { tenant = _tenant.TenantId, email, hash = passwordHash });
        return affected > 0;
    }

    public async Task<string?> GetPasswordHashAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT password_hash FROM auth.app_users WHERE id=@id;", new { id });
    }

    public async Task SetPasswordByIdAsync(Guid id, string passwordHash)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE auth.app_users SET password_hash=@hash, password_changed_at=now(), updated_at=now() WHERE id=@id;",
            new { id, hash = passwordHash });
    }

    public async Task UpdateProfileAsync(Guid id, string fullName, string? phone, string? gender, DateTime? dateOfBirth)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE auth.app_users
               SET full_name=@full, display_name=@full, phone=@phone,
                   gender=@gender, date_of_birth=@dob, updated_at=now()
             WHERE id=@id;
            """, new
            {
                id, full = fullName,
                phone = string.IsNullOrWhiteSpace(phone) ? null : phone,
                gender = string.IsNullOrWhiteSpace(gender) ? null : gender,
                dob = dateOfBirth
            });
    }

    public async Task<(string? Gender, DateTime? Dob)> GetProfileExtrasAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        var row = await conn.QuerySingleOrDefaultAsync<(string? Gender, DateTime? Dob)>(
            "SELECT gender AS Gender, date_of_birth AS Dob FROM auth.app_users WHERE id=@id;", new { id });
        return row;
    }

    public static TodoXUserRole RoleFromLoginCode(string userType, string? roleCode)
    {
        if (userType is "root" or "admin")
        {
            return SystemRoleFromCode(roleCode);
        }

        return TodoXUserRole.CustomerOwner;
    }
}

