using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;

namespace TodoX.Web.Models.Timelapse;

public enum CustomerServiceDestination
{
    TimelapseCreator,
    ComingSoon,
    Unavailable
}

public sealed record CustomerServiceRoute(CustomerServiceDestination Destination, string? Route, string? Message);

public static class CustomerServiceRouting
{
    public static CustomerServiceRoute Resolve(string? serviceCode)
    {
        if (string.Equals(serviceCode, FixedTodoXServiceCatalog.Timelapse, StringComparison.OrdinalIgnoreCase))
        {
            return new(CustomerServiceDestination.TimelapseCreator, "/jobs/timelapse/new", null);
        }

        if (string.Equals(serviceCode, FixedTodoXServiceCatalog.RenderVideo, StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceCode, FixedTodoXServiceCatalog.RDance, StringComparison.OrdinalIgnoreCase))
        {
            return new(CustomerServiceDestination.ComingSoon, null, "Dịch vụ đang hoàn thiện.");
        }

        return new(CustomerServiceDestination.Unavailable, null, "Dịch vụ hiện chưa khả dụng.");
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
    public string Engine { get; set; } = "timelapse";
    public Guid ServiceId { get; set; }
    public string ServiceCode { get; set; } = FixedTodoXServiceCatalog.Timelapse;
    public string ProfileCode { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int SceneCount { get; set; }
    public IReadOnlyList<int> ProgressMapping { get; set; } = Array.Empty<int>();
    public string VideoMode { get; set; } = TimelapseRequestRules.FastMode;
    public string Ratio { get; set; } = TimelapseRequestRules.LandscapeRatio;
    public string Title { get; set; } = "Video Timelapse";
    public TimelapseOriginalImageSnapshot OriginalImage { get; set; } = new();
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

public static class TimelapseJobAccess
{
    public static bool CanRead(Guid? jobUserId, Guid? jobCustomerId, CurrentUserSession? currentUser)
        => currentUser is { IsAuthenticated: true, IsCustomer: true }
           && currentUser.CustomerId.HasValue
           && jobUserId == currentUser.UserId
           && jobCustomerId == currentUser.CustomerId;
}
