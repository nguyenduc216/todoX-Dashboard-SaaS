namespace TodoX.Web.Models;

// ---------------------------------------------------------------------------
// Entities — map 1:1 to existing public.todox_ai_provider* tables (BIGINT ids).
// No migration / no schema change: read & write existing columns only.
// ---------------------------------------------------------------------------

public sealed class AiProvider
{
    public long Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiKeyConfigName { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsSystem { get; set; }
    public int Priority { get; set; } = 100;
    public string? Description { get; set; }
    public string? ConfigJson { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AiProviderCapability
{
    public long Id { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public string? EndpointPath { get; set; }
    public string UnitType { get; set; } = "request";
    public decimal UnitCostPoints { get; set; }
    public bool IsDefault { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowUserSelect { get; set; } = true;
    public string? ConfigJson { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AiProviderUsageLog
{
    public long Id { get; set; }
    public long? CustomerId { get; set; }
    public long? ProviderId { get; set; }
    public long? ProviderCapabilityId { get; set; }
    public string? ProviderCode { get; set; }
    public string? CapabilityCode { get; set; }
    public string? FeatureCode { get; set; }
    public string? ModelName { get; set; }
    public string? RequestId { get; set; }
    public string? JobId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? UnitType { get; set; }
    public decimal? UnitCostPoints { get; set; }
    public decimal? TotalPoints { get; set; }
    public decimal? ProviderRawCost { get; set; }
    public string? Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MetadataJson { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CustomerCreditTransaction
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public decimal Points { get; set; }
    public decimal? BalanceAfter { get; set; }
    public string? Description { get; set; }
    public string? MetadataJson { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

public sealed class AiProviderListItemDto
{
    public long Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiKeyConfigName { get; set; }
    public bool Enabled { get; set; }
    public bool IsSystem { get; set; }
    public int Priority { get; set; }
    public string? Description { get; set; }
    public int CapabilityCount { get; set; }
    public int EnabledCapabilityCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AiProviderDetailDto
{
    public long Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiKeyConfigName { get; set; }
    public bool Enabled { get; set; }
    public bool IsSystem { get; set; }
    public int Priority { get; set; }
    public string? Description { get; set; }
    public string? ConfigJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<AiProviderCapabilityDto> Capabilities { get; set; } = new();
}

public sealed class AiProviderCapabilityDto
{
    public long Id { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public string? EndpointPath { get; set; }
    public string UnitType { get; set; } = "request";
    public decimal UnitCostPoints { get; set; }
    public bool IsDefault { get; set; }
    public bool Enabled { get; set; }
    public bool AllowUserSelect { get; set; }
    public string? ConfigJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class UpdateAiProviderRequest
{
    public string ProviderName { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? ApiKeyConfigName { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string? Description { get; set; }
    public string? ConfigJson { get; set; }
}

public sealed class UpdateAiProviderCapabilityRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public string? EndpointPath { get; set; }
    public string UnitType { get; set; } = "request";
    public decimal UnitCostPoints { get; set; }
    public bool IsDefault { get; set; }
    public bool Enabled { get; set; } = true;
    public bool AllowUserSelect { get; set; } = true;
    public string? ConfigJson { get; set; }
}

/// <summary>Provider/capability option exposed to render screens. Never carries secrets.</summary>
public sealed class ProviderOptionDto
{
    public long ProviderCapabilityId { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public string UnitType { get; set; } = "request";
    public decimal UnitCostPoints { get; set; }
    public bool IsDefault { get; set; }
    public bool Enabled { get; set; }
    public bool AllowUserSelect { get; set; }

    /// <summary>Label for dropdowns, e.g. "OpenRouter - Image Generation - 3 điểm/image".</summary>
    public string OptionLabel => $"{ProviderName} - {DisplayName} - {UnitCostPoints:0.####} điểm/{UnitType}";
}

// ---------------------------------------------------------------------------
// Catalog & mapping helpers
// ---------------------------------------------------------------------------

public static class AiProviderCatalog
{
    public const string AvatarGeneration = "avatar_generation";
    public const string ChibiAvatarGeneration = "chibi_avatar_generation";
    public const string CharacterGeneration = "character_generation";
    public const string SceneImageGeneration = "scene_image_generation";
    public const string ImageToVideo = "image_to_video";
    public const string MotionControlVideo = "motion_control_video";

    public static IReadOnlyList<string> CapabilityCodes { get; } = new[]
    {
        AvatarGeneration,
        ChibiAvatarGeneration,
        CharacterGeneration,
        "image_generation",
        SceneImageGeneration,
        "poster_generation",
        "thumbnail_generation",
        "text_to_video",
        ImageToVideo,
        MotionControlVideo
    };

    public static IReadOnlyList<string> UnitTypes { get; } = new[]
    {
        "image", "second", "minute", "request", "scene", "character_1000", "token_1000"
    };

    public static IReadOnlyList<string> ProviderTypes { get; } = new[]
    {
        "external_api", "internal_api", "local_service"
    };
}

public sealed record AiFeatureProviderCatalogItem(
    string FeatureKey,
    string FeatureCode,
    string DisplayName,
    string CapabilityCode,
    string MediaKind,
    string Description,
    int SortOrder);

public static class AiFeatureProviderCatalog
{
    public static IReadOnlyList<AiFeatureProviderCatalogItem> QuickDefaultFeatures { get; } = new[]
    {
        new AiFeatureProviderCatalogItem(
            "avatar_image",
            "admin_avatar_manager",
            "Ảnh Avatar",
            AiProviderCatalog.AvatarGeneration,
            "image",
            "Render ảnh avatar trong khu vực quản trị và avatar thường.",
            10),
        new AiFeatureProviderCatalogItem(
            "character_image",
            "character_manager",
            "Ảnh Character",
            AiProviderCatalog.CharacterGeneration,
            "image",
            "Render ảnh nhân vật AI.",
            20),
        new AiFeatureProviderCatalogItem(
            "scene_image",
            "render_job_scene_image",
            "Ảnh Scene",
            AiProviderCatalog.SceneImageGeneration,
            "image",
            "Render ảnh tĩnh cho scene video.",
            30),
        new AiFeatureProviderCatalogItem(
            "avatar_builder",
            "avatar_builder",
            "Avatar Builder",
            AiProviderCatalog.ChibiAvatarGeneration,
            "image",
            "Render avatar builder/chibi độc lập với avatar thường.",
            40),
        new AiFeatureProviderCatalogItem(
            "image_to_video",
            "render_job_scene_video",
            "Video từ ảnh",
            AiProviderCatalog.ImageToVideo,
            "video",
            "Render video scene từ ảnh đã chọn.",
            50)
    };
}

/// <summary>Bridges DB provider_code values to the existing IAiImageProviderFactory keys.</summary>
public static class ProviderCodeMap
{
    public static bool IsRoutedImageProvider(string? providerCode)
    {
        var factoryKey = ToFactoryKey(providerCode);
        return factoryKey.Equals("todox_image", StringComparison.OrdinalIgnoreCase)
               || factoryKey.Equals("openrouter_image", StringComparison.OrdinalIgnoreCase)
               || factoryKey.Equals("yescale_task_image", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToFactoryKey(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return "todox_image";

        return providerCode.Trim().ToLowerInvariant() switch
        {
            "openrouter" or "openrouter_image" => "openrouter_image",
            "yescale" or "yescale_task" or "yescale_task_image" => "yescale_task_image",
            "yescale_task_video" => "yescale_task_video",
            "image_ai_creative_render" or "todox_image" or "todox" => "todox_image",
            _ => providerCode.Trim()
        };
    }
}

// ---------------------------------------------------------------------------
// Phase 1B model, pricing, sync and estimate DTOs.
// ---------------------------------------------------------------------------

public class AiProviderModelListItemDto
{
    public long Id { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string ProviderModelCode { get; set; } = string.Empty;
    public string? ProviderModelIdBase { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string? ServerCode { get; set; }
    public string? ProviderStatus { get; set; }
    public string? StatusMessage { get; set; }
    public string? RateType { get; set; }
    public decimal? BaseProviderPrice { get; set; }
    public string? ProviderPriceUnit { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool AllowUserSelect { get; set; }
    public bool IsDeprecated { get; set; }
    public string? Source { get; set; }
    public DateTime? LastProviderSyncAt { get; set; }
    public DateTime? LastHealthCheckAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public int FailureCount { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public AiModelPriceSummaryDto? PriceSummary { get; set; }
}

public sealed class AiProviderModelDetailDto : AiProviderModelListItemDto
{
    public string? RawJson { get; set; }
    public List<AiProviderModelCapabilityDto> ModelCapabilities { get; set; } = new();
    public List<AiModelPriceDto> Prices { get; set; } = new();
    public List<AiPricingPolicyDto> PricingPolicies { get; set; } = new();
    public List<AiProviderSyncHeaderDto> SyncHistory { get; set; } = new();
    public List<AiProviderSyncChangeDto> SyncChanges { get; set; } = new();
}

public sealed class AiProviderModelCapabilityDto
{
    public long Id { get; set; }
    public long ModelId { get; set; }
    public string CapabilityCode { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Source { get; set; }
    public string? ConfigJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AiModelPriceDto
{
    public long Id { get; set; }
    public long ModelId { get; set; }
    public string? Mode { get; set; }
    public string? Resolution { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Ratio { get; set; }
    public string? RateType { get; set; }
    public string? UnitType { get; set; }
    public decimal? ProviderPrice { get; set; }
    public decimal? ProviderPriceDefault { get; set; }
    public string? ProviderPriceUnit { get; set; }
    public decimal? InternalCostPoints { get; set; }
    public decimal? SellPoints { get; set; }
    public string SellPriceMode { get; set; } = "AUTO";
    public decimal? MarkupPercent { get; set; }
    public decimal? MinimumPoints { get; set; }
    public string? RoundingRule { get; set; }
    public string? PriceSource { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool Active { get; set; }
    public string? Warning { get; set; }
}

public sealed class AiModelPriceSummaryDto
{
    public int ActiveVariantCount { get; set; }
    public decimal? ProviderPrice { get; set; }
    public decimal? InternalCostPoints { get; set; }
    public decimal? SellPoints { get; set; }
    public string? SellPriceMode { get; set; }
    public string? StatusMessage { get; set; }
}

public sealed class AiPricingPolicyDto
{
    public long Id { get; set; }
    public long ProviderId { get; set; }
    public string PolicyCode { get; set; } = string.Empty;
    public string PolicyName { get; set; } = string.Empty;
    public decimal ProviderCreditPerInternalPoint { get; set; }
    public decimal InternalPointValueVnd { get; set; }
    public decimal DefaultMarkupPercent { get; set; }
    public decimal MinimumSellPoints { get; set; }
    public string RoundingRule { get; set; } = "ROUND";
    public bool AllowAutoSellUpdate { get; set; }
    public bool Enabled { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class AiProviderSyncHeaderDto
{
    public long Id { get; set; }
    public long ProviderId { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? RequestedBy { get; set; }
    public string? ModelCatalogEndpoint { get; set; }
    public int ModelInsertedCount { get; set; }
    public int ModelUpdatedCount { get; set; }
    public int ModelUnavailableCount { get; set; }
    public int PriceChangedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class AiProviderSyncChangeDto
{
    public long Id { get; set; }
    public long SyncId { get; set; }
    public string ChangeType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class EstimateCostRequestDto
{
    public long? ProviderModelId { get; set; }
    public long? ProviderId { get; set; }
    public string? ProviderCode { get; set; }
    public string? ProviderModelCode { get; set; }
    public string? Mode { get; set; }
    public string? Resolution { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Ratio { get; set; }
    public decimal Quantity { get; set; } = 1;
    public long? CustomerId { get; set; }
}

public sealed class EstimateCostResponseDto
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public AiProviderModelListItemDto? ProviderModel { get; set; }
    public AiModelPriceDto? MatchedPrice { get; set; }
    public AiPricingPolicyDto? PricingPolicy { get; set; }
    public decimal ProviderUnitCost { get; set; }
    public decimal ProviderTotalCost { get; set; }
    public decimal InternalUnitCostPoints { get; set; }
    public decimal InternalTotalCostPoints { get; set; }
    public decimal SellUnitPoints { get; set; }
    public decimal EstimatedTodoXPoints { get; set; }
    public bool? SufficientBalance { get; set; }
    public string? PriceSource { get; set; }
}
