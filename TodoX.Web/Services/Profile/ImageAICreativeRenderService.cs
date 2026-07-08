using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.ImageRender;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Settings;

namespace TodoX.Web.Services.Profile;

public sealed class ImageAICreativeRenderRequest
{
    public Guid UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public bool IsCustomer { get; set; }
    public string Scenario { get; set; } = "avatar_chibi";
    public string? CharacterType { get; set; } = "chibi";
    public string? Gender { get; set; }
    public string? CameraAngle { get; set; }
    public string? Outfit { get; set; } = "vest";
    public int Count { get; set; } = 3;
    public string PromptTemplateKey { get; set; } = "avatar_chibi";
    public string PromptLanguage { get; set; } = "vi";
    public string? BasePromptOverride { get; set; }
    public string? PromptOverride { get; set; }
    public string AspectRatio { get; set; } = "1:1";
    public string FileCategory { get; set; } = "chibi";
    public string? LogCode { get; set; }
    public Guid? AvatarMediaId { get; set; }
    public Guid? LogoMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public string? ProductImageUrl { get; set; }
    public Guid? UniformMediaId { get; set; }
    public Guid? SceneMediaId { get; set; }
    public bool PreserveFixedAssets { get; set; }
    public bool RequireReferenceImages { get; set; }
    public bool SkipReferenceOwnershipCheck { get; set; }
}

public sealed class ImageAICreativeRenderImage
{
    public int Index { get; set; }
    public Guid? RenderId { get; set; }
    public Guid? MediaId { get; set; }
    public string? Url { get; set; }
    public string? PromptInput { get; set; }
    public string? PromptUsed { get; set; }
    public string Status { get; set; } = "completed";
    public string? Error { get; set; }
    public string? LogCode { get; set; }
}

public sealed class ImageAICreativeRenderResult
{
    public Guid RenderJobId { get; set; }
    public string? LogCode { get; set; }
    public string RenderEngineMode { get; set; } = ChibiRenderEngineModes.ImageAiCreative;
    public string Status { get; set; } = string.Empty;
    public string? GeneratedPrompt { get; set; }
    public List<ImageAICreativeRenderImage> Images { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool UsedFallback { get; set; }
    public string? Error { get; set; }
    public decimal Charged { get; set; }
    public decimal BalanceAfter { get; set; }
    public List<RenderLogEntry> Logs { get; set; } = new();
}

public interface IImageAICreativeRenderService
{
    Task<ImageAICreativeRenderResult> RenderAsync(ImageAICreativeRenderRequest request, CancellationToken ct = default);
}

public sealed class ImageAICreativeRenderService : IImageAICreativeRenderService
{
    private const string EndpointName = "ImageAICreativeRender";
    private const string FallbackPromptTemplate = "Create premium 3D chibi avatar variations for TodoX. Preserve face identity from the avatar reference when available. Use a friendly, professional, cute 3D style, clean square composition, soft cinematic lighting, no text, no watermark, no bad anatomy.";

    private readonly TodoXConnectionFactory _factory;
    private readonly IImageRenderService _render;
    private readonly IMediaFileService _media;
    private readonly IPromptTemplateService _promptTemplates;
    private readonly GeminiPromptService _gemini;
    private readonly AvatarRenderActivityLogService _activityLogs;
    private readonly WalletService _wallet;
    private readonly TokenSettingsService _tokenSettings;
    private readonly TenantContext _tenant;
    private readonly ILogger<ImageAICreativeRenderService> _logger;

