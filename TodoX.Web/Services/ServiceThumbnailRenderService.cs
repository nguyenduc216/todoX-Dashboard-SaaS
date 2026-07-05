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
}

public sealed class ServiceThumbnailRenderResult
{
    public bool Ok { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Error { get; set; }
    public bool UsedMrTodoXReference { get; set; }
    public bool MissingMrTodoXAvatar { get; set; }
}

public sealed class ServiceThumbnailRenderService
{
    private readonly IImageRenderService _imageRender;
    private readonly MrTodoXAvatarService _mrTodoXAvatar;

    public ServiceThumbnailRenderService(IImageRenderService imageRender, MrTodoXAvatarService mrTodoXAvatar)
    {
        _imageRender = imageRender;
        _mrTodoXAvatar = mrTodoXAvatar;
    }

    public async Task<ServiceThumbnailRenderResult> RenderAsync(ServiceThumbnailRenderRequest request, CancellationToken ct = default)
    {
        var mrTodoX = await _mrTodoXAvatar.GetAsync(ct);
        var prompt = BuildPrompt(request, mrTodoX);
        var references = new List<ReferenceImage>();
        if (!string.IsNullOrWhiteSpace(mrTodoX.AvatarUrl))
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

        var render = await _imageRender.RenderAsync(new ImageRenderRequestModel
        {
            Prompt = prompt,
            ReferenceImages = references,
            Count = 1,
            AspectRatio = "9:16",
            MimeType = "image/png",
            FileCategory = "service_thumbnail",
            UserId = request.User?.UserId,
            CustomerId = request.User?.CustomerId,
            RequireReferenceImages = false,
            CharacterType = "Mr. todoX service presenter",
            ImageCount = 1
        }, ct);

        var first = render.Data.FirstOrDefault();
        return new ServiceThumbnailRenderResult
        {
            Ok = render.Ok && first?.Url is not null,
            Prompt = prompt,
            ImageUrl = first?.Url,
            Error = render.Error,
            UsedMrTodoXReference = references.Count > 0,
            MissingMrTodoXAvatar = string.IsNullOrWhiteSpace(mrTodoX.AvatarUrl)
        };
    }

    public async Task<string> BuildPromptPreviewAsync(ServiceThumbnailRenderRequest request, CancellationToken ct = default)
    {
        var mrTodoX = await _mrTodoXAvatar.GetAsync(ct);
        return BuildPrompt(request, mrTodoX);
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
}
