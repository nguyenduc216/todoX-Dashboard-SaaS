using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Profile;

public sealed partial class ChibiAvatarService
{
    private sealed class GenerationReferenceRow
    {
        public Guid? AvatarMediaId { get; set; }
        public Guid? LogoMediaId { get; set; }
        public Guid? ProductMediaId { get; set; }
        public Guid? UniformMediaId { get; set; }
        public Guid? SceneMediaId { get; set; }
        public string? Gender { get; set; }
    }

    public async Task<ChibiGenerationDto> GenerateAsync(ChibiGenerateInput input, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var logs = new List<RenderLogEntry>();
        var logCode = AvatarRenderActivityLogService.GenerateLogCode();
        void AddLog(string step, string message, object? data = null, string level = "info")
        {
            var entry = new RenderLogEntry { Step = step, Message = message, Data = data, Level = level };
            logs.Add(entry);
            _logger.LogInformation("CHIBI_RENDER {Step} {Message} {@Data}", step, message, data);
        }

        var generationId = Guid.NewGuid();
        AddLog("LOG_CODE_GENERATED", "Generated render log code.", new { logCode });
        var engineMode = ChibiRenderEngineModes.Normalize(input.RenderEngineMode);
        AddLog("CHIBI_RENDER_ENGINE_SELECTED", "Selected chibi render engine.", new
        {
            engineMode,
            scenario = "avatar_chibi"
        });

        if (engineMode == ChibiRenderEngineModes.ImageAiCreative)
        {
            var request = MapToCreativeRequest(input, logCode);
            _logger.LogInformation("CHIBI_RENDER_CREATIVE_REQUEST model={Model} count={Count} gender={Gender} camera={CameraAngle} outfit={Outfit}", request.CharacterType, request.Count, request.Gender, request.CameraAngle, request.Outfit);
            var creativeResult = await _creativeRender.RenderAsync(request, ct);
            creativeResult.Logs.InsertRange(0, logs);
            return MapCreativeResultToChibiDto(creativeResult);
        }

        if (!string.IsNullOrWhiteSpace(input.ProductImageUrl) && input.ProductMediaId is null)
        {
            try
            {
                AddLog("PRODUCT_URL_DOWNLOAD_START", "Downloading product reference image from URL.", new { input.ProductImageUrl });
                var downloaded = await DownloadProductReferenceAsync(input, ct);
                input.ProductMediaId = downloaded?.Id;
                AddLog("PRODUCT_URL_DOWNLOAD_SUCCESS", "Downloaded product reference image from URL.", new
                {
                    input.ProductImageUrl,
                    mediaId = downloaded?.Id,
                    downloaded?.MimeType,
                    downloaded?.FileSizeBytes,
                    downloaded?.PublicUrl
                });
            }
            catch (Exception ex)
            {
                AddLog("PRODUCT_URL_DOWNLOAD_FAILED", ex.Message, new { input.ProductImageUrl }, "error");
                throw;
            }
        }

        var count = Math.Clamp(input.Count, 1, 4);
        AddLog("RENDER_REQUEST_RECEIVED", "Received chibi avatar render request.", new
        {
            renderJobId = generationId,
            engineMode,
            requestedCount = input.Count,
            imageCount = count,
            input.Gender,
            input.CameraAngle,
            hasAvatar = input.AvatarMediaId is not null,
            hasLogo = input.LogoMediaId is not null,
            hasProduct = input.ProductMediaId is not null,
            hasUniform = input.UniformMediaId is not null,
            hasScene = input.SceneMediaId is not null
        });

        var template = await GetDefaultPromptTemplateAsync(ct);
        var defaultPrompt = ResolvePromptTemplate(template, input);
        var finalPrompt = !string.IsNullOrWhiteSpace(input.PromptOverride)
            ? input.PromptOverride.Trim()
            : !string.IsNullOrWhiteSpace(input.BasePromptOverride)
                ? input.BasePromptOverride.Trim()
                : defaultPrompt;
        finalPrompt = EnsureRenderDirectives(finalPrompt, input, count);
        var refs = await BuildReferenceImagesAsync(input, ct);
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

        if (input.AvatarMediaId is not null && !refs.Any(x => x.Role == "avatar" && x.Bytes?.Length > 0))
        {
            throw new InvalidOperationException("Anh avatar da chon nhung he thong khong doc duoc noi dung anh de gui sang Vertex.");
        }

        AddLog("PROMPT_BUILT", "Final render prompt prepared.", new { promptLength = finalPrompt.Length, imageCount = count });
        await InsertGenerationAsync(generationId, input, finalPrompt);

        // Charge points up-front for the whole batch (customers only). Admin => no charge.
        var imageCost = await _tokenSettings.GetChibiImageCostAsync();
        var total = imageCost * count;
        var charge = await _wallet.ChargeAsync(
            input.IsCustomer ? input.CustomerId : null, input.UserId, total, count,
            "chibi_image", "google-vertex-ai", "imagen-3.0-generate-002", "Vertex-image-render",
            unit: "image", referenceId: generationId, referenceType: "chibi_generation");

        if (!charge.Ok)
        {
            await CompleteGenerationAsync(generationId, "failed", new List<ChibiImage>(), Guid.Empty, charge.Error);
            AddLog("RENDER_FAILED", charge.Error ?? "Point charge failed.", level: "error");
            await _activityLogs.WriteAsync(input.UserId, input.CustomerId, logCode, "avatar-render", "failed",
                BuildActivityInput(input, count), finalPrompt, refs, new List<ChibiImage>(), logs, charge.Error, ct);
            return new ChibiGenerationDto { Id = generationId, RenderJobId = generationId, LogCode = logCode, Status = "failed", Error = charge.Error, Logs = logs };
        }

        var images = new List<ChibiImage>();
        var prompts = new List<string> { finalPrompt };
        var fixedAssetMode = IsFixedAssetMode(input, refs);
        if (count > 1)
        {
            if (fixedAssetMode)
            {
                prompts = BuildFixedAssetVariations(finalPrompt, count);
                AddLog("GEMINI_SCRIPT_SKIPPED", "Fixed asset/service poster mode uses deterministic prompt variations.", new { count = prompts.Count });
            }
            else
            {
                try
                {
                    AddLog("GEMINI_SCRIPT_REQUEST", "Requesting Gemini prompt variations.", new { model = _gemini.ModelCode, imageCount = count });
                    prompts = await _gemini.GenerateVariationsAsync(finalPrompt, count, ct);
                    AddLog("GEMINI_SCRIPT_RESPONSE", "Gemini returned prompt variations.", new { count = prompts.Count, lengths = prompts.Select(x => x.Length).ToArray() });
                }
                catch (Exception ex)
                {
                    prompts = BuildFallbackVariations(finalPrompt, count);
                    AddLog("GEMINI_SCRIPT_FALLBACK", "Gemini variation generation failed; using deterministic prompt variations.", new { error = ex.Message, count = prompts.Count }, "warning");
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            var promptUsed = EnsureVariationDirective(prompts[Math.Min(i, prompts.Count - 1)], i + 1, count);
            AddLog("GEMINI_IMAGE_REQUEST", $"Rendering variation {i + 1}/{count}.", new
            {
                variationIndex = i + 1,
                promptLength = promptUsed.Length,
                referenceCount = refs.Count
            });

            var result = await _render.RenderAsync(new ImageRenderRequestModel
            {
                CorrelationId = generationId,
                Prompt = promptUsed,
                ReferenceImages = refs,
                Count = 1,
                ImageCount = count,
                VariationIndex = i + 1,
                Gender = input.Gender,
                CharacterType = input.CharacterType,
                RenderPipeline = fixedAssetMode
                    ? ImageRenderRequestModel.PipelineBackgroundThenComposite
                    : ImageRenderRequestModel.PipelineModelGenerate,
                PreserveFixedAssets = fixedAssetMode,
                Theme = fixedAssetMode ? "yellow_black" : null,
                Outfit = input.Outfit,
                CameraAngle = input.CameraAngle,
                AspectRatio = "1:1",
                MimeType = "image/png",
                UserId = input.UserId,
                CustomerId = input.CustomerId,
                FileCategory = "chibi",
                LogCode = logCode,
                RequireReferenceImages = input.AvatarMediaId is not null || refs.Count > 0
            }, ct);
            logs.AddRange(result.Logs);

            if (result.Ok && result.Data.Count > 0)
            {
                var d = result.Data[0];
                var renderId = await InsertRenderAsync(generationId, input.UserId, d.MediaId, d.Url, finalPrompt, promptUsed, result.Model);
                images.Add(new ChibiImage { Index = i, RenderId = renderId, MediaId = d.MediaId, Url = d.Url, PromptInput = finalPrompt, PromptUsed = promptUsed, LogCode = logCode, RenderEngineMode = engineMode });
                AddLog("RENDER_RESULT_STORED", $"Stored variation {i + 1}/{count}.", new { renderId, d.MediaId, d.Url, result.RequestId, result.Model });
            }
            else
            {
                var renderId = await InsertRenderAsync(generationId, input.UserId, null, null, finalPrompt, promptUsed, result.Model, "failed", result.Error);
                images.Add(new ChibiImage { Index = i, RenderId = renderId, Status = "failed", PromptInput = finalPrompt, PromptUsed = promptUsed, Error = result.Error, LogCode = logCode, RenderEngineMode = engineMode });
                AddLog("GEMINI_IMAGE_RESPONSE", $"Variation {i + 1}/{count} failed.", new { renderId, result.RequestId, result.Error }, "error");
            }
        }

        var okImages = images.Where(x => x.Status != "failed").ToList();
        var status = okImages.Count > 0 ? "completed" : "failed";
        await CompleteGenerationAsync(generationId, status, okImages, Guid.Empty,
            okImages.Count == 0 ? images.FirstOrDefault(x => x.Error is not null)?.Error : null);

        // Surface the first concrete render error (full text) so the UI can show it for support.
        var firstError = images.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Error))?.Error;
        AddLog(status == "completed" ? "RENDER_COMPLETED" : "RENDER_FAILED",
            $"Chibi render job {status}.", new { renderJobId = generationId, okCount = okImages.Count, failedCount = images.Count - okImages.Count },
            status == "completed" ? "info" : "error");
        await _activityLogs.WriteAsync(input.UserId, input.CustomerId, logCode, "avatar-render", status,
            BuildActivityInput(input, count), finalPrompt, refs, images, logs, firstError, ct);

        return new ChibiGenerationDto
        {
            Id = generationId,
            RenderJobId = generationId,
            LogCode = logCode,
            RenderEngineMode = engineMode,
            Status = status,
            GeneratedPrompt = finalPrompt,
            Images = images,
            Charged = charge.Charged,
            BalanceAfter = charge.BalanceAfter,
            Error = firstError,
            CreatedAt = DateTime.UtcNow,
            Logs = logs
        };
    }

