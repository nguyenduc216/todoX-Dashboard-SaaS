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
    public static IReadOnlyList<string> CapabilityCodes { get; } = new[]
    {
        "avatar_generation",
        "chibi_avatar_generation",
        "character_generation",
        "image_generation",
        "scene_image_generation",
        "poster_generation",
        "thumbnail_generation",
        "text_to_video",
        "image_to_video"
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

/// <summary>Bridges DB provider_code values to the existing IAiImageProviderFactory keys.</summary>
public static class ProviderCodeMap
{
    public static bool IsRoutedImageProvider(string? providerCode)
    {
        var factoryKey = ToFactoryKey(providerCode);
        return factoryKey.Equals("openrouter_image", StringComparison.OrdinalIgnoreCase)
               || factoryKey.Equals("yescale_task_image", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToFactoryKey(string? providerCode)
    {
        if (string.IsNullOrWhiteSpace(providerCode)) return "todox_image";

        return providerCode.Trim().ToLowerInvariant() switch
        {
            "openrouter" or "openrouter_image" => "openrouter_image",
            "yescale" or "yescale_task" or "yescale_task_image" => "yescale_task_image",
            "image_ai_creative_render" or "todox_image" or "todox" => "todox_image",
            _ => providerCode.Trim()
        };
    }
}
