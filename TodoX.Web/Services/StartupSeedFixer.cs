using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

/// <summary>
/// One-time startup repair for placeholder credentials in Foundation V2 seed data.
/// Writes DATA only (never schema): if admin/root password_hash is a placeholder
/// (not a valid bcrypt hash), it is replaced with a real bcrypt hash so login works.
/// Idempotent — once a valid hash exists it is left untouched.
/// </summary>
public sealed class StartupSeedFixer
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly PasswordHasher _passwords;
    private readonly IConfiguration _config;
    private readonly ILogger<StartupSeedFixer> _logger;

    public StartupSeedFixer(TodoXConnectionFactory factory, TenantContext tenant,
        PasswordHasher passwords, IConfiguration config, ILogger<StartupSeedFixer> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _passwords = passwords;
        _config = config;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);

        var adminUser = _config["TodoX:SeedAdminUsername"] ?? "admin";
        var adminPassword = _config["TodoX:SeedAdminPassword"] ?? "toDOx@123#2026";
        var rootUser = _config["TodoX:SeedRootUsername"] ?? "administrator";

        var rows = await conn.QueryAsync<(Guid Id, string? Username, string? Hash)>(
            """
            SELECT id AS Id, username AS Username, password_hash AS Hash
              FROM auth.app_users
             WHERE tenant_id = @tenant AND username IN (@admin, @root);
            """, new { tenant = _tenant.TenantId, admin = adminUser, root = rootUser });

        foreach (var row in rows)
        {
            if (!PasswordHasher.IsPlaceholder(row.Hash))
            {
                continue;
            }

            await conn.ExecuteAsync(
                "UPDATE auth.app_users SET password_hash=@hash, password_changed_at=now(), updated_at=now() WHERE id=@id;",
                new { id = row.Id, hash = _passwords.Hash(adminPassword) });

            _logger.LogWarning("Repaired placeholder password hash for user '{User}'.", row.Username);
        }
    }
}