    public async Task<ChibiImage> RerenderAsync(Guid userId, Guid? customerId, bool isCustomer, Guid renderId, string prompt, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var logCode = AvatarRenderActivityLogService.GenerateLogCode();
        var logs = new List<RenderLogEntry>();
        void AddLog(string step, string message, object? data = null, string level = "info")
        {
            logs.Add(new RenderLogEntry { Step = step, Message = message, Data = data, Level = level });
            _logger.LogInformation("CHIBI_RERENDER {LogCode} {Step} {Message} {@Data}", logCode, step, message, data);
        }
        AddLog("LOG_CODE_GENERATED", "Generated rerender log code.", new { logCode, renderId });
        const string modelCode = "imagen-3.0-generate-002";

        Guid generationId;
        GenerationReferenceRow? refMeta;
        using (var conn = await _factory.OpenAsync(ct))
        {
            var gen = await conn.ExecuteScalarAsync<Guid?>(
                "SELECT generation_id FROM auth.user_avatar_renders WHERE id=@id AND user_id=@uid;",
                new { id = renderId, uid = userId });
            if (gen is null) throw new InvalidOperationException("Ban render khong thuoc ve nguoi dung.");
            generationId = gen.Value;
            refMeta = await conn.QuerySingleOrDefaultAsync<GenerationReferenceRow>(
                """
                SELECT reference_avatar_media_id AS AvatarMediaId,
                       reference_logo_media_id AS LogoMediaId,
                       reference_product_media_id AS ProductMediaId,
                       reference_uniform_media_id AS UniformMediaId,
                       reference_scene_media_id AS SceneMediaId,
                       gender AS Gender
                  FROM auth.user_chibi_generations
                 WHERE id=@generationId AND user_id=@userId;
                """,
                new { generationId, userId });
        }

        var refInput = new ChibiGenerateInput
        {
            UserId = userId,
            CustomerId = customerId,
            IsCustomer = isCustomer,
            AvatarMediaId = refMeta?.AvatarMediaId,
            LogoMediaId = refMeta?.LogoMediaId,
            ProductMediaId = refMeta?.ProductMediaId,
            UniformMediaId = refMeta?.UniformMediaId,
            SceneMediaId = refMeta?.SceneMediaId,
            Gender = refMeta?.Gender,
            Count = 1
        };
        var refs = await BuildReferenceImagesAsync(refInput, ct);
        var rerenderPrompt = EnsureRenderDirectives(prompt, refInput, 1);
        AddLog("RERENDER_REFERENCES_RESOLVED", "Resolved original reference images for rerender.", new
        {
            generationId,
            referenceCount = refs.Count,
            hasAvatar = refInput.AvatarMediaId is not null,
            hasLogo = refInput.LogoMediaId is not null,
            hasProduct = refInput.ProductMediaId is not null,
            hasUniform = refInput.UniformMediaId is not null,
            hasScene = refInput.SceneMediaId is not null
        });

        var imageCost = await _tokenSettings.GetChibiImageCostAsync();
        var charge = await _wallet.ChargeAsync(isCustomer ? customerId : null, userId, imageCost, 1,
            "chibi_image", "google-vertex-ai", "imagen-3.0-generate-002", "Vertex-image-render",
            unit: "image", referenceId: renderId, referenceType: "chibi_rerender");
        if (!charge.Ok)
        {
            AddLog("RERENDER_FAILED", charge.Error ?? "Point charge failed.", level: "error");
            DbDiagnostics.LogFieldLengths(_logger, "user_avatar_render_insert", ("model", modelCode), ("status", "failed"));
            _logger.LogInformation("CHIBI_RENDER_RERENDER_FAILED model={Model} status=failed logCode={LogCode}", "imagen-3.0-generate-002", logCode);
            await _activityLogs.WriteAsync(userId, customerId, logCode, "avatar-rerender", "failed",
                new { renderId, generationId, input = BuildActivityInput(refInput, 1) }, rerenderPrompt, refs, new List<ChibiImage>(), logs, charge.Error, ct);
            throw new InvalidOperationException(charge.Error ?? "KhÃ´ng Ä‘á»§ Ä‘iá»ƒm.");
        }

        AddLog("GEMINI_IMAGE_REQUEST", "Calling image render service for one rerender.", new { renderId, generationId, promptLength = rerenderPrompt.Length, referenceCount = refs.Count });
        var result = await _render.RenderAsync(new ImageRenderRequestModel
        {
            CorrelationId = generationId,
            LogCode = logCode,
            Prompt = rerenderPrompt,
            ReferenceImages = refs,
            Count = 1,
            ImageCount = 1,
            VariationIndex = 1,
            Gender = refInput.Gender,
            AspectRatio = "1:1",
            MimeType = "image/png",
            UserId = userId,
            CustomerId = customerId,
            FileCategory = "chibi",
            RequireReferenceImages = refs.Count > 0
        }, ct);
        logs.AddRange(result.Logs);

        if (!result.Ok || result.Data.Count == 0)
        {
            await UpdateRenderAsync(renderId, null, null, prompt, rerenderPrompt, "failed", result.Error);
            AddLog("RERENDER_FAILED", result.Error ?? "Render failed.", level: "error");
            DbDiagnostics.LogFieldLengths(_logger, "user_avatar_render_insert", ("model", modelCode), ("status", "failed"));
            _logger.LogInformation("CHIBI_RENDER_RERENDER_FAILED model={Model} status=failed logCode={LogCode}", "imagen-3.0-generate-002", logCode);
            await _activityLogs.WriteAsync(userId, customerId, logCode, "avatar-rerender", "failed",
                new { renderId, generationId, input = BuildActivityInput(refInput, 1) }, rerenderPrompt, refs, new List<ChibiImage>(), logs, result.Error, ct);
            throw new InvalidOperationException(result.Error ?? "Vertex render loi.");
        }

        var d = result.Data[0];
        await UpdateRenderAsync(renderId, d.MediaId, d.Url, prompt, rerenderPrompt, "completed", null);
        var image = new ChibiImage { RenderId = renderId, MediaId = d.MediaId, Url = d.Url, PromptInput = prompt, PromptUsed = rerenderPrompt, LogCode = logCode };
        AddLog("RERENDER_COMPLETED", "Single image rerender completed.", new { renderId, d.MediaId, d.Url });
        _logger.LogInformation("CHIBI_RENDER_RERENDER_COMPLETED model={Model} status=completed logCode={LogCode}", "imagen-3.0-generate-002", logCode);
        await _activityLogs.WriteAsync(userId, customerId, logCode, "avatar-rerender", "completed",
            new { renderId, generationId, input = BuildActivityInput(refInput, 1) }, rerenderPrompt, refs, new List<ChibiImage> { image }, logs, null, ct);
        return image;
    }

