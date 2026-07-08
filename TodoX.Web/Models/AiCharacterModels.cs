namespace TodoX.Web.Models;

public sealed class AiCharacter
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TenantId { get; set; }
    public string CharacterCode { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StylePreset { get; set; } = "3d_chibi";
    public string Gender { get; set; } = "not_specified";
    public string AspectRatio { get; set; } = "1:1";
    public string MasterPrompt { get; set; } = string.Empty;
    public string NormalizedPrompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;
    public string? MasterImageUrl { get; set; }
    public string? MasterImageObjectKey { get; set; }
    public string ProviderCode { get; set; } = "todox_image";
    public string ModelName { get; set; } = string.Empty;
    public long? Seed { get; set; }
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AiCharacterRender
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TenantId { get; set; }
    public string RenderCode { get; set; } = string.Empty;
    public string ProviderCode { get; set; } = "todox_image";
    public string ModelName { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? OutputImageUrl { get; set; }
    public string? OutputObjectKey { get; set; }
    public string AspectRatio { get; set; } = "1:1";
    public string OutputFormat { get; set; } = "png";
    public string Quality { get; set; } = "high";
    public string Resolution { get; set; } = "1K";
    public long? Seed { get; set; }
    public decimal? UsageCost { get; set; }
    public string? UsageJson { get; set; }
    public string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AiCharacterReference
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TenantId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? ObjectKey { get; set; }
    public string ReferenceType { get; set; } = "reference";
    public string? Note { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CharacterListItemDto
{
    public Guid Id { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? TenantId { get; set; }
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
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CharacterDetailDto : CharacterListItemDto
{
    public string MasterPrompt { get; set; } = string.Empty;
    public string NormalizedPrompt { get; set; } = string.Empty;
    public string NegativePrompt { get; set; } = string.Empty;
    public string? MasterImageObjectKey { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public long? Seed { get; set; }
    public int SortOrder { get; set; }
    public List<AiCharacterRenderDto> Renders { get; set; } = new();
    public List<AiCharacterReferenceDto> References { get; set; } = new();
}

public sealed class AiCharacterRenderDto
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
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
    public long? Seed { get; set; }
    public decimal? UsageCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AiCharacterReferenceDto
{
    public Guid Id { get; set; }
    public Guid CharacterId { get; set; }
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
    public string StylePreset { get; set; } = "3d_chibi";
    public string Gender { get; set; } = "not_specified";
    public string AspectRatio { get; set; } = "1:1";
    public long? Seed { get; set; }
    public bool AutoGenerateImage { get; set; }
}

public sealed class UpdateCharacterRequest
{
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? RenderPrompt { get; set; }
    public string StylePreset { get; set; } = "3d_chibi";
    public string Gender { get; set; } = "not_specified";
    public string AspectRatio { get; set; } = "1:1";
    public string Status { get; set; } = "active";
    public int SortOrder { get; set; }
}

public sealed class GenerateCharacterImageRequest
{
    public Guid? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string? Description { get; set; }
    public string? RenderPrompt { get; set; }
    public string? StylePreset { get; set; }
    public string? Gender { get; set; }
    public string? AspectRatio { get; set; }
    public long? Seed { get; set; }
    public string[]? ReferenceImageUrls { get; set; }
    public bool SaveAsMaster { get; set; } = true;
}

public sealed class GenerateCharacterImageResponse
{
    public Guid? CharacterId { get; set; }
    public Guid? RenderId { get; set; }
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
    public Guid Id { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StylePreset { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string? MasterImageUrl { get; set; }
    public string? MasterImageObjectKey { get; set; }
    public string NormalizedPrompt { get; set; } = string.Empty;
}
