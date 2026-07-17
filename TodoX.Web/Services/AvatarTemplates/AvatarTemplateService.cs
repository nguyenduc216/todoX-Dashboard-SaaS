using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
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
    private readonly IAiProviderService _aiProviders;
    private readonly IAiImageRenderRouter _imageRouter;
    private readonly IConfiguration _config;
    private readonly ILogger<AvatarTemplateService> _logger;

    public AvatarTemplateService(TodoXConnectionFactory factory, TenantContext tenant,
        IMediaFileService media, IImageAICreativeRenderService creativeRender,
        IAiProviderService aiProviders, IAiImageRenderRouter imageRouter,
        IConfiguration config, ILogger<AvatarTemplateService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _media = media;
        _creativeRender = creativeRender;
        _aiProviders = aiProviders;
        _imageRouter = imageRouter;
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
        DbDiagnostics.LogFieldLengths(_logger, "avatar_template_save",
            ("name", model.Name),
            ("slug", model.Slug),
            ("description", model.Description),
            ("scenario", model.Scenario),
            ("prompt_template", model.PromptTemplate),
            ("last_render_log_code", model.LastRenderLogCode),
            ("last_generated_prompt", model.LastGeneratedPrompt));
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
                characterType = NormalizeOptionalPreset(model.CharacterTypeCode),
                characterTypeVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.CharacterTypes, model.CharacterTypeCode),
                gender = NormalizeOptionalPreset(model.GenderCode),
                genderVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.Genders, model.GenderCode),
                camera = NormalizeOptionalPreset(model.CameraAngleCode),
                cameraVi = AvatarOptionCatalog.Display(AvatarOptionCatalog.CameraAngles, model.CameraAngleCode),
                outfit = NormalizeOptionalPreset(model.OutfitCode),
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

        var option = await ResolveAvatarOptionAsync(model.ProviderCapabilityId, ct);
        // Routed providers render from reference URLs; route through the shared image router.
        if (option is not null && ProviderCodeMap.IsRoutedImageProvider(option.ProviderCode))
        {
            return await RenderViaRouterAsync(option, model.PromptTemplate, "avatar_sample", "avatar_template",
                userId, customerId, model.AvatarMediaId, model.LogoMediaId, model.ProductMediaId, model.UniformMediaId, model.SceneMediaId, ct);
        }

        var result = await _creativeRender.RenderAsync(new ImageAICreativeRenderRequest
        {
            UserId = userId,
            CustomerId = customerId,
            IsCustomer = customerId is not null,
            Scenario = string.IsNullOrWhiteSpace(model.Scenario) ? "avatar_chibi" : model.Scenario,
            CharacterType = NormalizeOptionalPreset(model.CharacterTypeCode),
            Gender = NormalizeOptionalPreset(model.GenderCode),
            CameraAngle = NormalizeOptionalPreset(model.CameraAngleCode),
            Outfit = NormalizeOptionalPreset(model.OutfitCode),
            Count = 1,
            PromptOverride = model.PromptTemplate,
            AspectRatio = "1:1",
            FileCategory = "avatar_template",
            AvatarMediaId = model.AvatarMediaId,
            LogoMediaId = model.LogoMediaId,
            ProductMediaId = model.ProductMediaId,
            UniformMediaId = model.UniformMediaId,
            SceneMediaId = model.SceneMediaId,
            RequireReferenceImages = model.AvatarMediaId is not null,
            SkipReferenceOwnershipCheck = true
        }, ct);
        var publicResult = ToPublicResult(result);
        var customerIdLong = ToBigIntCustomerId(customerId);
        await LogCreativeUsageAsync(option, "avatar_sample", publicResult, customerIdLong, userId, ct);
        return publicResult;
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

        var option = await ResolveAvatarOptionAsync(request.ProviderCapabilityId, ct);
        var avatarMediaId = request.AvatarMediaId ?? template?.AvatarMediaId;
        var logoMediaId = request.LogoMediaId ?? template?.LogoMediaId;
        var productMediaId = request.ProductMediaId ?? template?.ProductMediaId;
        var uniformMediaId = request.UniformMediaId ?? template?.UniformMediaId;
        var sceneMediaId = request.SceneMediaId ?? template?.SceneMediaId;
        var scenario = template?.Scenario ?? "avatar_chibi";

        // Backward-compat: Builder public tạm thời chạy luồng legacy ImageAICreativeRenderService.
        // Chỉ đi qua shared image router khi feature flag bật VÀ provider hỗ trợ router ảnh.
        var useRouter = _config.GetValue<bool>("Features:AvatarBuilderUseImageRouter")
            && option is not null
            && ProviderCodeMap.IsRoutedImageProvider(option.ProviderCode);

        if (useRouter && !_config.GetValue<bool>("PublicAvatarBuilder:AllowAnonymousSponsoredRender"))
        {
            return new PublicAvatarBuilderRenderResult
            {
                Ok = false,
                Status = "failed",
                Error = "Public Avatar Builder cần đăng nhập hoặc cấu hình campaign tài trợ trước khi render bằng YEScale."
            };
        }

        _logger.LogInformation("PUBLIC_AVATAR_RENDER_START templateId={TemplateId} useRouter={UseRouter} providerCapabilityId={ProviderCapabilityId} avatarMediaId={AvatarMediaId} scenario={Scenario} promptLength={PromptLength}",
            request.TemplateId, useRouter, request.ProviderCapabilityId, avatarMediaId, scenario, prompt.Length);

        PublicAvatarBuilderRenderResult publicResult;
        if (useRouter)
        {
            // TODO(builder-router): AiImageRenderRouter hiện truyền CustomerId = null thay vì request.CustomerId.
            // Không sửa trong task backward-compat này để tránh regression; xử lý riêng khi bật router cho Builder.
            publicResult = await RenderViaRouterAsync(option!, prompt, "avatar_builder", "public_avatar_builder",
                userId, null, avatarMediaId, logoMediaId, productMediaId, uniformMediaId, sceneMediaId, ct);
        }
        else
        {
            var result = await _creativeRender.RenderAsync(new ImageAICreativeRenderRequest
            {
                UserId = userId,
                CustomerId = null,
                IsCustomer = false,
                Scenario = scenario,
                CharacterType = NormalizeOptionalPreset(request.CharacterTypeCode ?? template?.CharacterTypeCode),
                Gender = NormalizeOptionalPreset(request.GenderCode ?? template?.GenderCode),
                CameraAngle = NormalizeOptionalPreset(request.CameraAngleCode ?? template?.CameraAngleCode),
                Outfit = NormalizeOptionalPreset(request.OutfitCode ?? template?.OutfitCode),
                Count = 1,
                PromptOverride = prompt,
                AspectRatio = "1:1",
                FileCategory = "public_avatar_builder",
                AvatarMediaId = avatarMediaId,
                LogoMediaId = logoMediaId,
                ProductMediaId = productMediaId,
                UniformMediaId = uniformMediaId,
                SceneMediaId = sceneMediaId,
                RequireReferenceImages = avatarMediaId is not null,
                SkipReferenceOwnershipCheck = true
            }, ct);
            publicResult = ToPublicResult(result);
        }

        _logger.LogInformation("PUBLIC_AVATAR_RENDER_DONE templateId={TemplateId} ok={Ok} status={Status} mediaId={MediaId} logCode={LogCode} error={Error}",
            request.TemplateId, publicResult.Ok, publicResult.Status, publicResult.MediaId, publicResult.LogCode, publicResult.Error);

        if (!useRouter)
        {
            await LogCreativeUsageAsync(option, "avatar_builder", publicResult, null, userId, ct);
        }
        return publicResult;
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

    private async Task<ProviderOptionDto?> ResolveAvatarOptionAsync(long? providerCapabilityId, CancellationToken ct)
    {
        try
        {
            return await _aiProviders.ResolveProviderForCapabilityAsync(AiProviderCatalog.ChibiAvatarGeneration, providerCapabilityId, fromUser: true, ct);
        }
        catch (Exception ex) when (providerCapabilityId is null)
        {
            // No provider configured for Avatar Builder — keep the legacy ImageAICreativeRender path.
            _logger.LogInformation("AVATAR_PROVIDER_FALLBACK reason={Reason}", ex.Message);
            return null;
        }
    }

    private async Task<PublicAvatarBuilderRenderResult> RenderViaRouterAsync(
        ProviderOptionDto option, string prompt, string featureCode, string fileCategory,
        Guid userId, Guid? customerGuid,
        Guid? avatarMediaId, Guid? logoMediaId, Guid? productMediaId, Guid? uniformMediaId, Guid? sceneMediaId,
        CancellationToken ct)
    {
        var references = new List<string>();
        var avatarUrl = await MediaUrlAsync(avatarMediaId, ct);
        if (avatarMediaId is not null && string.IsNullOrWhiteSpace(avatarUrl))
        {
            throw new InvalidOperationException("Không tạo được URL public cho ảnh chân dung.");
        }

        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            references.Add(avatarUrl!);
        }

        foreach (var refMediaId in new[] { logoMediaId, productMediaId, uniformMediaId, sceneMediaId })
        {
            if (refMediaId is null)
            {
                continue;
            }

            var url = await MediaUrlAsync(refMediaId, ct);
            if (!string.IsNullOrWhiteSpace(url))
            {
                references.Add(url!);
                _logger.LogInformation("AVATAR_PUBLIC_REFERENCE_URL mediaId={MediaId} url={Url}", refMediaId, url);
            }
            else
            {
                _logger.LogWarning("AVATAR_PUBLIC_REFERENCE_URL_DROPPED mediaId={MediaId}", refMediaId);
            }
        }

        var render = await _imageRouter.RenderImageAsync(new AiImageRenderRequest
        {
            CustomerId = ToBigIntCustomerId(customerGuid),
            CustomerGuid = customerGuid,
            UserId = userId,
            FeatureCode = featureCode,
            CapabilityCode = AiProviderCatalog.ChibiAvatarGeneration,
            ProviderCapabilityId = option.ProviderCapabilityId,
            FromUser = true,
            Prompt = prompt,
            ReferenceImageUrls = references.ToArray(),
            AspectRatio = "1:1",
            FileCategory = fileCategory,
            RequestId = $"{featureCode}-{Guid.NewGuid():N}",
            CreatedBy = userId.ToString()
        }, ct);

        if (!render.Success)
        {
            return new PublicAvatarBuilderRenderResult { Ok = false, Status = "failed", Error = render.ErrorMessage ?? "Render chưa thành công." };
        }

        var imageUrl = render.ImageUrl;
        Guid? mediaId = null;
        if (render.ImageBytes is { Length: > 0 })
        {
            await _tenant.EnsureLoadedAsync(ct);
            var media = await _media.SaveAsync(render.ImageBytes, $"{featureCode}_{DateTime.UtcNow:yyyyMMddHHmmss}.png",
                render.MimeType ?? "image/png", fileCategory, userId, null, _tenant.TenantId, ct);
            imageUrl = media.PublicUrl ?? media.FileUrl;
            mediaId = media.Id;
        }
        else if (!string.IsNullOrWhiteSpace(render.ImageUrl))
        {
            await _tenant.EnsureLoadedAsync(ct);
            var media = await _media.DownloadAndSaveImageAsync(render.ImageUrl, fileCategory, userId, null, _tenant.TenantId, ct);
            imageUrl = media.PublicUrl ?? media.FileUrl;
            mediaId = media.Id;
        }

        return new PublicAvatarBuilderRenderResult
        {
            Ok = !string.IsNullOrWhiteSpace(imageUrl),
            Status = string.IsNullOrWhiteSpace(imageUrl) ? "failed" : "completed",
            ImageUrl = imageUrl,
            MediaId = mediaId,
            PromptUsed = prompt,
            Error = string.IsNullOrWhiteSpace(imageUrl) ? "Provider không trả về ảnh." : null
        };
    }
    private async Task LogCreativeUsageAsync(ProviderOptionDto? option, string featureCode,
        PublicAvatarBuilderRenderResult result, long? customerId, Guid userId, CancellationToken ct)
    {
        if (option is null) return; // no provider configured -> nothing to meter
        var requestId = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "request_id", result.LogCode);
        var jobId = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "job_id", result.MediaId?.ToString());
        var providerCode = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "provider_code", option.ProviderCode) ?? option.ProviderCode;
        var capabilityCode = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "capability_code", option.CapabilityCode) ?? option.CapabilityCode;
        var feature = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "feature_code", featureCode) ?? featureCode;
        var modelName = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "model_name", option.ModelName) ?? option.ModelName;
        var status = DbDiagnostics.Clip(_logger, "todox_ai_provider_usage_log", "status", result.Ok ? "success" : "failed") ?? (result.Ok ? "success" : "failed");
        DbDiagnostics.LogFieldLengths(_logger, "avatar_usage_log",
            ("provider_code", providerCode),
            ("capability_code", capabilityCode),
            ("feature_code", feature),
            ("model_name", modelName),
            ("request_id", requestId),
            ("job_id", jobId),
            ("status", status));
        await _aiProviders.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = customerId,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ProviderCode = providerCode,
            CapabilityCode = capabilityCode,
            FeatureCode = feature,
            ModelName = modelName,
            RequestId = requestId,
            JobId = jobId,
            Quantity = 1,
            UnitType = option.UnitType,
            UnitCostPoints = option.UnitCostPoints,
            TotalPoints = option.UnitCostPoints,
            Status = status,
            ErrorMessage = result.Ok ? null : result.Error,
            CreatedBy = userId.ToString()
        }, ct);
    }

    private async Task<string?> MediaUrlAsync(Guid? mediaId, CancellationToken ct)
    {
        if (mediaId is null) return null;
        var media = await _media.GetAsync(mediaId.Value, ct);
        return NormalizeRenderableUrl(media?.PublicUrl ?? media?.FileUrl);
    }

    private string? NormalizeRenderableUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return url;
        }

        if (!url.StartsWith("/", StringComparison.Ordinal))
        {
            return null;
        }

        var publicBase = (_config["TodoX:PublicBaseUrl"] ?? _config["Storage:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(publicBase))
        {
            return null;
        }

        return $"{publicBase}{url}";
    }

    private static long? ToBigIntCustomerId(Guid? id)
    {
        if (id is null) return null;
        var bytes = id.Value.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0);
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }

    private static bool IsFailedStatus(string? status)
        => string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsSucceededStatus(string? status)
        => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    // Chuẩn hóa kết quả render legacy sang hợp đồng UI. Engine trả Status="success",
    // trong khi trước đây phương thức này chỉ chấp nhận "completed" khiến ảnh render thành công
    // vẫn bị báo thất bại. Coi thành công khi có một ảnh không bị failed và có URL hợp lệ.
    private static PublicAvatarBuilderRenderResult ToPublicResult(ImageAICreativeRenderResult result)
    {
        var image = result.Images.FirstOrDefault(x =>
                        !IsFailedStatus(x.Status) && !string.IsNullOrWhiteSpace(x.Url))
                    ?? result.Images.FirstOrDefault();

        var succeeded =
            IsSucceededStatus(result.Status) &&
            image is not null &&
            !string.IsNullOrWhiteSpace(image.Url) &&
            !IsFailedStatus(image.Status);

        return new PublicAvatarBuilderRenderResult
        {
            Ok = succeeded,
            Status = succeeded ? "completed" : result.Status,
            LogCode = result.LogCode,
            ImageUrl = image?.Url,
            MediaId = image?.MediaId,
            PromptUsed = image?.PromptUsed ?? result.GeneratedPrompt,
            Error = succeeded ? null : result.Error ?? image?.Error,
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
        model.CharacterTypeCode = NormalizeCode(model.CharacterTypeCode, AvatarOptionCatalog.CharacterTypes, "not_specified");
        model.GenderCode = NormalizeCode(model.GenderCode, AvatarOptionCatalog.Genders, "not_specified");
        model.CameraAngleCode = NormalizeCode(model.CameraAngleCode, AvatarOptionCatalog.CameraAngles, "not_specified");
        model.OutfitCode = NormalizeCode(model.OutfitCode, AvatarOptionCatalog.Outfits, "not_specified");
    }

    private static string NormalizeCode(string? code, IReadOnlyList<AvatarOption> options, string fallback)
        => options.Any(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ? code!.Trim() : fallback;

    private static string? NormalizeOptionalPreset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().Equals("not_specified", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
    }

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
