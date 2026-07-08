using System.Text.Json;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Profile;

namespace TodoX.Web.Services.AiCharacters;

public interface ITodoXImageProviderService : IAiImageProviderService
{
}

public sealed class TodoXImageProviderService : ITodoXImageProviderService
{
    private readonly IImageAICreativeRenderService _creativeRender;
    private readonly IMediaFileService _media;
    private readonly ILogger<TodoXImageProviderService> _logger;

    public TodoXImageProviderService(IImageAICreativeRenderService creativeRender, IMediaFileService media, ILogger<TodoXImageProviderService> logger)
    {
        _creativeRender = creativeRender;
        _media = media;
        _logger = logger;
    }

    public async Task<OpenRouterImageResponse> GenerateImageAsync(OpenRouterImageRequest request, CancellationToken cancellationToken = default)
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            provider = "todox_image",
            request.Model,
            request.AspectRatio,
            request.OutputFormat,
            request.Quality,
            request.Resolution,
            request.Seed,
            request.FileCategory,
            referenceImageUrls = request.ReferenceImageUrls
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        try
        {
            var render = await _creativeRender.RenderAsync(new ImageAICreativeRenderRequest
            {
                UserId = request.UserId ?? Guid.Empty,
                CustomerId = request.CustomerId,
                IsCustomer = request.CustomerId is not null,
                Scenario = "ai_character",
                CharacterType = request.Model,
                Gender = null,
                CameraAngle = null,
                Outfit = null,
                Count = Math.Clamp(request.Count, 1, 4),
                PromptTemplateKey = "ai_character",
                PromptLanguage = "vi",
                PromptOverride = request.Prompt,
                AspectRatio = request.AspectRatio,
                FileCategory = string.IsNullOrWhiteSpace(request.FileCategory) ? "ai_character" : request.FileCategory,
                PreserveFixedAssets = false,
                RequireReferenceImages = false,
                SkipReferenceOwnershipCheck = true
            }, cancellationToken);

            var first = render.Images.FirstOrDefault(x => x.Status != "failed" && !string.IsNullOrWhiteSpace(x.Url));
            if (render.Status != "completed" || first is null)
            {
                return new OpenRouterImageResponse
                {
                    Success = false,
                    ProviderCode = "todox_image",
                    ModelName = render.RenderEngineMode,
                    RawRequestJson = requestJson,
                    RawResponseJson = JsonSerializer.Serialize(new
                    {
                        render.RenderJobId,
                        render.RenderEngineMode,
                        render.LogCode,
                        render.Status,
                        render.Error,
                        logs = render.Logs
                    }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    ErrorMessage = string.IsNullOrWhiteSpace(render.Error)
                        ? "ImageAICreativeRender chua tao duoc anh."
                        : render.Error
                };
            }

            var media = first.MediaId is Guid mediaId ? await _media.GetAsync(mediaId, cancellationToken) : null;
            return new OpenRouterImageResponse
            {
                Success = true,
                ImageUrl = first.Url ?? media?.PublicUrl ?? media?.FileUrl,
                ObjectKey = media?.ObjectKey,
                MimeType = media?.MimeType ?? "image/png",
                ProviderCode = "todox_image",
                ModelName = render.RenderEngineMode,
                RawRequestJson = requestJson,
                RawResponseJson = JsonSerializer.Serialize(new
                {
                    render.RenderJobId,
                    render.RenderEngineMode,
                    render.LogCode,
                    render.UsedFallback,
                    first.MediaId,
                    url = first.Url,
                    logs = render.Logs
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TODOX_IMAGE_PROVIDER_FAILED model={Model}", request.Model);
            return new OpenRouterImageResponse
            {
                Success = false,
                ProviderCode = "todox_image",
                ModelName = request.Model,
                RawRequestJson = requestJson,
                ErrorMessage = ex.Message
            };
        }
    }
}
