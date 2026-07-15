using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Profile;

namespace TodoX.Web.Services.Render;

/// <summary>Result of rendering a single scene's static image, provider-agnostic.</summary>
public sealed record SceneImageRenderOutcome(
    bool Success,
    string? ImageUrl,
    string? ObjectKey,
    string? ProviderCode,
    string? ModelName,
    long? ProviderCapabilityId,
    string? RequestId,
    string? Error,
    bool QuotaError);

/// <summary>Everything needed to render one scene image, independent of the chosen provider.</summary>
public sealed class SceneImageRenderContext
{
    public long ProjectId { get; init; }
    public long SceneId { get; init; }
    public int SceneIndex { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public long? CharacterId { get; init; }
    public Guid UserId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? CreatedBy { get; init; }
    public Guid? RenderJobId { get; init; }

    /// <summary>Media id of the character reference (Vertex path passes references by media id).</summary>
    public Guid? CharacterReferenceMediaId { get; init; }

    /// <summary>Character reference image URL (OpenRouter path passes references by URL).</summary>
    public string? CharacterReferenceUrl { get; init; }
}

public interface ISceneImageRenderService
{
    /// <summary>
    /// Resolves a character's master image into a media id usable as a Vertex reference. Downloads the
    /// image into media storage when needed. Returns null when the character has no usable master image.
    /// </summary>
    Task<Guid?> ResolveCharacterReferenceMediaIdAsync(long projectId, string? masterImageUrl, Guid userId, Guid? customerId, CancellationToken ct = default);

    /// <summary>Bulk / auto path: renders one scene image on Google Cloud (Vertex) via the legacy creative engine.</summary>
    Task<SceneImageRenderOutcome> RenderSceneImageWithVertexAsync(SceneImageRenderContext context, int attempt, CancellationToken ct = default);

    Task<SceneImageRenderOutcome> RenderSceneImageAsync(SceneImageRenderContext context, CancellationToken ct = default);

    /// <summary>Manual per-scene "Render lại ảnh": renders one scene image through the configured routed provider (never falls back to Google).</summary>
    Task<SceneImageRenderOutcome> RerenderSceneImageWithOpenRouterAsync(SceneImageRenderContext context, CancellationToken ct = default);
}

/// <summary>
/// Splits scene-image rendering into two deliberately separate provider paths so a caller can never
/// accidentally pick the wrong engine:
///   - Vertex (Google Cloud) for bulk auto-render after scene split, mirroring Avatar Builder's legacy
///     <see cref="IImageAICreativeRenderService"/> flow; references are passed by media id.
///   - Routed image providers for the manual single-scene "Render lại ảnh"; the default routed capability
///     must be configured and enabled, otherwise a clear error is surfaced with no silent Google fallback.
/// Provider/model/cost always come from the AI provider configuration, never hard-coded here.
/// </summary>
public sealed class SceneImageRenderService : ISceneImageRenderService
{
    // Capability used to resolve the image provider (matches the existing scene image render call site).
    public const string CapabilityCode = "scene_image_generation";

    private readonly IImageAICreativeRenderService _creativeRender;
    private readonly IAiProviderService _providers;
    private readonly IAiImageRenderRouter _imageRouter;
    private readonly IMediaFileService _media;
    private readonly TenantContext _tenant;
    private readonly ILogger<SceneImageRenderService> _logger;

    public SceneImageRenderService(
        IImageAICreativeRenderService creativeRender,
        IAiProviderService providers,
        IAiImageRenderRouter imageRouter,
        IMediaFileService media,
        TenantContext tenant,
        ILogger<SceneImageRenderService> logger)
    {
        _creativeRender = creativeRender;
        _providers = providers;
        _imageRouter = imageRouter;
        _media = media;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<Guid?> ResolveCharacterReferenceMediaIdAsync(long projectId, string? masterImageUrl, Guid userId, Guid? customerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(masterImageUrl))
        {
            return null;
        }

        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            var media = await _media.DownloadAndSaveImageAsync(
                masterImageUrl,
                $"video_scene_reference/{projectId}",
                userId,
                customerId,
                _tenant.TenantId,
                ct);
            _logger.LogInformation(
                "SCENE_IMAGE_CHARACTER_REFERENCE_READY projectId={ProjectId} mediaId={MediaId}",
                projectId, media.Id);
            return media.Id;
        }
        catch (Exception ex)
        {
            // A missing/broken reference must not abort the whole batch; render without it and log.
            _logger.LogWarning(ex,
                "SCENE_IMAGE_CHARACTER_REFERENCE_FAILED projectId={ProjectId} url={Url}",
                projectId, masterImageUrl);
            return null;
        }
    }

