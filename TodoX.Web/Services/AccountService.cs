using TodoX.Web.Models;

namespace TodoX.Web.Services;

public sealed class AccountService
{
    private readonly TodoXMockDataStore _store;

    public AccountService(TodoXMockDataStore store)
    {
        _store = store;
    }

    public IReadOnlyList<SystemUser> GetSystemUsers() => _store.SystemUsers.OrderBy(x => x.FullName).ToList();
    public IReadOnlyList<CustomerProfile> GetCustomers() => _store.Customers.OrderByDescending(x => x.CreatedAt).ToList();
    public IReadOnlyList<CustomerAccount> GetCustomerAccounts() => _store.CustomerAccounts.OrderByDescending(x => x.CreatedAt).ToList();

    public CurrentUserSession? Login(LoginRequest request)
    {
        var key = request.UsernameOrEmail.Trim().ToLowerInvariant();
        var systemUser = _store.SystemUsers.FirstOrDefault(x =>
            (x.Username.Equals(key, StringComparison.OrdinalIgnoreCase) || x.Email.Equals(key, StringComparison.OrdinalIgnoreCase))
            && x.Password == request.Password
            && x.Status == TodoXAccountStatus.Active);

        if (systemUser is not null)
        {
            return new CurrentUserSession
            {
                UserId = systemUser.Id,
                DisplayName = systemUser.FullName,
                Email = systemUser.Email,
                Role = systemUser.Role,
                IsAuthenticated = true
            };
        }

        var customerUser = _store.CustomerAccounts.FirstOrDefault(x =>
            x.Email.Equals(key, StringComparison.OrdinalIgnoreCase)
            && x.Password == request.Password
            && x.Status == TodoXAccountStatus.Active);

        if (customerUser is null)
        {
            return null;
        }

        return new CurrentUserSession
        {
            UserId = customerUser.Id,
            DisplayName = customerUser.FullName,
            Email = customerUser.Email,
            Role = customerUser.Role,
            IsAuthenticated = true
        };
    }

    public (bool Success, string Message) RegisterCustomer(CustomerRegistration model)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.ContactName) || string.IsNullOrWhiteSpace(model.Email))
        {
            return (false, "Vui lòng nhập đầy đủ tên công ty, người liên hệ và email.");
        }

        if (model.Password != model.ConfirmPassword)
        {
            return (false, "Mật khẩu xác nhận không khớp.");
        }

        if (_store.CustomerAccounts.Any(x => x.Email.Equals(model.Email, StringComparison.OrdinalIgnoreCase)) ||
            _store.SystemUsers.Any(x => x.Email.Equals(model.Email, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, "Email đã tồn tại trong hệ thống.");
        }

        var customerCode = $"CUST-{_store.Customers.Count + 1:0000}";
        var customer = new CustomerProfile
        {
            CustomerCode = customerCode,
            CompanyName = model.CompanyName.Trim(),
            ContactName = model.ContactName.Trim(),
            Email = model.Email.Trim(),
            Phone = model.Phone.Trim(),
            TaxCode = model.TaxCode.Trim(),
            Status = TodoXAccountStatus.Pending,
            CreatedAt = DateTime.Now
        };

        _store.Customers.Add(customer);
        _store.CustomerAccounts.Add(new CustomerAccount
        {
            CustomerId = customer.Id,
            CustomerCode = customer.CustomerCode,
            CompanyName = customer.CompanyName,
            FullName = customer.ContactName,
            Email = customer.Email,
            Phone = customer.Phone,
            Password = model.Password,
            Role = TodoXUserRole.CustomerOwner,
            Status = TodoXAccountStatus.Pending,
            CreatedAt = DateTime.Now
        });

        return (true, "Đăng ký thành công. Tài khoản đang chờ quản trị viên duyệt.");
    }

    public void AddSystemUser(SystemUser user)
    {
        user.Id = Guid.NewGuid();
        user.CreatedAt = DateTime.Now;
        _store.SystemUsers.Add(user);
    }

    public void AddCustomer(CustomerProfile customer)
    {
        customer.Id = Guid.NewGuid();
        customer.CustomerCode = string.IsNullOrWhiteSpace(customer.CustomerCode)
            ? $"CUST-{_store.Customers.Count + 1:0000}"
            : customer.CustomerCode;
        customer.CreatedAt = DateTime.Now;
        _store.Customers.Add(customer);
    }

    public void AddCustomerAccount(CustomerAccount account)
    {
        var customer = _store.Customers.FirstOrDefault(x => x.Id == account.CustomerId);
        if (customer is not null)
        {
            account.CustomerCode = customer.CustomerCode;
            account.CompanyName = customer.CompanyName;
        }

        account.Id = Guid.NewGuid();
        account.CreatedAt = DateTime.Now;
        _store.CustomerAccounts.Add(account);
    }
}
