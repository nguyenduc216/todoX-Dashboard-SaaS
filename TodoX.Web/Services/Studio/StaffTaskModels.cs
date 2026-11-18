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
    public DateTime? StartAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = StaffTaskStatuses.New;
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class StaffTaskStatuses
{
    public const string New = "new";
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [New, Pending, InProgress, Done, Cancelled];

    /// <summary>"Chưa hoàn thành": mới, chờ thực hiện, đang thực hiện (hủy is neither done nor open).</summary>
    public static bool IsOpen(string? status) => status is New or Pending or InProgress;

    public static string Normalize(string? status) => All.Contains(status ?? string.Empty) ? status! : New;

    public static string Label(string? status) => Normalize(status) switch
    {
        Pending => "Chờ thực hiện",
        InProgress => "Đang thực hiện",
        Done => "Đã hoàn thành",
        Cancelled => "Hủy",
        _ => "Mới"
    };
}

/// <summary>Counts for the summary cards: per status, plus done / not done.</summary>
public sealed record StaffTaskSummary(IReadOnlyDictionary<string, int> ByStatus)
{
    public int Count(string status) => ByStatus.TryGetValue(status, out var n) ? n : 0;
    public int Done => Count(StaffTaskStatuses.Done);
    public int Open => StaffTaskStatuses.All.Where(StaffTaskStatuses.IsOpen).Sum(Count);
    public int Total => ByStatus.Values.Sum();

    public static StaffTaskSummary From(IEnumerable<StaffTask> tasks)
        => new(StaffTaskStatuses.All.ToDictionary(s => s, s => tasks.Count(t => StaffTaskStatuses.Normalize(t.Status) == s)));
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
        return null;
    }

    /// <summary>Marking a task done without a completion time stamps it now; other statuses keep what was entered.</summary>
    public static DateTime? CompletedAtFor(string status, DateTime? completedAt, DateTime now)
        => status == StaffTaskStatuses.Done && completedAt is null ? now : completedAt;

    /// <summary>Open task whose planned completion time has passed.</summary>
    public static bool IsLate(StaffTask task, DateTime now)
        => StaffTaskStatuses.IsOpen(task.Status) && task.CompletedAt is { } due && due < now;
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
