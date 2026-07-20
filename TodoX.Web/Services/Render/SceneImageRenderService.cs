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
    bool QuotaError)
{
    public string? ProviderTaskId { get; init; }
    public Guid? ResultMediaId { get; init; }
    public string? BillingLogicalRequestId { get; init; }
    public decimal? EstimatedUsd { get; init; }
    public decimal? ActualUsd { get; init; }
    public decimal ChargedPoints { get; init; }
    public decimal RefundedPoints { get; init; }
    public string? CostSource { get; init; }
    public string? ProviderUsageJson { get; init; }
}

public enum ProviderImageSourceType
{
    Bytes,
    ExistingMedia,
    PublicHttpUrl,
    LocalPublicPath,
    Invalid
}

public sealed record ProviderImageOutputClassification(
    ProviderImageSourceType SourceType,
    string? ImageUrl,
    string? ObjectKey,
    Guid? MediaId,
    string? Error)
{
    public static ProviderImageOutputClassification Classify(byte[]? imageBytes, Guid? mediaId, string? objectKey, string? imageUrl)
    {
        if (imageBytes is { Length: > 0 })
        {
            return new(ProviderImageSourceType.Bytes, imageUrl, NormalizeObjectKeyOrNull(objectKey), mediaId, null);
        }

        var normalizedObjectKey = NormalizeObjectKeyOrNull(objectKey);
        var normalizedUrl = imageUrl?.Trim();
        if (mediaId is not null || !string.IsNullOrWhiteSpace(normalizedObjectKey))
        {
            return new(ProviderImageSourceType.ExistingMedia, normalizedUrl, normalizedObjectKey, mediaId, null);
        }

        if (IsPublicHttpUrl(normalizedUrl))
        {
            return new(ProviderImageSourceType.PublicHttpUrl, normalizedUrl, null, null, null);
        }

        if (TryGetLocalObjectKey(normalizedUrl, out var localObjectKey))
        {
            return new(ProviderImageSourceType.LocalPublicPath, normalizedUrl, localObjectKey, null, null);
        }

        return new(ProviderImageSourceType.Invalid, normalizedUrl, null, null, "Đường dẫn ảnh đầu ra của provider không hợp lệ.");
    }

    public static bool IsPublicHttpUrl(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool TryGetLocalObjectKey(string? value, out string? objectKey)
    {
        objectKey = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim().Replace('\\', '/');
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            text = uri.AbsolutePath;
        }

        var withoutLeadingSlash = text.TrimStart('/');
        if (withoutLeadingSlash.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            text = withoutLeadingSlash["uploads/".Length..];
        }
        else
        {
            return false;
        }

        var normalized = NormalizeObjectKeyOrNull(text);
        if (normalized is null)
        {
            return false;
        }

        objectKey = normalized;
        return true;
    }

    private static string? NormalizeObjectKeyOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["uploads/".Length..];
        }

        return string.IsNullOrWhiteSpace(normalized)
               || normalized.Contains("..", StringComparison.Ordinal)
               || Path.IsPathRooted(normalized)
            ? null
            : normalized;
    }
}

/// <summary>Everything needed to render one scene image, independent of the chosen provider.</summary>
public sealed class SceneImageRenderContext
{
    public long ProjectId { get; init; }
    public long SceneId { get; init; }
    public int SceneIndex { get; init; }
    public string Prompt { get; init; } = string.Empty;
    public string AspectRatio { get; init; } = "9:16";
    public long? CharacterId { get; init; }
    public Guid UserId { get; init; }
    public Guid? CustomerId { get; init; }
    public AiBillingTrustedPayerContext? TrustedPayerContext { get; init; }
    public string? CreatedBy { get; init; }
    public Guid? RenderJobId { get; init; }
    public string? LogicalRequestId { get; init; }
    public string? OutputObjectKey { get; init; }

    /// <summary>Media id of the character reference (Vertex path passes references by media id).</summary>
    public Guid? CharacterReferenceMediaId { get; init; }

    /// <summary>Object key of the character reference stored in TodoX media storage.</summary>
    public string? CharacterReferenceObjectKey { get; init; }

    /// <summary>Character reference image URL (OpenRouter path passes references by URL).</summary>
    public string? CharacterReferenceUrl { get; init; }
}

public interface ISceneImageRenderService
{
    /// <summary>
    /// Resolves a character's master image into a media id usable as a Vertex reference. Downloads the
    /// image into media storage when needed. Returns null when the character has no usable master image.
    /// </summary>
    Task<Guid?> ResolveCharacterReferenceMediaIdAsync(long projectId, string? masterImageUrl, string? masterImageObjectKey, Guid userId, Guid? customerId, CancellationToken ct = default);

