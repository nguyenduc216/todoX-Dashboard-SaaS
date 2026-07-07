namespace TodoX.Web.Models;

public sealed class ReupCampaignOptions
{
    public string? cache_video { get; set; }
    public string? CacheVideoPath { get; set; }
    public int MaxConcurrentPublishTasks { get; set; } = 2;
    public int MaxConcurrentTasksPerPage { get; set; } = 1;
    public int TaskTimeoutMinutes { get; set; } = 10;
    public int AutoRetryCount { get; set; } = 1;
    public int WorkerPollSeconds { get; set; } = 5;
    public bool WorkerEnabled { get; set; } = true;
    public string TikwmEndpoint { get; set; } = "https://www.tikwm.com/api/";
    public string FacebookGraphVersion { get; set; } = "v23.0";
}

public class CreateReupCampaignRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Caption { get; set; }
    public string? Hashtags { get; set; }
    public List<Guid> ReferenceVideoIds { get; set; } = new();
    public List<Guid> SocialPageIds { get; set; } = new();
}

public sealed class UpdateReupCampaignRequest : CreateReupCampaignRequest;

public sealed class ReupDuplicateCheckRequest
{
    public List<Guid> ReferenceVideoIds { get; set; } = new();
    public List<Guid> SocialPageIds { get; set; } = new();
}

public sealed class ReupCampaignDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Caption { get; set; }
    public string? Hashtags { get; set; }
    public string Status { get; set; } = "draft";
    public int VideoCount { get; set; }
    public int PageCount { get; set; }
    public int TotalTasks { get; set; }
    public int PendingTasks { get; set; }
    public int RunningTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public int CancelledTasks { get; set; }
    public int DuplicateWarningCount { get; set; }
    public bool StopRequested { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ReupReferenceVideoOption
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = "tiktok";
    public string SourceUrl { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? ChannelName { get; set; }
    public string? AuthorHandle { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AlreadyPostedCount { get; set; }
}

public sealed class ReupFacebookPageOption
{
    public Guid Id { get; set; }
    public string PageName { get; set; } = string.Empty;
    public string? PageUrl { get; set; }
    public string? ExternalPageId { get; set; }
    public string? AvatarUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string? TokenStatus { get; set; }
    public string? TokenHint { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string? LastValidationStatus { get; set; }
}

public sealed class ReupDuplicateWarningDto
{
    public Guid PreviousTaskId { get; set; }
    public Guid PreviousCampaignId { get; set; }
    public Guid ReferenceVideoId { get; set; }
    public Guid SocialPageId { get; set; }
    public string? FacebookVideoId { get; set; }
    public string? FacebookPostUrl { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Title { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string PageName { get; set; } = string.Empty;
}

public class ReupPublishTaskDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ReferenceVideoId { get; set; }
    public Guid SocialPageId { get; set; }
    public Guid? PageAccessTokenId { get; set; }
    public Guid? VideoAssetId { get; set; }
    public string Status { get; set; } = "pending";
    public bool DuplicateWarning { get; set; }
    public Guid? PreviousSuccessTaskId { get; set; }
    public string? CaptionUsed { get; set; }
    public string? HashtagsUsed { get; set; }
    public string? FacebookVideoId { get; set; }
    public string? FacebookPostUrl { get; set; }
    public string? TokenCheckStatus { get; set; }
    public string? TokenCheckError { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? VideoTitle { get; set; }
    public string? VideoSourceUrl { get; set; }
    public string? VideoThumbnailUrl { get; set; }
    public string? PageName { get; set; }
    public string? PageExternalId { get; set; }
}

public sealed class ReupPublishLogDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? CampaignId { get; set; }
    public Guid? TaskId { get; set; }
    public string Level { get; set; } = "info";
    public string Step { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Data { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ReupVideoAssetDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ReferenceVideoId { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string Platform { get; set; } = "tiktok";
    public string Provider { get; set; } = "tikwm";
    public string? ResolvedVideoUrl { get; set; }
    public string? LocalPath { get; set; }
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public string Status { get; set; } = "created";
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ReupTaskExecutionDto : ReupPublishTaskDto
{
    public string ReferenceSourceUrl { get; set; } = string.Empty;
    public string ReferencePlatform { get; set; } = "tiktok";
}

public sealed class ReupPageTokenDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid PageId { get; set; }
    public string TokenValue { get; set; } = string.Empty;
    public string? TokenHint { get; set; }
}

public sealed record TikwmResolveResult(string VideoUrl);

public sealed record FacebookTokenCheckResult(bool Ok, string? ErrorCode, string? ErrorMessage);

public sealed record FacebookPublishResult(bool Ok, string? FacebookVideoId, string? FacebookPostUrl, string? RawJson, string? ErrorCode, string? ErrorMessage);
