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
    private readonly IConfiguration _config;
    private readonly ILogger<VertexImageRenderService> _logger;

    public VertexImageRenderService(TodoXConnectionFactory factory, SettingsApiRepository settings,
        IMediaFileService media, TenantContext tenant, VertexClient vertex, IConfiguration config,
        ILogger<VertexImageRenderService> logger)
    {
        _factory = factory;
        _settings = settings;
        _media = media;
        _tenant = tenant;
        _vertex = vertex;
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

        List<byte[]> images;
        string status;
        string? error = null;

        if (mockMode)
        {
            // Explicit mock mode only: clearly labelled placeholder images.
            images = Enumerable.Range(0, count).Select(i => PlaceholderImage.Generate(request.Prompt, i)).ToList();
            result.UsedFallback = true;
            status = "mock";
            AddLog("MOCK_IMAGE_RESPONSE", "Mock mode generated placeholder images.", new { count = images.Count }, "warning");
            _logger.LogWarning("ImageRender running in MockMode — returning placeholder images.");
        }
        else
        {
            try
            {
                var hasReferences = request.ReferenceImages.Any(HasReferencePayload);
                var configuredMode = hasReferences
                    ? _config["Vertex:ReferenceRenderMode"] ?? "gemini_generate_content"
                    : "imagen_text_to_image";
                var fallbackMode = _config["Vertex:ReferenceFallbackMode"] ?? "imagen_capability_predict";
                var orderedRoles = request.ReferenceImages
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
                    foreach (var reference in request.ReferenceImages.Where(HasReferencePayload))
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
                AddLog("GEMINI_IMAGE_REQUEST", "Calling Vertex image render.", new { model = modelCode, count, request.AspectRatio, referenceCount = request.ReferenceImages.Count, mode = configuredMode });
                images = await _vertex.GenerateImagesAsync(request.Prompt, request.ReferenceImages, count, request.AspectRatio, ct);
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
            AddLog("RENDER_RESULT_STORED", "Generated image stored as media.", new { index = i, saved.Id, saved.PublicUrl, saved.MimeType, saved.FileSizeBytes });
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
}
