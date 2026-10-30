namespace TodoX.Web.Models;

public static class AdminJobMonitorKinds
{
    public const string RVideo = "rvideo";
    public const string RDance = "rdance";
    public const string Timelapse = "timelapse";
}

public static class AdminJobMonitorStatuses
{
    public const string Draft = "draft";
    public const string Queued = "queued";
    public const string Processing = "processing";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class AdminJobMonitorQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? Search { get; set; }
    public string? Kind { get; set; }
    public string? Status { get; set; }
}

public sealed class AdminJobMonitorPage
{
    public IReadOnlyList<AdminJobMonitorJob> Items { get; init; } = Array.Empty<AdminJobMonitorJob>();
    public long TotalCount { get; init; }
    public long UnfinishedCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
}

public sealed class AdminJobMonitorJob
{
    public string Kind { get; init; } = string.Empty;
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? ThumbnailUrl { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public decimal Credits { get; init; }
    public string Status { get; init; } = string.Empty;
    public int SceneCount { get; init; }
    public string? ResultVideoUrl { get; init; }
}

public sealed class AdminJobMonitorScene
{
    public string? ImageUrl { get; init; }
    public string? VideoUrl { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }

    /// <summary>Null when the job is billed as a whole and the scene has no credit of its own (Timelapse).</summary>
    public decimal? Credits { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
}
