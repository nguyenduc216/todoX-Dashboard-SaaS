namespace TodoX.Web.Services.DanceSell;

public static class DanceSellConstants
{
    public const string ProviderCode = "kie";
    public const string CapabilityCode = "dance_sell_motion_video";
    public const string ReferenceCapabilityCode = "dance_sell_reference_image";
    public const string FeatureCode = "dance_sell";
    public const string Model = "kling-2.6/motion-control";
    public const string ReferenceModel = "gpt-image-2-image-to-image";
    public const string BillingEnabledConfigKey = "dance_sell_billing_enabled";
    public const string AllowCodeProviderFallbackConfigKey = "dance_sell_allow_code_provider_fallback";
}

public static class DanceSellReferenceModes
{
    public const string DirectReference = "DIRECT_REFERENCE";
    public const string GenerateReference = "GENERATE_REFERENCE";

    public static IReadOnlyList<string> All { get; } = new[] { DirectReference, GenerateReference };
}

public static class DanceSellOperationTypes
{
    public const string ReferenceImage = "reference_image";
    public const string MotionVideo = "motion_video";
    public const string OutputStage = "output_stage";

    public static IReadOnlyList<string> All { get; } = new[] { ReferenceImage, MotionVideo, OutputStage };
}

public static class DanceSellOperationStatuses
{
    public const string Draft = "draft";
    public const string Queued = "queued";
    public const string Submitted = "submitted";
    public const string Generating = "generating";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Timeout = "timeout";
    public const string Cancelled = "cancelled";
}

