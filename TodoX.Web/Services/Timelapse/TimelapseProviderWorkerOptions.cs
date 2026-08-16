namespace TodoX.Web.Services.Timelapse;

public sealed class TimelapseProviderWorkerOptions
{
    private static readonly HashSet<string> SupportedVideoResolutions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "480p",
            "720p",
            "1080p"
        };

    public const string SectionName = "TimelapseProviderWorkers";

    public bool Enabled { get; set; } = true;
    public int ImageParallelism { get; set; } = 1;
    public int VideoParallelism { get; set; } = 3;
    public int FinalizerParallelism { get; set; } = 1;
    public int IdleDelayMs { get; set; } = 1500;
    public int PollDelayMs { get; set; } = 1500;
    public int ClaimMinutes { get; set; } = 10;
    public int FinalizerFfmpegTimeoutSeconds { get; set; } = 120;
    public string ProviderCode { get; set; } = "79ai";
    public string ImageCapabilityCode { get; set; } = "image_generation";
    public string ImageModelName { get; set; } = "seedream_5_0";
    public string VideoCapabilityCode { get; set; } = "image_to_video";
    public string VideoModelName { get; set; } = "seedance_20_pro";
    public string Default79AiBaseUrl { get; set; } = "https://api.gommo.net/ai";
    public string DefaultImageSubmitPath { get; set; } = "/generateImage";
    public string DefaultImageUploadPath { get; set; } = "/image-upload";
    public string DefaultImagePollPath { get; set; } = "/image";
    public string DefaultVideoSubmitPath { get; set; } = "/create-video";
    public string DefaultVideoPollPath { get; set; } = "/video";
    public string DefaultImageReferenceField { get; set; } = "base64Image";
    public string DefaultImageMode { get; set; } = "vip";
    public string DefaultImageResolution { get; set; } = "1k";
    public string DefaultImageProjectId { get; set; } = "default";
    public string DefaultVideoResolution { get; set; } = "720p";

    internal static string NormalizeVideoResolution(string? resolution)
    {
        var normalized = resolution?.Trim().ToLowerInvariant();
        if (normalized is null || !SupportedVideoResolutions.Contains(normalized))
        {
            throw new InvalidOperationException(
                "Cấu hình độ phân giải video Timelapse không hợp lệ. Giá trị hỗ trợ: 480p, 720p, 1080p.");
        }

        return normalized;
    }
}
