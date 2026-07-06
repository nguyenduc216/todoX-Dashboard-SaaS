using System.Diagnostics;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Settings;

namespace TodoX.Web.Services.ImageRender;

/// <summary>
/// Implements POST /api/render/image/vertex. Reads endpoint/provider/model from settings.api_*,
/// calls Google Vertex AI Imagen (real), persists outputs to media.media_files, records the
/// request in render.image_render_requests and a call log in settings.api_endpoint_logs.
/// Falls back to a locally generated placeholder image when Vertex credentials/network are
/// unavailable, so the flow is testable in dev; the fallback is clearly flagged and logged.
/// </summary>
public sealed class VertexImageRenderService : IImageRenderService
{
    private const string EndpointCode = "Vertex-image-render";

    private readonly TodoXConnectionFactory _factory;
    private readonly SettingsApiRepository _settings;
    private readonly IMediaFileService _media;
    private readonly TenantContext _tenant;
    private readonly VertexClient _vertex;
    private readonly IBrandAssetCompositeService _brandComposite;
    private readonly IConfiguration _config;
    private readonly ILogger<VertexImageRenderService> _logger;

    public VertexImageRenderService(TodoXConnectionFactory factory, SettingsApiRepository settings,
        IMediaFileService media, TenantContext tenant, VertexClient vertex, IBrandAssetCompositeService brandComposite, IConfiguration config,
        ILogger<VertexImageRenderService> logger)
    {
        _factory = factory;
        _settings = settings;
        _media = media;
        _tenant = tenant;
        _vertex = vertex;
        _brandComposite = brandComposite;
        _config = config;
        _logger = logger;
    }

    public async Task<ImageRenderResult> RenderAsync(ImageRenderRequestModel request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var sw = Stopwatch.StartNew();
        var requestId = Guid.NewGuid();
        var correlationId = request.CorrelationId ?? requestId;
        var count = Math.Clamp(request.Count, 1, 4);

        // Resolve endpoint -> provider -> model from settings.api_*.
        var endpoint = await _settings.GetEndpointAsync(EndpointCode);
        var provider = endpoint?.ProviderId is Guid pid ? await _settings.GetProviderAsync(pid) : null;
        var model = endpoint?.DefaultModelId is Guid mid ? await _settings.GetModelAsync(mid) : null;
        var providerCode = provider?.ProviderCode ?? "google-vertex-ai";
        var modelCode = model?.ModelCode ?? "imagen-3.0-generate-001";

        var result = new ImageRenderResult { RequestId = requestId, Provider = providerCode, Model = modelCode };
        void AddLog(string step, string message, object? data = null, string level = "info")
        {
            var entry = new RenderLogEntry { Step = step, Message = message, Data = data, Level = level };
            result.Logs.Add(entry);
            _logger.LogInformation("IMAGE_RENDER correlationId={CorrelationId} requestId={RequestId} step={Step} message={Message} data={@Data}",
                correlationId, requestId, step, message, data);
        }
        AddLog("RENDER_REQUEST_RECEIVED", "Image render request received.", new
        {
            correlationId,
            requestId,
            request.LogCode,
            count,
            request.ImageCount,
            request.VariationIndex,
            request.RenderPipeline,
            request.PreserveFixedAssets,
            request.Theme,
            request.Gender,
            request.CharacterType,
            request.Outfit,
            request.CameraAngle,
            referenceCount = request.ReferenceImages.Count,
            promptLength = request.Prompt.Length
        });
        foreach (var reference in request.ReferenceImages)
        {
            AddLog("REFERENCE_IMAGE_RECEIVED", $"Reference image queued: {reference.Role}.", new
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
                reference.FileName,
                reference.DisplayName,
                reference.PromptRoleDescription,
                base64Length = reference.Base64?.Length ?? 0,
                byteLength = reference.Bytes?.Length ?? 0
            });
            if (!HasReferencePayload(reference))
            {
                AddLog("REFERENCE_IMAGE_DROPPED", $"Reference image dropped: {reference.Role}.", new
                {
                    reference.Role,
                    reference.MediaId,
                    reason = "missing bytes/base64/url"
                }, "warning");
            }
        }

