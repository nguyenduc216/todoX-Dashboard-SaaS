using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TodoX.Web.Services.AiCharacters;

public sealed class OpenRouterImageRequest
{
    public Guid? UserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string Model { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string AspectRatio { get; set; } = "1:1";
    public string OutputFormat { get; set; } = "png";
    public string Quality { get; set; } = "high";
    public string Resolution { get; set; } = "1K";
    public int Count { get; set; } = 1;
    public long? Seed { get; set; }
    public string FileCategory { get; set; } = "ai_character";
    public string[] ReferenceImageUrls { get; set; } = Array.Empty<string>();

    /// <summary>Overrides OpenRouter:BaseUrl when the provider row supplies its own base_url.</summary>
    public string? BaseUrlOverride { get; set; }

    /// <summary>Endpoint appended to the base URL (from capability.endpoint_path), e.g. "/images".</summary>
    public string? EndpointPath { get; set; }

    /// <summary>Config key name for the API key (from provider.api_key_config_name), e.g. "OpenRouter__ApiKey".</summary>
    public string? ApiKeyConfigName { get; set; }
}

public sealed class OpenRouterImageResponse
{
    public bool Success { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageUrl { get; set; }
    public string? ObjectKey { get; set; }
    public string? MimeType { get; set; }
    public string? ProviderCode { get; set; }
    public string? ModelName { get; set; }
    public string? RawRequestJson { get; set; }
    public string? RawResponseJson { get; set; }
    public decimal? UsageCost { get; set; }
    public string? UsageJson { get; set; }
    public HttpStatusCode? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IAiImageProviderService
{
    Task<OpenRouterImageResponse> GenerateImageAsync(OpenRouterImageRequest request, CancellationToken cancellationToken = default);
}

public interface IOpenRouterImageService : IAiImageProviderService
{
}

public sealed class OpenRouterImageService : IOpenRouterImageService
{
    private static readonly HashSet<HttpStatusCode> RetryableStatusCodes = new()
    {
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
        (HttpStatusCode)524,
        (HttpStatusCode)529
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenRouterImageService> _logger;

    public OpenRouterImageService(HttpClient http, IConfiguration config, ILogger<OpenRouterImageService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<OpenRouterImageResponse> GenerateImageAsync(OpenRouterImageRequest request, CancellationToken cancellationToken = default)
    {
        if (!_config.GetValue("OpenRouter:Enabled", true))
        {
            return Fail("OpenRouter image provider is disabled.");
        }

        var apiKey = ResolveApiKey(request.ApiKeyConfigName);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Fail("Chưa cấu hình OpenRouter API key. Kiểm tra biến môi trường OpenRouter__ApiKey hoặc api_key_config_name của provider.");
        }

        var baseUrl = (FirstNonBlank(request.BaseUrlOverride, _config["OpenRouter:BaseUrl"]) ?? "https://openrouter.ai/api/v1").TrimEnd('/');
        var endpointPath = NormalizeEndpoint(request.EndpointPath);
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return Fail("Chua cau hinh model cho OpenRouter (provider capability model_name dang trong).");
        }
        var timeoutSeconds = Math.Clamp(_config.GetValue("OpenRouter:Image:TimeoutSeconds", 180), 15, 600);
        var maxRetry = Math.Clamp(_config.GetValue("OpenRouter:Image:MaxRetry", 2), 0, 5);
        _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        var payload = BuildPayload(request, includeReferences: request.ReferenceImageUrls.Length > 0);
        var requestJson = JsonSerializer.Serialize(payload, JsonOptions());
        var lastResponse = new OpenRouterImageResponse { RawRequestJson = requestJson };

        for (var attempt = 0; attempt <= maxRetry; attempt++)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}{endpointPath}");
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                AddOptionalHeader(message, "HTTP-Referer", _config["OpenRouter:HttpReferer"]);
                AddOptionalHeader(message, "X-Title", _config["OpenRouter:AppTitle"] ?? "TodoX");
                message.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(message, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                lastResponse = ParseResponse(response.StatusCode, body, requestJson);
                lastResponse.ProviderCode = "openrouter_image";
                lastResponse.ModelName = request.Model;

                if (lastResponse.Success || !RetryableStatusCodes.Contains(response.StatusCode))
                {
                    if (!lastResponse.Success && request.ReferenceImageUrls.Length > 0
                        && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableContent))
                    {
                        _logger.LogWarning("OPENROUTER_IMAGE_REFERENCE_REJECTED status={StatusCode} body={Body}", (int)response.StatusCode, Truncate(body));
                    }

                    return lastResponse;
                }

                _logger.LogWarning("OPENROUTER_IMAGE_RETRY status={StatusCode} attempt={Attempt}/{MaxRetry}", (int)response.StatusCode, attempt + 1, maxRetry);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastResponse = Fail($"OpenRouter timeout sau {timeoutSeconds} giay.", requestJson, ex.Message);
            }
            catch (Exception ex)
            {
                lastResponse = Fail("Khong goi duoc OpenRouter Image API.", requestJson, ex.Message);
            }

            if (attempt < maxRetry)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        return lastResponse;
    }

    private static object BuildPayload(OpenRouterImageRequest request, bool includeReferences)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["prompt"] = request.Prompt,
            ["aspect_ratio"] = request.AspectRatio,
            ["output_format"] = request.OutputFormat,
            ["quality"] = request.Quality,
            // Floor to >= 2K so the generated image meets the model's minimum pixel count
            // (e.g. seedream requires >= 3,686,400 px = 1920x1920). Never send "1K".
            ["resolution"] = NormalizeResolution(request.Resolution),
            ["n"] = Math.Clamp(request.Count, 1, 4)
        };

