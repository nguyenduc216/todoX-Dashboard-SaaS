using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TodoX.Web.Services.AiProviders;

public interface IAi79TaskClient
{
    Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default);
    Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default);
    Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default);
    Task<Ai79ProviderMediaListResult> ListImagesAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default);
    Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default);
    Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default);
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
    Ai79TaskOperation Operation,
    string? TaskIdField = null,
    bool UseBearerAuth = false,
    string? ProjectId = null);

public sealed record Ai79TaskSubmitResult(string TaskId, string SanitizedResponseJson);

public sealed record Ai79MultipartFilePart(
    string FieldName,
    string FileName,
    string MimeType,
    long SizeBytes,
    Func<CancellationToken, Task<Stream?>> OpenReadAsync);

public sealed record Ai79MultipartTaskSubmitRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string Model,
    string Prompt,
    IReadOnlyDictionary<string, string?> Fields,
    IReadOnlyList<Ai79MultipartFilePart> Files,
    Ai79TaskOperation Operation);

public sealed record Ai79MediaUploadRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string ProjectId,
    string FieldName,
    Ai79MultipartFilePart File);

public sealed record Ai79MediaUploadResult(
    string Url,
    string? IdBase,
    string? ProjectId,
    string? FileName,
    string SanitizedResponseJson);

public sealed record Ai79ProviderMediaListRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string ProjectId,
    IReadOnlyDictionary<string, string?>? OptionalFields = null);

public sealed record Ai79ProviderMediaItem(
    string? IdBase,
    string? Url,
    string? Status,
    string? DownloadUrl,
    string? ThumbnailUrl);

public sealed record Ai79ProviderMediaListResult(
    IReadOnlyList<Ai79ProviderMediaItem> Items,
    string SanitizedResponseJson);

