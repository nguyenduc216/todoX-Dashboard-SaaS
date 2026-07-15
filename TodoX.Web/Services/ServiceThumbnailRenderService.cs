using TodoX.Web.Models;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services;

public sealed class ServiceThumbnailRenderRequest
{
    public string ServiceCode { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string? GroupName { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public string? ShortDescription { get; set; }
    public string? DetailedDescription { get; set; }
    public CurrentUserSession? User { get; set; }
    public string? CustomPrompt { get; set; }
    public IReadOnlyList<string> ReferenceImageUrls { get; set; } = Array.Empty<string>();
    public string? BrandRobotImageUrl { get; set; }
    public bool PreserveFixedAssets { get; set; }
    public string? Theme { get; set; }
    public string? PosterTextHeadline { get; set; }
    public string? PosterTextSubheadline { get; set; }
    public string? PosterTextFooter { get; set; }
    public string AspectRatio { get; set; } = "9:16";
}

public sealed class ServiceThumbnailRenderResult
{
    public bool Ok { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public Guid? ImageMediaId { get; set; }
    public Guid? ImageRenderRequestId { get; set; }
    public long? ImageFileSizeBytes { get; set; }
    public string? Error { get; set; }
    public bool UsedMrTodoXReference { get; set; }
    public bool MissingMrTodoXAvatar { get; set; }
    public List<RenderLogEntry> ImageRenderLogs { get; set; } = new();
}

public sealed class ServiceThumbnailRenderService
{
    private readonly IImageRenderService _imageRender;
    private readonly TodoX.Web.Services.AiProviders.IAiImageRenderRouter _imageRouter;
    private readonly MrTodoXAvatarService _mrTodoXAvatar;
    private readonly TodoX.Web.Services.Media.IMediaFileService _media;
    private readonly TenantContext _tenant;

    public ServiceThumbnailRenderService(IImageRenderService imageRender,
        TodoX.Web.Services.AiProviders.IAiImageRenderRouter imageRouter,
        MrTodoXAvatarService mrTodoXAvatar,
        TodoX.Web.Services.Media.IMediaFileService media,
        TenantContext tenant)
    {
        _imageRender = imageRender;
        _imageRouter = imageRouter;
        _mrTodoXAvatar = mrTodoXAvatar;
        _media = media;
        _tenant = tenant;
    }

    public async Task<ServiceThumbnailRenderResult> RenderAsync(ServiceThumbnailRenderRequest request, CancellationToken ct = default)
    {
        var mrTodoX = await _mrTodoXAvatar.GetAsync(ct);
        var prompt = string.IsNullOrWhiteSpace(request.CustomPrompt)
            ? BuildPrompt(request, mrTodoX)
            : EnsureThumbnailDirectives(request.CustomPrompt.Trim());
        var references = new List<ReferenceImage>();
        var fixedRobotUrl = !string.IsNullOrWhiteSpace(request.BrandRobotImageUrl)
            ? request.BrandRobotImageUrl
            : request.PreserveFixedAssets ? mrTodoX.AvatarUrl : null;

        if (!string.IsNullOrWhiteSpace(fixedRobotUrl))
        {
            references.Add(new ReferenceImage
            {
                Role = "brand_robot",
                Url = fixedRobotUrl,
                SourceType = "url",
                SourceUrl = fixedRobotUrl,
                DisplayName = "TodoX brand robot",
                PromptRoleDescription = "Fixed TodoX brand robot asset. Do not send to model for redraw; composite by code."
            });
        }
        else if (!string.IsNullOrWhiteSpace(mrTodoX.AvatarUrl))
        {
            references.Add(new ReferenceImage
            {
                Role = "mr_todox",
                Url = mrTodoX.AvatarUrl,
                SourceType = "url",
                SourceUrl = mrTodoX.AvatarUrl,
                DisplayName = "Mr. todoX",
                PromptRoleDescription = "Official Mr. todoX mascot/avatar identity reference"
            });
        }

        foreach (var url in request.ReferenceImageUrls.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            references.Add(new ReferenceImage
            {
                Role = "service_reference",
                Url = url,
                SourceType = "url",
                SourceUrl = url,
                DisplayName = "Service illustration reference",
                PromptRoleDescription = "User uploaded visual reference for the service illustration"
            });
        }

        if (!request.PreserveFixedAssets)
        {
            return await RenderViaRouterAsync(request, prompt, references, mrTodoX, ct);
        }

        var render = await _imageRender.RenderAsync(new ImageRenderRequestModel
        {
            Prompt = prompt,
            ReferenceImages = references,
            Count = 1,
            AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? "9:16" : request.AspectRatio,
            MimeType = "image/png",
            FileCategory = "service_thumbnail",
            UserId = request.User?.UserId,
            CustomerId = request.User?.CustomerId,
            RequireReferenceImages = false,
            CharacterType = request.PreserveFixedAssets ? "service_poster" : "Mr. todoX service presenter",
            RenderPipeline = request.PreserveFixedAssets
                ? ImageRenderRequestModel.PipelineBackgroundThenComposite
                : ImageRenderRequestModel.PipelineModelGenerate,
            PreserveFixedAssets = request.PreserveFixedAssets,
            Theme = request.PreserveFixedAssets ? request.Theme ?? "yellow_black" : request.Theme,
            ServiceType = request.ServiceType,
            PosterTextHeadline = request.PreserveFixedAssets ? request.PosterTextHeadline : null,
            PosterTextSubheadline = request.PreserveFixedAssets ? request.PosterTextSubheadline : null,
            PosterTextFooter = request.PreserveFixedAssets ? request.PosterTextFooter : null,
            ImageCount = 1
        }, ct);

        var first = render.Data.FirstOrDefault();
        return new ServiceThumbnailRenderResult
        {
            Ok = render.Ok && first?.Url is not null,
            Prompt = prompt,
            ImageUrl = first?.Url,
            ImageMediaId = first?.MediaId,
            ImageRenderRequestId = render.RequestId,
            Error = render.Error,
            UsedMrTodoXReference = references.Count > 0,
            MissingMrTodoXAvatar = string.IsNullOrWhiteSpace(mrTodoX.AvatarUrl),
            ImageRenderLogs = render.Logs
        };
    }

    public async Task<string> BuildPromptPreviewAsync(ServiceThumbnailRenderRequest request, CancellationToken ct = default)
    {
        var mrTodoX = await _mrTodoXAvatar.GetAsync(ct);
        return BuildPrompt(request, mrTodoX);
    }

    private async Task<ServiceThumbnailRenderResult> RenderViaRouterAsync(
        ServiceThumbnailRenderRequest request,
        string prompt,
        IReadOnlyList<ReferenceImage> references,
        MrTodoXAvatarOptions mrTodoX,
        CancellationToken ct)
    {
        var render = await _imageRouter.RenderImageAsync(new TodoX.Web.Services.AiProviders.AiImageRenderRequest
        {
            CustomerId = ToBigIntCustomerId(request.User?.CustomerId),
            CustomerGuid = request.User?.CustomerId,
            UserId = request.User?.UserId,
            FeatureCode = "service_thumbnail",
            CapabilityCode = "thumbnail_generation",
            FromUser = false,
            Prompt = prompt,
            ReferenceImageUrls = references
                .Select(x => x.Url ?? x.SourceUrl)
                .Where(IsHttpUrl)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!,
            AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? "9:16" : request.AspectRatio,
            OutputFormat = "png",
            Quality = "high",
            Resolution = "1K",
            FileCategory = "service_thumbnail",
            RequestId = $"service-thumbnail-{Guid.NewGuid():N}",
            BillingExempt = request.User?.CustomerId is null,
            ExemptionReason = request.User?.CustomerId is null ? "system_service_thumbnail" : null,
            Metadata = new
            {
                request.ServiceCode,
                request.ServiceName,
                request.ServiceType,
                request.GroupName
            },
            CreatedBy = request.User?.UserId.ToString()
        }, ct);

        var imageUrl = render.ImageUrl;
        Guid? mediaId = null;
        if (render.Success && render.ImageBytes is { Length: > 0 })
        {
            await _tenant.EnsureLoadedAsync(ct);
            var media = await _media.SaveAsync(render.ImageBytes, $"service_thumbnail_{DateTime.UtcNow:yyyyMMddHHmmss}.png",
                render.MimeType ?? "image/png", "service_thumbnail", request.User?.UserId, request.User?.CustomerId, _tenant.TenantId, ct);
            imageUrl = media.PublicUrl ?? media.FileUrl;
            mediaId = media.Id;
        }

        return new ServiceThumbnailRenderResult
        {
            Ok = render.Success && !string.IsNullOrWhiteSpace(imageUrl),
            Prompt = prompt,
            ImageUrl = imageUrl,
            ImageMediaId = mediaId,
            Error = render.Success ? null : render.ErrorMessage,
            UsedMrTodoXReference = references.Count > 0,
            MissingMrTodoXAvatar = string.IsNullOrWhiteSpace(mrTodoX.AvatarUrl),
            ImageRenderLogs = new List<RenderLogEntry>
            {
                new()
                {
                    Step = "AI_PROVIDER_ROUTER_RENDER",
                    Message = "Service thumbnail rendered through AI provider router.",
                    Data = new { render.ProviderCode, render.ModelName, render.ProviderCapabilityId }
                }
            }
        };
    }

    private static string BuildPrompt(ServiceThumbnailRenderRequest request, MrTodoXAvatarOptions mrTodoX)
    {
        var referenceNote = string.IsNullOrWhiteSpace(mrTodoX.AvatarUrl)
            ? "No reference image URL is configured. Use the textual mascot description below."
            : $"Use this reference image URL for Mr. todoX character identity: {mrTodoX.AvatarUrl}";

        return $"""
        Create a professional vertical 9:16 service thumbnail for a SaaS/AI dashboard.

        Service code: {request.ServiceCode}
        Service name: {request.ServiceName}
        Service group: {request.GroupName}
        Service type: {request.ServiceType}
        Short description: {request.ShortDescription}
        Detailed description: {request.DetailedDescription}

        Mandatory character requirement:
        The image MUST include Mr. todoX, the official TodoX avatar/mascot, as a friendly AI assistant, guide, or presenter inside the scene.
        Mr. todoX should visually support or introduce the service.
        Mr. todoX must look professional, trustworthy, modern and consistent with TodoX branding.
        {referenceNote}

        Mr. todoX fallback description:
        {mrTodoX.PromptDescription}

        Visual requirements:
        - Vertical poster thumbnail, 9:16 aspect ratio.
        - Modern SaaS AI style.
        - Premium dark navy background with gold/yellow accent matching TodoX dashboard.
        - Clean composition, high contrast, suitable for a service card thumbnail.
        - No small unreadable text.
        - Avoid adding random logos.
        - Do not distort the Mr. todoX character.
        - Make it visually clear that this service is about the described functionality.
        """;
    }

    private static string EnsureThumbnailDirectives(string prompt)
    {
        return $"""
        {prompt}

        Output requirements:
        - Create exactly one professional service illustration thumbnail.
        - Vertical poster composition, clean and premium.
        - Suitable for TodoX service catalog card thumbnail.
        - Use uploaded reference images only as visual guidance; do not copy random text or unrelated logos.
        - Avoid small unreadable text.
        """;
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static long? ToBigIntCustomerId(Guid? id)
    {
        if (id is null) return null;
        var bytes = id.Value.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0);
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }
}