    // ---------- DB helpers ----------

    private async Task InsertGenerationAsync(Guid id, ChibiGenerateInput input, string prompt)
    {
        using var conn = await _factory.OpenAsync();
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
                id, tenant = _tenant.TenantId, uid = input.UserId, prompt, gender = input.Gender,
                avatar = input.AvatarMediaId, logo = input.LogoMediaId,
                product = input.ProductMediaId, uniform = input.UniformMediaId, scene = input.SceneMediaId
            });
    }

    private async Task<Guid> InsertRenderAsync(Guid generationId, Guid userId, Guid? mediaId, string? url,
        string promptInput, string promptUsed, string model, string status = "completed", string? error = null)
    {
        using var conn = await _factory.OpenAsync();
        var id = Guid.NewGuid();
        await conn.ExecuteAsync(
            """
            INSERT INTO auth.user_avatar_renders
                (id, tenant_id, user_id, generation_id, media_id, image_url, prompt_input, prompt_used, model, status, error_message, created_at)
            VALUES
                (@id, @tenant, @uid, @gen, @media, @url, @pin, @pused, @model, @status, @err, now());
            """,
            new { id, tenant = _tenant.TenantId, uid = userId, gen = generationId, media = mediaId,
                  url, pin = promptInput, pused = promptUsed, model, status, err = error });
        return id;
    }

    private async Task UpdateRenderAsync(Guid renderId, Guid? mediaId, string? url, string promptInput, string promptUsed, string status, string? error)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE auth.user_avatar_renders
               SET media_id=@media, image_url=@url, prompt_input=@pin, prompt_used=@pused,
                   status=@status, error_message=@err, updated_at=now()
             WHERE id=@id;
            """,
            new { id = renderId, media = mediaId, url, pin = promptInput, pused = promptUsed, status, err = error });
    }

    private async Task CompleteGenerationAsync(Guid id, string status, List<ChibiImage> images, Guid vertexRequestId, string? error)
    {
        using var conn = await _factory.OpenAsync();
        var json = JsonSerializer.Serialize(images.Select(i => new { renderId = i.RenderId, mediaId = i.MediaId, url = i.Url, promptUsed = i.PromptUsed }));
        await conn.ExecuteAsync(
            """
            UPDATE auth.user_chibi_generations
               SET status=@status, result=@json::jsonb, error_message=@err, completed_at=now()
             WHERE id=@id;
            """, new { id, status, json, err = error });
    }

    private static object BuildActivityInput(ChibiGenerateInput input, int count) => new
    {
        input.UserId,
        input.CustomerId,
        input.CharacterType,
        input.Gender,
        input.CameraAngle,
        input.Outfit,
        renderEngineMode = ChibiRenderEngineModes.Normalize(input.RenderEngineMode),
        quantity = count,
        references = new
        {
            avatar = input.AvatarMediaId,
            logo = input.LogoMediaId,
            product = input.ProductMediaId,
            productUrl = input.ProductImageUrl,
            uniform = input.UniformMediaId,
            scene = input.SceneMediaId
        },
        userEditedPrompt = !string.IsNullOrWhiteSpace(input.PromptOverride)
    };

    private static ImageAICreativeRenderRequest MapToCreativeRequest(ChibiGenerateInput input, string logCode)
    {
        return new ImageAICreativeRenderRequest
        {
            UserId = input.UserId,
            CustomerId = input.CustomerId,
            IsCustomer = input.IsCustomer,
            Scenario = "avatar_chibi",
            CharacterType = input.CharacterType,
            Gender = input.Gender,
            CameraAngle = input.CameraAngle,
            Outfit = input.Outfit,
            Count = input.Count,
            PromptTemplateKey = "avatar_chibi",
            PromptLanguage = "vi",
            BasePromptOverride = input.BasePromptOverride,
            PromptOverride = input.PromptOverride,
            AspectRatio = "1:1",
            FileCategory = "chibi",
            LogCode = logCode,
            AvatarMediaId = input.AvatarMediaId,
            LogoMediaId = input.LogoMediaId,
            ProductMediaId = input.ProductMediaId,
            ProductImageUrl = input.ProductImageUrl,
            UniformMediaId = input.UniformMediaId,
            SceneMediaId = input.SceneMediaId,
            PreserveFixedAssets = false,
            RequireReferenceImages = input.AvatarMediaId is not null
                || input.LogoMediaId is not null
                || input.ProductMediaId is not null
                || input.UniformMediaId is not null
                || input.SceneMediaId is not null
        };
    }

    private static ChibiGenerationDto MapCreativeResultToChibiDto(ImageAICreativeRenderResult result)
    {
        return new ChibiGenerationDto
        {
            Id = result.RenderJobId,
            RenderJobId = result.RenderJobId,
            LogCode = result.LogCode,
            RenderEngineMode = result.RenderEngineMode,
            Status = result.Status,
            GeneratedPrompt = result.GeneratedPrompt,
            Images = result.Images.Select(x => new ChibiImage
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
                RenderEngineMode = result.RenderEngineMode
            }).ToList(),
            CreatedAt = result.CreatedAt,
            UsedFallback = result.UsedFallback,
            Error = result.Error,
            Charged = result.Charged,
            BalanceAfter = result.BalanceAfter,
            Logs = result.Logs
        };
    }

    private static string EnsureRenderDirectives(string prompt, ChibiGenerateInput input, int count)
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

        if (input.ProductMediaId is not null
            && !prompt.Contains("PRODUCT MUST APPEAR IN THE FINAL IMAGE", StringComparison.OrdinalIgnoreCase)
            && !prompt.Contains("PRODUCT MANDATORY IN-FRAME CONSTRAINT", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("PRODUCT MUST APPEAR IN THE FINAL IMAGE:");
            sb.AppendLine("The product reference is not optional. The final image must clearly show the exact product inside the visible frame.");
            sb.AppendLine("The product must be recognizable by shape, color, label, packaging, logo, and key visual details.");
            sb.AppendLine("The character must hold the product, point to it, stand next to it, or present it on a small pedestal in the foreground.");
            sb.AppendLine("Do not omit the product. Do not hide it in the background. Do not replace it with a generic similar object.");
            sb.AppendLine("If the camera shot is close-up, keep the product partially or fully visible in the frame while preserving the character as the main subject.");
        }

        if (input.LogoMediaId is not null
            && !prompt.Contains("LOGO TRANSPARENCY CONSTRAINT", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("LOGO TRANSPARENCY CONSTRAINT:");
            sb.AppendLine("Use the logo reference without turning transparent areas into a black square or dark background. Preserve the transparent logo appearance; if a background is needed, use a clean white or transparent-safe treatment.");
        }

        if (input.UniformMediaId is not null
            && !prompt.Contains("UNIFORM MUST MATCH REFERENCE", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("UNIFORM MUST MATCH REFERENCE:");
            sb.AppendLine("Use the uniform/clothing reference for the character outfit. Preserve the main garment shape, colors, logo placement, material impression, and recognizable brand details.");
            sb.AppendLine("Do not replace the uniform with a generic hoodie, suit, or unrelated clothing style unless the reference itself shows that style.");
        }

        return sb.ToString().Trim();
    }

    private static bool IsFixedAssetMode(ChibiGenerateInput input, IReadOnlyList<ReferenceImage> refs)
    {
        return input.CharacterType?.Equals("service_poster", StringComparison.OrdinalIgnoreCase) == true
            || refs.Any(x => x.Role?.Equals("brand_robot", StringComparison.OrdinalIgnoreCase) == true
                || x.Role?.Equals("fixed_overlay", StringComparison.OrdinalIgnoreCase) == true);
    }

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

    public async Task<IReadOnlyList<ChibiGenerationDto>> GetGenerationsAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var gens = await conn.QueryAsync<(Guid Id, string Status, string? GeneratedPrompt, Guid? SelectedMediaId, DateTime CreatedAt)>(
            """
            SELECT id AS Id, status AS Status, generated_prompt AS GeneratedPrompt,
                   selected_media_id AS SelectedMediaId, created_at AS CreatedAt
              FROM auth.user_chibi_generations WHERE user_id=@uid ORDER BY created_at DESC LIMIT 20;
            """, new { uid = userId });

        var list = new List<ChibiGenerationDto>();
        foreach (var g in gens)
        {
            var renders = await conn.QueryAsync<ChibiImage>(
                """
                SELECT id AS RenderId, media_id AS MediaId, image_url AS Url,
                       prompt_input AS PromptInput, prompt_used AS PromptUsed, status AS Status
                  FROM auth.user_avatar_renders WHERE generation_id=@gid ORDER BY created_at;
                """, new { gid = g.Id });
            list.Add(new ChibiGenerationDto
            {
                Id = g.Id, Status = g.Status, GeneratedPrompt = g.GeneratedPrompt,
                SelectedMediaId = g.SelectedMediaId, CreatedAt = g.CreatedAt,
                Images = renders.ToList()
            });
        }
        return list;
    }

    public async Task SelectAsync(Guid userId, Guid generationId, Guid mediaId, CancellationToken ct = default)
    {
        using (var conn = await _factory.OpenAsync(ct))
        {
            var owns = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM media.media_files WHERE id=@m AND user_id=@u);",
                new { m = mediaId, u = userId });
            if (!owns) throw new InvalidOperationException("áº¢nh khÃ´ng thuá»™c vá» ngÆ°á»i dÃ¹ng.");

            await conn.ExecuteAsync(
                "UPDATE auth.user_chibi_generations SET status='selected', selected_media_id=@media, selected_at=now() WHERE id=@id AND user_id=@uid;",
                new { id = generationId, uid = userId, media = mediaId });
        }
        await _avatars.SetActiveFromMediaAsync(userId, mediaId, "chibi", ct);
    }
}