public static class DanceSellCurrentStages
{
    public const string Draft = "draft";
    public const string MediaUpload = "media_upload";
    public const string ReferenceGeneration = "reference_generation";
    public const string ReferenceReady = "reference_ready";
    public const string ReferenceApproved = "reference_approved";
    public const string MotionQueued = "motion_queued";
    public const string MotionRendering = "motion_rendering";
    public const string OutputStaging = "output_staging";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class DanceSellBillingStatuses
{
    public const string NotRequired = "not_required";
    public const string Estimated = "estimated";
    public const string Reserved = "reserved";
    public const string Charged = "charged";
    public const string ChargeFailed = "charge_failed";
    public const string Reconciliation = "reconciliation";
    public const string PartiallyRefunded = "partially_refunded";
    public const string Refunded = "refunded";
}

public static class DanceSellRefundStatuses
{
    public const string NotRequired = "not_required";
    public const string NotCharged = "not_charged";
    public const string Pending = "pending";
    public const string PartiallyRefunded = "partially_refunded";
    public const string Refunded = "refunded";
    public const string RefundFailed = "refund_failed";
    public const string ManualReview = "manual_review";
}

public static class DanceSellAssetRoles
{
    public const string CharacterInput = "character_input";
    public const string ProductInput = "product_input";
    public const string DirectReferenceInput = "direct_reference_input";
    public const string ReferenceOutput = "reference_output";
    public const string MotionInput = "motion_input";
    public const string VideoOutput = "video_output";
    public const string ProviderRawOutput = "provider_raw_output";
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
    public string ReferenceMode { get; set; } = DanceSellReferenceModes.GenerateReference;
    public string Prompt { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string? ImagePrompt { get; set; }
    public string? ReferenceProviderCode { get; set; }
    public string? ReferenceProviderModel { get; set; }
    public string? MotionProviderCode { get; set; }
    public string? MotionProviderModel { get; set; }
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
    public string ReferenceMode { get; set; } = DanceSellReferenceModes.GenerateReference;
    public Guid? DirectReferenceMediaId { get; set; }
    public string? DirectReferenceObjectKey { get; set; }
    public string? DirectReferenceUrl { get; set; }
    public string? ReferenceProviderCode { get; set; }
    public string? ReferenceProviderModel { get; set; }
    public long? ReferenceProviderCapabilityId { get; set; }
    public Guid? ReferenceProviderAccountId { get; set; }
    public string? MotionProviderCode { get; set; }
    public string? MotionProviderModel { get; set; }
    public long? MotionProviderCapabilityId { get; set; }
    public Guid? MotionProviderAccountId { get; set; }
    public string? ImagePrompt { get; set; }
    public DateTime? ReferenceApprovedAt { get; set; }
    public decimal? TotalProviderUsage { get; set; }
    public decimal? TotalProviderCost { get; set; }
    public string? TotalProviderCurrency { get; set; }
    public decimal? TotalProviderCostVnd { get; set; }
    public decimal? TotalTodoxPointsEstimated { get; set; }
    public decimal? TotalTodoxPointsCharged { get; set; }
    public decimal? TotalTodoxPointsRefunded { get; set; }
    public string CurrentStage { get; set; } = DanceSellCurrentStages.Draft;
    public string BillingStatus { get; set; } = DanceSellBillingStatuses.NotRequired;
    public string RefundStatus { get; set; } = DanceSellRefundStatuses.NotRequired;
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
    public Guid? OperationId { get; set; }
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
    public string ReferenceMode { get; set; } = DanceSellReferenceModes.GenerateReference;
    public string Prompt { get; set; } = string.Empty;
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string? ImagePrompt { get; set; }
    public string? ReferenceProviderCode { get; set; }
    public string? ReferenceProviderModel { get; set; }
    public string? MotionProviderCode { get; set; }
    public string? MotionProviderModel { get; set; }
}

public sealed class DanceSellUpdateBusinessRequest
{
    public string? Title { get; set; }
    public string ReferenceMode { get; set; } = DanceSellReferenceModes.GenerateReference;
    public string Prompt { get; set; } = string.Empty;
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string? ImagePrompt { get; set; }
    public string? ReferenceProviderCode { get; set; }
    public string? ReferenceProviderModel { get; set; }
    public string? MotionProviderCode { get; set; }
    public string? MotionProviderModel { get; set; }
}

public sealed class DanceSellTikTokStageRequest
{
    public string Url { get; set; } = string.Empty;
}

public sealed class DanceSellJsonBusinessRequest
{
    public string? Title { get; set; }
    public string ReferenceMode { get; set; } = DanceSellReferenceModes.GenerateReference;
    public Guid? CharacterMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? DirectReferenceMediaId { get; set; }
    public string MotionSourceType { get; set; } = DanceSellMotionSourceTypes.Upload;
    public Guid? MotionVideoMediaId { get; set; }
    public string? TiktokUrl { get; set; }
    public string PlacementMode { get; set; } = DanceSellPlacementModes.HoldProduct;
    public string? CustomPlacementInstruction { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string Mode { get; set; } = "720p";
    public string CharacterOrientation { get; set; } = "image";
    public string? ImagePrompt { get; set; }
    public string? ReferenceProviderCode { get; set; }
    public string? ReferenceProviderModel { get; set; }
    public string? MotionProviderCode { get; set; }
    public string? MotionProviderModel { get; set; }
}

public sealed record DanceSellCapabilityDto(
    IReadOnlyList<string> Modes,
    IReadOnlyList<string> CharacterOrientations,
    string DefaultMode,
    string DefaultCharacterOrientation);

public sealed class DanceSellProviderRouteDto
{
    public Guid Id { get; set; }
    public string FeatureCode { get; set; } = DanceSellConstants.FeatureCode;
    public string OperationType { get; set; } = DanceSellOperationTypes.MotionVideo;
    public string ProviderCode { get; set; } = DanceSellConstants.ProviderCode;
    public long? ProviderCapabilityId { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public string ModelName { get; set; } = DanceSellConstants.Model;
    public int Priority { get; set; } = 100;
    public bool IsDefault { get; set; }
    public bool Enabled { get; set; }
    public bool AllowUserSelect { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public string DisplayName => $"{ProviderCode} / {ModelName}";
}

public sealed class ProviderAccountDto
{
    public Guid Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Environment { get; set; } = "production";
    public string CredentialConfigName { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string BalanceUnit { get; set; } = "credits";
    public decimal? LastKnownBalance { get; set; }
    public string? LastBalanceSource { get; set; }
    public DateTime? LastBalanceSyncedAt { get; set; }
    public decimal? MinimumBalanceThreshold { get; set; }
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
    public string ConfigJson { get; set; } = "{}";
}

public class DanceSellProviderOperationDto
{
    public Guid Id { get; set; }
    public Guid DanceSellJobId { get; set; }
    public Guid? RenderJobId { get; set; }
    public Guid? ParentOperationId { get; set; }
    public string OperationType { get; set; } = DanceSellOperationTypes.MotionVideo;
    public int AttemptNo { get; set; } = 1;
    public string? ReferenceMode { get; set; }
    public string ProviderCode { get; set; } = DanceSellConstants.ProviderCode;
    public long? ProviderCapabilityId { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public string ProviderModel { get; set; } = DanceSellConstants.Model;
    public string? ProviderTaskId { get; set; }
    public string Status { get; set; } = DanceSellOperationStatuses.Draft;
    public string? ProviderStatus { get; set; }
    public string BillingStatus { get; set; } = DanceSellBillingStatuses.NotRequired;
    public string RefundStatus { get; set; } = DanceSellRefundStatuses.NotRequired;
    public string RequestJson { get; set; } = "{}";
    public string? ResponseJson { get; set; }
    public string? CallbackJson { get; set; }
    public string? ErrorJson { get; set; }
    public string? ProviderUsageJson { get; set; }
    public string? PricingSnapshotJson { get; set; }
    public decimal? UsageQuantity { get; set; }
    public string? UsageUnit { get; set; }
    public decimal? CreditsEstimated { get; set; }
    public decimal? CreditsConsumed { get; set; }
    public decimal? ProviderCost { get; set; }
    public string? ProviderCurrency { get; set; }
    public decimal? ProviderCostVnd { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? TodoxPointsEstimated { get; set; }
    public decimal? TodoxPointsReserved { get; set; }
    public decimal? TodoxPointsCharged { get; set; }
    public decimal? TodoxPointsRefunded { get; set; }
    public decimal? BalanceBefore { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? CostSource { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AiOperationAssetDto
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public string AssetRole { get; set; } = string.Empty;
    public Guid? MediaId { get; set; }
    public string? ObjectKey { get; set; }
    public string? PublicUrl { get; set; }
    public string? ProviderUrl { get; set; }
    public string? MimeType { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public sealed class DanceSellOperationLogFilter
{
    public string? Search { get; set; }
    public Guid? DanceSellJobId { get; set; }
    public Guid? RenderJobId { get; set; }
    public string? ProviderTaskId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string? ProviderCode { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public string? ModelName { get; set; }
    public string? OperationType { get; set; }
    public string? Status { get; set; }
    public string? BillingStatus { get; set; }
    public string? RefundStatus { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class DanceSellOperationLogItemDto : DanceSellProviderOperationDto
{
    public string? Title { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string? CurrentStage { get; set; }
    public string? ResultUrl { get; set; }
    public int AssetCount { get; set; }
}

public sealed class DanceSellOperationLogDetailDto
{
    public DanceSellOperationLogItemDto Operation { get; set; } = new();
    public IReadOnlyList<AiOperationAssetDto> Assets { get; set; } = Array.Empty<AiOperationAssetDto>();
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long Total);

public sealed class DanceSellCostEstimate
{
    public string OperationType { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string UsageUnit { get; set; } = "credits";
    public decimal EstimatedUsage { get; set; }
    public decimal? ProviderUnitPrice { get; set; }
    public decimal? EstimatedProviderCost { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? ExchangeRate { get; set; }
    public decimal? ProviderCostVnd { get; set; }
    public decimal? EstimatedTodoxPoints { get; set; }
    public string PricingSource { get; set; } = "estimated";
    public string? Warning { get; set; }
    public string? PricingUnit { get; set; }
    public decimal? Markup { get; set; }
    public string? RoundingRule { get; set; }
    public decimal? TodoXVndPerPoint { get; set; }
}

public sealed class ProviderBalanceResult
{
    public bool Success { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public Guid ProviderAccountId { get; set; }
    public decimal? Balance { get; set; }
    public string BalanceUnit { get; set; } = "credits";
    public string Source { get; set; } = "manual";
    public string? RawResponseJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
