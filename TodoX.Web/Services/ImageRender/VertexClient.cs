using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace TodoX.Web.Services.ImageRender;

/// <summary>
/// Thin client for Google Vertex AI Imagen (predict). Authenticates with the service-account
/// JSON key at Vertex:ServiceAccountKeyPath and calls the model's :predict endpoint.
/// Throws on any failure so the caller can fall back to a placeholder.
/// </summary>
public sealed class VertexClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<VertexClient> _logger;
    private GoogleCredential? _credential;

    public VertexClient(HttpClient http, IConfiguration config, IWebHostEnvironment env, ILogger<VertexClient> logger)
    {
        _http = http;
        _config = config;
        _env = env;
        _logger = logger;
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
        _credential ??= LoadCredential();
        var scoped = _credential.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        var token = await scoped.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        return token ?? throw new InvalidOperationException("Could not obtain Google access token.");
    }

    private GoogleCredential LoadCredential()
    {
        var rel = _config["Vertex:ServiceAccountKeyPath"] ?? "keys/todox-vertex-sa.json";
        var path = Path.IsPathRooted(rel) ? rel : Path.Combine(_env.ContentRootPath, rel);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Vertex service account key not found at {rel}");
        }
        return GoogleCredential.FromFile(path);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
