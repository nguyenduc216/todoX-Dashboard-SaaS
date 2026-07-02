using TodoX.Web.Models;

namespace TodoX.Web.Services;

/// <summary>
/// Application-facing account operations, backed by the todo_saas Foundation V2 database
/// through Dapper repositories. Method names/shapes preserved from the in-memory version.
/// </summary>
public sealed class AccountService
{
    private readonly AccountRepository _accounts;
    private readonly CustomerRepository _customers;
    private readonly PermissionRepository _permissions;
    private readonly PasswordHasher _passwords;

    public AccountService(AccountRepository accounts, CustomerRepository customers,
        PermissionRepository permissions, PasswordHasher passwords)
    {
        _accounts = accounts;
        _customers = customers;
        _permissions = permissions;
        _passwords = passwords;
    }

    // ===================== Reads =====================

    public Task<IReadOnlyList<SystemUser>> GetSystemUsersAsync() => _accounts.GetSystemUsersAsync();
    public Task<IReadOnlyList<CustomerProfile>> GetCustomersAsync() => _customers.GetCustomersAsync();
    public Task<IReadOnlyList<CustomerAccount>> GetCustomerAccountsAsync() => _customers.GetCustomerAccountsAsync();

    // ===================== Login / Register =====================

    public async Task<CurrentUserSession?> LoginAsync(LoginRequest request)
    {
        var row = await _accounts.FindForLoginAsync(request.UsernameOrEmail.Trim());
        if (row is null || !row.IsActive)
        {
            return null;
        }

        if (!_passwords.Verify(request.Password, row.PasswordHash))
        {
            return null;
        }

        await _accounts.TouchLastLoginAsync(row.Id);
        return await BuildSessionAsync(row);
    }

    /// <summary>Rebuild a session from a persisted user id (session restore). Null if account is gone/inactive.</summary>
    public async Task<CurrentUserSession?> RehydrateSessionAsync(Guid userId)
    {
        var row = await _accounts.FindByIdAsync(userId);
        if (row is null || !row.IsActive)
        {
            return null;
        }
        return await BuildSessionAsync(row);
    }

    private async Task<CurrentUserSession> BuildSessionAsync(AccountRepository.LoginRow row)
    {
        var role = AccountRepository.RoleFromLoginCode(row.UserType, row.RoleCode);
        Guid? customerId = null;
        if (role is TodoXUserRole.CustomerOwner or TodoXUserRole.CustomerUser)
        {
            customerId = await _customers.GetCustomerIdForUserAsync(row.Id);
        }

        var perms = await _permissions.GetUserPermissionCodesAsync(row.Id);

        return new CurrentUserSession
        {
            UserId = row.Id,
            DisplayName = row.FullName ?? row.DisplayName ?? row.Username ?? row.Email ?? "TodoX User",
            Email = row.Email ?? string.Empty,
            AvatarUrl = row.AvatarUrl,
            Role = role,
            CustomerId = customerId,
            IsRoot = row.IsRoot,
            Permissions = perms,
            IsAuthenticated = true
        };
    }

    /// <summary>Forgot-password: set a new password if the email exists and is active.</summary>
    public async Task<(bool Success, string Message)> ResetPasswordByEmailAsync(string email, string newPassword, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(newPassword))
        {
            return (false, "Vui lòng nhập email và mật khẩu mới.");
        }

        if (newPassword != confirmPassword)
        {
            return (false, "Mật khẩu xác nhận không khớp.");
        }