    public async Task<SceneImageRenderOutcome> RenderSceneImageWithVertexAsync(SceneImageRenderContext context, int attempt, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "SCENE_IMAGE_VERTEX_RENDER_START projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} characterId={CharacterId} attempt={Attempt} hasReference={HasReference}",
            context.ProjectId, context.SceneId, context.SceneIndex, context.CharacterId, attempt, context.CharacterReferenceMediaId is not null);

        var result = await _creativeRender.RenderAsync(new ImageAICreativeRenderRequest
        {
            UserId = context.UserId,
            CustomerId = context.CustomerId,
            IsCustomer = context.CustomerId is not null,
            Scenario = "video_scene",
            Count = 1,
            PromptOverride = context.Prompt,
            AspectRatio = "9:16",
            FileCategory = "video_scene_image",
            AvatarMediaId = context.CharacterReferenceMediaId,
            RequireReferenceImages = context.CharacterReferenceMediaId is not null,
            SkipReferenceOwnershipCheck = true,
            // Charge only on the first attempt. Attempts 2+ are technical 429 backoff retries of the SAME
            // logical scene render and must not re-charge points.
            SkipPointCharge = attempt > 1
        }, ct);

        var image = result.Images.FirstOrDefault(x =>
                        !string.Equals(x.Status, "failed", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(x.Url))
                    ?? result.Images.FirstOrDefault();

        var succeeded = IsSucceededStatus(result.Status)
                        && image is not null
                        && !string.IsNullOrWhiteSpace(image.Url)
                        && !string.Equals(image.Status, "failed", StringComparison.OrdinalIgnoreCase);

        if (succeeded)
        {
            return new SceneImageRenderOutcome(true, image!.Url, null, "todox_image",
                result.RenderEngineMode, null, result.LogCode, null, QuotaError: false);
        }

        var error = result.Error ?? image?.Error;
        var quota = GoogleVertexRateLimiter.IsQuotaMessage(error);
        _logger.LogWarning(
            "SCENE_IMAGE_VERTEX_RENDER_FAILED projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} attempt={Attempt} quota={Quota} logCode={LogCode} error={Error}",
            context.ProjectId, context.SceneId, context.SceneIndex, attempt, quota, result.LogCode, error);
        return new SceneImageRenderOutcome(false, null, null, "todox_image",
            result.RenderEngineMode, null, result.LogCode, error ?? "Render ảnh scene thất bại.", quota);
    }

    public Task<SceneImageRenderOutcome> RenderSceneImageAsync(SceneImageRenderContext context, CancellationToken ct = default)
        => RenderViaRouterAsync(context, "render_job_scene_image", ct);

    public Task<SceneImageRenderOutcome> RerenderSceneImageWithOpenRouterAsync(SceneImageRenderContext context, CancellationToken ct = default)
        => RenderViaRouterAsync(context, "render_job_scene_image_rerender", ct);

