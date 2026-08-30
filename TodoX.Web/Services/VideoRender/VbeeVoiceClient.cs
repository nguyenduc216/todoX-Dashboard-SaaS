using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.VideoRender;

public sealed record VbeeVoiceSubmitRequest(
    string VoiceCode,
    string NarrationText,
    decimal TtsRate,
    string? VoiceInstruction,
    string CallbackUrl,
    string? RequestId,
    int SampleRate,
    int Bitrate,
    decimal SpeedRate,
    string? AppId);

public sealed record VbeeVoiceSubmitResult(
    string? RequestId,
    string? AudioUrl,
    string? RawStatus,
    JsonObject? Response);

public sealed record VbeeVoiceCallbackResult(
    string RequestId,
    long? SceneId,
    string? AudioUrl,
    string? Status,
    string? ErrorCode,
    string? ErrorMessage,
    JsonObject Raw);

public interface IVbeeVoiceClient
{
    Task<VbeeVoiceSubmitResult> SubmitAsync(VbeeVoiceSubmitRequest request, CancellationToken ct = default);
    Task<VbeeVoiceSubmitResult> SubmitAsync(VbeeVoiceSubmitRequest request, VbeeOptions options, CancellationToken ct = default);
    Task<VbeeVoiceCallbackResult> ParseCallbackAsync(HttpRequest request, CancellationToken ct = default);
    Task<VbeeVoiceCallbackResult> ParseCallbackPayloadAsync(string rawBody, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<JsonObject> GetStatusAsync(string requestId, CancellationToken ct = default);
    Task<JsonObject> GetStatusAsync(string requestId, VbeeOptions options, CancellationToken ct = default);
}

public sealed class VbeeVoiceClient : IVbeeVoiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<VbeeOptions> _options;

    public VbeeVoiceClient(HttpClient http, IOptionsMonitor<VbeeOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<VbeeVoiceSubmitResult> SubmitAsync(VbeeVoiceSubmitRequest request, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        return await SubmitAsync(request, options, ct);
    }

    public async Task<VbeeVoiceSubmitResult> SubmitAsync(VbeeVoiceSubmitRequest request, VbeeOptions options, CancellationToken ct = default)
    {
        var token = options.GetTokenOrThrow();
        using var message = new HttpRequestMessage(HttpMethod.Post, options.GetTtsUri());
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.ParseAdd("application/json");
        var body = new JsonObject
        {
            ["app_id"] = request.AppId ?? options.AppId,
            ["callback_url"] = request.CallbackUrl,
            ["input_text"] = request.NarrationText,
            ["voice_code"] = request.VoiceCode,
            ["audio_type"] = "mp3",
            ["bitrate"] = request.Bitrate,
            ["speed_rate"] = request.SpeedRate
        };
        if (request.SampleRate > 0)
        {
            body["sample_rate"] = request.SampleRate;
        }
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            body["request_id"] = request.RequestId;
        }

        message.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _http.SendAsync(message, ct);
        var payload = await ReadJsonObjectAsync(response, ct);
        var requestId = FindString(payload, "request_id", "requestId", "requestID");
        var audioUrl = FindString(payload, "audio_link", "audio_url", "audioUrl", "download_url", "downloadUrl", "url");
        var status = FindString(payload, "status", "state");
        return new VbeeVoiceSubmitResult(requestId, NormalizeUrl(audioUrl), status, payload);
    }

    public Task<VbeeVoiceCallbackResult> ParseCallbackAsync(HttpRequest request, CancellationToken ct = default)
        => ParseCallbackPayloadAsyncFromRequestAsync(request, ct);

    public Task<VbeeVoiceCallbackResult> ParseCallbackPayloadAsync(string rawBody, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
        => Task.FromResult(BuildCallbackResult(ParsePayload(rawBody), query));

    public async Task<JsonObject> GetStatusAsync(string requestId, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        return await GetStatusAsync(requestId, options, ct);
    }

    public async Task<JsonObject> GetStatusAsync(string requestId, VbeeOptions options, CancellationToken ct = default)
    {
        var token = options.GetTokenOrThrow();
        using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(options.GetTtsUri().ToString().TrimEnd('/') + "/" + Uri.EscapeDataString(requestId) + "/callback-result"));
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.ParseAdd("application/json");
        using var response = await _http.SendAsync(message, ct);
        return await ReadJsonObjectAsync(response, ct);
    }

    private async Task<VbeeVoiceCallbackResult> ParseCallbackPayloadAsyncFromRequestAsync(HttpRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var query = request.Query.ToDictionary(x => x.Key, x => (string?)x.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        return BuildCallbackResult(ParsePayload(raw), query);
    }

    private static VbeeVoiceCallbackResult BuildCallbackResult(JsonObject payload, IReadOnlyDictionary<string, string?>? query)
    {
        string? GetValue(params string[] keys)
            => keys.Select(key => FindString(payload, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
               ?? (query is null
                   ? null
                   : keys.Select(key => query.TryGetValue(key, out var value) ? value : null)
                         .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)));

        var requestId = GetValue("request_id", "requestId", "requestID")
                        ?? throw new InvalidOperationException("Missing Vbee request_id.");
        var sceneId = TryReadLong(payload, "scene_id", "sceneId");
        var audioUrl = NormalizeUrl(GetValue("audio_link", "audio_url", "audioUrl", "download_url", "downloadUrl", "url"));
        var status = GetValue("status", "state");
        var errorCode = GetValue("error_code", "errorCode", "code");
        var errorMessage = GetValue("error_message", "errorMessage", "message", "error");
        return new VbeeVoiceCallbackResult(requestId, sceneId, audioUrl, status, errorCode, errorMessage, payload);
    }

    private static JsonObject ParsePayload(string rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return new JsonObject();
        }

        var text = rawBody.Trim().TrimStart('\uFEFF');
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            var payload = new JsonObject();
            var requestMatch = Regex.Match(text, @"(?:request_id|requestId|requestID)\s*[:=]\s*[""']?([A-Za-z0-9._:-]+)", RegexOptions.IgnoreCase);
            var urlMatch = Regex.Match(text, @"https?:\/\/[^\s""'<>()]+\.mp3(?:\?[^\s""'<>()]*)?", RegexOptions.IgnoreCase);
            if (requestMatch.Success)
            {
                payload["request_id"] = requestMatch.Groups[1].Value;
            }
            if (urlMatch.Success)
            {
                payload["audio_url"] = urlMatch.Value;
            }
            payload["raw_text"] = text[..Math.Min(text.Length, 6000)];
            return payload;
        }
    }

    private static async Task<JsonObject> ReadJsonObjectAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        var parsed = ParsePayload(raw);
        parsed["http_status"] = (int)response.StatusCode;
        return parsed;
    }

    private static string? FindString(JsonObject payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        return null;
    }

    private static long? TryReadLong(JsonObject payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetPropertyValue(key, out var node) && node is JsonValue valueNode)
            {
                if (valueNode.TryGetValue<long>(out var value))
                {
                    return value;
                }
                if (valueNode.TryGetValue<string>(out var text) && long.TryParse(text, out var parsed))
                {
                    return parsed;
                }
            }
        }
        return null;
    }

    private static string? NormalizeUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
}
