using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services;

public interface IPointPricingService
{
    Task<PointPricingRate> ResolveRateAsync(Guid? serviceId, string resourceType, string qualityTier, CancellationToken ct = default);
    Task<PointPricingEstimate> EstimateAsync(PointPricingEstimateRequest request, CancellationToken ct = default);
}

public sealed class PointPricingService : IPointPricingService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public PointPricingService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
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

        var row = await conn.QuerySingleAsync<PointPricingRateRow>(
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

        return row.ToRate();
    }

    public async Task<PointPricingEstimate> EstimateAsync(PointPricingEstimateRequest request, CancellationToken ct = default)
    {
        var imageCount = Math.Max(0, request.ImageCount);
        var videoSeconds = Math.Max(0, request.VideoSeconds);
        var voiceCount = request.VoiceEnabled ? Math.Max(0, request.VoiceCount) : 0;

        var imageRate = await ResolveRateAsync(request.ServiceId, PointPricingResourceTypes.Image, request.ImageQuality, ct);
        var videoRate = await ResolveRateAsync(request.ServiceId, PointPricingResourceTypes.Video, request.VideoQuality, ct);
        var voiceRate = await ResolveRateAsync(request.ServiceId, PointPricingResourceTypes.Voice, request.VoiceQuality, ct);

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