        // Insert the render request row (status=processing).
        await InsertRequestAsync(requestId, request, providerCode, modelCode, count);

        if (request.RequireReferenceImages && !request.ReferenceImages.Any(HasReferencePayload))
        {
            var message = "Request yeu cau anh tham chieu nhung khong co bytes/base64/url hop le de gui sang Vertex.";
            result.Ok = false;
            result.Error = message;
            AddLog("RENDER_FAILED", message, level: "error");
            await CompleteRequestAsync(requestId, "failed", new List<Guid>(), message);
            await _settings.LogCallAsync(_tenant.TenantId, request.UserId, EndpointCode, providerCode, modelCode,
                requestId, "failed", message, (int)sw.ElapsedMilliseconds);
            return result;
        }

        var mockMode = _config.GetValue("ImageRender:MockMode", false);
        var fixedAssets = request.ReferenceImages
            .Where(IsFixedAssetRole)
            .Where(HasReferencePayload)
            .ToList();
        var useFixedAssetPipeline = request.RenderPipeline.Equals(ImageRenderRequestModel.PipelineBackgroundThenComposite, StringComparison.OrdinalIgnoreCase)
            || request.PreserveFixedAssets
            || fixedAssets.Count > 0;
        var modelReferences = useFixedAssetPipeline
            ? request.ReferenceImages.Where(x => !IsFixedAssetRole(x)).ToList()
            : request.ReferenceImages;

        List<byte[]> images;
        string status;
        string? error = null;

