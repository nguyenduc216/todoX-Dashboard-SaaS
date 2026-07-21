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
    public const string Draft = "draft";
    public const string Queued = "queued";
    public const string Submitted = "submitted";
    public const string Rendering = "rendering";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
}

public static class DanceSellMotionSourceTypes
{
    public const string Upload = "upload";
    public const string TikTok = "tiktok";
}

public static class DanceSellReferenceStatuses
{
    public const string NotCreated = "not_created";
    public const string Generating = "generating";
    public const string Ready = "ready";
    public const string Approved = "approved";
    public const string Failed = "failed";
}

public static class DanceSellSourceStageStatuses
{
    public const string Pending = "pending";
    public const string Resolving = "resolving";
    public const string Downloading = "downloading";
    public const string Staging = "staging";
    public const string Ready = "ready";
    public const string Failed = "failed";
}

public static class DanceSellPlacementModes
{
    public const string HoldProduct = "HOLD_PRODUCT";
    public const string WearProduct = "WEAR_PRODUCT";
    public const string DisplayProduct = "DISPLAY_PRODUCT";
    public const string UseProduct = "USE_PRODUCT";
    public const string Custom = "CUSTOM";

    public static IReadOnlyList<string> All { get; } =
        new[] { HoldProduct, WearProduct, DisplayProduct, UseProduct, Custom };
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

public sealed class DanceSellDraftCreateRequest
{
    public Guid? TenantId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
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
    public string? Title { get; set; }
    public Guid? CharacterMediaId { get; set; }
    public string? CharacterObjectKey { get; set; }
    public Guid? ProductMediaId { get; set; }
    public string? ProductObjectKey { get; set; }
    public string? ProductImageUrl { get; set; }
    public string? MotionSourceType { get; set; }
    public string? MotionSourceUrl { get; set; }
    public Guid? MotionVideoMediaId { get; set; }
    public string? MotionVideoObjectKey { get; set; }
    public string? PlacementMode { get; set; }
    public string? CustomPlacementInstruction { get; set; }
    public Guid? PreparedReferenceMediaId { get; set; }
    public string? PreparedReferenceObjectKey { get; set; }
    public string? PreparedReferenceUrl { get; set; }
    public string PreparedReferenceStatus { get; set; } = DanceSellReferenceStatuses.NotCreated;
    public DateTime? PreparedReferenceApprovedAt { get; set; }
    public string SourceStageStatus { get; set; } = DanceSellSourceStageStatuses.Pending;
    public string? SourceStageError { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
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

public sealed class DanceSellReferenceVersionDto
{
    public Guid Id { get; set; }
    public Guid DanceSellJobId { get; set; }
    public int VersionNo { get; set; }
    public Guid? CharacterMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomInstruction { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? ProviderCode { get; set; }
    public string? ProviderModel { get; set; }
    public string RequestJson { get; set; } = "{}";
    public string? ResponseJson { get; set; }
    public string? ErrorJson { get; set; }
    public Guid? MediaId { get; set; }
    public string? ObjectKey { get; set; }
    public string? PublicUrl { get; set; }
    public string Status { get; set; } = DanceSellReferenceStatuses.Generating;
    public bool IsSelected { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class DanceSellCreateJobRequest
{
    public string? Title { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
}

public sealed class DanceSellUpdateBusinessRequest
{
    public string? Title { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
}

public sealed class DanceSellTikTokStageRequest
{
    public string Url { get; set; } = string.Empty;
}

public sealed class DanceSellJsonBusinessRequest
{
    public string? Title { get; set; }
    public Guid? CharacterMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public string MotionSourceType { get; set; } = DanceSellMotionSourceTypes.Upload;
    public Guid? MotionVideoMediaId { get; set; }
    public string? TiktokUrl { get; set; }
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
}

public sealed record DanceSellCapabilityDto(
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> CharacterOrientations,
    string DefaultMode,
    string DefaultCharacterOrientation);