    private async Task<SceneImageRenderOutcome> RenderViaRouterAsync(SceneImageRenderContext context, string featureCode, CancellationToken ct)
    {
        // Resolve the default provider for this capability and REQUIRE a routed image provider.
        // No silent fallback to Google Cloud when the routed provider is missing or disabled.
        ProviderOptionDto option;
        try
        {
            option = await _providers.ResolveProviderForCapabilityAsync(CapabilityCode, providerCapabilityId: null, fromUser: false, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SCENE_IMAGE_PROVIDER_RESOLVE_FAILED projectId={ProjectId} sceneId={SceneId}", context.ProjectId, context.SceneId);
            throw new InvalidOperationException("Chưa cấu hình provider ảnh mặc định để render lại ảnh scene.");
        }

        if (!ProviderCodeMap.IsRoutedImageProvider(option.ProviderCode))
        {
            throw new InvalidOperationException("Chưa cấu hình provider ảnh mặc định để render lại ảnh scene.");
        }

        var references = string.IsNullOrWhiteSpace(context.CharacterReferenceUrl)
            ? Array.Empty<string>()
            : new[] { context.CharacterReferenceUrl! };

        _logger.LogInformation(
            "SCENE_IMAGE_PROVIDER_RERENDER_START projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} characterId={CharacterId} providerCapabilityId={ProviderCapabilityId} providerCode={ProviderCode} modelName={ModelName} hasReference={HasReference}",
            context.ProjectId, context.SceneId, context.SceneIndex, context.CharacterId,
            option.ProviderCapabilityId, option.ProviderCode, option.ModelName, references.Length > 0);

        var requestId = BuildLogicalRequestId(featureCode, context.SceneId, context.RenderJobId);

        var render = await _imageRouter.RenderImageAsync(new AiImageRenderRequest
        {
            // Preserve the real customer scope for usage logging (Codex intent). AiImageRenderRequest.CustomerId
            // is the bigint customer convention used across the router, so convert from the Guid customer id
            // exactly like AvatarTemplateService does — never hard-code null or a fake customer.
            CustomerId = ToBigIntCustomerId(context.CustomerId),
            CustomerGuid = context.CustomerId,
            UserId = context.UserId,
            FeatureCode = featureCode,
            CapabilityCode = CapabilityCode,
            ProviderCapabilityId = option.ProviderCapabilityId,
            FromUser = false,
            Prompt = context.Prompt,
            ReferenceImageUrls = references,
            AspectRatio = "9:16",
            OutputFormat = "png",
            Quality = "high",
            Resolution = "4K",
            FileCategory = "video_scene_image",
            RequestId = requestId,
            LogicalRequestId = requestId,
            RenderJobId = context.RenderJobId?.ToString("N"),
            BillingExempt = context.CustomerId is null,
            ExemptionReason = context.CustomerId is null ? "system_scene_image_render" : null,
            Metadata = new
            {
                projectId = context.ProjectId,
                sceneId = context.SceneId,
                sceneIndex = context.SceneIndex,
                characterId = context.CharacterId,
                renderJobId = context.RenderJobId
            },
            CreatedBy = context.CreatedBy ?? context.UserId.ToString()
        }, ct);

        if (!render.Success)
        {
            _logger.LogWarning(
                "SCENE_IMAGE_PROVIDER_RERENDER_FAILED projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} providerCapabilityId={ProviderCapabilityId} providerCode={ProviderCode} modelName={ModelName} error={Error}",
                context.ProjectId, context.SceneId, context.SceneIndex, render.ProviderCapabilityId, render.ProviderCode, render.ModelName, render.ErrorMessage);
            return new SceneImageRenderOutcome(false, null, null, render.ProviderCode,
                render.ModelName, render.ProviderCapabilityId, null,
                render.ErrorMessage ?? "Render lại ảnh scene thất bại.", QuotaError: false);
        }

        // Persist bytes if the provider returned raw image data; otherwise trust the provider URL.
        var imageUrl = render.ImageUrl;
        var objectKey = render.ObjectKey;
        if (render.ImageBytes is { Length: > 0 })
        {
            await _tenant.EnsureLoadedAsync(ct);
            var media = await _media.SaveAsync(render.ImageBytes,
                $"scene_{context.SceneIndex:00}_{DateTime.UtcNow:yyyyMMddHHmmss}.png",
                render.MimeType ?? "image/png", "video_scene_image",
                context.UserId, context.CustomerId, _tenant.TenantId, ct);
            imageUrl = media.PublicUrl ?? media.FileUrl;
            objectKey = media.ObjectKey;
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return new SceneImageRenderOutcome(false, null, null, render.ProviderCode,
                render.ModelName, render.ProviderCapabilityId, null,
                "Provider ảnh không trả về ảnh cho scene.", QuotaError: false);
        }

        _logger.LogInformation(
            "SCENE_IMAGE_PROVIDER_RERENDER_DONE projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} providerCapabilityId={ProviderCapabilityId} providerCode={ProviderCode} modelName={ModelName} imageUrl={ImageUrl}",
            context.ProjectId, context.SceneId, context.SceneIndex, render.ProviderCapabilityId, render.ProviderCode, render.ModelName, imageUrl);
        return new SceneImageRenderOutcome(true, imageUrl, objectKey, render.ProviderCode,
            render.ModelName, render.ProviderCapabilityId, null, null, QuotaError: false);
    }

    private static bool IsSucceededStatus(string? status)
        => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    public static string BuildLogicalRequestId(string featureCode, long sceneId, Guid? renderJobId)
        => string.Equals(featureCode, "render_job_scene_image_rerender", StringComparison.OrdinalIgnoreCase)
            ? $"{featureCode}-scene-{sceneId}-{Guid.NewGuid():N}"
            : $"{featureCode}-job-{renderJobId?.ToString("N") ?? "direct"}-scene-{sceneId}";

    /// <summary>
    /// Converts a customer Guid into the non-negative bigint the AI provider router/usage-log uses,
    /// matching <c>AvatarTemplateService.ToBigIntCustomerId</c>. Returns null for system/admin (no customer).
    /// </summary>
    private static long? ToBigIntCustomerId(Guid? id)
    {
        if (id is null) return null;
        var bytes = id.Value.ToByteArray();
        var value = BitConverter.ToInt64(bytes, 0);
        return value == long.MinValue ? long.MaxValue : Math.Abs(value);
    }
}