    public ImageAICreativeRenderService(TodoXConnectionFactory factory, IImageRenderService render,
        IMediaFileService media, IPromptTemplateService promptTemplates, GeminiPromptService gemini,
        AvatarRenderActivityLogService activityLogs, WalletService wallet, TokenSettingsService tokenSettings,
        TenantContext tenant, ILogger<ImageAICreativeRenderService> logger)
    {
        _factory = factory;
        _render = render;
        _media = media;
        _promptTemplates = promptTemplates;
        _gemini = gemini;
        _activityLogs = activityLogs;
        _wallet = wallet;
        _tokenSettings = tokenSettings;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<ImageAICreativeRenderResult> RenderAsync(ImageAICreativeRenderRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var generationId = Guid.NewGuid();
        var logCode = string.IsNullOrWhiteSpace(request.LogCode)
            ? AvatarRenderActivityLogService.GenerateLogCode()
            : request.LogCode.Trim();
        var logs = new List<RenderLogEntry>();
        var result = new ImageAICreativeRenderResult
        {
            RenderJobId = generationId,
            LogCode = logCode,
            RenderEngineMode = ChibiRenderEngineModes.ImageAiCreative
        };

        void AddLog(string step, string message, object? data = null, string level = "info")
        {
            var entry = new RenderLogEntry { Step = step, Message = message, Data = data, Level = level };
            logs.Add(entry);
            _logger.LogInformation("IMAGE_AI_CREATIVE_RENDER {LogCode} {Step} {Message} {@Data}", logCode, step, message, data);
        }

        try
        {
            var count = Math.Clamp(request.Count, 1, 4);
            AddLog("CREATIVE_RENDER_ENGINE_STARTED", "ImageAICreativeRender engine started.", new
            {
                renderEngine = ChibiRenderEngineModes.ImageAiCreative,
                endpoint = EndpointName,
                request.Scenario,
                generationId,
                logCode
            });
            AddLog("CREATIVE_RENDER_REQUEST_RECEIVED", "Creative render request received.", new
            {
                request.UserId,
                request.CustomerId,
                request.Scenario,
                requestedCount = request.Count,
                imageCount = count,
                request.CharacterType,
                request.Gender,
                request.CameraAngle,
                request.Outfit,
                hasAvatar = request.AvatarMediaId is not null,
                hasLogo = request.LogoMediaId is not null,
                hasProduct = request.ProductMediaId is not null,
                hasUniform = request.UniformMediaId is not null,
                hasScene = request.SceneMediaId is not null
            });

            if (!string.IsNullOrWhiteSpace(request.ProductImageUrl) && request.ProductMediaId is null)
            {
                AddLog("PRODUCT_URL_DOWNLOAD_START", "Downloading product reference image from URL.", new { request.ProductImageUrl });
                var downloaded = await _media.DownloadAndSaveImageAsync(request.ProductImageUrl, "chibi_ref_product_url",
                    request.UserId, request.CustomerId, _tenant.TenantId, ct);
                request.ProductMediaId = downloaded.Id;
                AddLog("PRODUCT_URL_DOWNLOAD_SUCCESS", "Downloaded product reference image from URL.", new
                {
                    request.ProductImageUrl,
                    mediaId = downloaded.Id,
                    downloaded.MimeType,
                    downloaded.FileSizeBytes,
                    downloaded.PublicUrl
                });
            }

            var template = await ResolvePromptTemplateAsync(request, ct);
            AddLog("PROMPT_TEMPLATE_RESOLVED", "Prompt template resolved.", new
            {
                request.PromptTemplateKey,
                request.PromptLanguage,
                templateLength = template.Length
            });

            var finalPrompt = !string.IsNullOrWhiteSpace(request.PromptOverride)
                ? request.PromptOverride.Trim()
                : !string.IsNullOrWhiteSpace(request.BasePromptOverride)
                    ? request.BasePromptOverride.Trim()
                    : ResolvePromptTemplate(template, request, count);
            finalPrompt = EnsureRenderDirectives(finalPrompt, request, count);
            result.GeneratedPrompt = finalPrompt;

            var refs = await BuildReferenceImagesAsync(request, ct);
            foreach (var reference in refs)
            {
                AddLog("REFERENCE_IMAGE_RECEIVED", $"Reference image loaded: {reference.Role}.", new
                {
                    reference.Role,
                    reference.MediaId,
                    reference.MimeType,
                    reference.SizeBytes,
                    reference.Width,
                    reference.Height,
                    reference.HasAlpha,
                    reference.ObjectKey,
                    reference.Url,
                    base64Length = reference.Base64?.Length ?? 0
                });
            }
            AddLog("REFERENCE_IMAGE_BUILD_COMPLETED", "Reference images prepared.", new
            {
                count = refs.Count,
                roles = refs.Select(x => x.Role).ToArray()
            });
            AddLog("PROMPT_BUILT", "Final creative render prompt prepared.", new
            {
                promptLength = finalPrompt.Length,
                imageCount = count,
                referenceCount = refs.Count
            });

            await InsertGenerationAsync(generationId, request, finalPrompt, ct);

            var imageCost = await _tokenSettings.GetChibiImageCostAsync();
            var total = imageCost * count;
            var charge = await _wallet.ChargeAsync(
                request.IsCustomer ? request.CustomerId : null, request.UserId, total, count,
                "chibi_image", "google-vertex-ai", "imagen-3.0-generate-002", EndpointName,
                unit: "image", referenceId: generationId, referenceType: "image_ai_creative_render");

            if (!charge.Ok)
            {
                result.Status = "failed";
                result.Error = charge.Error;
                AddLog("CREATIVE_RENDER_FAILED", charge.Error ?? "Point charge failed.", level: "error");
                await CompleteGenerationAsync(generationId, "failed", new List<ImageAICreativeRenderImage>(), charge.Error, ct);
                await _activityLogs.WriteAsync(request.UserId, request.CustomerId, logCode, "avatar-render", "failed",
                    BuildActivityInput(request, count), finalPrompt, refs, new List<ChibiImage>(), logs, charge.Error, ct);
                result.Logs = logs;
                return result;
            }

            result.Charged = charge.Charged;
            result.BalanceAfter = charge.BalanceAfter;

            var prompts = new List<string> { finalPrompt };
            var fixedAssetMode = IsFixedAssetMode(request, refs);
            if (count > 1)
            {
                if (fixedAssetMode)
                {
                    prompts = BuildFixedAssetVariations(finalPrompt, count);
                    AddLog("PROMPT_VARIATION_SKIPPED", "Fixed asset mode uses deterministic prompt variations.", new { count = prompts.Count });
                }
                else
                {
                    try
                    {
                        AddLog("PROMPT_VARIATION_REQUEST", "Requesting Gemini prompt variations.", new { model = _gemini.ModelCode, imageCount = count });
                        prompts = await _gemini.GenerateVariationsAsync(finalPrompt, count, ct);
                        AddLog("PROMPT_VARIATION_RESPONSE", "Gemini returned prompt variations.", new { count = prompts.Count, lengths = prompts.Select(x => x.Length).ToArray() });
                    }
                    catch (Exception ex)
                    {
                        prompts = BuildFallbackVariations(finalPrompt, count);
                        AddLog("PROMPT_VARIATION_FALLBACK", "Gemini variation generation failed; using deterministic prompt variations.", new { error = ex.Message, count = prompts.Count }, "warning");
                    }
                }
            }

            for (var i = 0; i < count; i++)
            {
                var promptUsed = EnsureVariationDirective(prompts[Math.Min(i, prompts.Count - 1)], i + 1, count);
                AddLog("IMAGE_RENDER_REQUEST", $"Rendering creative variation {i + 1}/{count}.", new
                {
                    variationIndex = i + 1,
                    promptLength = promptUsed.Length,
                    referenceCount = refs.Count,
                    fixedAssetMode
                });

                var render = await _render.RenderAsync(new ImageRenderRequestModel
                {
                    CorrelationId = generationId,
                    Prompt = promptUsed,
                    ReferenceImages = refs,
                    Count = 1,
                    ImageCount = count,
                    VariationIndex = i + 1,
                    Gender = request.Gender,
                    CharacterType = request.CharacterType,
                    Outfit = request.Outfit,
                    CameraAngle = request.CameraAngle,
                    RenderPipeline = fixedAssetMode
                        ? ImageRenderRequestModel.PipelineBackgroundThenComposite
                        : ImageRenderRequestModel.PipelineModelGenerate,
                    PreserveFixedAssets = fixedAssetMode || request.PreserveFixedAssets,
                    Theme = fixedAssetMode ? "yellow_black" : null,
                    AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? "1:1" : request.AspectRatio,
                    MimeType = "image/png",
                    UserId = request.UserId,
                    CustomerId = request.CustomerId,
                    FileCategory = string.IsNullOrWhiteSpace(request.FileCategory) ? "chibi" : request.FileCategory,
                    LogCode = logCode,
                    RequireReferenceImages = request.RequireReferenceImages || request.AvatarMediaId is not null || refs.Count > 0
                }, ct);
                logs.AddRange(render.Logs);

                if (render.Ok && render.Data.Count > 0)
                {
                    var data = render.Data[0];
                    var renderId = await InsertRenderAsync(generationId, request.UserId, data.MediaId, data.Url, finalPrompt, promptUsed, render.Model, ct);
                    var image = new ImageAICreativeRenderImage
                    {
                        Index = i,
                        RenderId = renderId,
                        MediaId = data.MediaId,
                        Url = data.Url,
                        PromptInput = finalPrompt,
                        PromptUsed = promptUsed,
                        LogCode = logCode
                    };
                    result.Images.Add(image);
                    AddLog("IMAGE_RESULT_STORED", $"Stored creative variation {i + 1}/{count}.", new { renderId, data.MediaId, data.Url, render.RequestId, render.Model });
                }
                else
                {
                    var renderId = await InsertRenderAsync(generationId, request.UserId, null, null, finalPrompt, promptUsed, render.Model, ct, "failed", render.Error);
                    result.Images.Add(new ImageAICreativeRenderImage
                    {
                        Index = i,
                        RenderId = renderId,
                        Status = "failed",
                        PromptInput = finalPrompt,
                        PromptUsed = promptUsed,
                        Error = render.Error,
                        LogCode = logCode
                    });
                    AddLog("IMAGE_RENDER_RESPONSE", $"Creative variation {i + 1}/{count} failed.", new { renderId, render.RequestId, render.Error }, "error");
                }

                result.UsedFallback = result.UsedFallback || render.UsedFallback;
            }

            var okImages = result.Images.Where(x => x.Status != "failed").ToList();
            result.Status = okImages.Count > 0 ? "completed" : "failed";
            result.Error = result.Images.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Error))?.Error;
            await CompleteGenerationAsync(generationId, result.Status, okImages, result.Error, ct);
            AddLog(result.Status == "completed" ? "CREATIVE_RENDER_COMPLETED" : "CREATIVE_RENDER_FAILED",
                $"ImageAICreativeRender job {result.Status}.", new
                {
                    renderJobId = generationId,
                    okCount = okImages.Count,
                    failedCount = result.Images.Count - okImages.Count
                }, result.Status == "completed" ? "info" : "error");
            await _activityLogs.WriteAsync(request.UserId, request.CustomerId, logCode, "avatar-render", result.Status,
                BuildActivityInput(request, count), finalPrompt, refs, ToChibiImages(result.Images), logs, result.Error, ct);
            result.Logs = logs;
            return result;
        }
        catch (Exception ex)
        {
            result.Status = "failed";
            result.Error = ex.Message;
            AddLog("CREATIVE_RENDER_FAILED", ex.Message, new { exception = ex.ToString() }, "error");
            await CompleteGenerationAsync(generationId, "failed", result.Images, ex.Message, ct);
            result.Logs = logs;
            return result;
        }
    }

