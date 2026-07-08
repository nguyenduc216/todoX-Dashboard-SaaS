using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.AvatarTemplates;

public sealed record AvatarOption(string Code, string DisplayVi);

public static class AvatarOptionCatalog
{
    public static IReadOnlyList<AvatarOption> CharacterTypes { get; } =
    [
        new("not_specified", "Không chọn"),
        new("chibi", "Chibi"),
        new("cartoon_3d", "Hoạt hình 3D"),
        new("anime", "Anime"),
        new("realistic", "Chân dung AI"),
        new("mascot", "Linh vật")
    ];

    public static IReadOnlyList<AvatarOption> Genders { get; } =
    [
        new("not_specified", "Không chọn"),
        new("male", "Nam"),
        new("female", "Nữ"),
        new("neutral", "Trung tính")
    ];

    public static IReadOnlyList<AvatarOption> CameraAngles { get; } =
    [
        new("not_specified", "Không chọn"),
        new("front", "Chính diện"),
        new("three_quarter", "Góc 3/4"),
        new("side", "Nghiêng"),
        new("close_up", "Cận cảnh"),
        new("half_body", "Nửa người"),
        new("full_body", "Toàn thân")
    ];

    public static IReadOnlyList<AvatarOption> Outfits { get; } =
    [
        new("not_specified", "Không chọn"),
        new("suit", "Vest công sở"),
        new("shirt", "Áo sơ mi"),
        new("tshirt", "Áo thun"),
        new("dress", "Váy"),
        new("uniform", "Đồng phục"),
        new("casual", "Thường ngày"),
        new("swimwear", "Đồ bơi")
    ];

    public static string Display(IReadOnlyList<AvatarOption> options, string? code)
        => options.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))?.DisplayVi
           ?? options.FirstOrDefault()?.DisplayVi
           ?? string.Empty;
}

public sealed class AvatarTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string Scenario { get; set; } = "avatar_chibi";
    public string PromptTemplate { get; set; } = string.Empty;
    public string? CharacterTypeCode { get; set; }
    public string? CharacterTypeDisplayVi { get; set; } = "Chibi";
    public string? GenderCode { get; set; }
    public string? GenderDisplayVi { get; set; } = "Trung tính";
    public string? CameraAngleCode { get; set; }
    public string? CameraAngleDisplayVi { get; set; } = "Nửa người";
    public string? OutfitCode { get; set; }
    public string? OutfitDisplayVi { get; set; } = "Vest công sở";
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? UniformMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }
    public Guid? PreviewMediaId { get; set; }
    public string? PreviewMediaUrl { get; set; }
    public string? LastRenderLogCode { get; set; }
    public string? LastGeneratedPrompt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public sealed class AvatarTemplateEditModel
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string Scenario { get; set; } = "avatar_chibi";
    public string PromptTemplate { get; set; } = DefaultPrompt;
    public string? CharacterTypeCode { get; set; } = "not_specified";
    public string? GenderCode { get; set; } = "not_specified";
    public string? CameraAngleCode { get; set; } = "not_specified";
    public string? OutfitCode { get; set; } = "not_specified";
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? UniformMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }
    public Guid? PreviewMediaId { get; set; }
    public string? PreviewMediaUrl { get; set; }
    public string? LastRenderLogCode { get; set; }
    public string? LastGeneratedPrompt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsPublic { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>Optional AI provider selection (todox_ai_provider_capability.id). Null uses the default.</summary>
    public long? ProviderCapabilityId { get; set; }

    public const string DefaultPrompt = "Tạo avatar chibi 3D cao cấp, thân thiện, chuyên nghiệp. Giữ nét nhận diện khuôn mặt từ ảnh tham chiếu nếu có. Bố cục vuông 1:1, ánh sáng mềm, chi tiết rõ, không chữ, không watermark.";
}

public sealed class AvatarTemplateRenderPreviewRequest
{
    public AvatarTemplateEditModel Template { get; set; } = new();
}

public sealed class PublicAvatarBuilderRenderRequest
{
    public Guid? TemplateId { get; set; }
    public string? PromptOverride { get; set; }
    public string? CharacterTypeCode { get; set; }
    public string? GenderCode { get; set; }
    public string? CameraAngleCode { get; set; }
    public string? OutfitCode { get; set; }
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? UniformMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }

    /// <summary>Optional AI provider selection (todox_ai_provider_capability.id). Null uses the default.</summary>
    public long? ProviderCapabilityId { get; set; }
}

public sealed class PublicAvatarBuilderRenderResult
{
    public bool Ok { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LogCode { get; set; }
    public string? ImageUrl { get; set; }
    public Guid? MediaId { get; set; }
    public string? PromptUsed { get; set; }
    public string? Error { get; set; }
    public List<RenderLogEntry> Logs { get; set; } = new();
}
