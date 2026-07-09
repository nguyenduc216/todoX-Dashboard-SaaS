namespace TodoX.Web.Models;

public sealed class AiCharacter
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string CharacterCode { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? StylePreset { get; set; }
    public string? Gender { get; set; }
    public string AspectRatio { get; set; } = "1:1";
    public string? MasterPrompt { get; set; }
    public string? NormalizedPrompt { get; set; }
    public string? NegativePrompt { get; set; }
    public string? MasterImageUrl { get; set; }
    public string? MasterImageObjectKey { get; set; }
    public string ProviderCode { get; set; } = "todox_image";
    public string? ModelName { get; set; }
    public int? Seed { get; set; }
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AiCharacterRender
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public long CustomerId { get; set; }
    public string RenderCode { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = "todox_image";
    public string? ModelName { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? OutputImageUrl { get; set; }
    public string? OutputObjectKey { get; set; }
    public string? AspectRatio { get; set; }
    public string OutputFormat { get; set; } = "png";
    public string? Quality { get; set; }
    public string? Resolution { get; set; }
    public int? Seed { get; set; }
    public decimal? UsageCost { get; set; }
    public string? UsageJson { get; set; }
    public string Status { get; set; } = "success";
    public string? ErrorMessage { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AiCharacterReference
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public long CustomerId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? ObjectKey { get; set; }
    public string ReferenceType { get; set; } = "master";
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CharacterListItemDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string CharacterCode { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StylePreset { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = string.Empty;
    public string? MasterImageUrl { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CharacterDetailDto : CharacterListItemDto
{
    public string MasterPrompt { get; set; } = string.Empty;
    public string NormalizedPrompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;
    public string? MasterImageObjectKey { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public int? Seed { get; set; }
    public int SortOrder { get; set; }
    public List<AiCharacterRenderDto> Renders { get; set; } = new();
    public List<AiCharacterReferenceDto> References { get; set; } = new();
}

public sealed class AiCharacterRenderDto
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string RenderCode { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? OutputImageUrl { get; set; }
    public string? OutputObjectKey { get; set; }
    public string AspectRatio { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public int? Seed { get; set; }
    public decimal? UsageCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AiCharacterReferenceDto
{
    public long Id { get; set; }
    public long CharacterId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? ObjectKey { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class CreateCharacterRequest
{
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RenderPrompt { get; set; }
    public string StylePreset { get; set; } = "not_specified";
    public string Gender { get; set; } = "not_specified";
    public string AspectRatio { get; set; } = "1:1";
    public int? Seed { get; set; }
    public bool AutoGenerateImage { get; set; }
}

public sealed class UpdateCharacterRequest
{
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RenderPrompt { get; set; }
    public string StylePreset { get; set; } = "not_specified";
    public string Gender { get; set; } = "not_specified";
    public string AspectRatio { get; set; } = "1:1";
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }
}

public sealed class GenerateCharacterImageRequest
{
    public long? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string? Description { get; set; }
    public string? RenderPrompt { get; set; }
    public string? StylePreset { get; set; }
    public string? Gender { get; set; }
    public string? AspectRatio { get; set; }
    public int? Seed { get; set; }
    public string[]? ReferenceImageUrls { get; set; }
    public bool SaveAsMaster { get; set; } = true;

    /// <summary>Optional admin/user provider selection (todox_ai_provider_capability.id). Null uses the default.</summary>
    public long? ProviderCapabilityId { get; set; }
}

public sealed class GenerateCharacterImageResponse
{
    public long? CharacterId { get; set; }
    public long? RenderId { get; set; }
    public string? ImageUrl { get; set; }
    public string? ObjectKey { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = "todox_image";
    public string ModelName { get; set; } = string.Empty;
    public decimal? UsageCost { get; set; }
    public string Status { get; set; } = "failed";
    public string? ErrorMessage { get; set; }
}

public sealed class ActiveCharacterDto
{
    public long Id { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StylePreset { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string? MasterImageUrl { get; set; }
    public string? MasterImageObjectKey { get; set; }
    public string NormalizedPrompt { get; set; } = string.Empty;
}
