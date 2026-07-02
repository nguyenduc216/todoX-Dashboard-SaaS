using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

public sealed record RoleDto(Guid Id, string Code, string Name, string RoleType, bool IsSystem);

public sealed record PermissionDto(Guid Id, string Module, string Action, string Code, string Name);

/// <summary>
/// Access to auth.permissions / auth.roles / auth.role_permissions.
/// Loads a user's effective permission set and manages role-permission assignments.
/// </summary>
public sealed class PermissionRepository
{
    private readonly TodoXConnectionFactory _factory;

    public PermissionRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    /// <summary>All permission codes (module.action) granted to a user through their roles.</summary>
    public async Task<HashSet<string>> GetUserPermissionCodesAsync(Guid userId)
    {
        using var conn = await _factory.OpenAsync();
        var codes = await conn.QueryAsync<string>(
            """
            SELECT DISTINCT p.code
              FROM auth.user_roles ur
              JOIN auth.role_permissions rp ON rp.role_id = ur.role_id
              JOIN auth.permissions p ON p.id = rp.permission_id
             WHERE ur.user_id = @uid AND p.is_active;
            """, new { uid = userId });
        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<RoleDto>(
            """
            SELECT id AS Id, code AS Code, name AS Name, role_type AS RoleType, is_system AS IsSystem
              FROM auth.roles ORDER BY sort_order, name;
            """);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<PermissionDto>> GetAllPermissionsAsync()
    {
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<PermissionDto>(
            """
            SELECT id AS Id, module AS Module, action AS Action, code AS Code, name AS Name
              FROM auth.permissions WHERE is_active ORDER BY module, action;
            """);
        return rows.ToList();
    }

    public async Task<HashSet<Guid>> GetRolePermissionIdsAsync(Guid roleId)
    {
        using var conn = await _factory.OpenAsync();
        var ids = await conn.QueryAsync<Guid>(
            "SELECT permission_id FROM auth.role_permissions WHERE role_id=@rid;", new { rid = roleId });
        return new HashSet<Guid>(ids);
    }

    /// <summary>Replace the role's permission set with the given permission ids.</summary>
    public async Task SetRolePermissionsAsync(Guid roleId, IEnumerable<Guid> permissionIds)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync("DELETE FROM auth.role_permissions WHERE role_id=@rid;", new { rid = roleId });

        var list = permissionIds.Distinct().ToArray();
        if (list.Length > 0)
        {
            await conn.ExecuteAsync(
                "INSERT INTO auth.role_permissions (role_id, permission_id) SELECT @rid, unnest(@ids) ON CONFLICT DO NOTHING;",
                new { rid = roleId, ids = list });
        }
    }
}
