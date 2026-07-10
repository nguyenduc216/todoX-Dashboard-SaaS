namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderOptions
{
    public string StorageRoot { get; set; } = "wwwroot/uploads/video-render";
    public string PublicBase { get; set; } = "/uploads/video-render";
    public string MockSceneVideoPath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int MaxUploadImageBytes { get; set; } = 10 * 1024 * 1024;
    public int SceneSecondsDefault { get; set; } = 8;
    public int PollIntervalSeconds { get; set; } = 5;
    public bool MockMode { get; set; } = true;
}

