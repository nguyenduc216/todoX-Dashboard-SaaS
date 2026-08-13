namespace TodoX.Web.Models.Catalog;

public static class ServiceSellPriceAssetTypes
{
    public const string Image = "image";
    public const string VideoScene = "video_scene";

    public static IReadOnlyList<string> All { get; } = [Image, VideoScene];

    public static bool IsValid(string? value)
        => All.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    public static string LabelFor(string? value)
        => value?.ToLowerInvariant() switch
        {
            Image => "Ảnh",
            VideoScene => "Video scene",
            _ => value ?? string.Empty
        };
}

public static class ServiceSellPriceQualityTiers
{
    public const string Standard = "standard";
    public const string Premium = "premium";

    public static IReadOnlyList<string> All { get; } = [Standard, Premium];

    public static bool IsValid(string? value)
        => All.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    public static string LabelFor(string? value)
        => value?.ToLowerInvariant() switch
        {
            Standard => "Tiêu chuẩn",
            Premium => "Cao cấp",
            _ => value ?? string.Empty
        };
}

public sealed class ServiceSellPriceDto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string AssetType { get; set; } = ServiceSellPriceAssetTypes.VideoScene;
    public string QualityTier { get; set; } = ServiceSellPriceQualityTiers.Standard;
    public decimal? DurationSeconds { get; set; }
    public decimal SellPoints { get; set; }
    public string? DisplayLabel { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed record ServiceSellPriceResolution(
    bool Found,
    ServiceSellPriceDto? Price,
    string? Message)
{
    public static ServiceSellPriceResolution Missing(string message) => new(false, null, message);
    public static ServiceSellPriceResolution Success(ServiceSellPriceDto price) => new(true, price, null);
}

public sealed record ServiceSellPriceEstimateRequest(
    Guid ServiceId,
    string QualityTier,
    int? DurationSeconds,
    int SceneCount,
    int ImageCount);

public sealed record ServiceSellPriceEstimate(
    bool Success,
    string? Message,
    ServiceSellPriceDto? ImagePrice,
    ServiceSellPriceDto? VideoScenePrice,
    int ImageCount,
    int SceneCount,
    decimal ImageSubtotal,
    decimal VideoSubtotal,
    decimal TotalPoints);

public static class ServiceSellPriceRules
{
    public static void Validate(ServiceSellPriceDto price)
    {
        if (!ServiceSellPriceAssetTypes.IsValid(price.AssetType))
        {
            throw new InvalidOperationException("Loại giá bán không hợp lệ.");
        }

        if (!ServiceSellPriceQualityTiers.IsValid(price.QualityTier))
        {
            throw new InvalidOperationException("Chất lượng giá bán không hợp lệ.");
        }

        if (price.SellPoints < 0)
        {
            throw new InvalidOperationException("Giá điểm không được âm.");
        }

        if (string.Equals(price.AssetType, ServiceSellPriceAssetTypes.Image, StringComparison.OrdinalIgnoreCase) && price.DurationSeconds is not null)
        {
            throw new InvalidOperationException("Giá ảnh không dùng thời lượng.");
        }

        if (string.Equals(price.AssetType, ServiceSellPriceAssetTypes.VideoScene, StringComparison.OrdinalIgnoreCase)
            && price.DurationSeconds is <= 0)
        {
            throw new InvalidOperationException("Thời lượng video scene phải lớn hơn 0.");
        }
    }
}
