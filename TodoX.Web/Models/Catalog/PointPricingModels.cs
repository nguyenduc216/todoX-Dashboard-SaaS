namespace TodoX.Web.Models.Catalog;

public static class PointPricingResourceTypes
{
    public const string Image = "image";
    public const string Video = "video";
    public const string Voice = "voice";

    public static IReadOnlyList<string> All { get; } = [Image, Video, Voice];

    public static bool IsValid(string? value)
        => All.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
}

public sealed record PointPricingRate(
    string ResourceType,
    string QualityTier,
    decimal Rate,
    string Unit,
    string Source,
    Guid? ServiceId = null);

public sealed record PointPricingLine(
    int Count,
    string Quality,
    decimal Rate,
    string Source,
    decimal Points);

public sealed record PointPricingEstimate(
    PointPricingLine Image,
    PointPricingLine Video,
    PointPricingLine Voice,
    decimal TotalPoints);

public sealed record PointPricingEstimateRequest(
    Guid? ServiceId,
    int ImageCount,
    string ImageQuality,
    int VideoSeconds,
    string VideoQuality,
    int VoiceCount,
    string VoiceQuality,
    bool VoiceEnabled = true);

