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
    /// <summary>% hoa hồng trên mỗi lần nạp điểm của khách được giới thiệu (null = mặc định).</summary>
    public decimal? TopupCommissionPercent { get; set; }
}

/// <summary>Khách hàng do đại lý giới thiệu, hiển thị cho chính đại lý (không có email/điện thoại).</summary>
public sealed class AffiliateReferredCustomer
{
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime ReferredAt { get; set; }
    public int PurchaseCount { get; set; }
    public string? LatestPackageCode { get; set; }
    public DateTime? CurrentCycleEnd { get; set; }
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

/// <summary>Cấu hình hoa hồng nạp điểm (affiliate.settings).</summary>
public sealed class AffiliateSettings
{
    /// <summary>Giá trị 1 điểm (VND) dùng quy đổi lần nạp điểm ra tiền. 0 = chưa cấu hình, chưa tính hoa hồng nạp.</summary>
    public decimal VndPerPoint { get; set; }
    public decimal DefaultTopupCommissionPercent { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Hoa hồng phát sinh từ một lần nạp điểm của khách được giới thiệu (affiliate.topup_commissions).</summary>
public sealed class AffiliateTopupCommissionView
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string AgentCode { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime TopupAt { get; set; }
    public decimal Points { get; set; }
    public decimal VndPerPoint { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal CommissionPercent { get; set; }
    public decimal CommissionAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public string Status { get; set; } = AffiliateCommissionStatuses.Pending;
    public Guid? PayoutId { get; set; }
    public DateTime? PaidAt { get; set; }
}

/// <summary>Một lần TodoX thanh toán hoa hồng cho đại lý (affiliate.payouts).</summary>
public sealed class AffiliatePayoutView
{
    public Guid Id { get; set; }
    public Guid AgentId { get; set; }
    public string AgentCode { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "VND";
    public int ItemCount { get; set; }
    public DateTime PaidAt { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankReference { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "paid";
    public DateTime CreatedAt { get; set; }
}

/// <summary>Số hoa hồng đại lý chưa được thanh toán (gói + nạp điểm).</summary>
public sealed record AffiliatePendingSummary(int PackageCount, decimal PackageAmount, int TopupCount, decimal TopupAmount)
{
    public int Count => PackageCount + TopupCount;
    public decimal Amount => PackageAmount + TopupAmount;
}