    private async Task<string> ResolvePromptTemplateAsync(ImageAICreativeRenderRequest request, CancellationToken ct)
    {
        try
        {
            var template = await _promptTemplates.GetDefaultAsync(request.PromptTemplateKey, request.PromptLanguage, ct);
            if (template is not null)
            {
                return ComposePromptTemplate(template);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prompt template unavailable for ImageAICreativeRender.");
        }

        return FallbackPromptTemplate;
    }

    private async Task<List<ReferenceImage>> BuildReferenceImagesAsync(ImageAICreativeRenderRequest request, CancellationToken ct)
    {
        var refs = new List<ReferenceImage>();

        async Task AddAsync(Guid? mediaId, string role)
        {
            if (mediaId is null)
            {
                return;
            }

            var image = await _media.BuildReferenceImageAsync(
                mediaId.Value,
                role,
                request.UserId,
                enforceOwnership: !request.SkipReferenceOwnershipCheck,
                ct: ct);
            if (image is null)
            {
                throw new InvalidOperationException($"Da chon anh tham chieu {role} nhung he thong khong doc duoc noi dung anh.");
            }

            refs.Add(image);
        }

        await AddAsync(request.AvatarMediaId, "avatar");
        await AddAsync(request.LogoMediaId, "logo");
        await AddAsync(request.ProductMediaId, "product");
        await AddAsync(request.UniformMediaId, "uniform");
        await AddAsync(request.SceneMediaId, "scene");
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

    private static string ResolvePromptTemplate(string template, ImageAICreativeRenderRequest request, int count)
    {
        var resolved = (string.IsNullOrWhiteSpace(template) ? FallbackPromptTemplate : template)
            .Replace("{{CHARACTER_TYPE}}", request.CharacterType ?? "chibi", StringComparison.OrdinalIgnoreCase)
            .Replace("{{CHARACTER_TYPE_TEXT}}", CharacterTypeText(request.CharacterType), StringComparison.OrdinalIgnoreCase)
            .Replace("{{GENDER}}", request.Gender ?? "other", StringComparison.OrdinalIgnoreCase)
            .Replace("{{GENDER_TEXT}}", request.Gender == "female" ? "nu" : "nam", StringComparison.OrdinalIgnoreCase)
            .Replace("{{CAMERA_ANGLE}}", request.CameraAngle ?? "half_body", StringComparison.OrdinalIgnoreCase)
            .Replace("{{CAMERA_SHOT_TEXT}}", CameraShotText(request.CameraAngle), StringComparison.OrdinalIgnoreCase)
            .Replace("{{OUTFIT}}", request.Outfit ?? "vest", StringComparison.OrdinalIgnoreCase)
            .Replace("{{OUTFIT_TEXT}}", OutfitText(request.Outfit), StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_AVATAR}}", request.AvatarMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_LOGO}}", request.LogoMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_PRODUCT}}", request.ProductMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_UNIFORM}}", request.UniformMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase)
            .Replace("{{HAS_SCENE}}", request.SceneMediaId is null ? "no" : "yes", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine($"Create {count} {CharacterTypeText(request.CharacterType)} avatar image{(count == 1 ? string.Empty : "s")}.");
        sb.AppendLine($"Camera shot: {CameraShotText(request.CameraAngle)}. Main outfit: {OutfitText(request.Outfit)}.");
        sb.AppendLine("Make the result sharp, premium, cinematic, friendly, and suitable for a TodoX profile or brand avatar.");
        sb.AppendLine();
        sb.AppendLine(resolved.Trim());
        return sb.ToString();
    }

    private static string EnsureRenderDirectives(string prompt, ImageAICreativeRenderRequest request, int count)
    {
        var sb = new StringBuilder(prompt.Trim());
        if (!prompt.Contains("Image count requirements", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Image count requirements:");
            sb.AppendLine($"Create exactly {count} distinct avatar image variation{(count == 1 ? string.Empty : "s")}.");
            sb.AppendLine("Each variation must keep the same character identity and reference constraints, but vary pose, camera angle, expression, composition, lighting, or product interaction.");
            sb.AppendLine($"Return {count} final image{(count == 1 ? string.Empty : "s")}.");
        }

        if (request.ProductMediaId is not null && !prompt.Contains("PRODUCT MUST APPEAR IN THE FINAL IMAGE", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("PRODUCT MUST APPEAR IN THE FINAL IMAGE:");
            sb.AppendLine("The product reference is not optional. The final image must clearly show the exact product inside the visible frame.");
            sb.AppendLine("The product must be recognizable by shape, color, label, packaging, logo, and key visual details.");
            sb.AppendLine("The character must hold the product, point to it, stand next to it, or present it on a small pedestal in the foreground.");
            sb.AppendLine("Do not omit the product. Do not hide it in the background. Do not replace it with a generic similar object.");
        }

        if (request.UniformMediaId is not null && !prompt.Contains("UNIFORM MUST MATCH REFERENCE", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("UNIFORM MUST MATCH REFERENCE:");
            sb.AppendLine("Use the uniform/clothing reference for the character outfit. Preserve the main garment shape, colors, logo placement, material impression, and recognizable brand details.");
        }

        return sb.ToString().Trim();
    }

    private async Task InsertGenerationAsync(Guid id, ImageAICreativeRenderRequest request, string prompt, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_chibi_generations
                (id, tenant_id, user_id, status, prompt, generated_prompt, gender,
                 reference_avatar_media_id, reference_logo_media_id, reference_product_media_id, reference_uniform_media_id, reference_scene_media_id, created_at)
            VALUES
                (@id, @tenant, @uid, 'processing', @prompt, @prompt, @gender,
                 @avatar, @logo, @product, @uniform, @scene, now());
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                uid = request.UserId,
                prompt,
                gender = request.Gender,
                avatar = request.AvatarMediaId,
                logo = request.LogoMediaId,
                product = request.ProductMediaId,
                uniform = request.UniformMediaId,
                scene = request.SceneMediaId
            });
    }

    private async Task<Guid> InsertRenderAsync(Guid generationId, Guid userId, Guid? mediaId, string? url,
        string promptInput, string promptUsed, string model, CancellationToken ct, string status = "completed", string? error = null)
    {
        using var conn = await _factory.OpenAsync(ct);
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_avatar_renders
                (id, tenant_id, user_id, generation_id, media_id, image_url, prompt_input, prompt_used, model, status, error_message, created_at)
            VALUES
                (@id, @tenant, @uid, @gen, @media, @url, @pin, @pused, @model, @status, @err, now());
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                uid = userId,
                gen = generationId,
                media = mediaId,
                url,
                pin = promptInput,
                pused = promptUsed,
                model,
                status,
                err = error
            });
        return id;
    }

    private async Task CompleteGenerationAsync(Guid id, string status, List<ImageAICreativeRenderImage> images, string? error, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        var json = JsonSerializer.Serialize(images.Select(i => new { renderId = i.RenderId, mediaId = i.MediaId, url = i.Url, promptUsed = i.PromptUsed }));
        await conn.ExecuteAsync(
            """
            UPDATE auth.user_chibi_generations
               SET status=@status, result=@json::jsonb, error_message=@err, completed_at=now()
             WHERE id=@id;
            """,
            new { id, status, json, err = error });
    }

    private static object BuildActivityInput(ImageAICreativeRenderRequest request, int count) => new
    {
        request.UserId,
        request.CustomerId,
        request.Scenario,
        renderEngineMode = ChibiRenderEngineModes.ImageAiCreative,
        request.CharacterType,
        request.Gender,
        request.CameraAngle,
        request.Outfit,
        quantity = count,
        references = new
        {
            avatar = request.AvatarMediaId,
            logo = request.LogoMediaId,
            product = request.ProductMediaId,
            productUrl = request.ProductImageUrl,
            uniform = request.UniformMediaId,
            scene = request.SceneMediaId
        },
        userEditedPrompt = !string.IsNullOrWhiteSpace(request.PromptOverride)
    };

    private static List<ChibiImage> ToChibiImages(IEnumerable<ImageAICreativeRenderImage> images)
        => images.Select(x => new ChibiImage
        {
            Index = x.Index,
            RenderId = x.RenderId ?? Guid.Empty,
            MediaId = x.MediaId ?? Guid.Empty,
            Url = x.Url,
            PromptInput = x.PromptInput,
            PromptUsed = x.PromptUsed,
            Status = x.Status,
            Error = x.Error,
            LogCode = x.LogCode,
            RenderEngineMode = ChibiRenderEngineModes.ImageAiCreative
        }).ToList();

    private static bool IsFixedAssetMode(ImageAICreativeRenderRequest request, IReadOnlyList<ReferenceImage> refs)
        => request.PreserveFixedAssets
           || request.CharacterType?.Equals("service_poster", StringComparison.OrdinalIgnoreCase) == true
           || refs.Any(x => x.Role?.Equals("brand_robot", StringComparison.OrdinalIgnoreCase) == true
               || x.Role?.Equals("fixed_overlay", StringComparison.OrdinalIgnoreCase) == true);

    private static List<string> BuildFixedAssetVariations(string prompt, int count)
    {
        var styles = new[]
        {
            "Variation lighting: balanced gold rim light with clean high-contrast background.",
            "Variation lighting: slightly brighter gold data flow and deeper black background.",
            "Variation lighting: softer dashboard glow with the same fixed brand asset placement.",
            "Variation lighting: more cinematic depth while preserving the fixed brand asset unchanged."
        };

        return Enumerable.Range(0, count)
            .Select(i => prompt.Trim() + Environment.NewLine + styles[i % styles.Length])
            .ToList();
    }

    private static List<string> BuildFallbackVariations(string prompt, int count)
    {
        var styles = new[]
        {
            "Variation pose: friendly front-facing half-body pose with warm studio lighting.",
            "Variation pose: cheerful three-quarter angle with the character naturally presenting the product.",
            "Variation pose: confident full-body mascot stance with clean premium background depth.",
            "Variation pose: playful close portrait with expressive eyes and subtle brand details."
        };

        return Enumerable.Range(0, count)
            .Select(i => EnsureVariationDirective(prompt + Environment.NewLine + styles[i % styles.Length], i + 1, count))
            .ToList();
    }

    private static string EnsureVariationDirective(string prompt, int index, int count)
        => prompt.Trim() + Environment.NewLine + Environment.NewLine
           + $"Variation #{index} of {count}: produce exactly one final PNG image for this variation. Keep the same identity and all reference constraints. If a product reference exists, keep the product clearly visible inside this variation's frame.";

    private static string CharacterTypeText(string? characterType) => characterType?.Equals("cartoon", StringComparison.OrdinalIgnoreCase) == true
        ? "modern semi-realistic cartoon character, friendly and expressive"
        : "cute 3D chibi character with large head, small body, expressive friendly face";

    private static string CameraShotText(string? cameraShot) => cameraShot switch
    {
        "close_up" => "close-up portrait",
        "full_body" => "full-body view",
        _ => "half-body view"
    };

    private static string OutfitText(string? outfit) => outfit switch
    {
        "dress" => "elegant dress suitable for a female character",
        "formal" => "professional formal office outfit",
        "tshirt" => "young dynamic T-shirt outfit",
        "swimwear" => "context-appropriate tasteful swimwear, non-explicit",
        _ => "professional elegant vest"
    };
}
