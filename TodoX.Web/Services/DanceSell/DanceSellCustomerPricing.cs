using System.Text.Json;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.Platform;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellCustomerPricing
{
    Task<PointPricingEstimate> EstimateAsync(
        DanceSellJobDto job,
        int durationSeconds,
        string qualityTier,
        int imageCount,
        CancellationToken ct = default);
}

public sealed class DanceSellCustomerPricing : IDanceSellCustomerPricing
{
    private readonly IServiceSellPriceResolver _sellPrices;
    private readonly IPointPricingService _pointPricing;
    private readonly ICoreServiceCatalogService _catalog;

    public DanceSellCustomerPricing(
        IServiceSellPriceResolver sellPrices,
        IPointPricingService pointPricing,
        ICoreServiceCatalogService catalog)
    {
        _sellPrices = sellPrices;
        _pointPricing = pointPricing;
        _catalog = catalog;
    }

    public async Task<PointPricingEstimate> EstimateAsync(
        DanceSellJobDto job,
        int durationSeconds,
        string qualityTier,
        int imageCount,
        CancellationToken ct = default)
    {
        var service = await ResolveServiceAsync(job, ct);
        if (service is null)
        {
            var fallback = await _catalog.GetByCodeAsync(FixedTodoXServiceCatalog.RDance, ct);
            return await _pointPricing.EstimateAsync(new PointPricingEstimateRequest(
                fallback?.Id,
                imageCount,
                qualityTier,
                durationSeconds,
                qualityTier,
                0,
                ServiceSellPriceQualityTiers.Standard,
                false), ct);
        }

        var sceneCount = durationSeconds > 0 ? 1 : 0;
        var sellEstimate = await _sellPrices.EstimateAsync(new ServiceSellPriceEstimateRequest(
            service.Id,
            qualityTier,
            sceneCount > 0 ? durationSeconds : null,
            sceneCount,
            imageCount), ct);
        if (!sellEstimate.Success)
        {
            throw new InvalidOperationException(sellEstimate.Message ?? "DANCE_SELL_CUSTOMER_PRICE_NOT_CONFIGURED");
        }

        var imageRate = ToRate(
            sellEstimate.ImagePrice,
            PointPricingResourceTypes.Image,
            qualityTier,
            "per_render");
        var videoRate = ToVideoRate(sellEstimate.VideoScenePrice, qualityTier, durationSeconds);
        var voiceRate = new PointPricingRate(
            PointPricingResourceTypes.Voice,
            ServiceSellPriceQualityTiers.Standard,
            0,
            "per_render",
            "service_sell",
            service.Id);

        return PointPricingCalculator.Estimate(
            imageCount,
            imageRate,
            durationSeconds,
            videoRate,
            0,
            voiceRate);
    }

    private async Task<CoreServiceView?> ResolveServiceAsync(DanceSellJobDto job, CancellationToken ct)
    {
        var serviceCode = ReadString(job.RequestJson, "serviceCode", "service_code");
        var serviceId = ReadGuid(job.RequestJson, "serviceId", "service_id");
        var service = serviceId is Guid id && id != Guid.Empty
            ? await _catalog.GetByIdAsync(id, ct)
            : null;
        service ??= string.IsNullOrWhiteSpace(serviceCode)
            ? null
            : await _catalog.GetByCodeAsync(serviceCode, ct);
        if (service is null && (serviceId is not null || !string.IsNullOrWhiteSpace(serviceCode)))
        {
            throw new InvalidOperationException("DANCE_SELL_SERVICE_INVALID");
        }

        if (service is null
            || !service.Enabled
            || !string.Equals(service.ServiceType, TodoXServiceEngineTypes.RDance, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DANCE_SELL_SERVICE_INVALID");
        }

        if (serviceId is Guid persistedServiceId && persistedServiceId != service.Id)
        {
            throw new InvalidOperationException("DANCE_SELL_SERVICE_ID_MISMATCH");
        }

        return service;
    }

    private static PointPricingRate ToRate(
        ServiceSellPriceDto? price,
        string resourceType,
        string qualityTier,
        string unit)
        => new(
            resourceType,
            qualityTier,
            price?.SellPoints ?? 0,
            unit,
            "service_sell",
            price?.ServiceId);

    private static PointPricingRate ToVideoRate(ServiceSellPriceDto? price, string qualityTier, int durationSeconds)
    {
        var rate = durationSeconds > 0
            ? (price?.SellPoints ?? 0) / durationSeconds
            : 0;
        return new PointPricingRate(
            PointPricingResourceTypes.Video,
            qualityTier,
            rate,
            "per_scene",
            "service_sell",
            price?.ServiceId);
    }

    private static Guid? ReadGuid(string? rawJson, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            foreach (var name in propertyNames)
            {
                if (!document.RootElement.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.String
                    && Guid.TryParse(value.GetString(), out var id))
                {
                    return id;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? ReadString(string? rawJson, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            foreach (var name in propertyNames)
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
