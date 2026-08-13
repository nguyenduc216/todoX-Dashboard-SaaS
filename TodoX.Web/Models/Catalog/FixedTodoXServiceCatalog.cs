namespace TodoX.Web.Models.Catalog;

public sealed record FixedTodoXServiceDefinition(
    string ServiceCode,
    string DisplayName,
    string Description,
    string ServiceType,
    string WorkflowCode,
    string Status,
    int SortOrder);

public sealed class CatalogServiceView
{
    public Guid Id { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public string? WorkflowCode { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? CoverImageUrl { get; set; }
    public string? StartingPriceSummary { get; set; }
    public bool Enabled { get; set; }
    public int SortOrder { get; set; }
}

public static class FixedTodoXServiceCatalog
{
    public const string EnabledStatus = "enabled";
    public const string DisabledStatus = "disabled";

    public const string Timelapse = "TIMELAPSE";
    public const string RenderVideo = "RVIDEO";
    public const string RDance = "RDANCE";

    public static IReadOnlyList<FixedTodoXServiceDefinition> Services { get; } =
    [
        new(
            Timelapse,
            "Video Timelapse AI",
            "Tạo video mô phỏng quá trình xây dựng, thi công và hoàn thiện từ một ảnh thành phẩm.",
            TodoXServiceEngineTypes.Timelapse,
            "CONSTRUCTION_TIMELAPSE",
            EnabledStatus,
            10),
        new(
            RenderVideo,
            "Render Video AI",
            "Tạo video theo scene từ hình ảnh và prompt, hỗ trợ nội dung, giọng đọc và nhạc nền.",
            TodoXServiceEngineTypes.RVideo,
            "TODOX_RENDERVIDEO",
            EnabledStatus,
            20),
        new(
            RDance,
            "R Dance AI",
            "Tạo video chuyển động theo video mẫu bằng AI Motion Control.",
            TodoXServiceEngineTypes.RDance,
            "RDANCE_79AI",
            EnabledStatus,
            30)
    ];

    public static bool IsFixedServiceCode(string? serviceCode)
        => Services.Any(x => string.Equals(x.ServiceCode, serviceCode, StringComparison.OrdinalIgnoreCase));

    public static bool TryGetByCode(string? serviceCode, out FixedTodoXServiceDefinition definition)
    {
        var match = Services.FirstOrDefault(x => string.Equals(x.ServiceCode, serviceCode, StringComparison.OrdinalIgnoreCase));
        definition = match!;
        return match is not null;
    }

    public static string ResolveServiceType(string serviceCode)
    {
        if (!TryGetByCode(serviceCode, out var definition))
        {
            throw new ArgumentException($"Unknown fixed todoX service code '{serviceCode}'.", nameof(serviceCode));
        }

        return definition.ServiceType;
    }

    public static bool IsEnabledStatus(string? status)
        => string.Equals(status, EnabledStatus, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);
}
