using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Models.Timelapse;

public enum CustomerServiceDestination
{
    TimelapseCreator,
    RVideoCreator,
    RDanceCreator,
    Unavailable
}

public sealed record CustomerServiceRoute(CustomerServiceDestination Destination, string? Route, string? Message);

public static class CustomerServiceRouting
{
    public static CustomerServiceRoute Resolve(string? engineType, Guid? serviceId = null, string? serviceCode = null)
    {
        if (string.Equals(engineType, TodoXServiceEngineTypes.Timelapse, StringComparison.OrdinalIgnoreCase))
        {
            return new(CustomerServiceDestination.TimelapseCreator, BuildRoute("/jobs/timelapse/new", serviceId, serviceCode), null);
        }

        if (string.Equals(engineType, TodoXServiceEngineTypes.RVideo, StringComparison.OrdinalIgnoreCase))
        {
            return new(CustomerServiceDestination.RVideoCreator, null, "Dịch vụ RVideo đang hoàn thiện.");
        }

        if (string.Equals(engineType, TodoXServiceEngineTypes.RDance, StringComparison.OrdinalIgnoreCase))
        {
            return new(CustomerServiceDestination.RDanceCreator, null, "Dịch vụ RDance đang hoàn thiện.");
        }

        return new(CustomerServiceDestination.Unavailable, null, "Dịch vụ hiện chưa khả dụng.");
    }

    private static string BuildRoute(string route, Guid? serviceId, string? serviceCode)
    {
        var parts = new List<string>();
        if (serviceId.HasValue && serviceId.Value != Guid.Empty)
        {
            parts.Add($"serviceId={Uri.EscapeDataString(serviceId.Value.ToString())}");
        }

        if (!string.IsNullOrWhiteSpace(serviceCode))
        {
            parts.Add($"serviceCode={Uri.EscapeDataString(serviceCode)}");
        }

        return parts.Count == 0 ? route : route + "?" + string.Join("&", parts);
    }
}

public sealed class TimelapseProfileDto
{
    public string ProfileCode { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class TimelapseCreateRequest
{
    public Guid? ServiceId { get; set; }
    public string? ServiceCode { get; set; }
    public string? Title { get; set; } = "Video Timelapse";
    public string ProfileCode { get; set; } = string.Empty;
    public int SceneCount { get; set; } = 3;
    public string VideoMode { get; set; } = TimelapseRequestRules.FastMode;
    public string Ratio { get; set; } = TimelapseRequestRules.LandscapeRatio;
}

public sealed class TimelapseOriginalImageSnapshot
{
    public Guid MediaId { get; set; }
    public string? ObjectKey { get; set; }
    public string? PublicUrl { get; set; }
    public string? MimeType { get; set; }
}

public sealed class TimelapseJobSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string Engine { get; set; } = TodoXServiceEngineTypes.Timelapse;
    public Guid ServiceId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ProfileCode { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int SceneCount { get; set; }
    public IReadOnlyList<int> ProgressMapping { get; set; } = Array.Empty<int>();
    public string VideoMode { get; set; } = TimelapseRequestRules.FastMode;
    public string Ratio { get; set; } = TimelapseRequestRules.LandscapeRatio;
    public string Title { get; set; } = "Video Timelapse";
    public TimelapseSellPriceSnapshot? SellPrice { get; set; }
    public TimelapseOriginalImageSnapshot OriginalImage { get; set; } = new();
}

public sealed class TimelapseSellPriceSnapshot
{
    public string QualityTier { get; set; } = ServiceSellPriceQualityTiers.Standard;
    public int RuntimeClipDurationSeconds { get; set; }
    public int SceneCount { get; set; }
    public decimal VideoSceneSellPoints { get; set; }
    public decimal VideoSubtotal { get; set; }
    public decimal TotalPoints { get; set; }
}

public sealed class TimelapseJobView
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public TimelapseJobSnapshot Snapshot { get; set; } = new();
}

public static class TimelapseRequestRules
{
    public const string FastMode = "fast";
    public const string ProfessionalMode = "professional";
    public const string LandscapeRatio = "16_9";
    public const string PortraitRatio = "9_16";
    public const int RuntimeClipDurationSeconds = 6;

    public static IReadOnlyList<int> AllowedSceneCounts { get; } = [3, 4, 5, 6];

    public static IReadOnlyList<int> GetProgressMapping(int sceneCount) => sceneCount switch
    {
        3 => [0, 35, 70, 100],
        4 => [0, 25, 50, 75, 100],
        5 => [0, 20, 40, 60, 80, 100],
        6 => [0, 25, 40, 55, 70, 85, 100],
        _ => throw new ArgumentOutOfRangeException(nameof(sceneCount), "Scene count must be from 3 to 6.")
    };

    public static IReadOnlyList<string> Validate(TimelapseCreateRequest request, bool hasOriginalImage)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ProfileCode))
        {
            errors.Add("Vui lòng chọn loại công trình.");
        }

        if (!AllowedSceneCounts.Contains(request.SceneCount))
        {
            errors.Add("Số scene chỉ có thể là 3, 4, 5 hoặc 6.");
        }

        if (!IsSupportedMode(request.VideoMode))
        {
            errors.Add("Chế độ video không hợp lệ.");
        }

        if (!IsSupportedRatio(request.Ratio))
        {
            errors.Add("Tỷ lệ video không hợp lệ.");
        }

        if (!hasOriginalImage)
        {
            errors.Add("Vui lòng chọn ảnh thành phẩm / ảnh tham chiếu.");
        }

        return errors;
    }

    public static bool IsSupportedMode(string? mode)
        => string.Equals(mode, FastMode, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, ProfessionalMode, StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedRatio(string? ratio)
        => string.Equals(ratio, LandscapeRatio, StringComparison.OrdinalIgnoreCase)
           || string.Equals(ratio, PortraitRatio, StringComparison.OrdinalIgnoreCase);
}

public static class TimelapseSellPricing
{
    public static string QualityTierForMode(string? mode)
        => string.Equals(mode, TimelapseRequestRules.ProfessionalMode, StringComparison.OrdinalIgnoreCase)
            ? ServiceSellPriceQualityTiers.Premium
            : ServiceSellPriceQualityTiers.Standard;

    public static string CustomerQualityLabel(string? mode)
        => string.Equals(mode, TimelapseRequestRules.ProfessionalMode, StringComparison.OrdinalIgnoreCase)
            ? "Cao cấp"
            : "Tiêu chuẩn";

    public static decimal EstimateVideoSubtotal(decimal videoSceneSellPoints, int sceneCount)
        => videoSceneSellPoints * sceneCount;
}

public static class TimelapseJobAccess
{
    public static bool CanRead(Guid? jobUserId, Guid? jobCustomerId, CurrentUserSession? currentUser)
        => currentUser is { IsAuthenticated: true, IsCustomer: true }
           && currentUser.CustomerId.HasValue
           && jobUserId == currentUser.UserId
           && jobCustomerId == currentUser.CustomerId;
}
