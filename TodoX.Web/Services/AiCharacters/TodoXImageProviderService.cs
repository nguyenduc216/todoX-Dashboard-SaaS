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
            _logger.LogInformation(
                "TODOX_IMAGE_PROVIDER_REQUEST model={Model} aspect={Aspect} outputFormat={OutputFormat} quality={Quality} resolution={Resolution} seed={Seed} fileCategory={FileCategory} referenceCount={ReferenceCount} referenceMediaCount={ReferenceMediaCount}",
                request.Model, request.AspectRatio, request.OutputFormat, request.Quality, request.Resolution, request.Seed, request.FileCategory, request.ReferenceImageUrls?.Length ?? 0, request.ReferenceMediaIds?.Length ?? 0);

            var avatarMediaId = request.ReferenceMediaIds?.FirstOrDefault(id => id != Guid.Empty);
            var hasReference = avatarMediaId is Guid referenceMediaId && referenceMediaId != Guid.Empty;

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
                AvatarMediaId = hasReference ? avatarMediaId : null,
                RequireReferenceImages = hasReference,
                SkipReferenceOwnershipCheck = false
            }, cancellationToken);

            _logger.LogInformation(
                "TODOX_IMAGE_PROVIDER_RENDER_RESULT status={Status} imageCount={ImageCount} usedFallback={UsedFallback} error={Error} model={Model} logCode={LogCode}",
                render.Status, render.Images.Count, render.UsedFallback, render.Error, render.RenderEngineMode, render.LogCode);

            if (render.Logs.Count > 0)
            {
                _logger.LogInformation("TODOX_IMAGE_PROVIDER_RENDER_LOGS {@Logs}", render.Logs);
            }

            var first = render.Images.FirstOrDefault(x => x.Status != "failed" && x.MediaId is not null)
                        ?? render.Images.FirstOrDefault(x => x.Status != "failed" && !string.IsNullOrWhiteSpace(x.Url));
            var renderCompleted = IsSuccessfulRenderStatus(render.Status);
            if (!renderCompleted || first is null)
            {
                _logger.LogWarning(
                    "TODOX_IMAGE_PROVIDER_NO_IMAGE status={Status} ok={Ok} imageCount={ImageCount} usedFallback={UsedFallback} error={Error}",
                    render.Status, render.Images.Count > 0, render.Images.Count, render.UsedFallback, render.Error);
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
            var imageUrl = media?.PublicUrl ?? media?.FileUrl ?? first.Url;
            return new OpenRouterImageResponse
            {
                Success = true,
                ImageUrl = imageUrl,
                ObjectKey = media?.ObjectKey,
                ResultMediaId = media?.Id ?? first.MediaId,
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
                ErrorMessage = TruncateError(ex.Message)
            };
        }
    }

    private static bool IsSuccessfulRenderStatus(string? status)
        => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    private static string TruncateError(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= 180 ? value : value[..180];
}
