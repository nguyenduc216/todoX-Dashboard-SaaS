using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiImageRenderRequest
{
    public long? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string FeatureCode { get; set; } = string.Empty;
    public string CapabilityCode { get; set; } = string.Empty;
    public long? ProviderCapabilityId { get; set; }
    /// <summary>True when the selection originated from an end-user UI (enforces allow_user_select).</summary>
    public bool FromUser { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string[] ReferenceImageUrls { get; set; } = Array.Empty<string>();
    public string AspectRatio { get; set; } = "1:1";
    public string OutputFormat { get; set; } = "png";
    public string Quality { get; set; } = "high";
    public string Resolution { get; set; } = "1K";
    public long? Seed { get; set; }
    public string FileCategory { get; set; } = "ai_image";
    public string? RequestId { get; set; }
    public string? JobId { get; set; }
    public object? Metadata { get; set; }
    public string? CreatedBy { get; set; }
}

public sealed class AiImageRenderResult
{
    public bool Success { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ObjectKey { get; set; }
    public string? MimeType { get; set; }
    public string? ProviderCode { get; set; }
    public long? ProviderId { get; set; }
    public long? ProviderCapabilityId { get; set; }
    public string? ModelName { get; set; }
    public string UnitType { get; set; } = "image";
    public decimal UnitCostPoints { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal TotalPoints { get; set; }
    public decimal? ProviderRawCost { get; set; }
    public string? RawResponseJson { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IAiImageRenderRouter
{
    Task<AiImageRenderResult> RenderImageAsync(AiImageRenderRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the provider/model/cost for an image capability from the database, delegates the actual
/// render to the existing image-provider factory (OpenRouter or ImageAICreativeRender), and records a
/// usage-log row. Model, endpoint and unit cost always come from todox_ai_provider_capability — never
/// hard-coded here.
/// </summary>
public sealed class AiImageRenderRouter : IAiImageRenderRouter
{
    private readonly IAiProviderService _providers;
    private readonly AiProviderRepository _repo;
    private readonly IAiImageProviderFactory _imageProviders;
    private readonly ILogger<AiImageRenderRouter> _logger;

    public AiImageRenderRouter(IAiProviderService providers, AiProviderRepository repo,
        IAiImageProviderFactory imageProviders, ILogger<AiImageRenderRouter> logger)
    {
        _providers = providers;
        _repo = repo;
        _imageProviders = imageProviders;
        _logger = logger;
    }

    public async Task<AiImageRenderResult> RenderImageAsync(AiImageRenderRequest request, CancellationToken cancellationToken = default)
    {
        var option = await _providers.ResolveProviderForCapabilityAsync(
            request.CapabilityCode, request.ProviderCapabilityId, request.FromUser, cancellationToken);

        // Resolve base_url + endpoint_path from the provider/capability config (needed for OpenRouter).
        var detail = await _repo.GetProviderAsync(option.ProviderId, cancellationToken);
        var capability = detail?.Capabilities.FirstOrDefault(c => c.Id == option.ProviderCapabilityId);

        var factoryKey = ProviderCodeMap.ToFactoryKey(option.ProviderCode);
        var provider = _imageProviders.GetProvider(factoryKey);

        _logger.LogInformation("AI_IMAGE_ROUTER_RESOLVED capability={CapabilityCode} feature={FeatureCode} provider={ProviderCode} model={ModelName}",
            request.CapabilityCode, request.FeatureCode, option.ProviderCode, option.ModelName);

        var response = await provider.GenerateImageAsync(new OpenRouterImageRequest
        {
            UserId = request.UserId,
            CustomerId = null,
            Model = option.ModelName ?? string.Empty,
            Prompt = request.Prompt,
            AspectRatio = request.AspectRatio,
            OutputFormat = request.OutputFormat,
            Quality = request.Quality,
            Resolution = request.Resolution,
            Seed = request.Seed,
            Count = 1,
            FileCategory = request.FileCategory,
            ReferenceImageUrls = request.ReferenceImageUrls,
            BaseUrlOverride = detail?.BaseUrl,
            EndpointPath = capability?.EndpointPath
        }, cancellationToken);

        var quantity = 1m;
        var unitCost = option.UnitCostPoints;
        var result = new AiImageRenderResult
        {
            Success = response.Success,
            ImageBytes = response.ImageBytes,
            ImageUrl = response.ImageUrl,
            ObjectKey = response.ObjectKey,
            MimeType = response.MimeType,
            ProviderCode = option.ProviderCode,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ModelName = option.ModelName,
            UnitType = option.UnitType,
            UnitCostPoints = unitCost,
            Quantity = quantity,
            TotalPoints = quantity * unitCost,
            ProviderRawCost = response.UsageCost,
            RawResponseJson = response.RawResponseJson,
            ErrorMessage = response.Success ? null : response.ErrorMessage
        };

        await _providers.LogUsageAsync(new AiProviderUsageLog
        {
            CustomerId = request.CustomerId,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ProviderCode = option.ProviderCode,
            CapabilityCode = option.CapabilityCode,
            FeatureCode = request.FeatureCode,
            ModelName = option.ModelName,
            RequestId = request.RequestId,
            JobId = request.JobId,
            Quantity = quantity,
            UnitType = option.UnitType,
            UnitCostPoints = unitCost,
            TotalPoints = quantity * unitCost,
            ProviderRawCost = response.UsageCost,
            Status = response.Success ? "success" : "failed",
            ErrorMessage = response.Success ? null : response.ErrorMessage,
            MetadataJson = SerializeMetadata(request.Metadata),
            CreatedBy = request.CreatedBy
        }, cancellationToken);

        return result;
    }

    private static string? SerializeMetadata(object? metadata)
    {
        if (metadata is null) return null;
        try
        {
            return JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return null;
        }
    }
}