    /// <summary>Legacy direct ImageAICreativeRender path; normal scene rendering resolves the configured provider.</summary>
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

    public async Task<Guid?> ResolveCharacterReferenceMediaIdAsync(long projectId, string? masterImageUrl, string? masterImageObjectKey, Guid userId, Guid? customerId, CancellationToken ct = default)
    {
        var classification = ProviderImageOutputClassification.Classify(null, null, masterImageObjectKey, masterImageUrl);
        if (classification.SourceType == ProviderImageSourceType.Invalid)
        {
            return null;
        }

        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            var media = classification.SourceType switch
            {
                ProviderImageSourceType.ExistingMedia when classification.MediaId is Guid mediaId => await _media.GetAsync(mediaId, ct),
                ProviderImageSourceType.ExistingMedia when !string.IsNullOrWhiteSpace(classification.ObjectKey) => await _media.GetByObjectKeyAsync(classification.ObjectKey!, ct),
                ProviderImageSourceType.LocalPublicPath when !string.IsNullOrWhiteSpace(classification.ObjectKey) => await _media.GetByObjectKeyAsync(classification.ObjectKey!, ct)
                    ?? await _media.GetByPublicUrlAsync(masterImageUrl!, ct),
                ProviderImageSourceType.PublicHttpUrl => await _media.DownloadAndSaveImageAsync(
                    masterImageUrl!,
                    $"video_scene_reference/{projectId}",
                    userId,
                    customerId,
                    _tenant.TenantId,
                    ct),
                _ => null
            };

            if (media is null)
            {
                _logger.LogWarning(
                    "SCENE_IMAGE_CHARACTER_REFERENCE_UNAVAILABLE projectId={ProjectId} sourceType={SourceType} objectKey={ObjectKey} hasUrl={HasUrl}",
                    projectId, classification.SourceType, classification.ObjectKey, !string.IsNullOrWhiteSpace(masterImageUrl));
                return null;
            }

            _logger.LogInformation(
                "SCENE_IMAGE_CHARACTER_REFERENCE_READY projectId={ProjectId} mediaId={MediaId} objectKey={ObjectKey} sourceType={SourceType}",
                projectId, media.Id, media.ObjectKey, classification.SourceType);
            return media.Id;
        }
        catch (Exception ex)
        {
            // A missing/broken reference must not abort the whole batch; render without it and log.
            _logger.LogWarning(ex,
                "SCENE_IMAGE_CHARACTER_REFERENCE_FAILED projectId={ProjectId} sourceType={SourceType} objectKey={ObjectKey} hasUrl={HasUrl}",
                projectId, classification.SourceType, classification.ObjectKey, !string.IsNullOrWhiteSpace(masterImageUrl));
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
            AspectRatio = NormalizeAspectRatio(context.AspectRatio),
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
        // Resolve the default provider for this capability and require a supported image provider.
        // No silent fallback to ImageAICreativeRender when the provider row is missing or disabled.
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

        var references = ProviderImageOutputClassification.IsPublicHttpUrl(context.CharacterReferenceUrl)
            ? new[] { context.CharacterReferenceUrl!.Trim() }
            : Array.Empty<string>();
        var referenceMediaIds = context.CharacterReferenceMediaId is Guid referenceMediaId
            ? new[] { referenceMediaId }
            : Array.Empty<Guid>();
        if (referenceMediaIds.Length > 0
            && references.Length == 0
            && !ProviderCodeMap.ToFactoryKey(option.ProviderCode).Equals("todox_image", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "SCENE_IMAGE_REFERENCE_NOT_SENT_TO_PROVIDER projectId={ProjectId} sceneId={SceneId} providerCode={ProviderCode} modelName={ModelName} referenceMediaId={ReferenceMediaId} reason=provider_requires_http_reference_url",
                context.ProjectId, context.SceneId, option.ProviderCode, option.ModelName, context.CharacterReferenceMediaId);
        }

        var hasReference = references.Length > 0 || referenceMediaIds.Length > 0;

        _logger.LogInformation(
            "SCENE_IMAGE_PROVIDER_RERENDER_START projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} characterId={CharacterId} providerCapabilityId={ProviderCapabilityId} providerCode={ProviderCode} modelName={ModelName} hasReference={HasReference}",
            context.ProjectId, context.SceneId, context.SceneIndex, context.CharacterId,
            option.ProviderCapabilityId, option.ProviderCode, option.ModelName, hasReference);

        var requestId = string.IsNullOrWhiteSpace(context.LogicalRequestId)
            ? BuildLogicalRequestId(featureCode, context.SceneId, context.RenderJobId)
            : context.LogicalRequestId;

        var render = await _imageRouter.RenderImageAsync(new AiImageRenderRequest
        {
            // Preserve the real customer scope for usage logging (Codex intent). AiImageRenderRequest.CustomerId
            // is the bigint customer convention used across the router, so convert from the Guid customer id
            // exactly like AvatarTemplateService does — never hard-code null or a fake customer.
            CustomerId = ToBigIntCustomerId(context.CustomerId),
            CustomerGuid = context.CustomerId,
            UserId = context.UserId,
            TrustedPayerContext = context.TrustedPayerContext,
            FeatureCode = featureCode,
            CapabilityCode = CapabilityCode,
            ProviderCapabilityId = option.ProviderCapabilityId,
            FromUser = false,
            Prompt = context.Prompt,
            ReferenceImageUrls = references,
            ReferenceMediaIds = referenceMediaIds,
            AspectRatio = NormalizeAspectRatio(context.AspectRatio),
            OutputFormat = "png",
            Quality = "high",
            Resolution = "4K",
            FileCategory = "video_scene_image",
            RequestId = requestId,
            LogicalRequestId = requestId,
            RenderJobId = context.RenderJobId?.ToString("N"),
            Metadata = new
            {
                projectId = context.ProjectId,
                sceneId = context.SceneId,
                sceneIndex = context.SceneIndex,
                aspectRatio = NormalizeAspectRatio(context.AspectRatio),
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

        var source = ProviderImageOutputClassification.Classify(render.ImageBytes, render.ResultMediaId, render.ObjectKey, render.ImageUrl);
        _logger.LogInformation(
            "SCENE_IMAGE_PROVIDER_OUTPUT_CLASSIFIED projectId={ProjectId} sceneId={SceneId} renderJobId={RenderJobId} providerCode={ProviderCode} providerCapabilityId={ProviderCapabilityId} modelName={ModelName} providerTaskId={ProviderTaskId} hasImageBytes={HasImageBytes} rawImageUrl={RawImageUrl} objectKey={ObjectKey} resultMediaId={ResultMediaId} sourceType={SourceType}",
            context.ProjectId, context.SceneId, context.RenderJobId, render.ProviderCode, render.ProviderCapabilityId, render.ModelName, render.ProviderTaskId,
            render.ImageBytes is { Length: > 0 }, render.ImageUrl, render.ObjectKey, render.ResultMediaId, source.SourceType);

        var persisted = await PersistProviderImageOutputAsync(context, render, source, ct);
        if (!persisted.Success)
        {
            return new SceneImageRenderOutcome(false, null, null, render.ProviderCode,
                render.ModelName, render.ProviderCapabilityId, null,
                persisted.ErrorMessage, QuotaError: false)
            {
                ProviderTaskId = render.ProviderTaskId,
                BillingLogicalRequestId = render.BillingLogicalRequestId,
                EstimatedUsd = render.EstimatedUsd,
                ActualUsd = render.ActualUsd,
                ChargedPoints = render.ChargedPoints,
                RefundedPoints = render.RefundedPoints,
                CostSource = render.CostSource,
                ProviderUsageJson = render.UsageJson
            };
        }

        if (string.IsNullOrWhiteSpace(persisted.ImageUrl))
        {
            return new SceneImageRenderOutcome(false, null, null, render.ProviderCode,
                render.ModelName, render.ProviderCapabilityId, null,
                "Provider ảnh không trả về ảnh cho scene.", QuotaError: false);
        }

        _logger.LogInformation(
            "SCENE_IMAGE_PROVIDER_RERENDER_DONE projectId={ProjectId} sceneId={SceneId} sceneIndex={SceneIndex} renderJobId={RenderJobId} providerCapabilityId={ProviderCapabilityId} providerCode={ProviderCode} modelName={ModelName} providerTaskId={ProviderTaskId} imageUrl={ImageUrl} objectKey={ObjectKey} resultMediaId={ResultMediaId} sourceType={SourceType}",
            context.ProjectId, context.SceneId, context.SceneIndex, context.RenderJobId, render.ProviderCapabilityId, render.ProviderCode, render.ModelName,
            render.ProviderTaskId, persisted.ImageUrl, persisted.ObjectKey, persisted.MediaId, source.SourceType);
        return new SceneImageRenderOutcome(true, persisted.ImageUrl, persisted.ObjectKey, render.ProviderCode,
            render.ModelName, render.ProviderCapabilityId, null, null, QuotaError: false)
        {
            ProviderTaskId = render.ProviderTaskId,
            ResultMediaId = persisted.MediaId,
            BillingLogicalRequestId = render.BillingLogicalRequestId,
            EstimatedUsd = render.EstimatedUsd,
            ActualUsd = render.ActualUsd,
            ChargedPoints = render.ChargedPoints,
            RefundedPoints = render.RefundedPoints,
            CostSource = render.CostSource,
            ProviderUsageJson = render.UsageJson
        };
    }

    private async Task<PersistedProviderImageOutput> PersistProviderImageOutputAsync(
        SceneImageRenderContext context,
        AiImageRenderResult render,
        ProviderImageOutputClassification source,
        CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        switch (source.SourceType)
        {
            case ProviderImageSourceType.Bytes:
                {
                    var media = string.IsNullOrWhiteSpace(context.OutputObjectKey)
                        ? await _media.SaveAsync(render.ImageBytes!,
                            $"scene_{context.SceneIndex:00}_{DateTime.UtcNow:yyyyMMddHHmmss}.png",
                            render.MimeType ?? "image/png", "video_scene_image",
                            context.UserId, context.CustomerId, _tenant.TenantId, ct)
                        : await _media.SaveAtObjectKeyAsync(render.ImageBytes!,
                            context.OutputObjectKey,
                            $"scene_{context.SceneIndex:00}.png",
                            render.MimeType ?? "image/png", "video_scene_image",
                            context.UserId, context.CustomerId, _tenant.TenantId, ct);
                    return PersistedProviderImageOutput.Ok(media.PublicUrl ?? media.FileUrl, media.ObjectKey, media.Id);
                }

            case ProviderImageSourceType.ExistingMedia:
            case ProviderImageSourceType.LocalPublicPath:
                {
                    var media = source.MediaId is Guid mediaId
                        ? await _media.GetAsync(mediaId, ct)
                        : null;

                    if (media is null && !string.IsNullOrWhiteSpace(source.ObjectKey))
                    {
                        media = await _media.GetByObjectKeyAsync(source.ObjectKey!, ct);
                    }

                    if (media is null && !string.IsNullOrWhiteSpace(source.ImageUrl))
                    {
                        media = await _media.GetByPublicUrlAsync(source.ImageUrl!, ct);
                    }

                    return media is null
                        ? PersistedProviderImageOutput.Fail($"Không tìm thấy media nội bộ cho ảnh scene. sourceType={source.SourceType}; providerCode={render.ProviderCode}; modelName={render.ModelName}; taskId={render.ProviderTaskId}")
                        : PersistedProviderImageOutput.Ok(media.PublicUrl ?? media.FileUrl, media.ObjectKey, media.Id);
                }

            case ProviderImageSourceType.PublicHttpUrl:
                {
                    var media = string.IsNullOrWhiteSpace(context.OutputObjectKey)
                        ? await _media.DownloadAndSaveImageAsync(source.ImageUrl!,
                            "video_scene_image", context.UserId, context.CustomerId, _tenant.TenantId, ct)
                        : await _media.DownloadAndSaveImageAtObjectKeyAsync(source.ImageUrl!, context.OutputObjectKey,
                            "video_scene_image", context.UserId, context.CustomerId, _tenant.TenantId, ct);
                    return PersistedProviderImageOutput.Ok(media.PublicUrl ?? media.FileUrl, media.ObjectKey, media.Id);
                }

            default:
                return PersistedProviderImageOutput.Fail($"{source.Error ?? "Đường dẫn ảnh đầu ra của provider không hợp lệ."} sourceType={source.SourceType}; providerCode={render.ProviderCode}; modelName={render.ModelName}; taskId={render.ProviderTaskId}");
        }
    }

    private static bool IsSucceededStatus(string? status)
        => string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeAspectRatio(string? aspectRatio)
        => string.Equals(aspectRatio, "16:9", StringComparison.Ordinal) ? "16:9" : "9:16";

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

    private sealed record PersistedProviderImageOutput(bool Success, string? ImageUrl, string? ObjectKey, Guid? MediaId, string? ErrorMessage)
    {
        public static PersistedProviderImageOutput Ok(string? imageUrl, string? objectKey, Guid? mediaId)
            => new(true, imageUrl, objectKey, mediaId, null);

        public static PersistedProviderImageOutput Fail(string message)
            => new(false, null, null, null, message);
    }
}
