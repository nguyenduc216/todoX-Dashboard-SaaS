using TodoX.Web.Models;

namespace TodoX.Web.Services.Studio;

/// <summary>Stat cards on the customer list; clicking one filters the list.</summary>
public enum CustomerCardFilter
{
    All,
    Active,
    Pending,
    Locked,
    /// <summary>"Khách cần làm video": a chasing (bám đuổi) customer that has no video yet.</summary>
    NeedVideo
}

public enum CustomerVideoFilter
{
    Any,
    None,
    HasVideo,
    InProgress
}

public enum CustomerAgentFilter
{
    Any,
    IsAgent,
    Referred
}

/// <summary>"Tìm kiếm chi tiết" on the customer list. Every criterion left empty matches everything.</summary>
public sealed class CustomerSearchFilter
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Contact { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public HashSet<string> Segments { get; set; } = new();
    public TodoXAccountStatus? Status { get; set; }
    public CustomerVideoFilter Video { get; set; } = CustomerVideoFilter.Any;
    public CustomerAgentFilter Agent { get; set; } = CustomerAgentFilter.Any;
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }

    public CustomerSearchFilter Clone() => new()
    {
        Name = Name, Code = Code, Contact = Contact, Email = Email, Phone = Phone,
        Segments = new HashSet<string>(Segments), Status = Status, Video = Video, Agent = Agent,
        CreatedFrom = CreatedFrom, CreatedTo = CreatedTo
    };

    public bool IsEmpty => Describe().Count == 0;

    /// <summary>A chasing customer with no delivered video and no released plan.</summary>
    public static bool NeedsVideo(CustomerProfile customer, CustomerVideoCounts counts)
        => CustomerSegments.Normalize(customer.Segment) == CustomerSegments.Chasing && counts.Done == 0;

    public static bool MatchesCard(CustomerCardFilter card, CustomerProfile customer, CustomerVideoCounts counts) => card switch
    {
        CustomerCardFilter.Active => customer.Status == TodoXAccountStatus.Active,
        CustomerCardFilter.Pending => customer.Status == TodoXAccountStatus.Pending,
        CustomerCardFilter.Locked => customer.Status == TodoXAccountStatus.Locked,
        CustomerCardFilter.NeedVideo => NeedsVideo(customer, counts),
        _ => true
    };

    public bool Matches(CustomerProfile c, CustomerVideoCounts counts)
    {
        if (!Has(c.CompanyName, Name)) return false;
        if (!Has(c.CustomerCode, Code)) return false;
        if (!Has(c.ContactName, Contact)) return false;
        if (!Has(c.Email, Email)) return false;
        if (!string.IsNullOrWhiteSpace(Phone) && !Digits(c.Phone).Contains(Digits(Phone))) return false;
        if (Segments.Count > 0 && !Segments.Contains(CustomerSegments.Normalize(c.Segment))) return false;
        if (Status is { } status && c.Status != status) return false;
        switch (Video)
        {
            case CustomerVideoFilter.None when counts.Done > 0:
            case CustomerVideoFilter.HasVideo when counts.Done == 0:
            case CustomerVideoFilter.InProgress when counts.InProgress == 0:
                return false;
        }
        if (Agent == CustomerAgentFilter.IsAgent && !c.IsAgent) return false;
        if (Agent == CustomerAgentFilter.Referred && string.IsNullOrWhiteSpace(c.ReferralCode)) return false;
        if (CreatedFrom is { } from && c.CreatedAt.Date < from.Date) return false;
        if (CreatedTo is { } to && c.CreatedAt.Date > to.Date) return false;
        return true;
    }

    /// <summary>Labels of the active criteria, shown as chips above the list.</summary>
    public IReadOnlyList<string> Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Name)) parts.Add($"Tên: {Name.Trim()}");
        if (!string.IsNullOrWhiteSpace(Code)) parts.Add($"Mã: {Code.Trim()}");
        if (!string.IsNullOrWhiteSpace(Contact)) parts.Add($"Người liên hệ: {Contact.Trim()}");
        if (!string.IsNullOrWhiteSpace(Email)) parts.Add($"Email: {Email.Trim()}");
        if (!string.IsNullOrWhiteSpace(Phone)) parts.Add($"SĐT: {Phone.Trim()}");
        if (Segments.Count > 0) parts.Add("Loại: " + string.Join(", ", CustomerSegments.All.Where(Segments.Contains).Select(CustomerSegments.Label)));
        if (Status is { } status) parts.Add("Trạng thái: " + TodoXLabels.Status(status));
        if (Video != CustomerVideoFilter.Any) parts.Add(Video switch
        {
            CustomerVideoFilter.None => "Chưa có video",
            CustomerVideoFilter.HasVideo => "Đã có video",
            _ => "Có video đang làm"
        });
        if (Agent == CustomerAgentFilter.IsAgent) parts.Add("Là đại lý");
        if (Agent == CustomerAgentFilter.Referred) parts.Add("Có đại lý giới thiệu");
        if (CreatedFrom is { } from) parts.Add($"Tạo từ {from:dd/MM/yyyy}");
        if (CreatedTo is { } to) parts.Add($"Tạo đến {to:dd/MM/yyyy}");
        return parts;
    }

    private static bool Has(string? value, string? query)
        => string.IsNullOrWhiteSpace(query) || (value ?? string.Empty).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsDigit).ToArray());
}
