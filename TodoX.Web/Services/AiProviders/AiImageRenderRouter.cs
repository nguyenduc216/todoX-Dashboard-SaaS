using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.AiCharacters;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiImageRenderRequest
{
    public long? CustomerId { get; set; }
    public Guid? CustomerGuid { get; set; }
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
    public string? LogicalRequestId { get; set; }
    public string? RenderJobId { get; set; }
    public bool BillingExempt { get; set; }
    public string? ExemptionReason { get; set; }
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
/// Resolves the provider/model/cost for an image capability from the database, reserves billing before
/// provider submit, delegates rendering, completes or releases the billing hold, and records usage.
/// Provider/model/endpoint/unit cost are data-driven from todox_ai_provider_capability.
/// </summary>
public sealed class AiImageRenderRouter : IAiImageRenderRouter
{
    private readonly IAiProviderService _providers;
    private readonly AiProviderRepository _repo;
    private readonly IAiImageProviderFactory _imageProviders;
    private readonly IAiImageBillingService _billing;
    private readonly ILogger<AiImageRenderRouter> _logger;

    public AiImageRenderRouter(
        IAiProviderService providers,
        AiProviderRepository repo,
        IAiImageProviderFactory imageProviders,
        IAiImageBillingService billing,
        ILogger<AiImageRenderRouter> logger)
    {
        _providers = providers;
        _repo = repo;
        _imageProviders = imageProviders;
        _billing = billing;
        _logger = logger;
    }

    public async Task<AiImageRenderResult> RenderImageAsync(AiImageRenderRequest request, CancellationToken cancellationToken = default)
    {
        var option = await _providers.ResolveProviderForCapabilityAsync(
            request.CapabilityCode, request.ProviderCapabilityId, request.FromUser, cancellationToken);

        var detail = await _repo.GetProviderAsync(option.ProviderId, cancellationToken);
        var capability = detail?.Capabilities.FirstOrDefault(c => c.Id == option.ProviderCapabilityId);
        var factoryKey = ProviderCodeMap.ToFactoryKey(option.ProviderCode);
        var provider = _imageProviders.GetProvider(factoryKey);

        var (cfgResolution, cfgQuality, cfgFormat) = ParseImageConfig(capability?.ConfigJson);
        var resolution = factoryKey.Equals("yescale_task_image", StringComparison.OrdinalIgnoreCase)
            ? FirstNonBlank(cfgResolution, request.Resolution) ?? "1K"
            : HighestResolution(cfgResolution, request.Resolution);
        var quality = FirstNonBlank(cfgQuality, request.Quality) ?? "high";
        var outputFormat = FirstNonBlank(cfgFormat, request.OutputFormat) ?? "png";
        var quantity = 1m;
        var unitCost = option.UnitCostPoints;
        var logicalRequestId = ResolveLogicalRequestId(request);
        request.RequestId ??= logicalRequestId;
        request.JobId ??= request.RenderJobId;
        var billingCost = _billing.BuildConfiguredCost(unitCost, quantity);

        _logger.LogInformation(
            "AI_IMAGE_ROUTER_RESOLVED capability={CapabilityCode} feature={FeatureCode} provider={ProviderCode} model={ModelName} resolution={Resolution} logicalRequestId={LogicalRequestId}",
            request.CapabilityCode, request.FeatureCode, option.ProviderCode, option.ModelName, resolution, logicalRequestId);

        var reservation = await _billing.ReserveAsync(new AiImageBillingReserveRequest
        {
            LogicalRequestId = logicalRequestId,
            RenderJobId = request.RenderJobId ?? request.JobId,
            CustomerId = request.CustomerGuid,
            UserId = request.UserId,
            ProviderId = option.ProviderId,
            ProviderCapabilityId = option.ProviderCapabilityId,
            ProviderCode = option.ProviderCode,
            CapabilityCode = option.CapabilityCode,
            FeatureCode = request.FeatureCode,
            RequestedModel = option.ModelName,
            Cost = billingCost,
            BillingExempt = request.BillingExempt,
            ExemptionReason = request.ExemptionReason,
            Metadata = request.Metadata,
            CreatedBy = request.CreatedBy
        }, cancellationToken);

        if (!reservation.Ok || !reservation.ShouldSubmitProvider)
        {
            _logger.LogWarning(
                "AI_IMAGE_BILLING_BLOCKED capability={CapabilityCode} feature={FeatureCode} logicalRequestId={LogicalRequestId} status={Status} error={Error}",
                request.CapabilityCode, request.FeatureCode, logicalRequestId, reservation.Status, reservation.ErrorMessage);
            return new AiImageRenderResult
            {
                Success = false,
                ProviderCode = option.ProviderCode,
                ProviderId = option.ProviderId,
                ProviderCapabilityId = option.ProviderCapabilityId,
                ModelName = option.ModelName,
                UnitType = option.UnitType,
                UnitCostPoints = unitCost,
                Quantity = quantity,
                TotalPoints = unitCost * quantity,
                ErrorMessage = reservation.ErrorMessage ?? "Image billing reservation did not allow provider submit."
            };
        }

        OpenRouterImageResponse response;
        try
        {
            response = await provider.GenerateImageAsync(new OpenRouterImageRequest
            {
                UserId = request.UserId,
                CustomerId = null,
                Model = option.ModelName ?? string.Empty,
                Prompt = request.Prompt,
                AspectRatio = request.AspectRatio,
                OutputFormat = outputFormat,
                Quality = quality,
                Resolution = resolution,
                Seed = request.Seed,
                Count = 1,
                FileCategory = request.FileCategory,
                ReferenceImageUrls = request.ReferenceImageUrls,
                BaseUrlOverride = detail?.BaseUrl,
                EndpointPath = capability?.EndpointPath,
                ApiKeyConfigName = detail?.ApiKeyConfigName,
                ProviderConfigJson = detail?.ConfigJson,
                CapabilityConfigJson = capability?.ConfigJson
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = logicalRequestId,
                Success = false,
                ActualModel = option.ModelName,
                ErrorMessage = ex.Message
            }, cancellationToken);
            throw;
        }

        var finalModel = string.IsNullOrWhiteSpace(response.ModelName) ? option.ModelName : response.ModelName;
        var providerTaskId = TryReadTaskId(response.UsageJson);
        var billingCompletion = await _billing.CompleteAsync(new AiImageBillingCompleteRequest
        {
            LogicalRequestId = logicalRequestId,
            Success = response.Success,
            ActualModel = finalModel,
            ProviderTaskId = providerTaskId,
            ProviderActualCostUsd = response.UsageCost,
            ProviderUsageJson = response.UsageJson,
            ErrorMessage = response.Success ? null : response.ErrorMessage
        }, cancellationToken);
        if (!billingCompletion.Ok)
        {
            _logger.LogError(
                "AI_IMAGE_BILLING_COMPLETE_FAILED capability={CapabilityCode} feature={FeatureCode} logicalRequestId={LogicalRequestId} status={Status} error={Error}",
                request.CapabilityCode, request.FeatureCode, logicalRequestId, billingCompletion.Status, billingCompletion.ErrorMessage);
            return new AiImageRenderResult
            {
                Success = false,
                ProviderCode = option.ProviderCode,
                ProviderId = option.ProviderId,
                ProviderCapabilityId = option.ProviderCapabilityId,
                ModelName = finalModel,
                UnitType = option.UnitType,
                UnitCostPoints = unitCost,
                Quantity = quantity,
                TotalPoints = reservation.CustomerChargedPoints,
                ProviderRawCost = response.UsageCost,
                RawResponseJson = response.RawResponseJson,
                ErrorMessage = billingCompletion.ErrorMessage ?? "Image billing completion failed."
            };
        }

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
            ModelName = finalModel,
            UnitType = option.UnitType,
            UnitCostPoints = unitCost,
            Quantity = quantity,
            TotalPoints = reservation.CustomerChargedPoints,
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
            ModelName = finalModel,
            RequestId = request.RequestId,
            JobId = request.JobId,
            Quantity = quantity,
            UnitType = option.UnitType,
            UnitCostPoints = unitCost,
            TotalPoints = reservation.CustomerChargedPoints,
            ProviderRawCost = response.UsageCost,
            Status = response.Success ? "success" : "failed",
            ErrorMessage = response.Success ? null : response.ErrorMessage,
            MetadataJson = SerializeMetadata(new
            {
                request = request.Metadata,
                billing = new
                {
                    logicalRequestId,
                    reservation.Status,
                    completionStatus = billingCompletion.Status,
                    reservation.BillingExempt,
                    request.ExemptionReason,
                    billingCost.ProviderEstimatedCostUsd,
                    billingCost.ProviderActualCostUsd,
                    billingCost.ProviderCostSource,
                    billingCost.ExchangeRateVndPerUsd,
                    billingCost.TodoXVndPerPoint,
                    billingCost.ProviderCostPoints,
                    reservation.CustomerChargedPoints,
                    billingCompletion.WalletTransactionId,
                    providerTaskId
                }
            }, response.UsageJson),
            CreatedBy = request.CreatedBy
        }, cancellationToken);

        return result;
    }

    private static string? SerializeMetadata(object? metadata, string? providerUsageJson)
    {
        try
        {
            using var providerUsage = string.IsNullOrWhiteSpace(providerUsageJson) ? null : JsonDocument.Parse(providerUsageJson);
            if (metadata is null && providerUsage is null) return null;
            return JsonSerializer.Serialize(new
            {
                request = metadata,
                providerUsage = providerUsage?.RootElement
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return metadata is null ? null : JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }

    private static (string? Resolution, string? Quality, string? OutputFormat) ParseImageConfig(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return (null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (null, null, null);
            return (ReadString(root, "resolution"), ReadString(root, "quality"), ReadString(root, "output_format"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString())
            ? el.GetString()
            : null;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static string HighestResolution(params string?[] values)
    {
        var best = "2K";
        foreach (var value in values)
        {
            var normalized = OpenRouterImageService.NormalizeResolution(value);
            if (ResolutionRank(normalized) > ResolutionRank(best))
            {
                best = normalized;
            }
        }

        return best;
    }

    private static int ResolutionRank(string resolution)
        => resolution.Trim().ToUpperInvariant() switch
        {
            "1K" => 1,
            "2K" => 2,
            "4K" => 4,
            "8K" => 8,
            _ => 2
        };

    private static string ResolveLogicalRequestId(AiImageRenderRequest request)
        => FirstNonBlank(request.LogicalRequestId, request.RequestId, request.JobId)
           ?? $"image-{Guid.NewGuid():N}";

    private static string? TryReadTaskId(string? usageJson)
    {
        if (string.IsNullOrWhiteSpace(usageJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(usageJson);
            var root = doc.RootElement;
            return ReadString(root, "taskId") ?? ReadString(root, "task_id");
        }
        catch
        {
            return null;
        }
    }
}