public sealed record Ai79MotionControlSubmitRequest(
    string BaseUrl,
    string EndpointPath,
    string AccessToken,
    string Domain,
    string ProjectId,
    string Model,
    string Prompt,
    string ImageUrl,
    string VideoUrl,
    string Mode,
    string Ratio,
    string SubType,
    string BackgroundSource,
    bool IncludeImagesZeroUrl = true);

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
    private static readonly TimeSpan DefaultMotionControlSubmitTimeout = TimeSpan.FromSeconds(120);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _motionControlSubmitTimeout;

    public Ai79TaskClient(HttpClient httpClient, TimeSpan? motionControlSubmitTimeout = null)
    {
        _httpClient = httpClient;
        _motionControlSubmitTimeout = motionControlSubmitTimeout ?? DefaultMotionControlSubmitTimeout;
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
        return await ReadSubmitResultAsync(response, request.AccessToken, request.EndpointPath, request.Operation, ct);
    }

    public async Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default)
    {
        using var body = new MultipartFormDataContent();
        var form = new Dictionary<string, string?>
        {
            ["access_token"] = request.AccessToken,
            ["domain"] = request.Domain,
            ["model"] = request.Model,
            ["prompt"] = request.Prompt
        };

        foreach (var pair in request.Fields)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value) && !form.ContainsKey(pair.Key))
            {
                form[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in form)
        {
            body.Add(new StringContent(pair.Value ?? string.Empty), pair.Key);
        }

        foreach (var file in request.Files)
        {
            var stream = await file.OpenReadAsync(ct)
                ?? throw new Ai79TaskSubmitException(
                    $"79AI multipart file '{file.FieldName}' could not be opened.",
                    JsonSerializer.Serialize(new { error = "missing_file", field = file.FieldName }, JsonOptions),
                    errorCode: "missing_file");
            var content = new StreamContent(stream);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.MimeType);
            body.Add(content, file.FieldName, file.FileName);
        }

        using var response = await _httpClient.PostAsync(BuildUri(request.BaseUrl, request.EndpointPath), body, ct);
        return await ReadSubmitResultAsync(response, request.AccessToken, request.EndpointPath, request.Operation, ct);
    }

    public async Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default)
    {
        using var body = new MultipartFormDataContent();
        body.Add(CreateMultipartTextPart("domain", request.Domain));
        body.Add(CreateMultipartTextPart("project_id", request.ProjectId));

        var file = request.File;
        var stream = await file.OpenReadAsync(ct)
            ?? throw new Ai79TaskSubmitException(
                $"79AI upload file '{request.FieldName}' could not be opened.",
                JsonSerializer.Serialize(new { error = "missing_file", field = request.FieldName }, JsonOptions),
                errorCode: "missing_file");
        if (stream.CanSeek)
        {
            stream.Position = 0;
            if (stream.Length == 0)
            {
                throw new Ai79TaskSubmitException(
                    $"79AI upload file '{request.FieldName}' was empty.",
                    JsonSerializer.Serialize(new { error = "empty_file", field = request.FieldName }, JsonOptions),
                    errorCode: "empty_file");
            }
        }
        else if (file.SizeBytes == 0)
        {
            throw new Ai79TaskSubmitException(
                $"79AI upload file '{request.FieldName}' was empty.",
                JsonSerializer.Serialize(new { error = "empty_file", field = request.FieldName }, JsonOptions),
                errorCode: "empty_file");
        }

        var content = new StreamContent(stream);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.MimeType);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = QuoteMultipartValue(request.FieldName),
            FileName = QuoteMultipartValue(file.FileName)
        };
        body.Add(content);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(request.BaseUrl, request.EndpointPath));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(request.AccessToken));
        httpRequest.Content = body;

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new Ai79TaskSubmitException(
                $"79AI media upload returned HTTP {(int)response.StatusCode}.",
                string.IsNullOrWhiteSpace(json)
                    ? JsonSerializer.Serialize(string.Empty, JsonOptions)
                    : JsonSerializer.Serialize(SanitizeText(json, request.AccessToken), JsonOptions),
                response.StatusCode,
                $"http_{(int)response.StatusCode}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new Ai79TaskSubmitException(
                "79AI media upload response was empty.",
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
                "79AI media upload response was not valid JSON.",
                JsonSerializer.Serialize(SanitizeText(json, request.AccessToken), JsonOptions),
                response.StatusCode,
                response.IsSuccessStatusCode ? "invalid_json" : $"http_{(int)response.StatusCode}",
                ex);
        }

        using (document)
        {
            var sanitized = SanitizeSecretJson(document.RootElement, request.AccessToken);
            var providerError = FindSubmitError(document.RootElement, taskIdMissing: false, request.AccessToken);
            if (providerError is not null)
            {
                throw new Ai79TaskSubmitException(
                    $"79AI media upload failed: {providerError.ErrorMessage}",
                    sanitized,
                    response.StatusCode,
                    providerError.ErrorCode ?? "provider_error");
            }

            var url = FindUploadAssetUrl(document.RootElement);
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new Ai79TaskSubmitException(
                    "79AI media upload response missing asset URL.",
                    sanitized,
                    response.StatusCode,
                    "missing_asset_url");
            }

            return new Ai79MediaUploadResult(
                url!,
                FirstNonBlank(FindImageInfoString(document.RootElement, "id_base"), FindString(document.RootElement, "id_base", "idBase")),
                FirstNonBlank(FindImageInfoString(document.RootElement, "project_id"), FindString(document.RootElement, "project_id", "projectId"), request.ProjectId),
                FirstNonBlank(FindImageInfoString(document.RootElement, "file_name"), FindString(document.RootElement, "file_name", "fileName"), file.FileName),
                sanitized);
        }
    }

    public Task<Ai79ProviderMediaListResult> ListImagesAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
        => ListProviderMediaAsync(request, Ai79TaskOperation.Image, ct);

    public Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
        => ListProviderMediaAsync(request, Ai79TaskOperation.Video, ct);

    private async Task<Ai79ProviderMediaListResult> ListProviderMediaAsync(
        Ai79ProviderMediaListRequest request,
        Ai79TaskOperation operation,
        CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["access_token"] = request.AccessToken,
            ["domain"] = request.Domain,
            ["project_id"] = request.ProjectId
        };
        if (request.OptionalFields is not null)
        {
            foreach (var pair in request.OptionalFields)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    form[pair.Key] = pair.Value!;
                }
            }
        }

        using var body = new FormUrlEncodedContent(form);
        var endpointPath = NormalizeProviderMediaListPath(request.BaseUrl, request.EndpointPath);
        using var response = await _httpClient.PostAsync(BuildUri(request.BaseUrl, endpointPath), body, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        var sanitized = string.IsNullOrWhiteSpace(json)
            ? JsonSerializer.Serialize(string.Empty, JsonOptions)
            : JsonSerializer.Serialize(SanitizeText(json, request.AccessToken), JsonOptions);

        if (!response.IsSuccessStatusCode)
        {
            throw new Ai79TaskSubmitException(
                $"79AI {(operation == Ai79TaskOperation.Image ? "images" : "videos")} list returned HTTP {(int)response.StatusCode}.",
                sanitized,
                response.StatusCode,
                $"http_{(int)response.StatusCode}");
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new Ai79TaskSubmitException(
                $"79AI {(operation == Ai79TaskOperation.Image ? "images" : "videos")} list response was empty.",
                sanitized,
                response.StatusCode,
                "empty_response");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var providerError = FindSubmitError(document.RootElement, taskIdMissing: false, request.AccessToken);
            if (providerError is not null)
            {
                throw new Ai79TaskSubmitException(
                    $"79AI {(operation == Ai79TaskOperation.Image ? "images" : "videos")} list failed: {providerError.ErrorMessage}",
                    sanitized,
                    response.StatusCode,
                    providerError.ErrorCode ?? "provider_error");
            }

            var items = FindProviderMediaItems(document.RootElement, operation);
            return new Ai79ProviderMediaListResult(items, sanitized);
        }
        catch (JsonException ex)
        {
            throw new Ai79TaskSubmitException(
                $"79AI {(operation == Ai79TaskOperation.Image ? "images" : "videos")} list response was not valid JSON.",
                sanitized,
                response.StatusCode,
                "invalid_json",
                ex);
        }
    }

    public async Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["domain"] = request.Domain,
            ["project_id"] = request.ProjectId,
            ["prompt"] = request.Prompt,
            ["image_url"] = request.ImageUrl,
            ["video_url"] = request.VideoUrl,
            ["subType"] = request.SubType,
            ["background_source"] = request.BackgroundSource,
            ["mode"] = request.Mode,
            ["ratio"] = request.Ratio
        };

        if (request.IncludeImagesZeroUrl)
        {
            form["images[0][url]"] = request.ImageUrl;
        }

        using var body = new FormUrlEncodedContent(form);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(request.BaseUrl, request.EndpointPath));
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(request.AccessToken));
        httpRequest.Content = body;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_motionControlSubmitTimeout);
        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        return await ReadSubmitResultAsync(response, request.AccessToken, request.EndpointPath, Ai79TaskOperation.Video, timeoutCts.Token);
    }

    private static async Task<Ai79TaskSubmitResult> ReadSubmitResultAsync(
        HttpResponseMessage response,
        string accessToken,
        string endpointPath,
        Ai79TaskOperation operation,
        CancellationToken ct)
    {
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
            if (!response.IsSuccessStatusCode)
            {
                throw new Ai79TaskSubmitException(
                    $"79AI submit returned HTTP {(int)response.StatusCode}.",
                    JsonSerializer.Serialize(SanitizeText(json, accessToken), JsonOptions),
                    response.StatusCode,
                    $"http_{(int)response.StatusCode}",
                    ex);
            }

            throw new Ai79TaskSubmitException(
                "79AI submit response was not valid JSON.",
                JsonSerializer.Serialize(SanitizeText(json, accessToken), JsonOptions),
                response.StatusCode,
                "invalid_json",
                ex);
        }

        using (document)
        {
            var sanitized = SanitizeSecretJson(document.RootElement, accessToken);
            var taskId = FindTaskId(document.RootElement, operation);
            var providerError = FindSubmitError(document.RootElement, string.IsNullOrWhiteSpace(taskId), accessToken);

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
                    $"79AI {ResolveOperationName(endpointPath)} submit failed: {providerError.ErrorMessage}",
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
        var form = request.UseBearerAuth
            ? new Dictionary<string, string>
            {
                ["domain"] = request.Domain,
                ["project_id"] = request.ProjectId ?? "default"
            }
            : new Dictionary<string, string>
            {
                ["access_token"] = request.AccessToken,
                ["domain"] = request.Domain,
                [request.TaskIdField ?? (request.Operation == Ai79TaskOperation.Image ? "id_base" : "videoId")] = request.TaskId
            };

        using var body = new FormUrlEncodedContent(form);
        HttpResponseMessage response;
        if (request.UseBearerAuth)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(request.BaseUrl, path));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(request.AccessToken));
            httpRequest.Content = body;
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        else
        {
            response = await _httpClient.PostAsync(BuildUri(request.BaseUrl, path), body, ct);
        }

        using (response)
        {
            var json = await ReadJsonAsync(response, request.AccessToken, ct);
            using var document = JsonDocument.Parse(json);
            JsonElement statusRoot = document.RootElement;
            JsonDocument? fallbackDocument = null;
            if (request.Operation == Ai79TaskOperation.Video
                && TryFindVideoInfoById(statusRoot, request.TaskId, out var primaryMatchedInfo))
            {
                statusRoot = primaryMatchedInfo;
            }
            else if (!request.UseBearerAuth && request.Operation == Ai79TaskOperation.Video && !HasSingleVideoInfo(statusRoot))
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
    }

    private static Uri BuildUri(string baseUrl, string path)
        => new(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));

    private static string NormalizeProviderMediaListPath(string baseUrl, string path)
    {
        var normalizedBasePath = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute).AbsolutePath.TrimEnd('/');
        var normalizedPath = "/" + path.Trim('/');
        if (string.Equals(normalizedBasePath, "/ai", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(normalizedPath, "/ai", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("/ai/", StringComparison.OrdinalIgnoreCase)))
        {
            return normalizedPath["/ai".Length..];
        }

        return normalizedPath;
    }

    private static StringContent CreateMultipartTextPart(string name, string value)
    {
        var content = new StringContent(value);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = QuoteMultipartValue(name)
        };
        content.Headers.ContentType = null;
        return content;
    }

    private static string QuoteMultipartValue(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static IReadOnlyList<Ai79ProviderMediaItem> FindProviderMediaItems(JsonElement root, Ai79TaskOperation operation)
    {
        var items = new List<Ai79ProviderMediaItem>();
        foreach (var element in EnumerateProviderMediaElements(root, operation))
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            items.Add(new Ai79ProviderMediaItem(
                FindString(element, "id_base", "idBase"),
                FindString(element, "url"),
                FindString(element, "status", "state", "provider_status"),
                FindString(element, "download_url", "downloadUrl"),
                FindString(element, "thumbnail_url", "thumbnailUrl")));
        }

        return items;
    }

    private static IEnumerable<JsonElement> EnumerateProviderMediaElements(JsonElement root, Ai79TaskOperation operation)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        foreach (var propertyName in operation == Ai79TaskOperation.Image
                     ? new[] { "data", "images", "items" }
                     : new[] { "data", "videos", "items" })
        {
            if (!root.TryGetProperty(propertyName, out var child))
            {
                continue;
            }

            if (child.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in child.EnumerateArray())
                {
                    yield return item;
                }

                yield break;
            }

            foreach (var item in EnumerateProviderMediaElements(child, operation))
            {
                yield return item;
            }

            yield break;
        }
    }

    private static string NormalizeBearerToken(string accessToken)
    {
        var value = (accessToken ?? string.Empty).Trim();
        while (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            value = value["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("79AI bearer token is empty.");
        }

        return value;
    }

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
        if (request.Images.Count > 0 && !firstImageField.Equals("base64Image", StringComparison.Ordinal))
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

        return operation == Ai79TaskOperation.Video
            ? FindTaskIdAlias(element)
            : null;
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

            foreach (var containerName in new[] { "imageInfo", "videoInfo", "task", "data", "raw", "result", "response", "body" })
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
            foreach (var name in new[] { "url", "download_url", "downloadUrl", "result_url", "resultUrl", "video_url", "videoUrl", "image_url", "imageUrl", "output_url", "outputUrl" })
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

    private static string? FindUploadAssetUrl(JsonElement element)
    {
        foreach (var path in new[]
                 {
                     new[] { "url" },
                     new[] { "download_url" },
                     new[] { "downloadUrl" },
                     new[] { "assetUrl" },
                     new[] { "asset_url" },
                     new[] { "image_url" },
                     new[] { "video_url" },
                     new[] { "imageInfo", "url" },
                     new[] { "videoInfo", "url" },
                     new[] { "videoInfo", "download_url" },
                     new[] { "videoInfo", "downloadUrl" },
                     new[] { "fileInfo", "url" },
                     new[] { "data", "url" },
                     new[] { "data", "download_url" },
                     new[] { "data", "downloadUrl" },
                     new[] { "data", "assetUrl" },
                     new[] { "data", "asset_url" },
                     new[] { "data", "image_url" },
                     new[] { "data", "video_url" },
                     new[] { "data", "imageInfo", "url" },
                     new[] { "data", "videoInfo", "url" },
                     new[] { "data", "videoInfo", "download_url" },
                     new[] { "data", "videoInfo", "downloadUrl" },
                     new[] { "data", "fileInfo", "url" },
                     new[] { "body", "url" },
                     new[] { "body", "download_url" },
                     new[] { "body", "downloadUrl" },
                     new[] { "body", "data", "url" },
                     new[] { "body", "data", "download_url" },
                     new[] { "body", "data", "downloadUrl" },
                     new[] { "body", "data", "assetUrl" },
                     new[] { "body", "data", "asset_url" },
                     new[] { "body", "data", "imageInfo", "url" },
                     new[] { "body", "data", "videoInfo", "url" },
                     new[] { "body", "data", "videoInfo", "download_url" },
                     new[] { "body", "data", "videoInfo", "downloadUrl" },
                     new[] { "body", "data", "fileInfo", "url" }
                 })
        {
            if (TryGetPath(element, path, out var value))
            {
                var found = FindUrl(value);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return FindUrl(element);
    }

    private static string? FindVideoOutputUrl(JsonElement root)
    {
        foreach (var path in new[]
                 {
                     new[] { "data" },
                     new[] { "data", "videoInfo" },
                     new[] { "raw", "videoInfo" },
                     new[] { "videoInfo" },
                     Array.Empty<string>()
                 })
        {
            if (TryGetPath(root, path, out var container))
            {
                var aliases = path.Length == 0
                    ? new[] { "result_url", "resultUrl", "download_url", "downloadUrl", "video_url", "videoUrl", "source_url", "sourceUrl", "file_url", "fileUrl", "output_url", "outputUrl", "url" }
                    : new[] { "result_url", "resultUrl", "download_url", "downloadUrl", "video_url", "videoUrl", "url" };
                var found = FindFirstDirectUrl(container, aliases);
                if (found is not null)
                {
                    return found;
                }
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
        => JsonSerializer.Serialize(Sanitize(root, GetSensitiveTokenValues(accessToken)), JsonOptions);

    private static object? Sanitize(JsonElement element, IReadOnlySet<string> sensitiveTokens)
        => element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                x => x.Name,
                x => IsSecretName(x.Name) ? "***" : Sanitize(x.Value, sensitiveTokens),
                StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(x => Sanitize(x, sensitiveTokens)).ToArray(),
            JsonValueKind.String => sensitiveTokens.Contains(element.GetString() ?? string.Empty) ? "***" : element.GetString(),
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
    {
        foreach (var token in GetSensitiveTokenValues(accessToken))
        {
            value = value.Replace(token, "***", StringComparison.Ordinal);
        }

        return value;
    }

    private static IReadOnlySet<string> GetSensitiveTokenValues(string accessToken)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return tokens;
        }

        tokens.Add(accessToken);
        try
        {
            tokens.Add(NormalizeBearerToken(accessToken));
        }
        catch (InvalidOperationException)
        {
            // The raw configured value is still redacted even when it cannot form a bearer token.
        }

        return tokens;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record SubmitError(string? ErrorCode, string ErrorMessage);
}
