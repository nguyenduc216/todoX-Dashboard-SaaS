using System.Globalization;
using System.Text;

namespace TodoX.Web.Services.CustomerJobs;

/// <summary>
/// Search on "Jobs của tôi": title (accents optional), service, created date range (local days, inclusive) and status.
/// </summary>
public sealed class MyJobsFilter
{
    public string? Text { get; set; }
    public string? Service { get; set; }
    public string? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public bool IsActive => !string.IsNullOrWhiteSpace(Text) || !string.IsNullOrWhiteSpace(Service)
                            || !string.IsNullOrWhiteSpace(Status) || From is not null || To is not null;

    public void Clear() => (Text, Service, Status, From, To) = (null, null, null, null, null);

    /// <summary>Start of the "from" day in UTC; rows older than this can never match.</summary>
    public DateTime? FromUtc => From is { } from ? DateTime.SpecifyKind(from.Date, DateTimeKind.Local).ToUniversalTime() : null;

    public bool Matches(string? title, string? service, string? statusGroup, DateTime createdAtUtc)
    {
        if (!string.IsNullOrWhiteSpace(Text) && !Fold(title).Contains(Fold(Text), StringComparison.Ordinal)) return false;
        if (!string.IsNullOrWhiteSpace(Service) && !string.Equals(service?.Trim(), Service.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(Status) && !string.Equals(statusGroup, Status, StringComparison.Ordinal)) return false;
        var day = (createdAtUtc.Kind == DateTimeKind.Local ? createdAtUtc : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc).ToLocalTime()).Date;
        if (From is { } from && day < from.Date) return false;
        if (To is { } to && day > to.Date) return false;
        return true;
    }

    /// <summary>Lower case without Vietnamese accents, so "di lam" finds "Đi Làm".</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Trim().Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) builder.Append(ch);
        return System.Text.RegularExpressions.Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"\s+", " ");
    }
}

/// <summary>The few statuses a customer filters by; every job's own label maps to one of them.</summary>
public static class MyJobsStatus
{
    public const string Draft = "Nháp";
    public const string Running = "Đang xử lý";
    public const string Completed = "Hoàn tất";
    public const string Failed = "Thất bại";
    public const string Cancelled = "Đã hủy";

    public static readonly IReadOnlyList<string> All = [Draft, Running, Completed, Failed, Cancelled];

    public static string Group(string? label)
    {
        var text = MyJobsFilter.Fold(label);
        if (text.Length == 0 || text.Contains("nhap") || text.Contains("draft") || text.Contains("chua bat dau")) return Draft;
        if (text.Contains("huy") || text.Contains("da dung") || text.Contains("cancel")) return Cancelled;
        if (text.Contains("that bai") || text.Contains("loi") || text.Contains("qua thoi gian") || text.Contains("fail") || text.Contains("timeout")) return Failed;
        if (text.Contains("hoan tat") || text.Contains("hoan thanh") || text == "completed") return Completed;
        return Running;
    }
}
