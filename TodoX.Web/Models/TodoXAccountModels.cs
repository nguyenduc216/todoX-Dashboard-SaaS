namespace TodoX.Web.Models;

public enum TodoXUserRole
{
    Admin = 1,
    SystemOperator = 2,
    CustomerOwner = 10,
    CustomerUser = 11
}

public enum TodoXAccountStatus
{
    Active = 1,
    Pending = 2,
    Locked = 3
}

public sealed class SystemUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public TodoXUserRole Role { get; set; } = TodoXUserRole.SystemOperator;
    public TodoXAccountStatus Status { get; set; } = TodoXAccountStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class CustomerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CustomerCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public TodoXAccountStatus Status { get; set; } = TodoXAccountStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class CustomerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public TodoXUserRole Role { get; set; } = TodoXUserRole.CustomerOwner;
    public TodoXAccountStatus Status { get; set; } = TodoXAccountStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class CustomerRegistration
{
    public string CompanyName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class LoginRequest
{
    public string UsernameOrEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class CurrentUserSession
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public TodoXUserRole Role { get; set; }
    public bool IsAuthenticated { get; set; }
}
