namespace TodoX.Web.Models;

public sealed class AdminJobMonitorQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Search { get; set; }
    public string? Service { get; set; }
    public string? Status { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string Sort { get; set; } = "newest";
}

public sealed class AdminJobMonitorPage
{
    public IReadOnlyList<AdminJobMonitorJobSummary> Items { get; init; } = Array.Empty<AdminJobMonitorJobSummary>();
    public AdminJobMonitorStats Stats { get; init; } = new();
    public AdminJobMonitorQuery Query { get; init; } = new();
    public long TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed class AdminJobMonitorStats
{
    public long Total { get; init; }
    public long Processing { get; init; }
    public long Completed { get; init; }
    public long Failed { get; init; }
}

public sealed class AdminJobMonitorFilterOptions
{
    public IReadOnlyList<AdminJobMonitorFilterOption> Services { get; init; } = Array.Empty<AdminJobMonitorFilterOption>();
    public IReadOnlyList<AdminJobMonitorFilterOption> Statuses { get; init; } = Array.Empty<AdminJobMonitorFilterOption>();
}

public sealed class AdminJobMonitorFilterOption
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class AdminJobMonitorJobSummary
{
    public Guid Id { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? UserId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public string AccountEmail { get; init; } = string.Empty;
    public string JobType { get; init; } = string.Empty;
    public string ServiceCode { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CurrentStage { get; init; } = string.Empty;
    public int ProgressPercent { get; init; }
    public string? ThumbnailUrl { get; init; }
    public string? VideoUrl { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public decimal EstimatedPoints { get; init; }
    public decimal ConsumedPoints { get; init; }
    public string? ProviderCode { get; init; }
    public string? ModelCode { get; init; }
}

public sealed class AdminJobMonitorDetail
{
    public AdminJobMonitorOverview Overview { get; init; } = new();
    public IReadOnlyList<AdminJobMonitorTimelineItem> Timeline { get; init; } = Array.Empty<AdminJobMonitorTimelineItem>();
    public AdminJobMonitorInputOutput InputOutput { get; init; } = new();
    public IReadOnlyList<AdminJobMonitorLogItem> Logs { get; init; } = Array.Empty<AdminJobMonitorLogItem>();
}

public sealed class AdminJobMonitorOverview
{
    public AdminJobMonitorJobSummary Summary { get; init; } = new();
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? AspectRatio { get; init; }
    public int? DurationSeconds { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public DateTime? CancelledAt { get; init; }
    public string PointStatus { get; init; } = string.Empty;
    public string? RetryOfJobId { get; init; }
}

public sealed class AdminJobMonitorInputOutput
{
    public string InputJson { get; init; } = "{}";
    public string PromptJson { get; init; } = "{}";
    public string ReferenceJson { get; init; } = "[]";
    public string OptionsJson { get; init; } = "{}";
    public string OutputJson { get; init; } = "{}";
    public IReadOnlyList<AdminJobMonitorMediaLink> InputLinks { get; init; } = Array.Empty<AdminJobMonitorMediaLink>();
    public IReadOnlyList<AdminJobMonitorMediaLink> OutputLinks { get; init; } = Array.Empty<AdminJobMonitorMediaLink>();
}

public sealed class AdminJobMonitorMediaLink
{
    public string Url { get; init; } = string.Empty;
    public string Kind { get; init; } = "url";
    public string? Source { get; init; }
}

public sealed class AdminJobMonitorTimelineItem
{
    public string Stage { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string Level { get; init; } = "info";
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
    public DateTime Timestamp { get; init; }
    public TimeSpan? Duration { get; init; }
    public string? ProviderCode { get; init; }
    public string? ModelCode { get; init; }
}

public sealed class AdminJobMonitorLogItem
{
    public DateTime Timestamp { get; init; }
    public string Service { get; init; } = string.Empty;
    public string? ProviderCode { get; init; }
    public string? ModelCode { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Level { get; init; } = "info";
    public string? Message { get; init; }
    public string DataJson { get; init; } = "{}";
}
