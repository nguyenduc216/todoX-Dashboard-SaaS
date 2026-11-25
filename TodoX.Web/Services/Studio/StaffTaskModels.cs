namespace TodoX.Web.Services.Studio;

/// <summary>A task of a TodoX staff member (studio.staff_tasks). Times are Vietnam local time in the app, UTC in the DB.</summary>
public sealed class StaffTask
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Note { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string? AssigneeName { get; set; }
    /// <summary>Người giao việc (mặc định người tạo).</summary>
    public Guid? AssignerUserId { get; set; }
    public string? AssignerName { get; set; }
    public DateTime? StartAt { get; set; }
    /// <summary>Thời gian dự kiến hoàn thành (hạn). "Trễ hạn" reads this.</summary>
    public DateTime? DueAt { get; set; }
    /// <summary>Thời gian hoàn thành thực tế.</summary>
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = StaffTaskStatuses.New;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>"Sản phẩm": library videos this task produced (studio.staff_task_products).</summary>
    public List<StaffTaskProduct> Products { get; set; } = [];
    /// <summary>Number of entries in the task's feedback history (studio.staff_task_comments).</summary>
    public int CommentCount { get; set; }
    /// <summary>Newest written comment, shown under the title in the list.</summary>
    public string? LastComment { get; set; }
    public string? LastCommentBy { get; set; }
    public DateTime? LastCommentAt { get; set; }
}

/// <summary>One entry of a task's feedback history: a written comment, a status change, or the task's creation.</summary>
public sealed record StaffTaskComment(Guid Id, string Kind, string Body, string? OldStatus, string? NewStatus, DateTime CreatedAt, string? AuthorName)
{
    public const string KindComment = "comment";
    public const string KindStatus = "status";
    public const string KindCreated = "created";
    public const int MaxLength = 4000;

    public bool IsComment => Kind == KindComment;
}

/// <summary>A video from the uploaded video library (Video khách hàng / Kho mẫu video), as a task's product or a library choice.</summary>
public sealed record StaffTaskProduct(string VideoUrl, string? Title, string? Source, string? PosterUrl)
{
    public DateTime? UploadedAt { get; init; }

    /// <summary>Title of the customer video or sample that uses the file, else the file name.</summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Path.GetFileName(VideoUrl) : Title!;
}

public static class StaffTaskStatuses
{
    public const string New = "new";
    /// <summary>Đã bỏ (gộp vào "Mới"); chỉ còn để đọc dữ liệu cũ.</summary>
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [New, InProgress, Done, Cancelled];

    /// <summary>"Chưa hoàn thành": mới, đang thực hiện (hủy is neither done nor open).</summary>
    public static bool IsOpen(string? status) => status is New or Pending or InProgress;

    public static string Normalize(string? status) => All.Contains(status ?? string.Empty) ? status! : New;

    public static string Label(string? status) => Normalize(status) switch
    {
        InProgress => "Đang thực hiện",
        Done => "Đã hoàn thành",
        Cancelled => "Hủy",
        _ => "Mới"
    };
}

/// <summary>
/// Counts for the summary cards and filters. "Trễ hạn" is automatic (not stored): an unfinished task past its
/// completion time counts as late only, not also as Mới / Đang thực hiện.
/// </summary>
public sealed record StaffTaskSummary(IReadOnlyDictionary<string, int> ByStatus, int Late = 0)
{
    public int Count(string status) => ByStatus.TryGetValue(status, out var n) ? n : 0;
    public int Done => Count(StaffTaskStatuses.Done);
    public int Open => StaffTaskStatuses.All.Where(StaffTaskStatuses.IsOpen).Sum(Count) + Late;
    public int Total => ByStatus.Values.Sum() + Late;

    public static StaffTaskSummary From(IEnumerable<StaffTask> tasks, DateTime? now = null)
    {
        var list = tasks.ToList();
        var late = now is { } at ? list.Where(t => StaffTaskRules.IsLate(t, at)).ToHashSet() : [];
        var rest = list.Where(t => !late.Contains(t)).ToList();
        return new(StaffTaskStatuses.All.ToDictionary(s => s, s => rest.Count(t => StaffTaskStatuses.Normalize(t.Status) == s)), late.Count);
    }
}

public static class StaffTaskRules
{
    public const int MaxTitleLength = 300;

    public static string? Validate(StaffTask task)
    {
        if (string.IsNullOrWhiteSpace(task.Title)) return "Nhập tiêu đề công việc.";
        if (task.Title.Trim().Length > MaxTitleLength) return $"Tiêu đề tối đa {MaxTitleLength} ký tự.";
        if (!StaffTaskStatuses.All.Contains(task.Status)) return "Trạng thái không hợp lệ.";
        if (task.StartAt is { } start && task.CompletedAt is { } end && end < start)
            return "Ngày giờ hoàn thành phải sau ngày giờ thực hiện.";
        if (task.StartAt is { } begin && task.DueAt is { } due && due < begin)
            return "Hạn hoàn thành phải sau ngày giờ thực hiện.";
        return null;
    }

    public static string? ValidateComment(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Nhập nội dung phản hồi.";
        if (body.Trim().Length > StaffTaskComment.MaxLength) return $"Phản hồi tối đa {StaffTaskComment.MaxLength} ký tự.";
        return null;
    }

    /// <summary>History line written when a task's status changes.</summary>
    public static string StatusChangeText(string? from, string to)
        => $"Đổi trạng thái: {StaffTaskStatuses.Label(from)} → {StaffTaskStatuses.Label(to)}";

    /// <summary>
    /// Marking a task done without a completion time stamps it now; a cancelled task keeps what was entered; an
    /// unfinished task has no completion time (its plan is the due time, DueAt).
    /// </summary>
    public static DateTime? CompletedAtFor(string status, DateTime? completedAt, DateTime now)
        => StaffTaskStatuses.IsOpen(status) ? null
            : status == StaffTaskStatuses.Done && completedAt is null ? now : completedAt;

    /// <summary>Products are files in this server's media folder only (the library), never outside links.</summary>
    public static bool IsLibraryUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) && url.StartsWith("/media/", StringComparison.Ordinal) && !url.Contains("..");

    /// <summary>Open task whose due time (thời gian dự kiến hoàn thành) has passed.</summary>
    public static bool IsLate(StaffTask task, DateTime now)
        => StaffTaskStatuses.IsOpen(task.Status) && task.DueAt is { } due && due < now;
}

/// <summary>Vietnam time (UTC+7, no DST) for task times, independent of the server's time zone.</summary>
public static class VietnamTime
{
    private static readonly TimeZoneInfo Zone = Find();

    private static TimeZoneInfo Find()
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh", "Asia/Bangkok" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+7", TimeSpan.FromHours(7), "UTC+7", "UTC+7");
    }

    public static DateTime Now => FromUtc(DateTime.UtcNow);

    public static DateTime FromUtc(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime? FromUtc(DateTime? utc) => utc is { } value ? FromUtc(value) : null;

    public static DateTime? ToUtc(DateTime? local)
        => local is { } value ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), Zone) : null;
}