        if (mockMode)
        {
            // Explicit mock mode only: clearly labelled placeholder images.
            images = Enumerable.Range(0, count)
                .Select(i => PlaceholderImage.Generate(useFixedAssetPipeline ? BuildBackgroundOnlyPrompt(request) : request.Prompt, i))
                .ToList();
            if (useFixedAssetPipeline && fixedAssets.Count > 0)
            {
                images = await CompositeFixedAssetsAsync(images, fixedAssets[0], request, AddLog, ct);
            }
            result.UsedFallback = true;
            status = "mock";
            AddLog("MOCK_IMAGE_RESPONSE", "Mock mode generated placeholder images.", new { count = images.Count }, "warning");
            _logger.LogWarning("ImageRender running in MockMode — returning placeholder images.");
        }
        else
        {
            try
            {
                if (useFixedAssetPipeline)
                {
                    if (fixedAssets.Count == 0)
                    {
                        throw new InvalidOperationException("Fixed asset pipeline requires a brand_robot or fixed_overlay reference image.");
                    }

                    var fixedAsset = fixedAssets[0];
                    var backgroundPrompt = BuildBackgroundOnlyPrompt(request);
                    AddLog("FIXED_ASSET_PIPELINE_SELECTED", "Selected background_then_composite pipeline.", new
                    {
                        fixedAsset.Role,
                        fixedAsset.MediaId,
                        fixedAsset.Url,
                        request.RenderPipeline,
                        request.PreserveFixedAssets,
                        request.Theme,
                        modelReferenceCount = modelReferences.Count
                    });
                    AddLog("BACKGROUND_ONLY_PROMPT_BUILT", "Built background-only prompt for fixed asset pipeline.", new
                    {
                        promptLength = backgroundPrompt.Length,
                        prompt = backgroundPrompt
                    });
                    AddLog("BACKGROUND_RENDER_REQUEST", "Calling Vertex for background only.", new
                    {
                        model = modelCode,
                        count,
                        request.AspectRatio,
                        referenceCount = modelReferences.Count,
                        orderedRoles = modelReferences.Where(HasReferencePayload).Select(x => x.Role ?? "reference").ToArray()
                    });

                    var backgroundImages = await _vertex.GenerateImagesAsync(backgroundPrompt, modelReferences, count, request.AspectRatio, ct);
                    result.Model = _vertex.LastModelUsed ?? modelCode;
                    modelCode = result.Model;
                    result.UsedFallback = false;
                    AddLog("BACKGROUND_RENDER_RESPONSE", "Vertex returned background images.", new
                    {
                        model = modelCode,
                        count = backgroundImages.Count,
                        byteLengths = backgroundImages.Select(x => x.Length).ToArray(),
                        mode = _vertex.LastRenderModeUsed
                    });

                    images = await CompositeFixedAssetsAsync(backgroundImages, fixedAsset, request, AddLog, ct);
                    status = "success";
                }
                else
                {
                    var hasReferences = modelReferences.Any(HasReferencePayload);
                    var configuredMode = hasReferences
                        ? _config["Vertex:ReferenceRenderMode"] ?? "gemini_generate_content"
                        : "imagen_text_to_image";
                    var fallbackMode = _config["Vertex:ReferenceFallbackMode"] ?? "imagen_capability_predict";
                    var orderedRoles = modelReferences
                        .Where(HasReferencePayload)
                        .Select(x => x.Role ?? "reference")
                        .ToArray();
                    AddLog("REFERENCE_RENDER_MODE_SELECTED", "Selected image render mode.", new
                    {
                        mode = configuredMode,
                        fallbackMode,
                        referenceCount = orderedRoles.Length,
                        orderedRoles
                    });
                    if (hasReferences && (configuredMode.Equals("gemini_generate_content", StringComparison.OrdinalIgnoreCase)
                        || configuredMode.Equals("auto", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var reference in modelReferences.Where(HasReferencePayload))
                        {
                            AddLog("REFERENCE_INLINE_DATA_PART_ADDED", $"Reference inlineData prepared: {reference.Role}.", new
                            {
                                reference.Role,
                                reference.MediaId,
                                mimeType = string.IsNullOrWhiteSpace(reference.MimeType) ? "image/png" : reference.MimeType,
                                base64Length = reference.Base64?.Length ?? 0,
                                byteLength = reference.Bytes?.Length ?? 0,
                                sentAsInlineData = true
                            });
                        }

                        AddLog("GEMINI_GENERATE_CONTENT_REQUEST", "Calling Gemini image generateContent.", new
                        {
                            model = _config["Vertex:GeminiImageModel"] ?? "gemini-2.5-flash-image",
                            count,
                            request.AspectRatio,
                            referenceCount = orderedRoles.Length,
                            orderedRoles
                        });
                    }
                    AddLog("GEMINI_IMAGE_REQUEST", "Calling Vertex image render.", new { model = modelCode, count, request.AspectRatio, referenceCount = modelReferences.Count, mode = configuredMode });
                    images = await _vertex.GenerateImagesAsync(request.Prompt, modelReferences, count, request.AspectRatio, ct);
                    result.Model = _vertex.LastModelUsed ?? modelCode;
                    modelCode = result.Model;
                    result.UsedFallback = false;
                    status = "success";
                    var actualMode = _vertex.LastRenderModeUsed ?? configuredMode;
                    if (actualMode.Equals("gemini_generate_content", StringComparison.OrdinalIgnoreCase))
                    {
                        AddLog("GEMINI_GENERATE_CONTENT_RESPONSE", "Gemini generateContent returned images.", new { model = modelCode, count = images.Count, byteLengths = images.Select(x => x.Length).ToArray(), mode = actualMode });
                    }
                    AddLog("GEMINI_IMAGE_RESPONSE", "Vertex image render returned images.", new { model = modelCode, count = images.Count, byteLengths = images.Select(x => x.Length).ToArray(), mode = actualMode });
                }
            }
            catch (Exception ex)
            {
                // Real mode: do NOT fabricate images. Fail clearly and record the error.
                _logger.LogError(ex, "Vertex image render failed (MockMode=false).");
                sw.Stop();
                result.Ok = false;
                result.Error = ex.Message;
                if (ex.Message.Contains("generateContent", StringComparison.OrdinalIgnoreCase)
                    && ex.Message.Contains("khong tra ve anh", StringComparison.OrdinalIgnoreCase))
                {
                    AddLog("GEMINI_GENERATE_CONTENT_NO_IMAGE", ex.Message, level: "error");
                }
                AddLog("RENDER_FAILED", ex.Message, level: "error");
                await CompleteRequestAsync(requestId, "failed", new List<Guid>(), ex.Message);
                await _settings.LogCallAsync(_tenant.TenantId, request.UserId, EndpointCode, providerCode, modelCode,
                    requestId, "failed", ex.Message, (int)sw.ElapsedMilliseconds);
                return result;
            }
        }

        // Persist generated images to media.media_files.
        var mediaIds = new List<Guid>();
        for (var i = 0; i < images.Count; i++)
        {
            var saved = await _media.SaveAsync(images[i], $"render_{requestId:N}_{i}.png", "image/png",
                request.FileCategory, request.UserId, request.CustomerId, _tenant.TenantId, ct);
            mediaIds.Add(saved.Id);
            result.Data.Add(new GeneratedImage { Index = i, MediaId = saved.Id, Url = saved.PublicUrl });
            AddLog(useFixedAssetPipeline ? "FINAL_POSTER_STORED" : "RENDER_RESULT_STORED",
                "Generated image stored as media.", new { index = i, saved.Id, saved.PublicUrl, saved.MimeType, saved.FileSizeBytes });
        }

        result.Ok = images.Count > 0;
        sw.Stop();

        await CompleteRequestAsync(requestId, status, mediaIds, error);
        await _settings.LogCallAsync(_tenant.TenantId, request.UserId, EndpointCode, providerCode, modelCode,
            requestId, status, error, (int)sw.ElapsedMilliseconds);
        AddLog("RENDER_COMPLETED", "Image render request completed.", new { status, elapsedMs = sw.ElapsedMilliseconds, mediaIds });

        return result;
    }

