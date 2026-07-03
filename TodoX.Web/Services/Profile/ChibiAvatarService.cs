using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.Media;

namespace TodoX.Web.Services.Profile;

public sealed class ChibiImage
{
    public Guid RenderId { get; set; }
    public Guid MediaId { get; set; }
    public string? Url { get; set; }
    public string? PromptInput { get; set; }
    public string? PromptUsed { get; set; }
    public string Status { get; set; } = "completed";
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
    public int Count { get; set; } = 3;
    public string? BasePromptOverride { get; set; }
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }
}

public interface IChibiAvatarService
{
    string BuildDefaultPrompt(string? gender, bool hasAvatar, bool hasLogo, bool hasProduct, bool hasScene);
    Task<ChibiGenerationDto> GenerateAsync(ChibiGenerateInput input, CancellationToken ct = default);
    Task<ChibiImage> RerenderAsync(Guid userId, Guid? customerId, bool isCustomer, Guid renderId, string prompt, CancellationToken ct = default);
    Task<IReadOnlyList<ChibiGenerationDto>> GetGenerationsAsync(Guid userId, CancellationToken ct = default);
    Task SelectAsync(Guid userId, Guid generationId, Guid mediaId, CancellationToken ct = default);
}

public sealed partial class ChibiAvatarService : IChibiAvatarService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IImageRenderService _render;
    private readonly IAvatarService _avatars;
    private readonly GeminiPromptService _gemini;
    private readonly WalletService _wallet;
    private readonly TokenSettingsService _tokenSettings;
    private readonly TenantContext _tenant;
    private readonly ILogger<ChibiAvatarService> _logger;

    public ChibiAvatarService(TodoXConnectionFactory factory, IImageRenderService render,
        IAvatarService avatars, GeminiPromptService gemini, WalletService wallet,
        TokenSettingsService tokenSettings, TenantContext tenant, ILogger<ChibiAvatarService> logger)
    {
        _factory = factory;
        _render = render;
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

    // GenerateAsync + RerenderAsync in ChibiAvatarService.Generate.cs (partial)
}
