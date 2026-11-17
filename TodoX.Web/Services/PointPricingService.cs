using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Services.Pricing;

namespace TodoX.Web.Services;

public interface IPointPricingService
{
    Task<PointPricingRate> ResolveRateAsync(Guid? serviceId, string resourceType, string qualityTier, CancellationToken ct = default);
    Task<PointPricingEstimate> EstimateAsync(PointPricingEstimateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Rate for a known AI model: a service's own override first, then the model price book (79AI cost × markup),
    /// then the flat table. Implementations without a price book use the flat rate.
    /// </summary>
    Task<PointPricingRate> ResolveModelRateAsync(Guid? serviceId, string resourceType, string qualityTier, ModelPriceQuery? model, CancellationToken ct = default)
        => ResolveRateAsync(serviceId, resourceType, qualityTier, ct);
}

public sealed class PointPricingService : IPointPricingService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    private readonly ModelPriceService? _models;

    public PointPricingService(TodoXConnectionFactory factory, TenantContext tenant, ModelPriceService? models = null)
    {
        _factory = factory;
        _tenant = tenant;
        _models = models;
    }

    public async Task<PointPricingRate> ResolveModelRateAsync(Guid? serviceId, string resourceType, string qualityTier, ModelPriceQuery? model, CancellationToken ct = default)
    {
        if (model is null || _models is null || string.IsNullOrWhiteSpace(model.Model))
        {
            return await ResolveRateAsync(serviceId, resourceType, qualityTier, ct);
        }

        var normalizedResource = NormalizeResourceType(resourceType);
        if (serviceId is Guid sid && await ServiceOverrideAsync(sid, normalizedResource, NormalizeQualityTier(qualityTier), ct) is { } own)
        {
            return own;
        }

        var quote = await _models.QuoteAsync(normalizedResource, model, ct);
        if (quote is null)
        {
            return await ResolveRateAsync(serviceId, resourceType, qualityTier, ct);
        }

        return normalizedResource == PointPricingResourceTypes.Video
            ? new PointPricingRate(normalizedResource, quote.Tier, quote.PerSecond, "per_second", ModelPriceSources.ModelPrice, serviceId)
            : new PointPricingRate(normalizedResource, quote.Tier, quote.Points, "per_render", ModelPriceSources.ModelPrice, serviceId);
    }

    private async Task<PointPricingRate?> ServiceOverrideAsync(Guid serviceId, string resourceType, string qualityTier, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<PointPricingRateRow>(
            new CommandDefinition(
                """
                SELECT resource_type AS ResourceType, quality_tier AS QualityTier, rate AS Rate, unit AS Unit,
                       'service_override' AS Source, service_id AS ServiceId
                  FROM billing.service_point_rate_override
                 WHERE tenant_id = @tenant AND service_id = @serviceId AND is_active = true
                   AND lower(resource_type) = lower(@resourceType) AND lower(quality_tier) = lower(@qualityTier)
                 LIMIT 1;
                """,
                new { tenant = _tenant.TenantId, serviceId, resourceType, qualityTier },
                cancellationToken: ct));
        return row?.ToRate();
    }

