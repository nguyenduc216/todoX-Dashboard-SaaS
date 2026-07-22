namespace TodoX.Web.Services.AiProviders.Kie;

public sealed class KieOptions
{
    public const string SectionName = "AiProviders:Kie";
    public const string DefaultBaseUrl = "https://api.kie.ai";
    public const string DefaultModel = "kling-2.6/motion-control";
    public const string DefaultModeValue = "720p";

    public string ApiBaseUrl { get; set; } = DefaultBaseUrl;
    public string? CallbackUrl { get; set; }
    public string? CallbackSecret { get; set; }
    public string MotionControlModel { get; set; } = DefaultModel;
    public string DefaultMode { get; set; } = DefaultModeValue;
    public int HttpTimeoutSeconds { get; set; } = 120;
    public int PollIntervalSeconds { get; set; } = 10;
    public int MaxPollCount { get; set; } = 120;
    public int SubmitMaxRetry { get; set; } = 3;
    public int RateLimitRequestsPer10S { get; set; } = 20;
    // Phase 1 stores this for the future distributed/concurrent-task limiter; it is not currently enforced.
    public int MaxConcurrentTasks { get; set; } = 100;
    public string[] AllowedModes { get; set; } = new[] { "720p" };
    public string[] AllowedCharacterOrientations { get; set; } = new[] { "image" };

    public Uri GetBaseUri()
    {
        if (!Uri.TryCreate(ApiBaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("KIE_API_BASE_URL must be an absolute HTTP/HTTPS URL.");
        }

        return uri;
    }

    public Uri? GetCallbackUriOrNull()
    {
        if (string.IsNullOrWhiteSpace(CallbackUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(CallbackUrl.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("KIE_CALLBACK_URL must be an absolute HTTPS URL.");
        }

        return uri;
    }

    public TimeSpan HttpTimeout => TimeSpan.FromSeconds(Math.Clamp(HttpTimeoutSeconds, 1, 600));
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 1, 600));
}

public static class KieErrorCodes
{
    public const string ConfigMissing = "KIE_CONFIG_MISSING";
    public const string ApiKeyMissing = "KIE_API_KEY_MISSING";
    public const string InvalidInputUrl = "KIE_INVALID_INPUT_URL";
    public const string InvalidMode = "KIE_INVALID_MODE";
    public const string InvalidOrientation = "KIE_INVALID_ORIENTATION";
    public const string SubmitBadRequest = "KIE_SUBMIT_BAD_REQUEST";
    public const string Unauthorized = "KIE_UNAUTHORIZED";
    public const string Forbidden = "KIE_FORBIDDEN";
    public const string NotFound = "KIE_NOT_FOUND";
    public const string RateLimited = "KIE_RATE_LIMITED";
    public const string ProviderUnavailable = "KIE_PROVIDER_UNAVAILABLE";
    public const string TaskIdMissing = "KIE_TASK_ID_MISSING";
    public const string PollFailed = "KIE_POLL_FAILED";
    public const string ResultJsonInvalid = "KIE_RESULT_JSON_INVALID";
    public const string ResultUrlMissing = "KIE_RESULT_URL_MISSING";
    public const string TaskFailed = "KIE_TASK_FAILED";
    public const string PollTimeout = "KIE_POLL_TIMEOUT";
    public const string CallbackInvalid = "KIE_CALLBACK_INVALID";
    public const string Unknown = "KIE_UNKNOWN_ERROR";
}
