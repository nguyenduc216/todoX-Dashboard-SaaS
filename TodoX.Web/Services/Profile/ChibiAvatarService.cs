using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Settings;

namespace TodoX.Web.Services.Profile;

public sealed class ChibiImage
{
    public Guid RenderId { get; set; }
    public Guid MediaId { get; set; }
    public string? Url { get; set; }
    public string? PromptInput { get; set; }
    public string? PromptUsed { get; set; }
    public string Status { get; set; } = "completed";
    public string? Error { get; set; }
}

public sealed class ChibiGenerationDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? GeneratedPrompt { get; set; }
    public List<ChibiImage> Images { get; set; } = new();
    public Guid? SelectedMediaId { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool UsedFallback { get; set; }
    public string? Error { get; set; }
    public decimal Charged { get; set; }
    public decimal BalanceAfter { get; set; }
}

public sealed class ChibiGenerateInput
{
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public bool IsCustomer { get; set; }
    public string? Gender { get; set; }
    public string? CameraAngle { get; set; }
    public int Count { get; set; } = 3;
    public string? BasePromptOverride { get; set; }
    public string? PromptOverride { get; set; }
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? UniformMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }
}

public interface IChibiAvatarService
{
    string BuildDefaultPrompt(string? gender, bool hasAvatar, bool hasLogo, bool hasProduct, bool hasScene);
    Task<string> GetDefaultPromptTemplateAsync(CancellationToken ct = default);
    string ResolvePromptTemplate(string template, ChibiGenerateInput input);
    Task<List<ReferenceImage>> BuildReferenceImagesAsync(ChibiGenerateInput input, CancellationToken ct = default);
    Task<ChibiGenerationDto> GenerateAsync(ChibiGenerateInput input, CancellationToken ct = default);
    Task<ChibiImage> RerenderAsync(Guid userId, Guid? customerId, bool isCustomer, Guid renderId, string prompt, CancellationToken ct = default);
    Task<IReadOnlyList<ChibiGenerationDto>> GetGenerationsAsync(Guid userId, CancellationToken ct = default);
    Task SelectAsync(Guid userId, Guid generationId, Guid mediaId, CancellationToken ct = default);
}

