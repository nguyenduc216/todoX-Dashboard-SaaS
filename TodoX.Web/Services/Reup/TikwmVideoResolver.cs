using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class TikwmVideoResolver
{
    private readonly HttpClient _http;
    private readonly ReupCampaignOptions _options;

    public TikwmVideoResolver(HttpClient http, IOptions<ReupCampaignOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<TikwmResolveResult> ResolveAsync(string sourceUrl, CancellationToken ct)
    {
        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["url"] = sourceUrl });
            using var response = await _http.PostAsync(_options.TikwmEndpoint, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"TIKWM_HTTP_{(int)response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var data))
            {
                throw new InvalidOperationException("TIKWM_NO_DATA");
            }

            foreach (var name in new[] { "play", "wmplay", "hdplay", "video_url", "url" })
            {
                if (data.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    var url = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return new TikwmResolveResult(url);
                    }
                }
            }

            throw new InvalidOperationException("TIKWM_NO_VIDEO_URL");
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException("TIKWM_TIMEOUT", ex);
        }
    }
}
