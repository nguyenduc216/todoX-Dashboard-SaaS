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
        var count = Math.Clamp(request.Count, 1, 4);

        // Resolve endpoint -> provider -> model from settings.api_*.
        var endpoint = await _settings.GetEndpointAsync(EndpointCode);
        var provider = endpoint?.ProviderId is Guid pid ? await _settings.GetProviderAsync(pid) : null;
        var model = endpoint?.DefaultModelId is Guid mid ? await _settings.GetModelAsync(mid) : null;
        var providerCode = provider?.ProviderCode ?? "google-vertex-ai";
        var modelCode = model?.ModelCode ?? "imagen-3.0-generate-001";

        var result = new ImageRenderResult { RequestId = requestId, Provider = providerCode, Model = modelCode };

        // Insert the render request row (status=processing).
        await InsertRequestAsync(requestId, request, providerCode, modelCode, count);

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
            _logger.LogWarning("ImageRender running in MockMode — returning placeholder images.");
        }
        else
        {
            try
            {
                images = await _vertex.GenerateImagesAsync(request.Prompt, count, request.AspectRatio, ct);
                result.UsedFallback = false;
                status = "success";
            }
            catch (Exception ex)
            {
                // Real mode: do NOT fabricate images. Fail clearly and record the error.
                _logger.LogError(ex, "Vertex image render failed (MockMode=false).");
                sw.Stop();
                result.Ok = false;
                result.Error = ex.Message;
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
        }

        result.Ok = images.Count > 0;
        sw.Stop();

        await CompleteRequestAsync(requestId, status, mediaIds, error);
        await _settings.LogCallAsync(_tenant.TenantId, request.UserId, EndpointCode, providerCode, modelCode,
            requestId, status, error, (int)sw.ElapsedMilliseconds);

        return result;
    }

    private async Task InsertRequestAsync(Guid id, ImageRenderRequestModel r, string providerCode, string modelCode, int count)
    {
        using var conn = await _factory.OpenAsync();
        var refJson = JsonSerializer.Serialize(r.ReferenceImages.Select(x => new { x.MediaId, x.Url }));
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
}