public sealed partial class ChibiAvatarService : IChibiAvatarService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IImageRenderService _render;
    private readonly IMediaFileService _media;
    private readonly IPromptTemplateService _promptTemplates;
    private readonly IAvatarService _avatars;
    private readonly GeminiPromptService _gemini;
    private readonly WalletService _wallet;
    private readonly TokenSettingsService _tokenSettings;
    private readonly TenantContext _tenant;
    private readonly ILogger<ChibiAvatarService> _logger;

    private const string FallbackPromptTemplate = "Create exactly 3 premium 3D chibi avatar variations based on the user avatar reference if available. Gender: {{GENDER}}. Style: cute, professional, friendly, modern, premium Pixar-quality 3D collectible mascot, suitable for TodoX SaaS profile avatar. Preserve recognizable facial identity from the avatar reference without making it photorealistic. Use large expressive eyes, friendly smile, cute proportions about 3 heads tall, clean centered square avatar composition, soft global illumination, luxury golden lighting, high detail. If a logo reference is provided, integrate brand colors or a small tasteful logo badge naturally on clothing/accessory/background. If product reference is provided, let the character hold or interact naturally with the product. If background reference is provided, use it only as inspiration and redesign into a simplified cinematic background, do not copy exactly. No text, no watermark, no extra fingers, no bad anatomy, no cropped face. Output PNG, square 1:1.";

    public ChibiAvatarService(TodoXConnectionFactory factory, IImageRenderService render,
        IMediaFileService media, IPromptTemplateService promptTemplates, IAvatarService avatars, GeminiPromptService gemini, WalletService wallet,
        TokenSettingsService tokenSettings, TenantContext tenant, ILogger<ChibiAvatarService> logger)
    {
        _factory = factory;
        _render = render;
        _media = media;
        _promptTemplates = promptTemplates;
        _avatars = avatars;
        _gemini = gemini;
        _wallet = wallet;
        _tokenSettings = tokenSettings;
        _tenant = tenant;
        _logger = logger;
    }

    public string BuildDefaultPrompt(string? gender, bool hasAvatar, bool hasLogo, bool hasProduct, bool hasScene)
    {
        var g = gender switch { "male" => "male", "female" => "female", _ => "other" };
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"task\": \"create_chibi_avatar\",\n");
        sb.Append("  \"description\": \"Tạo avatar chibi 3D cực kỳ dễ thương, chất lượng cao, dùng cho thương hiệu hoặc cá nhân.\",\n");
        sb.Append("  \"style\": \"3D chibi, ultra cute, high detail, soft lighting, vibrant colors\",\n");
        sb.Append("  \"avatar_identity\": \"Giữ nguyên đặc điểm khuôn mặt của ảnh avatar gốc, chuyển thành nhân vật chibi.\",\n");
        sb.Append($"  \"gender\": \"{g}\",\n");
        sb.Append("  \"requirements\": [\n");
        sb.Append("    \"Giữ giống khuôn mặt avatar gốc.\",\n");
        sb.Append("    \"Tỉ lệ chibi ~ 1:1, mắt to, biểu cảm thân thiện.\",\n");
        sb.Append("    \"Ánh sáng mềm mại, màu sắc tươi sáng, chất lượng cao.\",\n");
        sb.Append("    \"Không có chữ viết, không watermark, không viền.\"\n");
        sb.Append("  ],\n");
        sb.Append("  \"integrations\": {\n");
        sb.Append($"    \"logo\": \"{(hasLogo ? "Tích hợp logo tự nhiên trên áo, phụ kiện hoặc background." : "none")}\",\n");
        sb.Append($"    \"product\": \"{(hasProduct ? "Nhân vật chibi cầm, trưng bày hoặc tương tác tự nhiên với sản phẩm." : "none")}\",\n");
        sb.Append($"    \"background\": \"{(hasScene ? "Dùng ảnh bối cảnh làm ý tưởng, tạo lại bối cảnh phù hợp." : "none")}\"\n");
        sb.Append("  },\n");
        sb.Append("  \"negative_prompt\": \"realistic photo, ugly, low quality, blurry, watermark, text, extra fingers, deformed, bad anatomy\",\n");
        sb.Append("  \"output\": { \"size\": \"1024x1024\", \"format\": \"png\" }\n");
        sb.Append("}");
        return sb.ToString();
    }

    public async Task<string> GetDefaultPromptTemplateAsync(CancellationToken ct = default)
    {
        try
        {
            var template = await _promptTemplates.GetDefaultAsync("avatar_chibi", "vi", ct);
            if (template is not null)
            {
                return ComposePromptTemplate(template);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prompt template table unavailable; falling back to system_settings.");
        }

        using var conn = await _factory.OpenAsync(ct);
        var fallback = await conn.ExecuteScalarAsync<string?>(
            """
            SELECT setting_value
              FROM settings.system_settings
             WHERE setting_key = 'avatar_chibi.default_prompt_template'
               AND is_active = true
             LIMIT 1;
            """);
        return string.IsNullOrWhiteSpace(fallback) ? FallbackPromptTemplate : fallback;
    }

    public string ResolvePromptTemplate(string template, ChibiGenerateInput input)
    {
        var gender = input.Gender switch { "male" => "male", "female" => "female", _ => "other" };
        var cameraAngle = NormalizeCameraAngle(input.CameraAngle);
        var resolved = (string.IsNullOrWhiteSpace(template) ? FallbackPromptTemplate : template)
            .Replace("{{GENDER}}", gender, StringComparison.OrdinalIgnoreCase)
            .Replace("{{CAMERA_ANGLE}}", cameraAngle, StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_AVATAR}}", input.AvatarMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_LOGO}}", input.LogoMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_PRODUCT}}", input.ProductMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_UNIFORM}}", input.UniformMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_SCENE}}", input.SceneMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase);

        return resolved.Trim() + """

Reference images:
- Personal avatar/face reference: {{AVATAR_STATUS}}.
- Brand logo reference: {{LOGO_STATUS}}.
- Product reference: {{PRODUCT_STATUS}}.
- Uniform reference: {{UNIFORM_STATUS}}.
- Background/scene reference: {{SCENE_STATUS}}.
Use reference images as visual guidance, not as exact copies. Prioritize face identity, hairstyle, age impression, gender expression, uniform colors, product shape, logo placement, and scene mood from the available references.
"""
            .Replace("{{AVATAR_STATUS}}", input.AvatarMediaId is null ? "not available" : "available", StringComparison.OrdinalIgnoreCase)
            .Replace("{{LOGO_STATUS}}", input.LogoMediaId is null ? "not available" : "available", StringComparison.OrdinalIgnoreCase)
            .Replace("{{PRODUCT_STATUS}}", input.ProductMediaId is null ? "not available" : "available", StringComparison.OrdinalIgnoreCase)
            .Replace("{{UNIFORM_STATUS}}", input.UniformMediaId is null ? "not available" : "available", StringComparison.OrdinalIgnoreCase)
            .Replace("{{SCENE_STATUS}}", input.SceneMediaId is null ? "not available" : "available", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<ReferenceImage>> BuildReferenceImagesAsync(ChibiGenerateInput input, CancellationToken ct = default)
    {
        var refs = new List<ReferenceImage>();

        async Task AddAsync(Guid? mediaId, string role)
        {
            if (mediaId is null) return;
            var image = await _media.BuildReferenceImageAsync(mediaId.Value, role, input.UserId, ct);
            if (image is null)
            {
                throw new InvalidOperationException($"Da chon anh tham chieu {role} nhung he thong khong doc duoc noi dung anh.");
            }
            refs.Add(image);
        }

        await AddAsync(input.AvatarMediaId, "avatar");
        await AddAsync(input.LogoMediaId, "logo");
        await AddAsync(input.ProductMediaId, "product");
        await AddAsync(input.UniformMediaId, "uniform");
        await AddAsync(input.SceneMediaId, "scene");

        return refs;
    }

    private static string ComposePromptTemplate(PromptTemplateDto template)
    {
        if (string.IsNullOrWhiteSpace(template.NegativePrompt))
        {
            return template.TemplateText;
        }

        return template.TemplateText.Trim() + Environment.NewLine + Environment.NewLine
            + "Negative prompt: " + template.NegativePrompt.Trim();
    }

    private static string NormalizeCameraAngle(string? cameraAngle)
    {
        return cameraAngle switch
        {
            "full_body" => "toan than",
            "half_body" => "nua nguoi",
            _ => "nua nguoi"
        };
    }

    // GenerateAsync + RerenderAsync in ChibiAvatarService.Generate.cs (partial)
}
