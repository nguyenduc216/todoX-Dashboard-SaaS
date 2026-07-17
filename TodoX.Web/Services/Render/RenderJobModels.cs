namespace TodoX.Web.Services.Render;

public static class RenderJobStatuses
{
    public const string Queued = "queued";
    public const string Preparing = "preparing";
    public const string Rendering = "rendering";
    public const string PostProcessing = "post_processing";
    public const string PendingReconciliation = "pending_reconciliation";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Processing = "processing";
}

public static class RenderPointStatuses
{
    public const string NotRequired = "not_required";
    public const string Pending = "pending";
    public const string Charged = "charged";
    public const string Insufficient = "insufficient";
    public const string Refunded = "refunded";
    public const string Cancelled = "cancelled";
}

public sealed class RenderJobCreateModel
{
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
    public object? Input { get; set; }
    public object? Prompt { get; set; }
    public object? References { get; set; }
    public string? LogCode { get; set; }
    public decimal PointCostEstimate { get; set; }
    public string PointStatus { get; set; } = RenderPointStatuses.NotRequired;
    public string? ProviderCode { get; set; }
    public string? ModelCode { get; set; }
    public int MaxAttempts { get; set; } = 1;
}

public sealed class RenderJobDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string Status { get; set; } = RenderJobStatuses.Queued;
    public int Priority { get; set; }
    public string? WorkerKey { get; set; }
    public string InputJson { get; set; } = "{}";
    public string PromptJson { get; set; } = "{}";
    public string ReferenceJson { get; set; } = "[]";
    public string OutputJson { get; set; } = "[]";
    public string? LogCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? CancelReason { get; set; }
    public Guid? RetryOfJobId { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? RetryAfter { get; set; }
    public decimal PointCostEstimate { get; set; }
    public decimal PointCostCharged { get; set; }
    public string PointStatus { get; set; } = RenderPointStatuses.NotRequired;
    public string? ProviderCode { get; set; }
    public string? ModelCode { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public static class RenderJobTypes
{
    public const string RenderVideoBatch = "render_video_job";
    public const string RenderSceneVideo = "render_scene_video";
    public const string MergeProjectVideo = "merge_video_job";
}

public sealed class RenderJobEventDto
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? TenantId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string? Message { get; set; }
    public string DataJson { get; set; } = "{}";
    public string? ProviderCode { get; set; }
    public string? ModelCode { get; set; }
    public DateTime CreatedAt { get; set; }
}
