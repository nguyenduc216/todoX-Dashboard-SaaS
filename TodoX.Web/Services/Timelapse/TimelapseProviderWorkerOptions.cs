namespace TodoX.Web.Services.Timelapse;

public sealed class TimelapseProviderWorkerOptions
{
    private const string Seedream50ModelName = "seedream_5_0";
    private const string NanoBanana2ModelName = "google_image_gen_banana_2";

    private static readonly HashSet<string> SupportedVideoResolutions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "480p",
            "720p",
            "1080p"
        };

    private static readonly HashSet<string> Seedream50ImageResolutions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "2k",
            "4k"
        };

    public const string SectionName = "TimelapseProviderWorkers";

    public bool Enabled { get; set; } = true;
    public int ImageParallelism { get; set; } = 1;
    public int VideoParallelism { get; set; } = 3;
    public int FinalizerParallelism { get; set; } = 1;
    public int IdleDelayMs { get; set; } = 1500;
    public int PollDelayMs { get; set; } = 1500;
    public int ClaimMinutes { get; set; } = 10;
    public int HeartbeatSeconds { get; set; } = 60;
    public int FinalizerFfmpegTimeoutSeconds { get; set; } = 120;
    public string ProviderCode { get; set; } = "79ai";
    public string ImageCapabilityCode { get; set; } = "image_generation";
    public string ImageModelName { get; set; } = NanoBanana2ModelName;
    public string[] ImageModelsWithReference { get; set; } = [NanoBanana2ModelName, Seedream50ModelName];
    public string[] ImageModelsWithoutReference { get; set; } = [Seedream50ModelName, NanoBanana2ModelName];
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
    public string DefaultImageResolution { get; set; } = "2k";
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

    internal static string NormalizeImageResolution(string? modelName, string? resolution)
    {
        var normalized = resolution?.Trim().ToLowerInvariant();
        if (string.Equals(modelName, Seedream50ModelName, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "1k", StringComparison.OrdinalIgnoreCase))
            {
                return "2k";
            }

            if (Seedream50ImageResolutions.Contains(normalized))
            {
                return normalized;
            }

            throw new InvalidOperationException(
                "Cấu hình độ phân giải ảnh Timelapse cho seedream_5_0 không hợp lệ. Giá trị hỗ trợ: 2k, 4k.");
        }

        if (string.Equals(modelName, NanoBanana2ModelName, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "2k";
            }

            return normalized switch
            {
                "1k" or "2k" or "4k" or "8k" or "10k" or "12k" => normalized,
                _ => throw new InvalidOperationException(
                    "Cau hinh do phan giai anh Timelapse cho google_image_gen_banana_2 khong hop le. Gia tri ho tro: 1k, 2k, 4k, 8k, 10k, 12k.")
            };
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Cấu hình độ phân giải ảnh Timelapse không được để trống.");
        }

        return normalized;
    }
}
