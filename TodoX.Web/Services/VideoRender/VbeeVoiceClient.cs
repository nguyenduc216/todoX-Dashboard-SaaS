using System.Net;
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

public sealed class VbeeVoiceSubmitException : InvalidOperationException
{
    public VbeeVoiceSubmitException(
        string message,
        HttpStatusCode httpStatusCode,
        IReadOnlyList<string> responseTopLevelKeys,
        JsonObject responseShape,
        string? providerStatus = null,
        string? errorCode = null,
        string? errorMessage = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        ResponseTopLevelKeys = responseTopLevelKeys;
        ResponseShape = responseShape;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public HttpStatusCode HttpStatusCode { get; }
    public IReadOnlyList<string> ResponseTopLevelKeys { get; }
    public JsonObject ResponseShape { get; }
    public string? ProviderStatus { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
}

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
        var callbackUrl = string.IsNullOrWhiteSpace(request.CallbackUrl)
            ? options.GetCallbackUriOrNull()?.ToString()
            : VbeeOptions.BuildAuthorizedCallbackUriOrNull(request.CallbackUrl, options.CallbackSecret)?.ToString();
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            throw new InvalidOperationException("VBEE_CALLBACK_URL_MISSING");
        }

        var token = options.GetTokenOrThrow();
        using var message = new HttpRequestMessage(HttpMethod.Post, options.GetTtsUri());
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        message.Headers.Accept.ParseAdd("application/json");
        var body = new JsonObject
        {
            ["app_id"] = request.AppId ?? options.AppId,
            ["input_text"] = request.NarrationText,
            ["voice_code"] = request.VoiceCode,
            ["audio_type"] = "mp3",
            ["bitrate"] = request.Bitrate,
            ["speed_rate"] = request.SpeedRate,
            ["callback_url"] = callbackUrl
        };
        if (request.SampleRate > 0)
        {
            body["sample_rate"] = request.SampleRate;
        }

        message.Content = JsonContent.Create(body, options: JsonOptions);

        using var response = await _http.SendAsync(message, ct);
        var payload = await ReadJsonObjectAsync(response, ct);
        var requestId = FindStringRecursive(payload, "request_id", "requestId", "requestID");
        var audioUrl = FindStringRecursive(payload, "audio_link", "audio_url", "audioUrl", "download_url", "downloadUrl", "url");
        var status = FindStringRecursive(payload, "status", "state");
        if (!response.IsSuccessStatusCode)
        {
            var topLevelKeys = GetResponseTopLevelKeys(payload);
            throw new VbeeVoiceSubmitException(
                $"Vbee submit returned HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                topLevelKeys,
                BuildResponseShape(payload),
                FindStringRecursive(payload, "status", "state"),
                FindStringRecursive(payload, "error_code", "errorCode", "code"),
                FindStringRecursive(payload, "error_message", "errorMessage", "message", "error"));
        }

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
            => keys.Select(key => FindStringRecursive(payload, key)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
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
            payload["parse_error"] = "invalid_json";
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

    internal static string? FindStringRecursive(JsonNode? payload, params string[] keys)
    {
        if (payload is JsonObject obj)
        {
            if (TryFindStringDirect(obj, keys, out var direct))
            {
                return direct;
            }

            foreach (var property in obj)
            {
                var found = FindStringRecursive(property.Value, keys);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }
        else if (payload is JsonArray array)
        {
            foreach (var item in array)
            {
                var found = FindStringRecursive(item, keys);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> GetResponseTopLevelKeys(JsonObject payload)
        => payload.Select(x => x.Key).ToArray();

    internal static JsonObject BuildResponseShape(JsonObject payload)
    {
        var shape = new JsonObject
        {
            ["keys"] = new JsonArray(payload.Select(x => JsonValue.Create(x.Key)).ToArray())
        };

        foreach (var property in payload)
        {
            if (property.Value is JsonObject childObject)
            {
                shape[$"{property.Key}Keys"] = new JsonArray(childObject.Select(x => JsonValue.Create(x.Key)).ToArray());
                continue;
            }

            if (property.Value is JsonArray childArray)
            {
                var firstObject = childArray.OfType<JsonObject>().FirstOrDefault(item => item.Count > 0);
                if (firstObject is not null)
                {
                    shape[$"{property.Key}Keys"] = new JsonArray(firstObject.Select(x => JsonValue.Create(x.Key)).ToArray());
                }
            }
        }

        return shape;
    }

    private static string? FindString(JsonObject payload, params string[] keys)
        => FindStringRecursive(payload, keys);

    private static bool TryFindStringDirect(JsonObject payload, IReadOnlyCollection<string> keys, out string? value)
    {
        foreach (var key in keys)
        {
            if (payload[key] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                value = text.Trim();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static long? TryReadLong(JsonObject payload, params string[] keys)
        => TryReadLongRecursive(payload, keys);

    private static long? TryReadLongRecursive(JsonNode? payload, params string[] keys)
    {
        if (payload is JsonObject obj)
        {
            foreach (var key in keys)
            {
                if (obj.TryGetPropertyValue(key, out var node) && node is JsonValue valueNode)
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

                if (obj.TryGetPropertyValue(key, out var child))
                {
                    var found = TryReadLongRecursive(child, keys);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            foreach (var property in obj)
            {
                var found = TryReadLongRecursive(property.Value, keys);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (payload is JsonArray array)
        {
            foreach (var item in array)
            {
                var found = TryReadLongRecursive(item, keys);
                if (found is not null)
                {
                    return found;
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
