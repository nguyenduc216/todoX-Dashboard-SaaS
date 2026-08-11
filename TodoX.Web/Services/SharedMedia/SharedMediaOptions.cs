namespace TodoX.Web.Services.SharedMedia;

public sealed class SharedMediaOptions
{
    public const string SectionName = "SharedMedia";

    public string? StorageRoot { get; set; }
    public string RequestPath { get; set; } = "/media";
    public IndustrySolutionMediaOptions IndustrySolutions { get; set; } = new();
}

public sealed class IndustrySolutionMediaOptions
{
    public string RootSubfolder { get; set; } = @"landing\industries";
    public string ThumbnailSubfolder { get; set; } = "thumbnails";
    public string VideoSubfolder { get; set; } = "videos";
    public string TempSubfolder { get; set; } = "temp";
    public long MaxThumbnailBytes { get; set; } = 5 * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 200L * 1024 * 1024;
    public string[] AllowedThumbnailExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];
    public string[] AllowedVideoExtensions { get; set; } = [".mp4", ".webm"];
    public VideoTranscodeOptions VideoTranscode { get; set; } = new();
}

public sealed class VideoTranscodeOptions
{
    public bool Enabled { get; set; } = true;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int TimeoutSeconds { get; set; } = 900;
    public string Preset { get; set; } = "medium";
    public int Crf { get; set; } = 23;
    public int AudioBitrateKbps { get; set; } = 160;
}
