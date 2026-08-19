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
            return new(CustomerServiceDestination.RVideoCreator, BuildRoute("/jobs/rvideo/new", serviceId, serviceCode), null);
        }

        if (string.Equals(engineType, TodoXServiceEngineTypes.RDance, StringComparison.OrdinalIgnoreCase))
        {
            return new(CustomerServiceDestination.RDanceCreator, BuildRoute("/jobs/rdance/new", serviceId, serviceCode), null);
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

public sealed class TimelapseRenderProfileDto
{
    public string ProfileCode { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string ProfileJson { get; set; } = "{}";
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
    public bool RequireVideoConfirmation { get; set; }
    public bool AutoFinish { get; set; } = true;
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
    public Guid? CoreJobId { get; set; }
    public Guid ServiceId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string ProfileCode { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public int SceneCount { get; set; }
    public IReadOnlyList<int> ProgressMapping { get; set; } = Array.Empty<int>();
    public string VideoMode { get; set; } = TimelapseRequestRules.FastMode;
    public string Ratio { get; set; } = TimelapseRequestRules.LandscapeRatio;
    public string Title { get; set; } = "Video Timelapse";
    public bool RequireVideoConfirmation { get; set; }
    public bool AutoFinish { get; set; }
    public bool VideoRenderConfirmed { get; set; }
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
    public TimelapseWorkflowState Workflow { get; set; } = TimelapseWorkflowState.Empty;
}

public static class TimelapseParentStatuses
{
    public const string Draft = "DRAFT";
    public const string GeneratingImages = "GENERATING_IMAGES";
    public const string ImagesReady = "IMAGES_READY";
    public const string GeneratingVideos = "GENERATING_VIDEOS";
    public const string VideosReady = "VIDEOS_READY";
    public const string Finalizing = "FINALIZING";
    public const string Completed = "COMPLETED";
    public const string Paused = "PAUSED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";

    public static bool IsEditableStopped(string? status)
        => string.Equals(status, Draft, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Paused, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase);
}

public static class TimelapseOperationStatuses
{
    public const string Waiting = "WAITING";
    public const string Rendering = "RENDERING";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Invalidated = "INVALIDATED";
    public const string Cancelled = "CANCELLED";

    public static bool IsActive(string? status)
        => string.Equals(status, Rendering, StringComparison.OrdinalIgnoreCase);

    public static bool IsCurrentCompleted(string? status)
        => string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase);
}

public sealed class TimelapseStageImage
{
    public Guid Id { get; set; }
    public int StageIndex { get; set; }
    public int ProgressPercent { get; set; }
    public bool IsOriginal { get; set; }
    public int? DependsOnProgressPercent { get; set; }
    public string Status { get; set; } = TimelapseOperationStatuses.Waiting;
    public int Attempt { get; set; }
    public Guid? MediaId { get; set; }
    public string? PublicUrl { get; set; }
    public string? ObjectKey { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ErrorMessage { get; set; }
    public string PromptSnapshotJson { get; set; } = "{}";
    public string EffectivePrompt { get; set; } = string.Empty;
    public bool HasCustomerPromptOverride { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class TimelapseVideoClip
{
    public int ClipIndex { get; set; }
    public int StartProgressPercent { get; set; }
    public int EndProgressPercent { get; set; }
    public string Status { get; set; } = TimelapseOperationStatuses.Waiting;
    public int Attempt { get; set; }
    public Guid? MediaId { get; set; }
    public string? PublicUrl { get; set; }
    public string? ObjectKey { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class TimelapseFinalOutput
{
    public string Status { get; set; } = TimelapseOperationStatuses.Waiting;
    public int Version { get; set; }
    public Guid? MediaId { get; set; }
    public string? PublicUrl { get; set; }
    public string? ObjectKey { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class TimelapseWorkflowState
{
    public static TimelapseWorkflowState Empty { get; } = new();

    public string ParentStatus { get; set; } = TimelapseParentStatuses.Draft;
    public IReadOnlyList<TimelapseStageImage> Images { get; set; } = Array.Empty<TimelapseStageImage>();
    public IReadOnlyList<TimelapseVideoClip> Videos { get; set; } = Array.Empty<TimelapseVideoClip>();
    public TimelapseFinalOutput? FinalOutput { get; set; }
    public bool HasActiveOperations { get; set; }
    public bool CanEditRequest { get; set; } = true;
    public bool CanStartRender { get; set; } = true;
    public bool CanFinalize { get; set; }
    public bool RequiresVideoConfirmation { get; set; }
    public bool CanConfirmVideoRender { get; set; }
    public int ReadyVideoCount { get; set; }
    public int GeneratedImageCount { get; set; }
    public string CurrentStep { get; set; } = "Chưa bắt đầu";
}

public sealed record TimelapseImageProgressSummary(int Completed, int Total, int Percent);

public static class TimelapseProgress
{
    public static TimelapseImageProgressSummary CalculateImageProgress(IEnumerable<TimelapseStageImage> images)
    {
        var generated = images
            .Where(x => !x.IsOriginal && x.ProgressPercent < 100)
            .ToArray();
        var completed = generated.Count(x => TimelapseOperationStatuses.IsCurrentCompleted(x.Status));
        var percent = generated.Length == 0 ? 0 : completed * 100 / generated.Length;
        return new TimelapseImageProgressSummary(completed, generated.Length, percent);
    }
}

public static class TimelapseVideoOrchestration
{
    public static bool IsReady(
        TimelapseVideoClip clip,
        IEnumerable<TimelapseStageImage> images,
        bool requiresConfirmation = false,
        bool videoRenderConfirmed = false)
    {
        if (requiresConfirmation && !videoRenderConfirmed)
        {
            return false;
        }

        var completedProgress = images
            .Where(x => TimelapseOperationStatuses.IsCurrentCompleted(x.Status))
            .Select(x => x.ProgressPercent)
            .ToHashSet();
        return completedProgress.Contains(clip.StartProgressPercent)
               && completedProgress.Contains(clip.EndProgressPercent);
    }

    public static bool HasCompletedPreview(TimelapseVideoClip clip)
        => TimelapseOperationStatuses.IsCurrentCompleted(clip.Status)
           && !string.IsNullOrWhiteSpace(clip.PublicUrl);
}

public static class TimelapseStatusText
{
    public static string Parent(string? status)
        => status?.ToUpperInvariant() switch
        {
            TimelapseParentStatuses.Draft => "Bản nháp",
            TimelapseParentStatuses.GeneratingImages => "Đang tạo ảnh",
            TimelapseParentStatuses.ImagesReady => "Ảnh đã sẵn sàng",
            TimelapseParentStatuses.GeneratingVideos => "Đang tạo video",
            TimelapseParentStatuses.VideosReady => "Video đã sẵn sàng",
            TimelapseParentStatuses.Finalizing => "Đang ghép video",
            TimelapseParentStatuses.Completed => "Hoàn thành",
            TimelapseParentStatuses.Failed => "Thất bại",
            TimelapseParentStatuses.Paused => "Tạm dừng",
            TimelapseParentStatuses.Cancelled => "Đã dừng",
            _ => "Chưa bắt đầu"
        };

    public static string Operation(string? status)
        => status?.ToUpperInvariant() switch
        {
            TimelapseOperationStatuses.Waiting => "Đang chờ",
            TimelapseOperationStatuses.Rendering => "Đang xử lý",
            TimelapseOperationStatuses.Completed => "Hoàn thành",
            TimelapseOperationStatuses.Failed => "Thất bại",
            TimelapseOperationStatuses.Invalidated => "Cần tạo lại",
            TimelapseOperationStatuses.Cancelled => "Đã dừng",
            _ => "Chưa bắt đầu"
        };
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
        6 => [0, 25, 40, 55, 70, 75, 90, 100],
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

public sealed record TimelapseStageGraph(
    IReadOnlyList<int> ImageProgressions,
    IReadOnlyList<TimelapseVideoEdge> VideoClips,
    IReadOnlyList<int> GeneratedImageOrder);

public sealed record TimelapseVideoEdge(int ClipIndex, int StartProgressPercent, int EndProgressPercent);

public sealed record TimelapseInvalidationPlan(
    IReadOnlyList<int> ImageProgressions,
    IReadOnlyList<TimelapseVideoEdge> VideoClips,
    bool FinalOutput);

public sealed record TimelapseRerenderImpact(
    int SelectedProgressPercent,
    IReadOnlyList<int> ImageProgressesToInvalidate,
    IReadOnlyList<int> VideoClipIndexesToInvalidate,
    bool InvalidatesFinalOutput);

public sealed record TimelapseImagePromptDialogResult(string Prompt, bool Rerender);

public static class TimelapseStageGraphBuilder
{
    public static TimelapseStageGraph Build(int sceneCount)
    {
        var images = TimelapseRequestRules.GetProgressMapping(sceneCount).ToArray();
        var clips = images.Zip(images.Skip(1), (start, end) => new { start, end })
            .Select((x, index) => new TimelapseVideoEdge(index + 1, x.start, x.end))
            .ToArray();
        var generatedOrder = images
            .Where(x => x < 100)
            .OrderByDescending(x => x)
            .ToArray();

        return new TimelapseStageGraph(images, clips, generatedOrder);
    }

    public static TimelapseInvalidationPlan PlanImageRerender(int sceneCount, int progressPercent)
    {
        if (progressPercent >= 100)
        {
            return new TimelapseInvalidationPlan(Array.Empty<int>(), Array.Empty<TimelapseVideoEdge>(), false);
        }

        var graph = Build(sceneCount);
        var invalidImages = graph.ImageProgressions
            .Where(x => x < progressPercent)
            .ToArray();
        var invalidVideos = graph.VideoClips
            .Where(x => x.StartProgressPercent <= progressPercent && x.EndProgressPercent >= invalidImages.DefaultIfEmpty(progressPercent).Min())
            .ToArray();

        return new TimelapseInvalidationPlan(invalidImages, invalidVideos, invalidVideos.Length > 0);
    }

    public static TimelapseInvalidationPlan PlanOriginalReplacement(int sceneCount)
    {
        var graph = Build(sceneCount);
        return new TimelapseInvalidationPlan(
            graph.ImageProgressions.Where(x => x < 100).ToArray(),
            graph.VideoClips,
            true);
    }

    public static TimelapseInvalidationPlan PlanVideoRerender(int sceneCount, int clipIndex)
    {
        var clip = Build(sceneCount).VideoClips.FirstOrDefault(x => x.ClipIndex == clipIndex);
        return new TimelapseInvalidationPlan(
            Array.Empty<int>(),
            clip is null ? Array.Empty<TimelapseVideoEdge>() : new[] { clip },
            true);
    }
}

public static class TimelapseRerenderImpactPlanner
{
    public static TimelapseRerenderImpact Plan(int sceneCount, int progressPercent)
    {
        var graph = TimelapseStageGraphBuilder.Build(sceneCount);
        if (progressPercent >= 100 || !graph.GeneratedImageOrder.Contains(progressPercent))
        {
            throw new InvalidOperationException("Ảnh thành phẩm 100% không thể render lại bằng AI.");
        }

        var invalidation = TimelapseStageGraphBuilder.PlanImageRerender(sceneCount, progressPercent);
        var images = new[] { progressPercent }
            .Concat(invalidation.ImageProgressions)
            .Distinct()
            .OrderByDescending(x => x)
            .ToArray();

        return new TimelapseRerenderImpact(
            progressPercent,
            images,
            invalidation.VideoClips.Select(x => x.ClipIndex).Distinct().OrderBy(x => x).ToArray(),
            invalidation.FinalOutput);
    }
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
           && jobCustomerId == currentUser.CustomerId;
}
