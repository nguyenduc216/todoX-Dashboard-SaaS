using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Profile;

namespace TodoX.Web.Services.AvatarTemplates;

public interface IAvatarTemplateService
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AvatarTemplateDto>> ListAsync(bool publicOnly = false, CancellationToken ct = default);
    Task<AvatarTemplateDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<AvatarTemplateDto> SaveAsync(AvatarTemplateEditModel model, Guid? userId, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task TogglePublicAsync(Guid id, Guid? userId, CancellationToken ct = default);
    Task<PublicAvatarBuilderRenderResult> RenderPreviewAsync(AvatarTemplateEditModel model, Guid userId, Guid? customerId, CancellationToken ct = default);
    Task<MediaFileDto> SavePublicUploadAsync(byte[] content, string fileName, string contentType, CancellationToken ct = default);
    Task<PublicAvatarBuilderRenderResult> RenderPublicAsync(PublicAvatarBuilderRenderRequest request, CancellationToken ct = default);
}

public sealed class AvatarTemplateService : IAvatarTemplateService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IMediaFileService _media;
    private readonly IImageAICreativeRenderService _creativeRender;
    private readonly IConfiguration _config;
    private readonly ILogger<AvatarTemplateService> _logger;

    public AvatarTemplateService(TodoXConnectionFactory factory, TenantContext tenant,
        IMediaFileService media, IImageAICreativeRenderService creativeRender,
        IConfiguration config, ILogger<AvatarTemplateService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _media = media;
        _creativeRender = creativeRender;
        _config = config;
        _logger = logger;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            CREATE SCHEMA IF NOT EXISTS marketing;

            CREATE TABLE IF NOT EXISTS marketing.avatar_templates (
                id uuid PRIMARY KEY,
                tenant_id uuid NULL,
                name text NOT NULL,
                slug text NULL,
                description text NULL,
                scenario text NOT NULL DEFAULT 'avatar_chibi',
                prompt_template text NOT NULL,
                character_type_code text NULL,
                character_type_display_vi text NULL,
                gender_code text NULL,
                gender_display_vi text NULL,
                camera_angle_code text NULL,
                camera_angle_display_vi text NULL,
                outfit_code text NULL,
                outfit_display_vi text NULL,
                avatar_media_id uuid NULL,
                logo_media_id uuid NULL,
                product_media_id uuid NULL,
                uniform_media_id uuid NULL,
                scene_media_id uuid NULL,
                preview_media_id uuid NULL,
                last_render_log_code text NULL,
                last_generated_prompt text NULL,
                is_active boolean NOT NULL DEFAULT true,
                is_public boolean NOT NULL DEFAULT true,
                sort_order int NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );

            CREATE INDEX IF NOT EXISTS ix_avatar_templates_public
                ON marketing.avatar_templates (is_public, is_active, sort_order, created_at);
            """);
    }

    public async Task<IReadOnlyList<AvatarTemplateDto>> ListAsync(bool publicOnly = false, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<AvatarTemplateDto>(
            """
            SELECT t.id AS Id, t.name AS Name, t.slug AS Slug, t.description AS Description,
                   t.scenario AS Scenario, t.prompt_template AS PromptTemplate,
                   t.character_type_code AS CharacterTypeCode, t.character_type_display_vi AS CharacterTypeDisplayVi,
                   t.gender_code AS GenderCode, t.gender_display_vi AS GenderDisplayVi,
                   t.camera_angle_code AS CameraAngleCode, t.camera_angle_display_vi AS CameraAngleDisplayVi,
                   t.outfit_code AS OutfitCode, t.outfit_display_vi AS OutfitDisplayVi,
                   t.avatar_media_id AS AvatarMediaId, t.logo_media_id AS LogoMediaId,
                   t.product_media_id AS ProductMediaId, t.uniform_media_id AS UniformMediaId,
                   t.scene_media_id AS SceneMediaId, t.preview_media_id AS PreviewMediaId,
                   m.public_url AS PreviewMediaUrl,
                   t.last_render_log_code AS LastRenderLogCode,
                   t.last_generated_prompt AS LastGeneratedPrompt,
                   t.is_active AS IsActive, t.is_public AS IsPublic, t.sort_order AS SortOrder,
                   t.created_at AS CreatedAt, t.created_by AS CreatedBy, t.updated_at AS UpdatedAt, t.updated_by AS UpdatedBy
              FROM marketing.avatar_templates t
              LEFT JOIN media.media_files m ON m.id = t.preview_media_id
             WHERE (@publicOnly = false OR (t.is_public = true AND t.is_active = true))
             ORDER BY t.sort_order, t.created_at DESC;
            """, new { publicOnly });
        return rows.ToList();
    }

    public async Task<AvatarTemplateDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        return (await ListAsync(publicOnly: false, ct)).FirstOrDefault(x => x.Id == id);
    }

    public async Task<AvatarTemplateDto> SaveAsync(AvatarTemplateEditModel model, Guid? userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        Normalize(model);
        var id = model.Id is Guid existing && existing != Guid.Empty ? existing : Guid.NewGuid();
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO marketing.avatar_templates
                (id, tenant_id, name, slug, description, scenario, prompt_template,
                 character_type_code, character_type_display_vi, gender_code, gender_display_vi,
                 camera_angle_code, camera_angle_display_vi, outfit_code, outfit_display_vi,
                 avatar_media_id, logo_media_id, product_media_id, uniform_media_id, scene_media_id,
                 preview_media_id, last_render_log_code, last_generated_prompt,
                 is_active, is_public, sort_order, created_at, created_by, updated_at, updated_by)
            VALUES
                (@id, @tenant, @name, @slug, @description, @scenario, @prompt,
                 @characterType, @characterTypeVi, @gender, @genderVi,
                 @camera, @cameraVi, @outfit, @outfitVi,
                 @avatar, @logo, @product, @uniform, @scene,
                 @preview, @logCode, @generatedPrompt,
                 @active, @public, @sort, now(), @user, now(), @user)
            ON CONFLICT (id) DO UPDATE SET
                name = EXCLUDED.name,
                slug = EXCLUDED.slug,
                description = EXCLUDED.description,
                scenario = EXCLUDED.scenario,
                prompt_template = EXCLUDED.prompt_template,
                character_type_code = EXCLUDED.character_type_code,
                character_type_display_vi = EXCLUDED.character_type_display_vi,
                gender_code = EXCLUDED.gender_code,
                gender_display_vi = EXCLUDED.gender_display_vi,
                camera_angle_code = EXCLUDED.camera_angle_code,
                camera_angle_display_vi = EXCLUDED.camera_angle_display_vi,
                outfit_code = EXCLUDED.outfit_code,
                outfit_display_vi = EXCLUDED.outfit_display_vi,
                avatar_media_id = EXCLUDED.avatar_media_id,
                logo_media_id = EXCLUDED.logo_media_id,
                product_media_id = EXCLUDED.product_media_id,
                uniform_media_id = EXCLUDED.uniform_media_id,
                scene_media_id = EXCLUDED.scene_media_id,
                preview_media_id = EXCLUDED.preview_media_id,
                last_render_log_code = EXCLUDED.last_render_log_code,
                last_generated_prompt = EXCLUDED.last_generated_prompt,
                is_active = EXCLUDED.is_active,
                is_public = EXCLUDED.is_public,
                sort_order = EXCLUDED.sort_order,
                updated_at = now(),
                updated_by = EXCLUDED.updated_by;
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                name = model.Name.Trim(),
                slug = string.IsNullOrWhiteSpace(model.Slug) ? Slugify(model.Name) : Slugify(model.Slug),
                description = model.Description,
                scenario = string.IsNullOrWhiteSpace(model.Scenario) ? "avatar_chibi" : model.Scenario.Trim(),
                prompt = model.PromptTemplate.Trim(),
                characterType = model.CharacterTypeCode,
                characterTypeVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.CharacterTypes, model.CharacterTypeCode),
                gender = model.GenderCode,
                genderVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.Genders, model.GenderCode),
                camera = model.CameraAngleCode,
                cameraVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.CameraAngles, model.CameraAngleCode),
                outfit = model.OutfitCode,
                outfitVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.Outfits, model.OutfitCode),
                avatar = model.AvatarMediaId,
                logo = model.LogoMediaId,
                product = model.ProductMediaId,
                uniform = model.UniformMediaId,
                scene = model.SceneMediaId,
                preview = model.PreviewMediaId,
                logCode = model.LastRenderLogCode,
                generatedPrompt = model.LastGeneratedPrompt,
                active = model.IsActive,
                @public = model.IsPublic,
                sort = model.SortOrder,
                user = userId
            });

        _logger.LogInformation("AVATAR_TEMPLATE_SAVED id={Id} name={Name} userId={UserId}", id, model.Name, userId);
        return await GetAsync(id, ct) ?? throw new InvalidOperationException("Không đọc lại được avatar template vừa lưu.");
    }

    public async Task DeleteAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE marketing.avatar_templates SET is_active=false, updated_at=now(), updated_by=@user WHERE id=@id;",
            new { id, user = userId });
        _logger.LogInformation("AVATAR_TEMPLATE_DELETED id={Id} userId={UserId}", id, userId);
    }

    public async Task TogglePublicAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE marketing.avatar_templates SET is_public = NOT is_public, updated_at=now(), updated_by=@user WHERE id=@id;",
            new { id, user = userId });
    }

    public async Task<PublicAvatarBuilderRenderResult> RenderPreviewAsync(AvatarTemplateEditModel model, Guid userId, Guid? customerId, CancellationToken ct = default)
    {
        Normalize(model);
        _logger.LogInformation("AVATAR_TEMPLATE_RENDER_REQUESTED name={Name} userId={UserId}", model.Name, userId);
        var result = await _creativeRender.RenderAsync(new ImageAICreativeRenderRequest
        {
            UserId = userId,
            CustomerId = customerId,
            IsCustomer = customerId is not null,
            Scenario = string.IsNullOrWhiteSpace(model.Scenario) ? "avatar_chibi" : model.Scenario,
            CharacterType = model.CharacterTypeCode,
            Gender = model.GenderCode,
            CameraAngle = model.CameraAngleCode,
            Outfit = model.OutfitCode,
            Count = 1,
            PromptOverride = model.PromptTemplate,
            AspectRatio = "1:1",
            FileCategory = "avatar_template",
            AvatarMediaId = model.AvatarMediaId,
            LogoMediaId = model.LogoMediaId,
            ProductMediaId = model.ProductMediaId,
            UniformMediaId = model.UniformMediaId,
            SceneMediaId = model.SceneMediaId,
            RequireReferenceImages = model.AvatarMediaId is not null
        }, ct);
        return ToPublicResult(result);
    }

    public async Task<MediaFileDto> SavePublicUploadAsync(byte[] content, string fileName, string contentType, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, contentType, "avatar_builder_input_temp",
            userId: null, customerId: null, tenantId: _tenant.TenantId, ct);
        _logger.LogInformation("PUBLIC_AVATAR_UPLOAD_COMPLETED mediaId={MediaId} fileName={FileName}", media.Id, fileName);
        return media;
    }

    public async Task<PublicAvatarBuilderRenderResult> RenderPublicAsync(PublicAvatarBuilderRenderRequest request, CancellationToken ct = default)
    {
        var template = request.TemplateId is Guid templateId ? await GetAsync(templateId, ct) : null;
        if (request.TemplateId is not null && template is null)
        {
            return new PublicAvatarBuilderRenderResult { Ok = false, Status = "failed", Error = "Không tìm thấy avatar mẫu." };
        }

        var userId = await ResolvePublicUserIdAsync(ct);
        var prompt = !string.IsNullOrWhiteSpace(request.PromptOverride)
            ? request.PromptOverride.Trim()
            : template?.PromptTemplate ?? AvatarTemplateEditModel.DefaultPrompt;
        _logger.LogInformation("PUBLIC_AVATAR_RENDER_REQUESTED templateId={TemplateId} userId={UserId}", request.TemplateId, userId);

        var result = await _creativeRender.RenderAsync(new ImageAICreativeRenderRequest
        {
            UserId = userId,
            CustomerId = null,
            IsCustomer = false,
            Scenario = template?.Scenario ?? "avatar_chibi",
            CharacterType = request.CharacterTypeCode ?? template?.CharacterTypeCode ?? "chibi",
            Gender = request.GenderCode ?? template?.GenderCode ?? "neutral",
            CameraAngle = request.CameraAngleCode ?? template?.CameraAngleCode ?? "half_body",
            Outfit = request.OutfitCode ?? template?.OutfitCode ?? "suit",
            Count = 1,
            PromptOverride = prompt,
            AspectRatio = "1:1",
            FileCategory = "public_avatar_builder",
            AvatarMediaId = request.AvatarMediaId ?? template?.AvatarMediaId,
            LogoMediaId = request.LogoMediaId ?? template?.LogoMediaId,
            ProductMediaId = request.ProductMediaId ?? template?.ProductMediaId,
            UniformMediaId = request.UniformMediaId ?? template?.UniformMediaId,
            SceneMediaId = request.SceneMediaId ?? template?.SceneMediaId,
            RequireReferenceImages = request.AvatarMediaId is not null
        }, ct);
        return ToPublicResult(result);
    }

    private async Task<Guid> ResolvePublicUserIdAsync(CancellationToken ct)
    {
        if (Guid.TryParse(_config["PublicAvatarBuilder:UserId"], out var configured) && configured != Guid.Empty)
        {
            return configured;
        }

        using var conn = await _factory.OpenAsync(ct);
        var userId = await conn.ExecuteScalarAsync<Guid?>(
            """
            SELECT u.id
              FROM auth.app_users u
              LEFT JOIN auth.user_roles ur ON ur.user_id = u.id
              LEFT JOIN auth.roles r ON r.id = ur.role_id
             WHERE u.is_active = true
             ORDER BY CASE WHEN COALESCE(u.is_root,false) OR r.code IN ('administrator_root','admin') THEN 0 ELSE 1 END,
                      u.created_at
             LIMIT 1;
            """);
        return userId ?? throw new InvalidOperationException("Chưa cấu hình PublicAvatarBuilder:UserId và không tìm thấy user hệ thống để render public.");
    }

    private static PublicAvatarBuilderRenderResult ToPublicResult(ImageAICreativeRenderResult result)
    {
        var image = result.Images.FirstOrDefault(x => x.Status != "failed") ?? result.Images.FirstOrDefault();
        return new PublicAvatarBuilderRenderResult
        {
            Ok = result.Status == "completed" && image?.Url is not null,
            Status = result.Status,
            LogCode = result.LogCode,
            ImageUrl = image?.Url,
            MediaId = image?.MediaId,
            PromptUsed = image?.PromptUsed ?? result.GeneratedPrompt,
            Error = result.Error ?? image?.Error,
            Logs = result.Logs
        };
    }

    private static void Normalize(AvatarTemplateEditModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name))
        {
            model.Name = "Avatar mẫu";
        }

        if (string.IsNullOrWhiteSpace(model.PromptTemplate))
        {
            model.PromptTemplate = AvatarTemplateEditModel.DefaultPrompt;
        }

        model.Scenario = string.IsNullOrWhiteSpace(model.Scenario) ? "avatar_chibi" : model.Scenario.Trim();
        model.CharacterTypeCode = NormalizeCode(model.CharacterTypeCode, AvatarOptionCatalog.CharacterTypes, "chibi");
        model.GenderCode = NormalizeCode(model.GenderCode, AvatarOptionCatalog.Genders, "neutral");
        model.CameraAngleCode = NormalizeCode(model.CameraAngleCode, AvatarOptionCatalog.CameraAngles, "half_body");
        model.OutfitCode = NormalizeCode(model.OutfitCode, AvatarOptionCatalog.Outfits, "suit");
    }

    private static string NormalizeCode(string? code, IReadOnlyList<AvatarOption> options, string fallback)
        => options.Any(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ? code!.Trim() : fallback;

    private static string Slugify(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "avatar-template" : value.Trim().ToLowerInvariant();
        var chars = text.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }
}
