using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

/// <summary>
/// Access to crm.customers, crm.customer_users and their auth.app_users logins.
/// Maps onto CustomerProfile / CustomerAccount. Matches Foundation V2 contract; no schema changes.
/// </summary>
public sealed class CustomerRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public CustomerRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    private static TodoXAccountStatus StatusFromString(string? s) => s switch
    {
        "active" => TodoXAccountStatus.Active,
        "pending" => TodoXAccountStatus.Pending,
        "locked" => TodoXAccountStatus.Locked,
        _ => TodoXAccountStatus.Active
    };

    private static string StatusToString(TodoXAccountStatus status) => status switch
    {
        TodoXAccountStatus.Active => "active",
        TodoXAccountStatus.Pending => "pending",
        TodoXAccountStatus.Locked => "locked",
        _ => "active"
    };

    // ===================== Customers (crm.customers) =====================

    public async Task<IReadOnlyList<CustomerProfile>> GetCustomersAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<CustomerRow>(
            """
            SELECT id, customer_code, company_name, full_name, email, phone, tax_code, address, status, created_at
              FROM crm.customers
             WHERE tenant_id = @tenant
             ORDER BY created_at DESC;
            """, new { tenant = _tenant.TenantId });

        return rows.Select(MapCustomer).ToList();
    }

    private static CustomerProfile MapCustomer(CustomerRow r) => new()
    {
        Id = r.id,
        CustomerCode = r.customer_code ?? string.Empty,
        CompanyName = string.IsNullOrWhiteSpace(r.company_name) ? (r.full_name ?? string.Empty) : r.company_name,
        ContactName = r.full_name ?? string.Empty,
        Email = r.email ?? string.Empty,
        Phone = r.phone ?? string.Empty,
        TaxCode = r.tax_code ?? string.Empty,
        Address = r.address ?? string.Empty,
        Status = StatusFromString(r.status),
        CreatedAt = r.created_at.LocalDateTime
    };

    private sealed class CustomerRow
    {
        public Guid id { get; set; }
        public string? customer_code { get; set; }
        public string? company_name { get; set; }
        public string? full_name { get; set; }
        public string? email { get; set; }
        public string? phone { get; set; }
        public string? tax_code { get; set; }
        public string? address { get; set; }
        public string? status { get; set; }
        public DateTimeOffset created_at { get; set; }
    }

    public async Task<Guid> InsertCustomerAsync(CustomerProfile c)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var id = Guid.NewGuid();
        var code = string.IsNullOrWhiteSpace(c.CustomerCode) ? await NextCustomerCodeAsync(conn) : c.CustomerCode;

        await conn.ExecuteAsync(
            """
            INSERT INTO crm.customers
                (id, tenant_id, customer_code, customer_type, full_name, company_name,
                 email, phone, tax_code, address, status, created_at)
            VALUES
                (@id, @tenant, @code, @ctype, @full, @company,
                 @email, @phone, @tax, @address, @status, now());
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                code,
                ctype = string.IsNullOrWhiteSpace(c.CompanyName) ? "individual" : "business",
                full = c.ContactName,
                company = string.IsNullOrWhiteSpace(c.CompanyName) ? null : c.CompanyName,
                email = string.IsNullOrWhiteSpace(c.Email) ? null : c.Email,
                phone = string.IsNullOrWhiteSpace(c.Phone) ? null : c.Phone,
                tax = string.IsNullOrWhiteSpace(c.TaxCode) ? null : c.TaxCode,
                address = string.IsNullOrWhiteSpace(c.Address) ? null : c.Address,
                status = StatusToString(c.Status)
            });

        return id;
    }

    public async Task UpdateCustomerAsync(CustomerProfile c)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE crm.customers
               SET full_name=@full, company_name=@company, email=@email, phone=@phone,
                   tax_code=@tax, address=@address, status=@status,
                   customer_type=@ctype, updated_at=now()
             WHERE id=@id;
            """,
            new
            {
                id = c.Id,
                full = c.ContactName,
                company = string.IsNullOrWhiteSpace(c.CompanyName) ? null : c.CompanyName,
                email = string.IsNullOrWhiteSpace(c.Email) ? null : c.Email,
                phone = string.IsNullOrWhiteSpace(c.Phone) ? null : c.Phone,
                tax = string.IsNullOrWhiteSpace(c.TaxCode) ? null : c.TaxCode,
                address = string.IsNullOrWhiteSpace(c.Address) ? null : c.Address,
                status = StatusToString(c.Status),
                ctype = string.IsNullOrWhiteSpace(c.CompanyName) ? "individual" : "business"
            });
    }

    public async Task SetCustomerStatusAsync(Guid id, TodoXAccountStatus status)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE crm.customers SET status=@status, updated_at=now() WHERE id=@id;",
            new { id, status = StatusToString(status) });
    }

    public async Task DeleteCustomerAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        // Delete linked login users first (crm.customer_users cascades from customer,
        // but the auth.app_users rows must be removed explicitly).
        var userIds = (await conn.QueryAsync<Guid>(
            "SELECT user_id FROM crm.customer_users WHERE customer_id=@id;", new { id })).ToList();

        await conn.ExecuteAsync("DELETE FROM crm.customers WHERE id=@id;", new { id });

        if (userIds.Count > 0)
        {
            await conn.ExecuteAsync("DELETE FROM auth.app_users WHERE id = ANY(@ids);", new { ids = userIds });
        }
    }

    private async Task<string> NextCustomerCodeAsync(System.Data.IDbConnection conn)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM crm.customers WHERE tenant_id=@tenant;", new { tenant = _tenant.TenantId });
        return $"CUST-{count + 1:0000}";
    }

    // ===================== Customer accounts (auth.app_users + crm.customer_users) =====================

    public async Task<IReadOnlyList<CustomerAccount>> GetCustomerAccountsAsync()
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var rows = await conn.QueryAsync<AccountRow>(
            """
            SELECT u.id, u.username, u.full_name, u.email, u.phone, u.is_active, u.created_at,
                   cu.customer_id, cu.relation_type,
                   c.company_name, c.full_name AS customer_full_name, c.customer_code
              FROM crm.customer_users cu
              JOIN auth.app_users u ON u.id = cu.user_id
              JOIN crm.customers c ON c.id = cu.customer_id
             WHERE u.tenant_id = @tenant
             ORDER BY u.created_at DESC;
            """, new { tenant = _tenant.TenantId });

        return rows.Select(r => new CustomerAccount
        {
            Id = r.id,
            CustomerId = r.customer_id,
            CustomerCode = r.customer_code ?? string.Empty,
            CompanyName = string.IsNullOrWhiteSpace(r.company_name) ? (r.customer_full_name ?? string.Empty) : r.company_name,
            FullName = r.full_name ?? string.Empty,
            Email = r.email ?? string.Empty,
            Phone = r.phone ?? string.Empty,
            Role = r.relation_type == "owner" ? TodoXUserRole.CustomerOwner : TodoXUserRole.CustomerUser,
            Status = StatusFromString(r.is_active ? "active" : "locked"),
            CreatedAt = r.created_at.LocalDateTime
        }).ToList();
    }

    private sealed class AccountRow
    {
        public Guid id { get; set; }
        public string? username { get; set; }
        public string? full_name { get; set; }
        public string? email { get; set; }
        public string? phone { get; set; }
        public bool is_active { get; set; }
        public DateTimeOffset created_at { get; set; }
        public Guid customer_id { get; set; }
        public string? relation_type { get; set; }
        public string? company_name { get; set; }
        public string? customer_full_name { get; set; }
        public string? customer_code { get; set; }
    }

    public async Task<Guid> InsertCustomerAccountAsync(CustomerAccount a, string passwordHash)
    {
        await _tenant.EnsureLoadedAsync();
        using var conn = await _factory.OpenAsync();
        var id = Guid.NewGuid();

        await conn.ExecuteAsync(
            """
            INSERT INTO auth.app_users
                (id, tenant_id, user_type, username, email, phone, password_hash,
                 display_name, full_name, is_root, is_active, created_at)
            VALUES
                (@id, @tenant, 'customer', @username, @email, @phone, @hash,
                 @full, @full, false, @active, now());
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                username = string.IsNullOrWhiteSpace(a.Email) ? null : a.Email,
                email = string.IsNullOrWhiteSpace(a.Email) ? null : a.Email,
                phone = string.IsNullOrWhiteSpace(a.Phone) ? null : a.Phone,
                hash = passwordHash,
                full = a.FullName,
                active = a.Status == TodoXAccountStatus.Active
            });

        await conn.ExecuteAsync(
            """
            INSERT INTO crm.customer_users (customer_id, user_id, relation_type, is_primary)
            VALUES (@cid, @uid, @rel, @primary)
            ON CONFLICT DO NOTHING;
            """,
            new
            {
                cid = a.CustomerId,
                uid = id,
                rel = a.Role == TodoXUserRole.CustomerOwner ? "owner" : "member",
                primary = a.Role == TodoXUserRole.CustomerOwner
            });

        return id;
    }

    public async Task UpdateCustomerAccountAsync(CustomerAccount a, string? newPasswordHash)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE auth.app_users
               SET username=@username, email=@email, phone=@phone,
                   full_name=@full, display_name=@full, is_active=@active,
                   password_hash=COALESCE(@hash, password_hash), updated_at=now()
             WHERE id=@id;
            """,
            new
            {
                id = a.Id,
                username = string.IsNullOrWhiteSpace(a.Email) ? null : a.Email,
                email = string.IsNullOrWhiteSpace(a.Email) ? null : a.Email,
                phone = string.IsNullOrWhiteSpace(a.Phone) ? null : a.Phone,
                full = a.FullName,
                active = a.Status == TodoXAccountStatus.Active,
                hash = newPasswordHash
            });

        await conn.ExecuteAsync(
            """
            UPDATE crm.customer_users
               SET relation_type=@rel, is_primary=@primary, customer_id=@cid
             WHERE user_id=@uid;
            """,
            new
            {
                uid = a.Id,
                cid = a.CustomerId,
                rel = a.Role == TodoXUserRole.CustomerOwner ? "owner" : "member",
                primary = a.Role == TodoXUserRole.CustomerOwner
            });
    }

    public async Task SetCustomerAccountActiveAsync(Guid id, bool active)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE auth.app_users SET is_active=@active, updated_at=now() WHERE id=@id;",
            new { id, active });
    }

    public async Task ResetCustomerAccountPasswordAsync(Guid id, string passwordHash)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE auth.app_users SET password_hash=@hash, password_changed_at=now(), updated_at=now() WHERE id=@id;",
            new { id, hash = passwordHash });
    }

    public async Task DeleteCustomerAccountAsync(Guid id)
    {
        using var conn = await _factory.OpenAsync();
        // crm.customer_users cascades when the app_user is removed.
        await conn.ExecuteAsync("DELETE FROM auth.app_users WHERE id=@id;", new { id });
    }

    public async Task<bool> AccountEmailExistsAsync(string email, Guid? excludeId = null)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM auth.app_users WHERE lower(email)=lower(@e) AND (@id IS NULL OR id<>@id));",
            new { e = email, id = excludeId });
    }

    /// <summary>Resolve the crm.customers id a login user belongs to (via crm.customer_users).</summary>
    public async Task<Guid?> GetCustomerIdForUserAsync(Guid userId)
    {
        using var conn = await _factory.OpenAsync();
        return await conn.ExecuteScalarAsync<Guid?>(
            "SELECT customer_id FROM crm.customer_users WHERE user_id=@uid LIMIT 1;", new { uid = userId });
    }
}