        var ok = await _accounts.SetPasswordByEmailAsync(email.Trim(), _passwords.Hash(newPassword));
        return ok
            ? (true, "Đã cập nhật mật khẩu. Vui lòng đăng nhập lại.")
            : (false, "Không tìm thấy tài khoản active với email này.");
    }

    /// <summary>Change password after verifying the current one.</summary>
    public async Task<(bool Success, string Message)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return (false, "Vui lòng nhập mật khẩu mới.");
        }

        if (newPassword != confirmPassword)
        {
            return (false, "Mật khẩu xác nhận không khớp.");
        }

        var hash = await _accounts.GetPasswordHashAsync(userId);
        if (!_passwords.Verify(currentPassword, hash))
        {
            return (false, "Mật khẩu hiện tại không đúng.");
        }

        await _accounts.SetPasswordByIdAsync(userId, _passwords.Hash(newPassword));
        return (true, "Đã đổi mật khẩu.");
    }

    /// <summary>Update the current user's own profile (name/phone/gender/date of birth).</summary>
    public Task UpdateOwnProfileAsync(Guid userId, string fullName, string? phone, string? gender, DateTime? dateOfBirth)
        => _accounts.UpdateProfileAsync(userId, fullName, phone, gender, dateOfBirth);

    public Task<(string? Gender, DateTime? Dob)> GetProfileExtrasAsync(Guid userId)
        => _accounts.GetProfileExtrasAsync(userId);

    public async Task<(bool Success, string Message)> RegisterCustomerAsync(CustomerRegistration model)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.ContactName) || string.IsNullOrWhiteSpace(model.Email))
        {
            return (false, "Vui lòng nhập đầy đủ tên công ty, người liên hệ và email.");
        }

        if (model.Password != model.ConfirmPassword)
        {
            return (false, "Mật khẩu xác nhận không khớp.");
        }

        if (await _accounts.EmailExistsAsync(model.Email.Trim()))
        {
            return (false, "Email đã tồn tại trong hệ thống.");
        }

        var customerId = await _customers.InsertCustomerAsync(new CustomerProfile
        {
            CompanyName = model.CompanyName.Trim(),
            ContactName = model.ContactName.Trim(),
            Email = model.Email.Trim(),
            Phone = model.Phone.Trim(),
            TaxCode = model.TaxCode.Trim(),
            Status = TodoXAccountStatus.Pending,
            Gender = model.Gender,
            DateOfBirth = model.DateOfBirth
        });

        await _customers.InsertCustomerAccountAsync(new CustomerAccount
        {
            CustomerId = customerId,
            FullName = model.ContactName.Trim(),
            Email = model.Email.Trim(),
            Phone = model.Phone.Trim(),
            Role = TodoXUserRole.CustomerOwner,
            Status = TodoXAccountStatus.Pending
        }, _passwords.Hash(model.Password));

        return (true, "Đăng ký thành công. Tài khoản đang chờ quản trị viên duyệt.");
    }

    // ===================== System (admin) users =====================

    public async Task<(bool Success, string Message)> SaveSystemUserAsync(SystemUser model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.Username) || string.IsNullOrWhiteSpace(model.Email))
        {
            return (false, "Vui lòng nhập đầy đủ họ tên, username và email.");
        }

        if (await _accounts.UsernameExistsAsync(model.Username.Trim(), model.Id == Guid.Empty ? null : model.Id))
        {
            return (false, "Username đã tồn tại.");
        }

        if (await _accounts.EmailExistsAsync(model.Email.Trim(), model.Id == Guid.Empty ? null : model.Id))
        {
            return (false, "Email đã tồn tại.");
        }

        model.Username = model.Username.Trim();
        model.Email = model.Email.Trim();

        if (model.Id == Guid.Empty)
        {
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                return (false, "Vui lòng nhập mật khẩu.");
            }

            await _accounts.InsertSystemUserAsync(model, _passwords.Hash(model.Password));
            return (true, "Đã thêm quản trị viên.");
        }

        var newHash = string.IsNullOrWhiteSpace(model.Password) ? null : _passwords.Hash(model.Password);
        await _accounts.UpdateSystemUserAsync(model, newHash);
        return (true, "Đã cập nhật quản trị viên.");
    }

    public Task SetSystemUserStatusAsync(Guid id, TodoXAccountStatus status)
        => _accounts.SetSystemUserActiveAsync(id, status == TodoXAccountStatus.Active);

    public async Task<(bool Success, string Message)> DeleteSystemUserAsync(Guid id)
    {
        if (await _accounts.IsRootAsync(id))
        {
            return (false, "Không thể xóa tài khoản root.");
        }

        if (await _accounts.CountActiveAdminsAsync() <= 1)
        {
            return (false, "Không thể xóa quản trị viên hoạt động cuối cùng.");
        }

        await _accounts.DeleteUserAsync(id);
        return (true, "Đã xóa quản trị viên.");
    }

    // ===================== Customers =====================

    public async Task<(bool Success, string Message)> SaveCustomerAsync(CustomerProfile model)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.ContactName) || string.IsNullOrWhiteSpace(model.Email))
        {
            return (false, "Vui lòng nhập đầy đủ tên công ty, người liên hệ và email.");
        }

        if (model.Id == Guid.Empty)
        {
            await _customers.InsertCustomerAsync(model);
            return (true, "Đã thêm khách hàng.");
        }

        await _customers.UpdateCustomerAsync(model);
        return (true, "Đã cập nhật khách hàng.");
    }

    public Task SetCustomerStatusAsync(Guid id, TodoXAccountStatus status)
        => _customers.SetCustomerStatusAsync(id, status);

    public async Task<(bool Success, string Message)> DeleteCustomerAsync(Guid id)
    {
        await _customers.DeleteCustomerAsync(id);
        return (true, "Đã xóa khách hàng và các tài khoản liên quan.");
    }

    // ===================== Customer accounts =====================

    public async Task<(bool Success, string Message)> SaveCustomerAccountAsync(CustomerAccount model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName) || string.IsNullOrWhiteSpace(model.Email))
        {
            return (false, "Vui lòng nhập đầy đủ họ tên và email.");
        }

        if (model.CustomerId == Guid.Empty)
        {
            return (false, "Vui lòng chọn khách hàng hợp lệ.");
        }

        if (await _customers.AccountEmailExistsAsync(model.Email.Trim(), model.Id == Guid.Empty ? null : model.Id))
        {
            return (false, "Email đã tồn tại.");
        }

        model.Email = model.Email.Trim();

        if (model.Id == Guid.Empty)
        {
            if (string.IsNullOrWhiteSpace(model.Password))
            {
                return (false, "Vui lòng nhập mật khẩu.");
            }

            await _customers.InsertCustomerAccountAsync(model, _passwords.Hash(model.Password));
            return (true, "Đã tạo tài khoản khách hàng.");
        }

        var newHash = string.IsNullOrWhiteSpace(model.Password) ? null : _passwords.Hash(model.Password);
        await _customers.UpdateCustomerAccountAsync(model, newHash);
        return (true, "Đã cập nhật tài khoản khách hàng.");
    }

    public Task SetCustomerAccountStatusAsync(Guid id, TodoXAccountStatus status)
        => _customers.SetCustomerAccountActiveAsync(id, status == TodoXAccountStatus.Active);

    public async Task<(bool Success, string Message)> ResetCustomerAccountPasswordAsync(Guid id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return (false, "Mật khẩu mới không được để trống.");
        }

        await _customers.ResetCustomerAccountPasswordAsync(id, _passwords.Hash(newPassword));
        return (true, "Đã đặt lại mật khẩu.");
    }

    public async Task<(bool Success, string Message)> DeleteCustomerAccountAsync(Guid id)
    {
        await _customers.DeleteCustomerAccountAsync(id);
        return (true, "Đã xóa tài khoản khách hàng.");
    }
}
