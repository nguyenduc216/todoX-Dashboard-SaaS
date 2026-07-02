using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Profile;

namespace TodoX.Web.Services.Profile;

public sealed class ChibiGenerationDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? GeneratedPrompt { get; set; }
    public List<ChibiImage> Images { get; set; } = new();
    public Guid? SelectedMediaId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ChibiImage
{
    public Guid MediaId { get; set; }
    public string? Url { get; set; }
}

public sealed class ChibiGenerateInput
{
    public Guid UserId { get; set; }
    public string? Gender { get; set; }
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }
}

public interface IChibiAvatarService
{
    Task<ChibiGenerationDto> GenerateAsync(ChibiGenerateInput input, CancellationToken ct = default);
    Task<IReadOnlyList<ChibiGenerationDto>> GetGenerationsAsync(Guid userId, CancellationToken ct = default);
    Task SelectAsync(Guid userId, Guid generationId, Guid mediaId, CancellationToken ct = default);
}

/// <summary>
/// Chibi avatar flow: builds a professional prompt from the user's references, calls the
/// Vertex image render (count=3), stores results in media + auth.user_chibi_generations,
/// and promotes a chosen image to the active avatar.
/// </summary>
public sealed class ChibiAvatarService : IChibiAvatarService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IImageRenderService _render;
    private readonly IAvatarService _avatars;
    private readonly TenantContext _tenant;
    private readonly ILogger<ChibiAvatarService> _logger;

    public ChibiAvatarService(TodoXConnectionFactory factory, IImageRenderService render,
        IAvatarService avatars, TenantContext tenant, ILogger<ChibiAvatarService> logger)
    {
        _factory = factory;
        _render = render;
        _avatars = avatars;
        _tenant = tenant;
        _logger = logger;
    }

    public static string BuildPrompt(string? gender, bool hasAvatar, bool hasLogo, bool hasProduct, bool hasScene)
    {
        var person = gender switch
        {
            "male" => "a male person",
            "female" => "a female person",
            _ => "a person"
        };

        var sb = new StringBuilder();
        sb.Append($"Create a high quality 3D chibi avatar of {person} based on the user's avatar reference if available. ");
        sb.Append("Style: cute, professional, friendly, modern, suitable for TodoX SaaS profile avatar. ");
        sb.Append("Keep facial identity inspired by the reference photo without copying photo-realistically. ");
        if (hasLogo) sb.Append("A logo reference is provided: subtly include brand colors or a small logo badge. ");
        if (hasProduct) sb.Append("A product reference is provided: place the product as a small prop in the character's hand or beside the character. ");
        if (hasScene) sb.Append("A scene/background reference is provided: use it as a simplified soft background. ");
        sb.Append("Clean composition, square avatar, centered character, bright eyes, premium lighting, high detail, no text, no watermark.");
        return sb.ToString();
    }

    public async Task<ChibiGenerationDto> GenerateAsync(ChibiGenerateInput input, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var prompt = BuildPrompt(input.Gender, input.AvatarMediaId is not null,
            input.LogoMediaId is not null, input.ProductMediaId is not null, input.SceneMediaId is not null);

        var generationId = Guid.NewGuid();
        await InsertGenerationAsync(generationId, input, prompt);

        // Reference images = whichever media the user provided.
        var refs = new List<ReferenceImage>();
        foreach (var m in new[] { input.AvatarMediaId, input.LogoMediaId, input.ProductMediaId, input.SceneMediaId })
        {
            if (m is Guid id) refs.Add(new ReferenceImage { MediaId = id });
        }

        var result = await _render.RenderAsync(new ImageRenderRequestModel
        {
            Prompt = prompt,
            ReferenceImages = refs,
            Count = 3,
            AspectRatio = "1:1",
            MimeType = "image/png",
            UserId = input.UserId,
            FileCategory = "chibi"
        }, ct);

        var images = result.Data.Select(d => new ChibiImage { MediaId = d.MediaId, Url = d.Url }).ToList();
        await CompleteGenerationAsync(generationId, result.Ok ? "completed" : "failed", images, result.RequestId, result.Error);

        return new ChibiGenerationDto
        {
            Id = generationId,
            Status = result.Ok ? "completed" : "failed",
            GeneratedPrompt = prompt,
            Images = images,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task InsertGenerationAsync(Guid id, ChibiGenerateInput input, string prompt)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_chibi_generations
                (id, tenant_id, user_id, status, prompt, generated_prompt, gender,
                 reference_avatar_media_id, reference_logo_media_id, reference_product_media_id, reference_scene_media_id, created_at)
            VALUES
                (@id, @tenant, @uid, 'processing', @prompt, @prompt, @gender,
                 @avatar, @logo, @product, @scene, now());
            """,
            new
            {
                id, tenant = _tenant.TenantId, uid = input.UserId, prompt, gender = input.Gender,
                avatar = input.AvatarMediaId, logo = input.LogoMediaId,
                product = input.ProductMediaId, scene = input.SceneMediaId
            });
    }

    private async Task CompleteGenerationAsync(Guid id, string status, List<ChibiImage> images, Guid vertexRequestId, string? error)
    {
        using var conn = await _factory.OpenAsync();
        var json = JsonSerializer.Serialize(images.Select(i => new { mediaId = i.MediaId, url = i.Url }));
        await conn.ExecuteAsync(
            """
            UPDATE auth.user_chibi_generations
               SET status=@status, result=@json::jsonb, vertex_request_id=@req, error_message=@err, completed_at=now()
             WHERE id=@id;
            """,
            new { id, status, json, req = vertexRequestId, err = error });
    }

    public async Task<IReadOnlyList<ChibiGenerationDto>> GetGenerationsAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<(Guid Id, string Status, string? GeneratedPrompt, string? Result, Guid? SelectedMediaId, DateTime CreatedAt)>(
            """
            SELECT id AS Id, status AS Status, generated_prompt AS GeneratedPrompt,
                   result::text AS Result, selected_media_id AS SelectedMediaId, created_at AS CreatedAt
              FROM auth.user_chibi_generations
             WHERE user_id=@uid
             ORDER BY created_at DESC LIMIT 20;
            """, new { uid = userId });

        var list = new List<ChibiGenerationDto>();
        foreach (var r in rows)
        {
            var images = new List<ChibiImage>();
            if (!string.IsNullOrWhiteSpace(r.Result))
            {
                try
                {
                    using var doc = JsonDocument.Parse(r.Result);
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        images.Add(new ChibiImage
                        {
                            MediaId = el.TryGetProperty("mediaId", out var m) && m.TryGetGuid(out var g) ? g : Guid.Empty,
                            Url = el.TryGetProperty("url", out var u) ? u.GetString() : null
                        });
                    }
                }
                catch { /* ignore malformed */ }
            }
            list.Add(new ChibiGenerationDto
            {
                Id = r.Id, Status = r.Status, GeneratedPrompt = r.GeneratedPrompt,
                Images = images, SelectedMediaId = r.SelectedMediaId, CreatedAt = r.CreatedAt
            });
        }
        return list;
    }

    public async Task SelectAsync(Guid userId, Guid generationId, Guid mediaId, CancellationToken ct = default)
    {
        // Ownership: the media must belong to this user.
        using (var conn = await _factory.OpenAsync(ct))
        {
            var owns = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM media.media_files WHERE id=@m AND user_id=@u);",
                new { m = mediaId, u = userId });
            if (!owns) throw new InvalidOperationException("Ảnh không thuộc về người dùng.");

            await conn.ExecuteAsync(
                """
                UPDATE auth.user_chibi_generations
                   SET status='selected', selected_media_id=@media, selected_at=now()
                 WHERE id=@id AND user_id=@uid;
                """, new { id = generationId, uid = userId, media = mediaId });
        }

        await _avatars.SetActiveFromMediaAsync(userId, mediaId, "chibi", ct);
    }
}
