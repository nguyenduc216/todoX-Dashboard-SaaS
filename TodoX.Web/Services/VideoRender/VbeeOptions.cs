namespace TodoX.Web.Services.VideoRender;

public sealed class VbeeOptions
{
    public const string SectionName = "Vbee";
    public const string DefaultApiBaseUrl = "https://vbee.vn/api/v1";
    public const string DefaultTtsPath = "/tts";
    public const string DefaultCallbackPath = "/api/providers/vbee/callback";

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;
    public string TtsPath { get; set; } = DefaultTtsPath;
    public string? ApiToken { get; set; }
    public string? AppId { get; set; }
    public string? CallbackUrl { get; set; }
    public string? CallbackSecret { get; set; }
    public int DefaultSampleRate { get; set; } = 0;
    public Dictionary<string, int> VoiceSampleRates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int DefaultBitrate { get; set; } = 128;
    public decimal DefaultSpeedRate { get; set; } = 1.0m;
    public int HttpTimeoutSeconds { get; set; } = 120;
    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxPollCount { get; set; } = 3;

    public Uri GetApiBaseUri()
    {
        if (!Uri.TryCreate(ApiBaseUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("VBEE_API_BASE_URL must be an absolute HTTP/HTTPS URL.");
        }

        return uri;
    }

    public Uri GetTtsUri()
        => new(new Uri(GetApiBaseUri().ToString().TrimEnd('/') + "/"), TtsPath.TrimStart('/'));

    public Uri? GetCallbackUriOrNull()
        => BuildAuthorizedCallbackUriOrNull(CallbackUrl, CallbackSecret);

    public static Uri? BuildAuthorizedCallbackUriOrNull(string? callbackUrl, string? callbackSecret)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            return null;
        }

        if (!Uri.TryCreate(callbackUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("VBEE_CALLBACK_URL must be an absolute HTTP/HTTPS URL.");
        }

        var queryParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var key = Uri.UnescapeDataString(part.Split('=', 2)[0]);
                return !string.Equals(key, "secret", StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(key, "callback_secret", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (!string.IsNullOrWhiteSpace(callbackSecret))
        {
            queryParts.Add("secret=" + Uri.EscapeDataString(callbackSecret.Trim()));
        }
        var builder = new UriBuilder(uri) { Query = string.Join("&", queryParts) };
        uri = builder.Uri;

        return uri;
    }

    public string GetTokenOrThrow()
    {
        var token = ApiToken ?? Environment.GetEnvironmentVariable("VBEE_API_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("VBEE_API_TOKEN is missing.");
        }

        return token.Trim();
    }

    public TimeSpan HttpTimeout => TimeSpan.FromSeconds(Math.Clamp(HttpTimeoutSeconds, 1, 600));
    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 1, 600));

    public int ResolveSampleRate(string? voiceCode)
    {
        if (!string.IsNullOrWhiteSpace(voiceCode)
            && VoiceSampleRates.TryGetValue(voiceCode.Trim(), out var voiceSpecific)
            && voiceSpecific > 0)
        {
            return voiceSpecific;
        }

        return DefaultSampleRate > 0 ? DefaultSampleRate : 0;
    }
}
