using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TodoX.Web.Services.AiProviders;

public interface IAi79TaskClient
{
    Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default);
    Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default);
    Task<Ai79TaskStatusResult> GetStatusAsync(Ai79TaskStatusRequest request, CancellationToken ct = default);
}

public enum Ai79TaskOperation
{
    Image,
    Video
}

public sealed record Ai79TaskSubmitRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string Model,
    string Prompt,
    IReadOnlyList<string> Images,
    IReadOnlyDictionary<string, string?> Options,
    Ai79TaskOperation Operation,
    string? FirstImageField = null,
    string? SecondImageField = null);

public sealed record Ai79TaskStatusRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string TaskId,
    Ai79TaskOperation Operation);

public sealed record Ai79TaskSubmitResult(string TaskId, string SanitizedResponseJson);

public sealed record Ai79ImageUploadRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string DataBase64,
    string ProjectId,
    string FileName,
    long SizeBytes);

public sealed record Ai79ImageUploadResult(
    string IdBase,
    string Url,
    string ProjectId,
    string FileName,
    string SanitizedResponseJson);

public sealed class Ai79TaskSubmitException : InvalidOperationException
{
    public Ai79TaskSubmitException(
        string errorMessage,
        string sanitizedResponseJson,
        HttpStatusCode? httpStatusCode = null,
        string? errorCode = null,
        Exception? innerException = null)
        : base(errorMessage, innerException)
    {
        ErrorMessage = errorMessage;
        SanitizedResponseJson = sanitizedResponseJson;
        HttpStatusCode = httpStatusCode;
        ErrorCode = errorCode;
    }

    public string SanitizedResponseJson { get; }
    public HttpStatusCode? HttpStatusCode { get; }
    public string? ErrorCode { get; }
    public string ErrorMessage { get; }
}

public sealed class Ai79TaskPollException : InvalidOperationException
{
    public Ai79TaskPollException(
        string errorMessage,
        HttpStatusCode? httpStatusCode = null,
        string? sanitizedResponseJson = null,
        Exception? innerException = null)
        : base(errorMessage, innerException)
    {
        HttpStatusCode = httpStatusCode;
        SanitizedResponseJson = sanitizedResponseJson ?? JsonSerializer.Serialize(string.Empty, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HttpStatusCode? HttpStatusCode { get; }
    public string SanitizedResponseJson { get; }
}

public sealed record Ai79TaskStatusResult(
    string NormalizedStatus,
    string SanitizedResponseJson,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage);

public static class Ai79TaskStatusNormalizer
{
    public const string Running = "RUNNING";
    public const string Success = "SUCCESS";
    public const string Failed = "FAILED";

    public static string Normalize(string? status)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return Running;
        }

        return value.ToUpperInvariant() switch
        {
            "SUCCESS" or "SUCCEEDED" or "COMPLETED" or "COMPLETE" or "DONE" or "FINISHED"
                or "MEDIA_GENERATION_STATUS_SUCCESSFUL" or "MEDIA_GENERATION_COMPLETED" => Success,
            "FAILURE" or "FAILED" or "ERROR" or "CANCELLED" or "CANCELED" or "REJECTED"
                or "MEDIA_GENERATION_STATUS_FAILED" or "MEDIA_GENERATION_FAILED" => Failed,
            _ => Running
        };
    }
}

