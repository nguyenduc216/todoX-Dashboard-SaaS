using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class TodoXMockDataStore
{
    public List<SystemUser> SystemUsers { get; } = new();
    public List<CustomerProfile> Customers { get; } = new();
    public List<CustomerAccount> CustomerAccounts { get; } = new();

    public TodoXMockDataStore()
    {
        var adminId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        SystemUsers.Add(new SystemUser
        {
            Id = adminId,
            Username = "admin",
            FullName = "Admin System",
            Email = "admin@todox.local",
            Phone = "0901234567",
            Password = "toDOx@123#2026",
            Role = TodoXUserRole.Admin,
            Status = TodoXAccountStatus.Active,
            CreatedAt = DateTime.Now.AddDays(-7)
        });

        var customer = new CustomerProfile
        {
            Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            CustomerCode = "CUST-0001",
            CompanyName = "Công ty Demo TodoX",
            ContactName = "Nguyễn Văn A",
            Email = "demo@todox.local",
            Phone = "0912345678",
            TaxCode = "0100000001",
            Address = "TP. Hồ Chí Minh",
            Status = TodoXAccountStatus.Active,
            CreatedAt = DateTime.Now.AddDays(-3)
        };
        Customers.Add(customer);

        CustomerAccounts.Add(new CustomerAccount
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            CustomerId = customer.Id,
            CustomerCode = customer.CustomerCode,
            CompanyName = customer.CompanyName,
            FullName = customer.ContactName,
            Email = customer.Email,
            Phone = customer.Phone,
            Password = "Customer@123",
            Role = TodoXUserRole.CustomerOwner,
            Status = TodoXAccountStatus.Active,
            CreatedAt = DateTime.Now.AddDays(-3)
        });
    }
}
