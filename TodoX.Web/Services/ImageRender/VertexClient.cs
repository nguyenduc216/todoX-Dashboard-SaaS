using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TodoX.Web.Services.ImageRender;

/// <summary>
/// Thin client for Google Vertex AI Imagen (predict). Authenticates with the service-account
/// JSON key at Vertex:ServiceAccountKeyPath by signing a JWT (RS256) and exchanging it for an
/// access token at oauth2.googleapis.com — mirroring the proven PowerShell test script.
/// Throws on any failure so the caller can decide how to handle it.
/// </summary>
public sealed class VertexClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VertexClient> _logger;

    private ServiceAccountKey? _sa;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    public VertexClient(HttpClient http, IConfiguration config, IWebHostEnvironment env, ILogger<VertexClient> logger)
    {
        _http = http;
        _config = config;
        _env = env;
        _logger = logger;
    }

    private sealed class ServiceAccountKey
    {
        public string client_email { get; set; } = string.Empty;
        public string private_key { get; set; } = string.Empty;
        public string project_id { get; set; } = string.Empty;
        public string token_uri { get; set; } = "https://oauth2.googleapis.com/token";
    }

    public async Task<List<byte[]>> GenerateImagesAsync(string prompt, int count, string aspectRatio, CancellationToken ct)
    {
        var project = _config["Vertex:ProjectId"] ?? throw new InvalidOperationException("Missing Vertex:ProjectId");
        var location = _config["Vertex:Location"] ?? "us-central1";

        // Try the primary model, then the fallback model (mirrors the proven PowerShell script).
        var models = new[] { _config["Vertex:ImageModel"], _config["Vertex:FallbackModel"] }
            .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!).Distinct().ToArray();
        if (models.Length == 0) models = new[] { "imagen-3.0-generate-002" };

        var token = await GetAccessTokenAsync(ct);
        Exception? last = null;

        foreach (var model in models)
        {
            try
            {
                var images = await CallModelAsync(token, project, location, model, prompt, count, aspectRatio, ct);
                if (images.Count > 0)
                {
                    _logger.LogInformation("Vertex render OK with model {Model} ({N} images)", model, images.Count);
                    return images;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Vertex model {Model} failed: {Msg}", model, ex.Message);
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("Vertex returned no images for any model.");
    }

    private async Task<List<byte[]>> CallModelAsync(string token, string project, string location, string model,
        string prompt, int count, string aspectRatio, CancellationToken ct)
    {
        var url = $"https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}/publishers/google/models/{model}:predict";

        var payload = new
        {
            instances = new[] { new { prompt } },
            parameters = new
            {
                sampleCount = count,
                aspectRatio
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            // Imagen returns HTTP 400 when every image is removed by the safety filter — surface a clear message.
            if (body.Contains("filtered out", StringComparison.OrdinalIgnoreCase)
                || body.Contains("safety", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Ảnh bị bộ lọc an toàn của Imagen loại bỏ. Hãy chỉnh prompt (tránh nội dung nhạy cảm) và thử lại.");
            }
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(body, 400)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("predictions", out var preds) || preds.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("No predictions in response.");
        }

        var images = new List<byte[]>();
        foreach (var p in preds.EnumerateArray())
        {
            // Imagen may return bytesBase64Encoded at the top or nested under image.
            string? b64 = null;
            if (p.TryGetProperty("bytesBase64Encoded", out var top) && top.ValueKind == JsonValueKind.String)
            {
                b64 = top.GetString();
            }
            else if (p.TryGetProperty("image", out var img) && img.TryGetProperty("bytesBase64Encoded", out var nested))
            {
                b64 = nested.GetString();
            }

            if (!string.IsNullOrWhiteSpace(b64))
            {
                images.Add(Convert.FromBase64String(b64));
            }
        }

        if (images.Count == 0)
        {
            throw new InvalidOperationException("Predictions contained no image bytes (possibly filtered by safety).");
        }

        return images;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        // Reuse a cached token until shortly before expiry.
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
        {
            return _cachedToken;
        }

        var sa = LoadServiceAccount();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = now + 3600;

        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var claimJson = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = sa.client_email,
            ["scope"] = "https://www.googleapis.com/auth/cloud-platform",
            ["aud"] = sa.token_uri,
            ["iat"] = now,
            ["exp"] = exp
        });
        var claim = Base64Url(Encoding.UTF8.GetBytes(claimJson));
        var unsigned = $"{header}.{claim}";

        using var rsa = RSA.Create();
        // System.Text.Json already unescapes "\n" in the JSON string into real newlines,
        // so the PEM is used as-is (matches the proven PowerShell/node signing path).
        rsa.ImportFromPem(sa.private_key);
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(unsigned), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var jwt = $"{unsigned}.{Base64Url(signature)}";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = jwt
        });

        using var resp = await _http.PostAsync(sa.token_uri, form, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token exchange HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}");
        }

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException($"No access_token in token response: {Truncate(body, 200)}");
        }

        _cachedToken = token;
        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(3600);
        return token;
    }

    /// <summary>Exposed for other Vertex callers (e.g. Gemini text) to reuse the SA credential.</summary>
    public Task<string> AcquireAccessTokenAsync(CancellationToken ct = default) => GetAccessTokenAsync(ct);

    private ServiceAccountKey LoadServiceAccount()
    {
        if (_sa is not null) return _sa;
        var rel = _config["Vertex:ServiceAccountKeyPath"] ?? "keys/todox-vertex-sa.json";
        var path = Path.IsPathRooted(rel) ? rel : Path.Combine(_env.ContentRootPath, rel);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Vertex service account key not found at {rel}");
        }
        var json = File.ReadAllText(path);
        _sa = JsonSerializer.Deserialize<ServiceAccountKey>(json)
            ?? throw new InvalidOperationException("Invalid service account JSON.");
        return _sa;
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
