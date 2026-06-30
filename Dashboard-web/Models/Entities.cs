using System.ComponentModel.DataAnnotations;

namespace TodoX.Dashboard.Models;

public class SystemUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(80)] public string UserName { get; set; } = string.Empty;
    [MaxLength(160)] public string? Email { get; set; }
    [MaxLength(200)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string PasswordHash { get; set; } = string.Empty;
    [MaxLength(40)] public string Role { get; set; } = "Admin";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(180)] public string Name { get; set; } = string.Empty;
    [MaxLength(120)] public string? TaxCode { get; set; }
    [MaxLength(160)] public string? Email { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CustomerAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [MaxLength(80)] public string UserName { get; set; } = string.Empty;
    [MaxLength(160)] public string? Email { get; set; }
    [MaxLength(80)] public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TokenWallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public decimal Balance { get; set; }
    public decimal Reserved { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TokenTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [MaxLength(80)] public string Type { get; set; } = "Credit";
    public decimal Amount { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class PricingRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(120)] public string ServiceCode { get; set; } = string.Empty;
    [MaxLength(180)] public string ServiceName { get; set; } = string.Empty;
    public decimal BaseToken { get; set; }
    public decimal PerSecondToken { get; set; }
    public bool VoiceEnabled { get; set; }
    public bool CaptionEnabled { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RenderJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    [MaxLength(120)] public string JobType { get; set; } = "Video";
    [MaxLength(80)] public string Status { get; set; } = "Queued";
    public int DurationSeconds { get; set; }
    public bool HasVoice { get; set; }
    public bool HasCaption { get; set; }
    public decimal EstimatedToken { get; set; }
    [MaxLength(1000)] public string? OutputUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(120)] public string Key { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Value { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}
