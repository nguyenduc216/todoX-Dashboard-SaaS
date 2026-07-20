namespace TodoX.Web.Services.DanceSell;

public static class DanceSellConstants
{
    public const string ProviderCode = "kie";
    public const string CapabilityCode = "motion_control_video";
    public const string FeatureCode = "dance_sell";
    public const string Model = "kling-2.6/motion-control";
}

public static class DanceSellJobStatuses
{
    public const string Queued = "queued";
    public const string Submitted = "submitted";
    public const string Rendering = "rendering";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
}

public sealed class DanceSellJobCreateRequest
{
    public Guid? TenantId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RenderJobId { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string CharacterImageUrl { get; set; } = string.Empty;
    public string MotionVideoUrl { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string ProviderCode { get; set; } = DanceSellConstants.ProviderCode;
    public string ProviderModel { get; set; } = DanceSellConstants.Model;
}

public sealed class DanceSellJobDto
{
    public Guid Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? RenderJobId { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
    public string Status { get; set; } = DanceSellJobStatuses.Queued;
    public string Prompt { get; set; } = string.Empty;
    public string CharacterImageUrl { get; set; } = string.Empty;
    public string MotionVideoUrl { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string ProviderCode { get; set; } = DanceSellConstants.ProviderCode;
    public string ProviderModel { get; set; } = DanceSellConstants.Model;
    public string? ProviderTaskId { get; set; }
    public string? ProviderStatus { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string? SubmitResponseJson { get; set; }
    public string? PollResponseJson { get; set; }
    public string? CallbackJson { get; set; }
    public string? ErrorJson { get; set; }
    public string? ResultVideoUrl { get; set; }
    public int PollCount { get; set; }
    public DateTime? NextPollAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? LastPolledAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class DanceSellAdminTestRequest
{
    public string Prompt { get; set; } = "The character is dancing naturally.";
    public string ImageUrl { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
}

public sealed class DanceSellRenderInput
{
    public Guid DanceSellJobId { get; set; }
    public string LogicalRequestId { get; set; } = string.Empty;
}
