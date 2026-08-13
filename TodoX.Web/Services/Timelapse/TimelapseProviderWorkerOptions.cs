namespace TodoX.Web.Services.Timelapse;

public sealed class TimelapseProviderWorkerOptions
{
    public const string SectionName = "TimelapseProviderWorkers";

    public bool Enabled { get; set; } = true;
    public int ImageParallelism { get; set; } = 1;
    public int VideoParallelism { get; set; } = 3;
    public int FinalizerParallelism { get; set; } = 1;
    public int IdleDelayMs { get; set; } = 1500;
    public int PollDelayMs { get; set; } = 1500;
    public int ClaimMinutes { get; set; } = 10;
    public string Default79AiBaseUrl { get; set; } = "https://api.gommo.net/ai";
    public string DefaultSubmitPath { get; set; } = "/task/submit";
    public string DefaultPollPath { get; set; } = "/task/{task_id}";
}