public sealed class Ai79TaskClient : IAi79TaskClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public Ai79TaskClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default)
    {
        EnsureGenerateImageContract(request);

        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken,
            ["domain"] = request.Domain,
            ["model"] = request.Model,
            ["prompt"] = request.Prompt
        };

        for (var i = 0; i < request.Images.Count; i++)
        {
            var field = i switch
            {
                0 => request.FirstImageField ?? "image",
                1 => request.SecondImageField ?? "image_2",
                _ => $"image_{i + 1}"
            };
            form[field] = request.Images[i];
        }

        if (request.Images.Count > 2)
        {
            form["images"] = JsonSerializer.Serialize(request.Images, JsonOptions);
        }

        foreach (var pair in request.Options)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && !form.ContainsKey(pair.Key))
            {
                form[pair.Key] = pair.Value!;
            }
        }

        using var body = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(BuildUri(request.BaseUrl, request.EndpointPath), body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new Ai79TaskSubmitException(
                "79AI submit response was empty.",
                JsonSerializer.Serialize(string.Empty, JsonOptions),
                response.StatusCode,
                response.IsSuccessStatusCode ? "empty_response" : $"http_{(int)response.StatusCode}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new Ai79TaskSubmitException(
                "79AI submit response was not valid JSON.",
                JsonSerializer.Serialize(SanitizeText(json, request.AccessToken), JsonOptions),
                response.StatusCode,
                response.IsSuccessStatusCode ? "invalid_json" : $"http_{(int)response.StatusCode}",
                ex);
        }

        using (document)
        {
            var sanitized = SanitizeSecretJson(document.RootElement, request.AccessToken);
            var taskId = FindTaskId(document.RootElement, request.Operation);
            var providerError = FindSubmitError(document.RootElement, string.IsNullOrWhiteSpace(taskId), request.AccessToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorCode = providerError?.ErrorCode ?? $"http_{(int)response.StatusCode}";
                var errorMessage = providerError?.ErrorMessage
                    ?? $"79AI submit returned HTTP {(int)response.StatusCode}.";
                throw new Ai79TaskSubmitException(errorMessage, sanitized, response.StatusCode, errorCode);
            }

            if (providerError is not null)
            {
                throw new Ai79TaskSubmitException(
                    $"79AI {ResolveOperationName(request.EndpointPath)} submit failed: {providerError.ErrorMessage}",
                    sanitized,
                    response.StatusCode,
                    providerError.ErrorCode ?? "provider_error");
            }

            if (string.IsNullOrWhiteSpace(taskId))
            {
                throw new Ai79TaskSubmitException(
                    "79AI submit response missing async task identifier.",
                    sanitized,
                    response.StatusCode,
                    "missing_task_id");
            }

            return new Ai79TaskSubmitResult(taskId!, sanitized);
        }
    }

    public async Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken,
            ["domain"] = request.Domain,
            ["data"] = request.DataBase64,
            ["project_id"] = request.ProjectId,
            ["file_name"] = request.FileName,
            ["size"] = request.SizeBytes.ToString()
        };

        using var body = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(BuildUri(request.BaseUrl, request.EndpointPath), body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new Ai79TaskSubmitException(
                "79AI image upload response was empty.",
                JsonSerializer.Serialize(string.Empty, JsonOptions),
                response.StatusCode,
                response.IsSuccessStatusCode ? "empty_response" : $"http_{(int)response.StatusCode}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new Ai79TaskSubmitException(
                "79AI image upload response was not valid JSON.",
                JsonSerializer.Serialize(SanitizeText(json, request.AccessToken), JsonOptions),
                response.StatusCode,
                response.IsSuccessStatusCode ? "invalid_json" : $"http_{(int)response.StatusCode}",
                ex);
        }

        using (document)
        {
            var sanitized = SanitizeSecretJson(document.RootElement, request.AccessToken);
            var providerError = FindSubmitError(document.RootElement, taskIdMissing: false, request.AccessToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new Ai79TaskSubmitException(
                    providerError?.ErrorMessage ?? $"79AI image upload returned HTTP {(int)response.StatusCode}.",
                    sanitized,
                    response.StatusCode,
                    providerError?.ErrorCode ?? $"http_{(int)response.StatusCode}");
            }

            if (providerError is not null)
            {
                throw new Ai79TaskSubmitException(
                    $"79AI image upload failed: {providerError.ErrorMessage}",
                    sanitized,
                    response.StatusCode,
                    providerError.ErrorCode ?? "provider_error");
            }

            var idBase = FindImageInfoString(document.RootElement, "id_base");
            var url = FindImageInfoString(document.RootElement, "url");
            if (string.IsNullOrWhiteSpace(idBase) || string.IsNullOrWhiteSpace(url))
            {
                throw new Ai79TaskSubmitException(
                    "79AI image upload response missing imageInfo.id_base or imageInfo.url.",
                    sanitized,
                    response.StatusCode,
                    "missing_image_info");
            }

            return new Ai79ImageUploadResult(
                idBase!,
                url!,
                FirstNonBlank(FindImageInfoString(document.RootElement, "project_id"), request.ProjectId)!,
                FirstNonBlank(FindImageInfoString(document.RootElement, "file_name"), request.FileName)!,
                sanitized);
        }
    }

    public async Task<Ai79TaskStatusResult> GetStatusAsync(Ai79TaskStatusRequest request, CancellationToken ct = default)
    {
        var path = request.EndpointPath.Replace("{task_id}", Uri.EscapeDataString(request.TaskId), StringComparison.OrdinalIgnoreCase)
            .Replace("{taskId}", Uri.EscapeDataString(request.TaskId), StringComparison.OrdinalIgnoreCase);
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken,
            ["domain"] = request.Domain,
            [request.Operation == Ai79TaskOperation.Image ? "id_base" : "videoId"] = request.TaskId
        };

        using var body = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(BuildUri(request.BaseUrl, path), body, ct);
        var json = await ReadJsonAsync(response, request.AccessToken, ct);
        using var document = JsonDocument.Parse(json);
        JsonElement statusRoot = document.RootElement;
        JsonDocument? fallbackDocument = null;
        if (request.Operation == Ai79TaskOperation.Video
            && TryFindVideoInfoById(statusRoot, request.TaskId, out var primaryMatchedInfo))
        {
            statusRoot = primaryMatchedInfo;
        }
        else if (request.Operation == Ai79TaskOperation.Video && !HasSingleVideoInfo(statusRoot))
        {
            var fallbackPath = ResolveVideosListPath(path);
            using var fallbackBody = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["access_token"] = request.AccessToken,
                ["domain"] = request.Domain
            });
            using var fallbackResponse = await _httpClient.PostAsync(BuildUri(request.BaseUrl, fallbackPath), fallbackBody, ct);
            var fallbackJson = await ReadJsonAsync(fallbackResponse, request.AccessToken, ct);
            fallbackDocument = JsonDocument.Parse(fallbackJson);
            if (TryFindVideoInfoById(fallbackDocument.RootElement, request.TaskId, out var matchedInfo))
            {
                statusRoot = matchedInfo;
            }
            else
            {
                statusRoot = fallbackDocument.RootElement;
            }
        }

        using (fallbackDocument)
        {
            var sanitized = SanitizeSecretJson(statusRoot, request.AccessToken);
            var status = Ai79TaskStatusNormalizer.Normalize(FindStatus(statusRoot));
            var outputUrl = request.Operation == Ai79TaskOperation.Video
                ? FindVideoOutputUrl(statusRoot)
                : FindUrl(statusRoot);
            var errorCode = FindErrorValue(statusRoot, "error_code", "errorCode", "code");
            var errorMessage = FindErrorValue(statusRoot, "error_message", "errorMessage", "message", "msg");

            return new Ai79TaskStatusResult(status, sanitized, outputUrl, errorCode, errorMessage);
        }
    }

    private static Uri BuildUri(string baseUrl, string path)
        => new(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static void EnsureGenerateImageContract(Ai79TaskSubmitRequest request)
    {
        if (request.Operation != Ai79TaskOperation.Image
            || !request.EndpointPath.Contains("generateImage", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (request.Images.Count > 1)
        {
            throw new InvalidOperationException("79AI /generateImage supports one base edit image; pass additional references through subjects.");
        }

        var firstImageField = request.FirstImageField ?? "image";
        if (!firstImageField.Equals("base64Image", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("79AI /generateImage base edit image field must be base64Image.");
        }

        if (request.SecondImageField is not null)
        {
            throw new InvalidOperationException("79AI /generateImage does not support a second image field; pass additional references through subjects.");
        }
    }

    private static async Task<string> ReadJsonAsync(HttpResponseMessage response, string accessToken, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new Ai79TaskPollException(
                $"79AI task API returned HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                string.IsNullOrWhiteSpace(text)
                    ? JsonSerializer.Serialize(string.Empty, JsonOptions)
                    : JsonSerializer.Serialize(SanitizeText(text, accessToken), JsonOptions));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new Ai79TaskPollException("79AI task API returned an empty response.", response.StatusCode);
        }

        return text;
    }

    private static string? FindString(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }

                    if (value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    {
                        return value.ToString();
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindString(item, names);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string? FindTaskId(JsonElement element, Ai79TaskOperation operation)
    {
        if (operation == Ai79TaskOperation.Image)
        {
            var imageId = FindImageIdBase(element);
            if (!string.IsNullOrWhiteSpace(imageId))
            {
                return imageId;
            }
        }
        else
        {
            var videoId = FindVideoIdBase(element);
            if (!string.IsNullOrWhiteSpace(videoId))
            {
                return videoId;
            }
        }

        return FindTaskIdAlias(element);
    }

    private static string? FindVideoIdBase(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("id_base", out var directId))
        {
            var value = ScalarString(directId);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        foreach (var containerName in new[] { "videoInfo", "data" })
        {
            if (element.TryGetProperty(containerName, out var child))
            {
                var found = FindVideoIdBase(child);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string? FindImageIdBase(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("imageInfo", out var imageInfo)
            && imageInfo.ValueKind == JsonValueKind.Object
            && imageInfo.TryGetProperty("id_base", out var directId))
        {
            var value = ScalarString(directId);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (element.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("imageInfo", out var nestedImageInfo)
            && nestedImageInfo.ValueKind == JsonValueKind.Object
            && nestedImageInfo.TryGetProperty("id_base", out var nestedId))
        {
            return ScalarString(nestedId);
        }

        return null;
    }

    private static string? FindTaskIdAlias(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "task_id", "taskId", "request_id", "requestId" })
            {
                if (element.TryGetProperty(name, out var value))
                {
                    var found = ScalarString(value);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }

            foreach (var containerName in new[] { "task", "data", "result", "response" })
            {
                if (element.TryGetProperty(containerName, out var child))
                {
                    var found = FindTaskIdAlias(child);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindTaskIdAlias(item);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string? FindStatus(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "status", "state", "task_status", "taskStatus", "generation_status", "generationStatus" })
            {
                if (element.TryGetProperty(name, out var value))
                {
                    var found = ScalarString(value);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }

            foreach (var containerName in new[] { "imageInfo", "videoInfo", "task", "data", "result", "response", "body" })
            {
                if (element.TryGetProperty(containerName, out var child))
                {
                    var found = FindStatus(child);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    private static string? FindErrorValue(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (element.TryGetProperty(name, out var value))
                {
                    var found = ScalarString(value);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }

            foreach (var containerName in new[] { "error", "errors", "data", "result", "response" })
            {
                if (element.TryGetProperty(containerName, out var child))
                {
                    var found = FindErrorValue(child, names);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindErrorValue(item, names);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static SubmitError? FindSubmitError(JsonElement element, bool taskIdMissing, string accessToken)
    {
        var errorCode = FindErrorValue(element, "error_code", "errorCode", "code");
        var errorMessage = FindErrorValue(element, "error_message", "errorMessage", "message", "msg");
        var scalarError = FindErrorValue(element, "error", "errors");
        var status = FindStatus(element);
        var success = FindString(element, "success");

        var hasErrorPayload = HasNonEmptyNamedValue(element, "error", "errors");
        var successIsFalse = string.Equals(success, "false", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(success, "0", StringComparison.OrdinalIgnoreCase);
        var statusIsFailed = string.Equals(
            Ai79TaskStatusNormalizer.Normalize(status),
            Ai79TaskStatusNormalizer.Failed,
            StringComparison.Ordinal);
        var codeIsFailure = !string.IsNullOrWhiteSpace(errorCode)
                            && !string.Equals(errorCode, "0", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(errorCode, "200", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(errorCode, "ok", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(errorCode, "success", StringComparison.OrdinalIgnoreCase);
        var messageOnlyFailure = taskIdMissing
                                 && (!string.IsNullOrWhiteSpace(errorMessage) || !string.IsNullOrWhiteSpace(scalarError));

        if (!hasErrorPayload && !successIsFalse && !statusIsFailed && !codeIsFailure && !messageOnlyFailure)
        {
            return null;
        }

        var message = FirstNonBlank(errorMessage, scalarError, errorCode, status, "Provider rejected the request.")!;
        return new SubmitError(
            errorCode,
            SanitizeText(message, accessToken));
    }

    private static bool HasNonEmptyNamedValue(JsonElement element, params string[] names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)
                    && property.Value.ValueKind is not JsonValueKind.Null
                    && property.Value.ValueKind is not JsonValueKind.Undefined
                    && property.Value.GetRawText() is not "\"\"" and not "[]" and not "{}")
                {
                    return true;
                }

                if (HasNonEmptyNamedValue(property.Value, names))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(item => HasNonEmptyNamedValue(item, names));
        }

        return false;
    }

    private static string? ScalarString(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False
                ? value.ToString()
                : null;

    private static string? FindUrl(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            return IsHttpUrl(value) ? value : null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "url", "video_url", "videoUrl", "image_url", "imageUrl", "output_url", "outputUrl" })
            {
                if (element.TryGetProperty(name, out var value))
                {
                    var found = FindUrl(value);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var found = FindUrl(property.Value);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var found = FindUrl(item);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string? FindVideoOutputUrl(JsonElement root)
    {
        foreach (var container in VideoStatusContainers(root))
        {
            var found = FindFirstDirectUrl(container, "download_url", "downloadUrl", "video_url", "videoUrl", "source_url", "sourceUrl", "file_url", "fileUrl", "output_url", "outputUrl", "url");
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<JsonElement> VideoStatusContainers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var path in new[]
                 {
                     new[] { "videoInfo" },
                     new[] { "data", "videoInfo" },
                     new[] { "body", "videoInfo" },
                     new[] { "body", "data", "videoInfo" }
                 })
        {
            if (TryGetPath(root, path, out var value))
            {
                yield return value;
            }
        }

        if (LooksLikeVideoInfo(root))
        {
            yield return root;
        }
    }

    private static bool HasSingleVideoInfo(JsonElement root)
    {
        foreach (var path in new[] { new[] { "videoInfo" }, new[] { "body", "videoInfo" }, new[] { "data", "videoInfo" }, new[] { "body", "data", "videoInfo" } })
        {
            if (TryGetPath(root, path, out var info)
                && ((info.ValueKind == JsonValueKind.Object && info.EnumerateObject().Any())
                    || (info.ValueKind == JsonValueKind.Array && info.GetArrayLength() > 0)))
            {
                return true;
            }
        }

        return LooksLikeVideoInfo(root);
    }

    private static bool TryFindVideoInfoById(JsonElement root, string taskId, out JsonElement matched)
    {
        var target = taskId.Trim();
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                if (VideoObjectId(item) == target)
                {
                    matched = item;
                    return true;
                }
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "videoInfo", "data", "videos", "items", "rows", "list", "results" })
            {
                if (root.TryGetProperty(name, out var child) && TryFindVideoInfoById(child, target, out matched))
                {
                    return true;
                }
            }

            if (VideoObjectId(root) == target)
            {
                matched = root;
                return true;
            }
        }

        matched = default;
        return false;
    }

    private static string? VideoObjectId(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
            ? FirstNonBlank(
                element.TryGetProperty("id_base", out var idBase) ? ScalarString(idBase) : null,
                element.TryGetProperty("id", out var id) ? ScalarString(id) : null,
                element.TryGetProperty("videoId", out var videoId) ? ScalarString(videoId) : null,
                element.TryGetProperty("video_id", out var videoIdSnake) ? ScalarString(videoIdSnake) : null,
                element.TryGetProperty("task_id", out var taskId) ? ScalarString(taskId) : null)
            : null;

    private static bool LooksLikeVideoInfo(JsonElement element)
        => element.ValueKind == JsonValueKind.Object
           && (!string.IsNullOrWhiteSpace(VideoObjectId(element))
               || element.TryGetProperty("download_url", out _)
               || element.TryGetProperty("downloadUrl", out _)
               || element.TryGetProperty("video_url", out _)
               || element.TryGetProperty("videoUrl", out _));

    private static string ResolveVideosListPath(string videoPath)
    {
        var trimmed = videoPath.Trim();
        return trimmed.EndsWith("/video", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^"/video".Length] + "/videos"
            : "/videos";
    }

    private static string? FindImageInfoString(JsonElement element, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var path in new[]
                 {
                     new[] { "imageInfo" },
                     new[] { "body", "imageInfo" },
                     new[] { "data", "imageInfo" },
                     new[] { "body", "data", "imageInfo" }
                 })
        {
            if (TryGetPath(element, path, out var imageInfo)
                && imageInfo.ValueKind == JsonValueKind.Object
                && imageInfo.TryGetProperty(fieldName, out var value))
            {
                var found = ScalarString(value);
                if (!string.IsNullOrWhiteSpace(found))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static bool TryGetPath(JsonElement element, IReadOnlyList<string> path, out JsonElement value)
    {
        value = element;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindFirstDirectUrl(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                var text = ScalarString(value);
                if (IsHttpUrl(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string SanitizeSecretJson(JsonElement root, string accessToken)
        => JsonSerializer.Serialize(Sanitize(root, accessToken), JsonOptions);

    private static object? Sanitize(JsonElement element, string accessToken)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                x => x.Name,
                x => IsSecretName(x.Name) ? "***" : Sanitize(x.Value, accessToken),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(x => Sanitize(x, accessToken)).ToArray(),
            JsonValueKind.String => string.Equals(element.GetString(), accessToken, StringComparison.Ordinal) ? "***" : element.GetString(),
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };

    private static bool IsSecretName(string name)
        => name.Contains("token", StringComparison.OrdinalIgnoreCase)
           || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
           || name.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static string ResolveOperationName(string endpointPath)
        => endpointPath.Contains("generateImage", StringComparison.OrdinalIgnoreCase) ? "image"
            : endpointPath.Contains("video", StringComparison.OrdinalIgnoreCase) ? "video"
            : "task";

    private static string SanitizeText(string value, string accessToken)
        => string.IsNullOrEmpty(accessToken)
            ? value
            : value.Replace(accessToken, "***", StringComparison.Ordinal);

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record SubmitError(string? ErrorCode, string ErrorMessage);
}
