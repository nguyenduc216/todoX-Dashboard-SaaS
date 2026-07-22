namespace TodoX.Web.Services.AiProviders;

public sealed class YEScaleOptions
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.yescale.io";
    public string TaskSubmitPath { get; set; } = "/task/submit";
    public string TaskStatusPathTemplate { get; set; } = "/task/{task_id}";
    public int RequestTimeoutSeconds { get; set; } = 120;
    public int PollIntervalSeconds { get; set; } = 5;
    public int PollTimeoutSeconds { get; set; } = 600;
    public int MaxTransientRetries { get; set; } = 2;

    public Uri GetBaseUri()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("YEScale BaseUrl không hợp lệ.");
        }

        return uri;
    }

    public void EnsureEnabled()
    {
        if (!Enabled)
        {
            throw new InvalidOperationException("YEScale đang bị tắt trong cấu hình hệ thống.");
        }
    }

    public TimeSpan RequestTimeout => TimeSpan.FromSeconds(Math.Clamp(RequestTimeoutSeconds, 1, 600));
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 1, 60));
    public TimeSpan PollTimeout => TimeSpan.FromSeconds(Math.Clamp(PollTimeoutSeconds, 1, 3600));
    public int RetryCount => Math.Clamp(MaxTransientRetries, 0, 5);
}
