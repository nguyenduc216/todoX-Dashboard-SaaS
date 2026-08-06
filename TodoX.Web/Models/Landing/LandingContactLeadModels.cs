namespace TodoX.Web.Models.Landing;

public static class LandingContactPermissions
{
    public const string View = "landing_contacts.view";
    public const string Update = "landing_contacts.update";
    public const string Assign = "landing_contacts.assign";
    public const string Delete = "landing_contacts.delete";
    public const string Export = "landing_contacts.export";
}

public static class LandingLeadStatuses
{
    public static readonly string[] All =
    [
        "new", "contacted", "consulting", "quotation_sent", "qualified", "converted", "not_suitable", "closed"
    ];

    public static string Label(string value) => value switch
    {
        "new" => "Mới",
        "contacted" => "Đã liên hệ",
        "consulting" => "Đang tư vấn",
        "quotation_sent" => "Đã gửi báo giá",
        "qualified" => "Tiềm năng",
        "converted" => "Đã chuyển đổi",
        "not_suitable" => "Không phù hợp",
        "closed" => "Đã đóng",
        _ => value
    };
}

public static class LandingLeadPriorities
{
    public static readonly string[] All = ["low", "normal", "high", "urgent"];

    public static string Label(string value) => value switch
    {
        "low" => "Thấp",
        "normal" => "Bình thường",
        "high" => "Cao",
        "urgent" => "Khẩn",
        _ => value
    };
}

public sealed class LandingContactLeadFilter
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedUserId { get; set; }
    public string? Industry { get; set; }
    public string? Need { get; set; }
    public string? Utm { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public bool IncludeDeleted { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class LandingContactLeadListItem
{
    public Guid Id { get; set; }
    public string LeadCode { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? CompanyName { get; set; }
    public string? IndustryCode { get; set; }
    public string? InterestedProduct { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmCampaign { get; set; }
    public string Status { get; set; } = "new";
    public string Priority { get; set; } = "normal";
    public Guid? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public DateTimeOffset? NextFollowUpAt { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class LandingContactLeadDetail : LandingContactLeadListItem
{
    public string? Message { get; set; }
    public string? SourceUrl { get; set; }
    public string? ReferrerUrl { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmContent { get; set; }
    public string? UtmTerm { get; set; }
    public bool ConsentAccepted { get; set; }
    public DateTimeOffset? ConsentAt { get; set; }
    public string? InternalNote { get; set; }
    public DateTimeOffset? FirstContactedAt { get; set; }
    public DateTimeOffset? ConvertedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
}

public sealed class LandingContactLeadActivity
{
    public Guid Id { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? OldStatus { get; set; }
    public string? NewStatus { get; set; }
    public DateTimeOffset ActivityAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
}

public sealed class LandingContactLeadSummary
{
    public long NewCount { get; set; }
    public long ConsultingCount { get; set; }
    public long FollowUpTodayCount { get; set; }
    public long ConvertedCount { get; set; }
}

public sealed class LandingContactLeadPage
{
    public IReadOnlyList<LandingContactLeadListItem> Items { get; init; } = Array.Empty<LandingContactLeadListItem>();
    public long Total { get; init; }
}

public sealed class LandingStaffOption
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class LandingLeadActionRequest
{
    public string? Status { get; set; }
    public string? Priority { get; set; }
    public Guid? AssignedUserId { get; set; }
    public DateTimeOffset? NextFollowUpAt { get; set; }
    public string? Note { get; set; }
    public string ActivityType { get; set; } = "note";
}

public sealed class LandingContactLeadDetailResult
{
    public LandingContactLeadDetail? Lead { get; init; }
    public IReadOnlyList<LandingContactLeadActivity> Activities { get; init; } = Array.Empty<LandingContactLeadActivity>();
}

public sealed class LandingContactSchemaUnavailableException : Exception
{
    public LandingContactSchemaUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
