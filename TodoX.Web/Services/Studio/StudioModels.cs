namespace TodoX.Web.Services.Studio;

/// <summary>Loại khách hàng (studio.customer_profiles.segment).</summary>
public static class CustomerSegments
{
    public const string Chasing = "bam_duoi";
    public const string Regular = "thong_thuong";
    public const string Vip = "vip";

    public static readonly IReadOnlyList<string> All = [Chasing, Regular, Vip];

    public static string Normalize(string? segment)
        => All.FirstOrDefault(x => string.Equals(x, segment?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Regular;

    public static string Label(string? segment) => Normalize(segment) switch
    {
        Chasing => "Bám đuổi",
        Vip => "VIP",
        _ => "Thông thường"
    };
}

/// <summary>
/// Số video đã làm cho một khách: video đã bàn giao trong "Video khách hàng" cộng kế hoạch đã release,
/// và số kế hoạch đang làm.
/// </summary>
public sealed record CustomerVideoCounts(int Delivered, int Released, int InProgress)
{
    public int Done => Delivered + Released;
}

/// <summary>Hồ sơ nhân viên TodoX kèm tài khoản đăng nhập dashboard.</summary>
public sealed class StaffMember
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? EmployeeCode { get; set; }
    public string? Position { get; set; }
    public string? Department { get; set; }
    public DateTime? JoinedOn { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Kế hoạch video chưa release / chưa hủy đang giao cho nhân viên.</summary>
    public int OpenPlans { get; set; }
}

/// <summary>Người có thể nhận làm một kế hoạch video (nhân viên hoặc quản trị viên).</summary>
public sealed record PlanAssignee(Guid UserId, string Name, bool IsStaff);

public static class VideoPlanStatuses
{
    public const string Planning = "planning";
    public const string Scripting = "scripting";
    public const string Producing = "producing";
    public const string Reviewing = "reviewing";
    public const string Released = "released";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Planning, Scripting, Producing, Reviewing, Released, Cancelled];

    public static bool IsOpen(string? status) => status is not (Released or Cancelled);

    public static string Label(string? status) => status switch
    {
        Scripting => "Viết kịch bản",
        Producing => "Đang sản xuất",
        Reviewing => "Khách duyệt",
        Released => "Đã release",
        Cancelled => "Đã hủy",
        _ => "Lên kế hoạch"
    };
}

public static class VideoPlanVersionStatuses
{
    public const string Draft = "draft";
    public const string Sent = "sent";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly IReadOnlyList<string> All = [Draft, Sent, Approved, Rejected];

    public static string Label(string? status) => status switch
    {
        Sent => "Đã gửi khách",
        Approved => "Khách duyệt",
        Rejected => "Khách yêu cầu sửa",
        _ => "Bản nháp"
    };
}

public sealed class VideoPlan
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerCode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public string? Script { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public string Status { get; set; } = VideoPlanStatuses.Planning;
    public int VersionCount { get; set; }
    public int NoteCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class VideoPlanVersion
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public int VersionNo { get; set; }
    public string? VideoUrl { get; set; }
    public string? ChangeNote { get; set; }
    public string Status { get; set; } = VideoPlanVersionStatuses.Draft;
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}

public sealed class VideoPlanNote
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? CreatedByName { get; set; }
}

/// <summary>Pure validation (unit-tested).</summary>
public static class VideoPlanRules
{
    public const int MaxTitleLength = 300;

    public static string? Validate(VideoPlan plan)
    {
        if (plan.CustomerId == Guid.Empty) return "Vui lòng chọn khách hàng.";
        if (string.IsNullOrWhiteSpace(plan.Title)) return "Vui lòng nhập nội dung video.";
        if (plan.Title.Trim().Length > MaxTitleLength) return $"Nội dung video tối đa {MaxTitleLength} ký tự.";
        if (!VideoPlanStatuses.All.Contains(plan.Status)) return "Trạng thái không hợp lệ.";
        return null;
    }

    public static string? ValidateVersion(VideoPlanVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.VideoUrl) && string.IsNullOrWhiteSpace(version.ChangeNote))
            return "Nhập link video hoặc ghi chú thay đổi của phiên bản.";
        if (!string.IsNullOrWhiteSpace(version.VideoUrl)
            && !(Uri.TryCreate(version.VideoUrl.Trim(), UriKind.Absolute, out var uri)
                 && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            return "Link video phải bắt đầu bằng http:// hoặc https://";
        if (!VideoPlanVersionStatuses.All.Contains(version.Status)) return "Trạng thái phiên bản không hợp lệ.";
        return null;
    }

    /// <summary>Ngày release đã qua mà video chưa release / chưa hủy.</summary>
    public static bool IsOverdue(VideoPlan plan, DateTime today)
        => plan.ReleaseDate is { } date && date.Date < today.Date && VideoPlanStatuses.IsOpen(plan.Status);
}