    private async Task InsertRequestAsync(Guid id, ImageRenderRequestModel r, string providerCode, string modelCode, int count)
    {
        using var conn = await _factory.OpenAsync();
        var refJson = JsonSerializer.Serialize(r.ReferenceImages.Select(x => new
        {
            x.Role,
            x.MediaId,
            x.Url,
            x.MimeType,
            x.SizeBytes,
            x.Width,
            x.Height,
            x.HasAlpha,
            x.ObjectKey,
            x.SourceType,
            x.SourceUrl,
            HasBytes = x.Bytes?.Length > 0,
            ByteLength = x.Bytes?.Length ?? 0,
            HasBase64 = !string.IsNullOrWhiteSpace(x.Base64),
            Base64Length = x.Base64?.Length ?? 0,
            x.FileName,
            x.DisplayName,
            x.PromptRoleDescription
        }));
        await conn.ExecuteAsync(
            """
            INSERT INTO render.image_render_requests
                (id, tenant_id, user_id, customer_id, endpoint_code, provider_code, model_code, status,
                 prompt, reference_images, count, aspect_ratio, mime_type, safety_level, created_at)
            VALUES
                (@id, @tenant, @user, @customer, @endpoint, @provider, @model, 'processing',
                 @prompt, @refs::jsonb, @count, @aspect, @mime, @safety, now());
            """,
            new
            {
                id, tenant = _tenant.TenantId, user = r.UserId, customer = r.CustomerId,
                endpoint = EndpointCode, provider = providerCode, model = modelCode,
                prompt = r.Prompt, refs = refJson, count, aspect = r.AspectRatio,
                mime = r.MimeType, safety = r.SafetyLevel
            });
    }

    private async Task CompleteRequestAsync(Guid id, string status, List<Guid> mediaIds, string? error)
    {
        using var conn = await _factory.OpenAsync();
        await conn.ExecuteAsync(
            """
            UPDATE render.image_render_requests
               SET status=@status, result_media_ids=@ids::jsonb, error_message=@err, completed_at=now()
             WHERE id=@id;
            """,
            new { id, status = status == "success" ? "completed" : status,
                  ids = JsonSerializer.Serialize(mediaIds), err = error });
    }

