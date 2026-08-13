using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Services;

public interface IServiceSellPriceResolver
{
    Task<IReadOnlyList<ServiceSellPriceDto>> GetActivePricesAsync(Guid serviceId, CancellationToken ct = default);
    Task<ServiceSellPriceResolution> ResolveImagePriceAsync(Guid serviceId, string qualityTier, CancellationToken ct = default);
    Task<ServiceSellPriceResolution> ResolveVideoScenePriceAsync(Guid serviceId, string qualityTier, int durationSeconds, CancellationToken ct = default);
    Task<ServiceSellPriceEstimate> EstimateAsync(ServiceSellPriceEstimateRequest request, CancellationToken ct = default);
}

public sealed class ServiceSellPriceResolver : IServiceSellPriceResolver
{
    private readonly CatalogAdminRepository _catalog;

    public ServiceSellPriceResolver(CatalogAdminRepository catalog)
    {
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<ServiceSellPriceDto>> GetActivePricesAsync(Guid serviceId, CancellationToken ct = default)
    {
        var prices = await _catalog.GetSellPricesAsync(serviceId, ct);
        return prices.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToList();
    }

    public async Task<ServiceSellPriceResolution> ResolveImagePriceAsync(Guid serviceId, string qualityTier, CancellationToken ct = default)
    {
        var prices = await GetActivePricesAsync(serviceId, ct);
        var price = prices.FirstOrDefault(x =>
            string.Equals(x.AssetType, ServiceSellPriceAssetTypes.Image, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.QualityTier, qualityTier, StringComparison.OrdinalIgnoreCase)
            && x.DurationSeconds is null);

        return price is null
            ? ServiceSellPriceResolution.Missing("Chưa cấu hình giá ảnh cho chất lượng đã chọn.")
            : ServiceSellPriceResolution.Success(price);
    }

    public async Task<ServiceSellPriceResolution> ResolveVideoScenePriceAsync(Guid serviceId, string qualityTier, int durationSeconds, CancellationToken ct = default)
    {
        var prices = await GetActivePricesAsync(serviceId, ct);
        var price = prices.FirstOrDefault(x =>
            string.Equals(x.AssetType, ServiceSellPriceAssetTypes.VideoScene, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.QualityTier, qualityTier, StringComparison.OrdinalIgnoreCase)
            && x.DurationSeconds == durationSeconds);

        return price is null
            ? ServiceSellPriceResolution.Missing("Chưa cấu hình giá video scene cho chất lượng và thời lượng đã chọn.")
            : ServiceSellPriceResolution.Success(price);
    }

    public async Task<ServiceSellPriceEstimate> EstimateAsync(ServiceSellPriceEstimateRequest request, CancellationToken ct = default)
    {
        var imageCount = Math.Max(0, request.ImageCount);
        var sceneCount = Math.Max(0, request.SceneCount);

        ServiceSellPriceDto? imagePrice = null;
        ServiceSellPriceDto? videoPrice = null;

        if (imageCount > 0)
        {
            var image = await ResolveImagePriceAsync(request.ServiceId, request.QualityTier, ct);
            if (!image.Found)
            {
                return new(false, image.Message, null, null, imageCount, sceneCount, 0, 0, 0);
            }

            imagePrice = image.Price;
        }

        if (sceneCount > 0)
        {
            if (!request.DurationSeconds.HasValue)
            {
                return new(false, "Vui lòng chọn thời lượng video scene.", imagePrice, null, imageCount, sceneCount, 0, 0, 0);
            }

            var video = await ResolveVideoScenePriceAsync(request.ServiceId, request.QualityTier, request.DurationSeconds.Value, ct);
            if (!video.Found)
            {
                return new(false, video.Message, imagePrice, null, imageCount, sceneCount, 0, 0, 0);
            }

            videoPrice = video.Price;
        }

        var imageSubtotal = (imagePrice?.SellPoints ?? 0) * imageCount;
        var videoSubtotal = (videoPrice?.SellPoints ?? 0) * sceneCount;
        return new(true, null, imagePrice, videoPrice, imageCount, sceneCount, imageSubtotal, videoSubtotal, imageSubtotal + videoSubtotal);
    }
}
