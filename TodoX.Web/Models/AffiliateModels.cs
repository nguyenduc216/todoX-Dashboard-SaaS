namespace TodoX.Web.Models;

/// <summary>Đại lý (affiliate.agents) kèm số liệu tổng hợp hoa hồng.</summary>
public sealed class AffiliateAgentView
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AgentCode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankAccountName { get; set; }
    public string? Note { get; set; }
    public int ReferredCount { get; set; }
    public decimal PendingAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Thông tin đại lý gắn với một khách hàng: bản thân có là đại lý không, và được đại lý nào giới thiệu.</summary>
public sealed class CustomerAffiliateInfo
{
    public Guid CustomerId { get; set; }
    public string? OwnAgentCode { get; set; }
    public bool IsAgent { get; set; }
    public string? ReferredByAgentCode { get; set; }
    public string? ReferredByAgentName { get; set; }
}

/// <summary>Gói khách hàng (affiliate.packages) và tỷ lệ hoa hồng đại lý.</summary>
public sealed class AffiliatePackage
{
    public Guid Id { get; set; }
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public int CycleMonths { get; set; } = 1;
    public decimal CommissionPercent { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class AffiliatePurchaseView
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime PurchasedAt { get; set; }
    public DateTime? CycleStart { get; set; }
    public DateTime? CycleEnd { get; set; }
    public string Status { get; set; } = "paid";
    public string? Note { get; set; }
    public string? AgentCode { get; set; }
    public decimal? CommissionAmount { get; set; }
    public string? CommissionStatus { get; set; }
}

public sealed class AffiliatePurchaseRequest
{
    public Guid CustomerId { get; set; }
    public Guid PackageId { get; set; }
    /// <summary>Số tiền thực thu; null = lấy giá niêm yết của gói.</summary>
    public decimal? Amount { get; set; }
    public DateTime PurchasedAt { get; set; } = DateTime.Now;
    /// <summary>Ngày bắt đầu chu kỳ; null = nối tiếp chu kỳ hiện tại của khách hàng (hoặc hôm nay).</summary>
    public DateTime? CycleStart { get; set; }
    public string? Note { get; set; }
}

public sealed class AffiliateCommissionView
{
    public Guid Id { get; set; }
    public Guid PurchaseId { get; set; }
    public Guid AgentId { get; set; }
    public string AgentCode { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankAccountName { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PackageCode { get; set; } = string.Empty;
    public string PackageName { get; set; } = string.Empty;
    public DateTime PurchasedAt { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = "pending";
    public DateTime? PaidAt { get; set; }
    public string? BankReference { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class AffiliateCommissionStatuses
{
    public const string Pending = "pending";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";

    public static readonly string[] All = { Pending, Paid, Cancelled };

    public static string Label(string? status) => status switch
    {
        Pending => "Chưa chuyển khoản",
        Paid => "Đã chuyển khoản",
        Cancelled => "Đã hủy",
        _ => status ?? string.Empty
    };
}
