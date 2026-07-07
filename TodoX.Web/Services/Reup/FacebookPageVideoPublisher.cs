using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class FacebookPageVideoPublisher
{
    private readonly HttpClient _http;
    private readonly ReupCampaignOptions _options;
    private readonly ReupLogService _logs;

    public FacebookPageVideoPublisher(HttpClient http, IOptions<ReupCampaignOptions> options, ReupLogService logs)
    {
        _http = http;
        _options = options.Value;
        _logs = logs;
    }

    public async Task<FacebookPublishResult> PublishAsync(Guid campaignId, Guid taskId, string externalPageId, string token, string localVideoPath, string? resolvedVideoUrl, string? description, CancellationToken ct)
    {
        var endpoint = $"https://graph-video.facebook.com/{_options.FacebookGraphVersion}/{Uri.EscapeDataString(externalPageId)}/videos";
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(token), "access_token");
            if (!string.IsNullOrWhiteSpace(description))
            {
                form.Add(new StringContent(description), "description");
            }

            await using var file = File.OpenRead(localVideoPath);
            using var source = new StreamContent(file);
            source.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            form.Add(source, "source", Path.GetFileName(localVideoPath));

            using var response = await _http.PostAsync(endpoint, form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return ParseSuccess(body);
            }

            await _logs.WriteAsync(campaignId, taskId, "FACEBOOK_BINARY_UPLOAD_FAILED_FALLBACK_FILE_URL", "Binary upload failed; trying file_url fallback.", "warning", new { status = (int)response.StatusCode }, ct);
            if (!string.IsNullOrWhiteSpace(resolvedVideoUrl))
            {
                return await PublishByFileUrlAsync(endpoint, token, resolvedVideoUrl, description, ct);
            }

            return new(false, null, null, body, ClassifyFacebookError(response.StatusCode), body);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return new(false, null, null, null, "FACEBOOK_TIMEOUT", ex.Message);
        }
        catch (Exception ex)
        {
            return new(false, null, null, null, "FACEBOOK_UPLOAD_FAILED", ex.Message);
        }
    }

    private async Task<FacebookPublishResult> PublishByFileUrlAsync(string endpoint, string token, string fileUrl, string? description, CancellationToken ct)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["access_token"] = token,
            ["file_url"] = fileUrl,
            ["description"] = description
        }.Where(x => !string.IsNullOrWhiteSpace(x.Value)).ToDictionary(x => x.Key, x => x.Value!));

        using var response = await _http.PostAsync(endpoint, form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return response.IsSuccessStatusCode
            ? ParseSuccess(body)
            : new(false, null, null, body, ClassifyFacebookError(response.StatusCode), body);
    }

    private static FacebookPublishResult ParseSuccess(string body)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var id = doc.RootElement.TryGetProperty("id", out var prop) ? prop.GetString() : null;
            return new(true, id, string.IsNullOrWhiteSpace(id) ? null : $"https://www.facebook.com/{id}", body, null, null);
        }
        catch
        {
            return new(true, null, null, body, null, null);
        }
    }

    private static string ClassifyFacebookError(System.Net.HttpStatusCode status)
        => (int)status >= 500 ? "FACEBOOK_5XX" : "FACEBOOK_UPLOAD_FAILED";
}
