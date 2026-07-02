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
        var model = _config["Vertex:ImageModel"] ?? "imagen-3.0-generate-001";

        var token = await GetAccessTokenAsync(ct);

        var url = $"https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}/publishers/google/models/{model}:predict";

        var payload = new
        {
            instances = new[] { new { prompt } },
            parameters = new
            {
                sampleCount = count,
                aspectRatio = aspectRatio,
                // Ask for base64-encoded bytes back.
                includeRaiReason = true
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Vertex predict HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("predictions", out var preds) || preds.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Vertex returned no predictions.");
        }

        var images = new List<byte[]>();
        foreach (var p in preds.EnumerateArray())
        {
            // Imagen returns bytesBase64Encoded per prediction.
            if (p.TryGetProperty("bytesBase64Encoded", out var b64) && b64.GetString() is { } s)
            {
                images.Add(Convert.FromBase64String(s));
            }
        }

        if (images.Count == 0)
        {
            throw new InvalidOperationException("Vertex predictions contained no image bytes.");
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
