namespace TodoX.Web.Services.VideoRender;

public sealed class VideoRenderOptions
{
    public string StorageRoot { get; set; } = "wwwroot/uploads/video-render";
    public string PublicBase { get; set; } = "/uploads/video-render";
    public string MockSceneVideoPath { get; set; } = string.Empty;
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int MaxUploadImageBytes { get; set; } = 10 * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 500 * 1024 * 1024;
    public int SceneSecondsDefault { get; set; } = 8;
    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxPollDurationMinutes { get; set; } = 30;
    public int MaxConsecutivePollErrors { get; set; } = 5;
    public int MaxConcurrentSceneJobs { get; set; } = 3;
    public int MaxConcurrentMergeJobs { get; set; } = 1;
    public int MergeTimeoutMinutes { get; set; } = 30;
    public bool AutoMergeWhenAllScenesReady { get; set; } = false;
    public bool MockMode { get; set; } = true;
    public string[] SupportedResolutions { get; set; } = ["720p", "1080p"];
}
