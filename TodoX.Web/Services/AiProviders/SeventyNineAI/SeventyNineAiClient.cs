using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoX.Web.Services.AiProviders.SeventyNineAI;

public interface ISeventyNineAiClient
{
    Task<SeventyNineAiModelsResponse> GetModelsAsync(string type, SeventyNineAiExecutionContext context, CancellationToken ct = default);
    Task<SeventyNineAiAccountInfoResponse> GetAccountInfoAsync(SeventyNineAiExecutionContext context, CancellationToken ct = default);
    Task<SeventyNineAiImageSubmitResponse> CreateImageAsync(SeventyNineAiCreateImageRequest request, SeventyNineAiExecutionContext context, CancellationToken ct = default);
    Task<SeventyNineAiImageStatusResponse> GetImageStatusAsync(string idBase, SeventyNineAiExecutionContext context, CancellationToken ct = default);
    Task<SeventyNineAiUploadImageResponse> UploadImageAsync(SeventyNineAiUploadImageRequest request, SeventyNineAiExecutionContext context, CancellationToken ct = default);
    Task<SeventyNineAiVideoSubmitResponse> CreateVideoAsync(SeventyNineAiCreateVideoRequest request, SeventyNineAiExecutionContext context, CancellationToken ct = default);
    Task<SeventyNineAiVideoStatusResponse> GetVideoStatusAsync(string videoId, SeventyNineAiExecutionContext context, CancellationToken ct = default);
}