    private static bool HasReferencePayload(ReferenceImage image)
    {
        return image.Bytes?.Length > 0
            || !string.IsNullOrWhiteSpace(image.Base64)
            || !string.IsNullOrWhiteSpace(image.Url);
    }

    private static bool IsFixedAssetRole(ReferenceImage image)
    {
        return image.Role?.Equals("brand_robot", StringComparison.OrdinalIgnoreCase) == true
            || image.Role?.Equals("fixed_overlay", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string BuildBackgroundOnlyPrompt(ImageRenderRequestModel request)
    {
        var theme = request.Theme?.Equals("yellow_black", StringComparison.OrdinalIgnoreCase) == true
            ? "black and gold"
            : string.IsNullOrWhiteSpace(request.Theme) ? "black and gold" : request.Theme;

        return $"""
        Create a premium vertical 9:16 service poster background only.
        No robot. No mascot. No character. No human.
        Theme: {theme}, futuristic AI automation, digital marketing.
        Left side: TikTok source video frame.
        Right side: Facebook destination feed frame.
        Show bright gold data arrows and streaming media flow from TikTok to Facebook.
        Add subtle dashboard and growth chart elements.
        Leave clean empty center or bottom-center space for a brand robot asset to be composited later.
        Avoid small unreadable text.

        User service intent:
        {request.Prompt}
        """;
    }

    private async Task<List<byte[]>> CompositeFixedAssetsAsync(
        IReadOnlyList<byte[]> backgroundImages,
        ReferenceImage fixedAsset,
        ImageRenderRequestModel request,
        Action<string, string, object?, string> addLog,
        CancellationToken ct)
    {
        var output = new List<byte[]>(backgroundImages.Count);
        foreach (var background in backgroundImages)
        {
            addLog("FIXED_ASSET_LOADED", "Fixed asset selected for code composite.", new
            {
                fixedAsset.Role,
                fixedAsset.MediaId,
                fixedAsset.Url,
                fixedAsset.MimeType,
                byteLength = fixedAsset.Bytes?.Length ?? (string.IsNullOrWhiteSpace(fixedAsset.Base64) ? 0 : fixedAsset.Base64.Length)
            }, "info");

            var compositeRequest = new BrandAssetCompositeRequest
            {
                BackgroundBytes = background,
                MainAsset = fixedAsset,
                AspectRatio = request.AspectRatio,
                Theme = request.Theme ?? "yellow_black",
                Headline = request.PosterTextHeadline,
                Subheadline = request.PosterTextSubheadline,
                Footer = request.PosterTextFooter
            };

            var finalBytes = await _brandComposite.ComposeServicePosterAsync(compositeRequest, ct);
            output.Add(finalBytes);
            addLog("FIXED_ASSET_COMPOSITED", "Fixed asset composited into background by code.", new
            {
                fixedAsset.Role,
                fixedAsset.MediaId,
                loadedByteLength = compositeRequest.LoadedAssetByteLength,
                canvasWidth = compositeRequest.Placement?.CanvasWidth,
                canvasHeight = compositeRequest.Placement?.CanvasHeight,
                robotPlacement = compositeRequest.Placement is null ? null : new
                {
                    x = compositeRequest.Placement.AssetX,
                    y = compositeRequest.Placement.AssetY,
                    w = compositeRequest.Placement.AssetWidth,
                    h = compositeRequest.Placement.AssetHeight
                },
                finalByteLength = finalBytes.Length
            }, "info");
            addLog("TEXT_OVERLAY_APPLIED", "Poster text overlay applied by code.", new
            {
                headline = string.IsNullOrWhiteSpace(request.PosterTextHeadline) ? "REUP VIDEO TIKTOK" : request.PosterTextHeadline,
                subheadline = string.IsNullOrWhiteSpace(request.PosterTextSubheadline) ? "SANG FACEBOOK" : request.PosterTextSubheadline,
                footer = string.IsNullOrWhiteSpace(request.PosterTextFooter) ? "XÂY DỰNG KÊNH TỰ ĐỘNG" : request.PosterTextFooter
            }, "info");
        }

        return output;
    }
}
