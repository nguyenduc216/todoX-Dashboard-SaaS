using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services;

/// <summary>Resolves and caches the active tenant id from system.tenants by tenant code.</summary>
public sealed class TenantContext
{
    private readonly TodoXConnectionFactory _factory;
    private readonly string _tenantCode;
    private Guid? _tenantId;

    public TenantContext(TodoXConnectionFactory factory, IConfiguration configuration)
    {
        _factory = factory;
        _tenantCode = configuration["TodoX:TenantCode"] ?? "TODOX_INTERNAL";
    }

    public Guid TenantId => _tenantId
        ?? throw new InvalidOperationException("TenantContext has not been initialized. Call EnsureLoadedAsync first.");

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_tenantId.HasValue)
        {
            return;
        }

        using var connection = await _factory.OpenAsync(ct);
        var id = await connection.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT id FROM system.tenants WHERE tenant_code = @code LIMIT 1;",
            new { code = _tenantCode });

        _tenantId = id ?? throw new InvalidOperationException($"Tenant '{_tenantCode}' not found in system.tenants.");
    }
}