public sealed class SeventyNineAiClient : ISeventyNineAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly ILogger<SeventyNineAiClient> _logger;

    public SeventyNineAiClient(HttpClient http, ILogger<SeventyNineAiClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<SeventyNineAiModelsResponse> GetModelsAsync(string type, SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiModelsResponse>("/ai/models", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain,
            ["type"] = type
        }, context, ct, response =>
        {
            response.Models = ReadArrayRoot<SeventyNineAiModelInfo>(response.RawJson);
            return response;
        });

    public Task<SeventyNineAiAccountInfoResponse> GetAccountInfoAsync(SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiAccountInfoResponse>("/api/apps/go-mmo/ai/me", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain
        }, context, ct);

    public Task<SeventyNineAiImageSubmitResponse> CreateImageAsync(SeventyNineAiCreateImageRequest request, SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiImageSubmitResponse>("/ai/generateImage", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain,
            ["action_type"] = "create",
            ["model"] = request.Model,
            ["prompt"] = request.Prompt,
            ["editImage"] = request.EditImage ? "true" : "false",
            ["base64Image"] = request.Base64Image,
            ["project_id"] = context.ProjectId,
            ["subjects"] = request.Subjects is null ? null : JsonSerializer.Serialize(request.Subjects, JsonOptions),
            ["ratio"] = request.Ratio
        }, context, ct, response =>
        {
            response.ImageInfo = ReadObject<SeventyNineAiImageInfo>(response.RawJson, "imageInfo");
            return response;
        });

    public Task<SeventyNineAiImageStatusResponse> GetImageStatusAsync(string idBase, SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiImageStatusResponse>("/ai/image", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain,
            ["id_base"] = idBase
        }, context, ct, response =>
        {
            response.ImageInfo = ReadObject<SeventyNineAiImageInfo>(response.RawJson, "imageInfo");
            return response;
        });

    public Task<SeventyNineAiUploadImageResponse> UploadImageAsync(SeventyNineAiUploadImageRequest request, SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiUploadImageResponse>("/ai/image-upload", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain,
            ["data"] = request.Base64Data,
            ["project_id"] = context.ProjectId,
            ["file_name"] = request.FileName,
            ["size"] = request.Size.ToString()
        }, context, ct, response =>
        {
            response.ImageInfo = ReadObject<SeventyNineAiImageInfo>(response.RawJson, "imageInfo");
            return response;
        });

    public Task<SeventyNineAiVideoSubmitResponse> CreateVideoAsync(SeventyNineAiCreateVideoRequest request, SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiVideoSubmitResponse>("/ai/create-video", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain,
            ["model"] = request.Model,
            ["privacy"] = "PRIVATE",
            ["prompt"] = request.Prompt,
            ["translate_to_en"] = "true",
            ["project_id"] = context.ProjectId,
            ["ratio"] = request.Ratio,
            ["resolution"] = request.Resolution,
            ["duration"] = request.Duration.ToString(),
            ["mode"] = request.Mode,
            ["images"] = JsonSerializer.Serialize(request.Images, JsonOptions)
        }, context, ct, response =>
        {
            response.VideoInfo = ReadObject<SeventyNineAiVideoInfo>(response.RawJson, "videoInfo");
            return response;
        });

    public Task<SeventyNineAiVideoStatusResponse> GetVideoStatusAsync(string videoId, SeventyNineAiExecutionContext context, CancellationToken ct = default)
        => SendAsync<SeventyNineAiVideoStatusResponse>("/ai/video", new Dictionary<string, string?>
        {
            ["access_token"] = context.AccessToken,
            ["domain"] = context.Domain,
            ["videoId"] = videoId
        }, context, ct, response =>
        {
            response.VideoInfo = ReadObject<SeventyNineAiVideoInfo>(response.RawJson, "videoInfo");
            return response;
        });

    private async Task<TResponse> SendAsync<TResponse>(
        string path,
        IReadOnlyDictionary<string, string?> formFields,
        SeventyNineAiExecutionContext context,
        CancellationToken ct,
        Func<TResponse, TResponse>? postProcess = null)
        where TResponse : new()
    {
        if (string.IsNullOrWhiteSpace(context.AccessToken))
        {
            throw new InvalidOperationException("79AI access token is required.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(context.BaseUrl, path));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new FormUrlEncodedContent(formFields.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => new KeyValuePair<string, string>(x.Key, x.Value!)));

        using var response = await _http.SendAsync(message, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var parsed = Deserialize<TResponse>(raw);
        ApplyRawJson(parsed, raw);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderVideoRenderException(
                $"79AI HTTP {(int)response.StatusCode}.",
                SeventyNineAiConstants.ProviderCode,
                transient: response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
                statusCode: (int)response.StatusCode);
        }

        var result = postProcess is null ? parsed : postProcess(parsed);
        return result;
    }

    private static Uri BuildUrl(string baseUrl, string path)
    {
        var normalizedBase = baseUrl.TrimEnd('/');
        var normalizedPath = path.StartsWith('/') ? path : "/" + path;
        return new Uri(normalizedBase + normalizedPath, UriKind.Absolute);
    }

    private static T Deserialize<T>(string raw)
        => JsonSerializer.Deserialize<T>(raw, JsonOptions)
           ?? throw new ProviderVideoRenderException("79AI response body is empty.", SeventyNineAiConstants.ProviderCode);

    private static void ApplyRawJson<T>(T response, string raw)
    {
        if (response is SeventyNineAiModelsResponse models)
        {
            models.RawJson = raw;
        }
        else if (response is SeventyNineAiAccountInfoResponse account)
        {
            account.RawJson = raw;
        }
        else if (response is SeventyNineAiImageSubmitResponse image)
        {
            image.RawJson = raw;
        }
        else if (response is SeventyNineAiVideoSubmitResponse video)
        {
            video.RawJson = raw;
        }
    }

    private static List<TItem> ReadArrayRoot<TItem>(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.Deserialize<List<TItem>>(JsonOptions) ?? new List<TItem>()
                : new List<TItem>();
        }
        catch
        {
            return new List<TItem>();
        }
    }

    private static TItem? ReadObject<TItem>(string raw, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var element))
            {
                return element.Deserialize<TItem>(JsonOptions);
            }
        }
        catch
        {
        }

        return default;
    }
}