        if (request.Seed is not null)
        {
            payload["seed"] = request.Seed;
        }

        if (includeReferences)
        {
            var references = request.ReferenceImageUrls
                .Where(IsRenderableReferenceUrl)
                .Select(url => new
                {
                    type = "image_url",
                    image_url = new { url }
                })
                .ToArray();

            payload["input_references"] = references;
        }

        return payload;
    }

    private static OpenRouterImageResponse ParseResponse(HttpStatusCode statusCode, string body, string requestJson)
    {
        using var doc = TryParse(body);
        if (!IsSuccess(statusCode))
        {
            return new OpenRouterImageResponse
            {
                Success = false,
                StatusCode = statusCode,
                RawRequestJson = requestJson,
                RawResponseJson = body,
                ErrorMessage = ExtractError(doc) ?? $"OpenRouter returned HTTP {(int)statusCode}."
            };
        }

        var root = doc?.RootElement;
        var data = root is JsonElement r && r.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().FirstOrDefault()
            : default;

        string? b64 = null;
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("b64_json", out var b64Prop))
            {
                b64 = b64Prop.GetString();
            }
            else if (data.TryGetProperty("image", out var imageProp))
            {
                b64 = imageProp.GetString();
            }
        }

        if (string.IsNullOrWhiteSpace(b64))
        {
            return new OpenRouterImageResponse
            {
                Success = false,
                StatusCode = statusCode,
                RawRequestJson = requestJson,
                RawResponseJson = body,
                ErrorMessage = "OpenRouter khong tra ve b64_json trong data[0]."
            };
        }

        var comma = b64.IndexOf(',', StringComparison.Ordinal);
        if (b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
        {
            b64 = b64[(comma + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(b64);
        }
        catch
        {
            return new OpenRouterImageResponse
            {
                Success = false,
                StatusCode = statusCode,
                RawRequestJson = requestJson,
                RawResponseJson = body,
                ErrorMessage = "OpenRouter tra ve anh base64 khong hop le."
            };
        }

        var usage = root is JsonElement rr && rr.TryGetProperty("usage", out var usageProp) ? usageProp : default;
        return new OpenRouterImageResponse
        {
            Success = true,
            StatusCode = statusCode,
            ImageBytes = bytes,
            MimeType = "image/png",
            RawRequestJson = requestJson,
            RawResponseJson = body,
            UsageCost = TryGetCost(usage),
            UsageJson = usage.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? usage.GetRawText() : null
        };
    }

    private static OpenRouterImageResponse Fail(string message, string? requestJson = null, string? detail = null)
        => new()
        {
            Success = false,
            RawRequestJson = requestJson,
            ErrorMessage = string.IsNullOrWhiteSpace(detail) ? message : $"{message} {detail}"
        };

    private static void AddOptionalHeader(HttpRequestMessage request, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static JsonDocument? TryParse(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch { return null; }
    }

    private static bool IsSuccess(HttpStatusCode code) => (int)code is >= 200 and <= 299;

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    private static bool IsRenderableReferenceUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Resolves the OpenRouter API key. Prefers the provider's configured key name
    /// (api_key_config_name), tolerating both "OpenRouter__ApiKey" (env style) and the
    /// "OpenRouter:ApiKey" section form. Falls back to "OpenRouter:ApiKey". Never logs the key.
    /// </summary>
    private string? ResolveApiKey(string? apiKeyConfigName)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyConfigName))
        {
            var configName = apiKeyConfigName.Trim();
            // Direct lookup by the configured name (handles "OpenRouter__ApiKey" and any custom name).
            var direct = _config[configName];
            if (!string.IsNullOrWhiteSpace(direct)) return direct;

            // Also try the ":" section form for the same name (e.g. "OpenRouter__ApiKey" -> "OpenRouter:ApiKey").
            var sectionForm = configName.Replace("__", ":", StringComparison.Ordinal);
            if (!sectionForm.Equals(configName, StringComparison.Ordinal))
            {
                var viaSection = _config[sectionForm];
                if (!string.IsNullOrWhiteSpace(viaSection)) return viaSection;
            }
        }

        // Fallback: the default OpenRouter section key.
        return _config["OpenRouter:ApiKey"];
    }

    private static string NormalizeEndpoint(string? endpointPath)
    {
        if (string.IsNullOrWhiteSpace(endpointPath)) return "/images";
        var trimmed = endpointPath.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    /// <summary>
    /// Enforces a minimum resolution of 2K for OpenRouter image models. "1K", empty or unknown
    /// values are raised to "2K"; "2K"/"4K" (and higher) are kept as-is. This guarantees the output
    /// meets the model's minimum pixel requirement (e.g. seedream: >= 3,686,400 px).
    /// </summary>
    public static string NormalizeResolution(string? resolution)
    {
        var value = (resolution ?? string.Empty).Trim().ToUpperInvariant();
        return value switch
        {
            "2K" => "2K",
            "4K" => "4K",
            "8K" => "8K",
            _ => "2K"
        };
    }

    private static string? ExtractError(JsonDocument? doc)
    {
        if (doc is null) return null;
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var err))
        {
            if (err.ValueKind == JsonValueKind.String) return err.GetString();
            if (err.TryGetProperty("message", out var msg)) return msg.GetString();
            return err.GetRawText();
        }

        return root.TryGetProperty("message", out var message) ? message.GetString() : null;
    }

    private static decimal? TryGetCost(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object) return null;
        if (!usage.TryGetProperty("cost", out var cost)) return null;
        return cost.ValueKind == JsonValueKind.Number && cost.TryGetDecimal(out var value) ? value : null;
    }

    private static string Truncate(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= 1200 ? value : value[..1200];
}