    public async Task<PointPricingRate> ResolveRateAsync(Guid? serviceId, string resourceType, string qualityTier, CancellationToken ct = default)
    {
        var normalizedResource = NormalizeResourceType(resourceType);
        var normalizedQuality = NormalizeQualityTier(qualityTier);
        await _tenant.EnsureLoadedAsync(ct);

        using var conn = await _factory.OpenAsync(ct);
        if (serviceId is Guid sid)
        {
            var overrideRate = await conn.QuerySingleOrDefaultAsync<PointPricingRateRow>(
                new CommandDefinition(
                    """
                    SELECT resource_type AS ResourceType,
                           quality_tier AS QualityTier,
                           rate AS Rate,
                           unit AS Unit,
                           'service_override' AS Source,
                           service_id AS ServiceId
                      FROM billing.service_point_rate_override
                     WHERE tenant_id = @tenant
                       AND service_id = @serviceId
                       AND is_active = true
                       AND lower(resource_type) = lower(@resourceType)
                       AND lower(quality_tier) = lower(@qualityTier)
                     LIMIT 1;
                    """,
                    new { tenant = _tenant.TenantId, serviceId = sid, resourceType = normalizedResource, qualityTier = normalizedQuality },
                    cancellationToken: ct));

            if (overrideRate is not null)
            {
                return overrideRate.ToRate();
            }
        }

        var row = await conn.QuerySingleOrDefaultAsync<PointPricingRateRow>(
            new CommandDefinition(
                """
                SELECT resource_type AS ResourceType,
                       quality_tier AS QualityTier,
                       rate AS Rate,
                       unit AS Unit,
                       'global' AS Source,
                       NULL::uuid AS ServiceId
                  FROM billing.point_rate_config
                 WHERE tenant_id = @tenant
                   AND is_active = true
                   AND lower(resource_type) = lower(@resourceType)
                   AND lower(quality_tier) = lower(@qualityTier)
                 LIMIT 1;
                """,
                new { tenant = _tenant.TenantId, resourceType = normalizedResource, qualityTier = normalizedQuality },
                cancellationToken: ct));

        if (row is null)
        {
            throw new InvalidOperationException($"POINT_RATE_NOT_CONFIGURED: {normalizedResource}/{normalizedQuality}");
        }

        return row.ToRate();
    }

    public async Task<PointPricingEstimate> EstimateAsync(PointPricingEstimateRequest request, CancellationToken ct = default)
    {
        var imageCount = Math.Max(0, request.ImageCount);
        var videoSeconds = Math.Max(0, request.VideoSeconds);
        var voiceCount = request.VoiceEnabled ? Math.Max(0, request.VoiceCount) : 0;

        var imageRate = await ResolveModelRateAsync(request.ServiceId, PointPricingResourceTypes.Image, request.ImageQuality, request.ImageModel, ct);
        var videoRate = await ResolveModelRateAsync(request.ServiceId, PointPricingResourceTypes.Video, request.VideoQuality,
            request.VideoModel is { } vm && vm.Seconds is null ? vm with { Seconds = Math.Max(1, videoSeconds) } : request.VideoModel, ct);
        var voiceRate = await ResolveModelRateAsync(request.ServiceId, PointPricingResourceTypes.Voice, request.VoiceQuality, request.VoiceModel, ct);

        return PointPricingCalculator.Estimate(imageCount, imageRate, videoSeconds, videoRate, voiceCount, voiceRate);
    }

    private static string NormalizeResourceType(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!PointPricingResourceTypes.IsValid(normalized))
        {
            throw new InvalidOperationException("Unsupported point resource type.");
        }
        return normalized;
    }

    private static string NormalizeQualityTier(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!ServiceSellPriceQualityTiers.IsValid(normalized))
        {
            throw new InvalidOperationException("Unsupported point quality tier.");
        }
        return normalized;
    }

    private sealed record PointPricingRateRow
    {
        public string ResourceType { get; init; } = string.Empty;
        public string QualityTier { get; init; } = string.Empty;
        public decimal Rate { get; init; }
        public string Unit { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public Guid? ServiceId { get; init; }

        public PointPricingRate ToRate()
            => new(ResourceType, QualityTier, Rate, Unit, Source, ServiceId);
    }
}

public static class PointPricingCalculator
{
    public static PointPricingEstimate Estimate(
        int imageCount,
        PointPricingRate imageRate,
        int videoSeconds,
        PointPricingRate videoRate,
        int voiceCount,
        PointPricingRate voiceRate)
    {
        var imagePoints = Math.Max(0, imageCount) * imageRate.Rate;
        var videoPoints = Math.Max(0, videoSeconds) * videoRate.Rate;
        var voicePoints = Math.Max(0, voiceCount) * voiceRate.Rate;

        return new PointPricingEstimate(
            new PointPricingLine(Math.Max(0, imageCount), imageRate.QualityTier, imageRate.Rate, imageRate.Source, imagePoints),
            new PointPricingLine(Math.Max(0, videoSeconds), videoRate.QualityTier, videoRate.Rate, videoRate.Source, videoPoints),
            new PointPricingLine(Math.Max(0, voiceCount), voiceRate.QualityTier, voiceRate.Rate, voiceRate.Source, voicePoints),
            imagePoints + videoPoints + voicePoints);
    }
}
