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
    public string? LastModelUsed { get; private set; }

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

    public Task<List<byte[]>> GenerateImagesAsync(string prompt, int count, string aspectRatio, CancellationToken ct)
        => GenerateImagesAsync(prompt, Array.Empty<ReferenceImage>(), count, aspectRatio, ct);

    public async Task<List<byte[]>> GenerateImagesAsync(string prompt, IReadOnlyList<ReferenceImage> referenceImages,
        int count, string aspectRatio, CancellationToken ct = default)
    {
        var project = _config["Vertex:ProjectId"] ?? throw new InvalidOperationException("Missing Vertex:ProjectId");
        var location = _config["Vertex:Location"] ?? "us-central1";
        LastModelUsed = null;

        var hasReferences = NormalizeReferenceImages(referenceImages).Count > 0;
        var models = hasReferences
            ? new[] { _config["Vertex:CapabilityModel"] ?? "imagen-3.0-capability-001" }
            : new[] { _config["Vertex:ImageModel"], _config["Vertex:FallbackModel"] }
                .Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m!).Distinct().ToArray();
        if (models.Length == 0) models = new[] { "imagen-3.0-generate-002" };

        var token = await GetAccessTokenAsync(ct);
        Exception? last = null;

        foreach (var model in models)
        {
            try
            {
                var images = await CallModelAsync(token, project, location, model, prompt, referenceImages, count, aspectRatio, ct);
                if (images.Count > 0)
                {
                    LastModelUsed = model;
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
        string prompt, IReadOnlyList<ReferenceImage> referenceImages, int count, string aspectRatio, CancellationToken ct)
    {
        var url = $"https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}/publishers/google/models/{model}:predict";
        var refs = NormalizeReferenceImages(referenceImages);
        if (refs.Count > 0 && !SupportsReferenceImages(model))
        {
            throw new InvalidOperationException($"Model hien tai ({model}) khong ho tro anh tham chieu. Can cau hinh Gemini/Imagen image model co ho tro image input.");
        }

        object instance = refs.Count == 0
            ? new { prompt }
            : new { prompt = BuildCapabilityPrompt(prompt, refs), referenceImages = BuildCapabilityReferences(refs) };

        object parameters = refs.Count == 0
            ? new
            {
                sampleCount = count,
                aspectRatio,
                // Chibi avatars depict people; Imagen silently returns 0 images unless person
                // generation is explicitly allowed. Verified: without this, predictions is empty.
                personGeneration = "allow_all"
            }
            : new
            {
                sampleCount = count,
                aspectRatio,
                personGeneration = "allow_all",
                editMode = "EDIT_MODE_DEFAULT"
            };

        var payload = new { instances = new[] { instance }, parameters };

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
            throw new InvalidOperationException($"[{model}] Không có 'predictions' trong phản hồi. Response: {Truncate(body, 600)}");
        }

        var images = new List<byte[]>();
        string? raiReason = null;
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

            // Capture any safety / RAI filter reason the model returned.
            if (p.TryGetProperty("raiFilteredReason", out var rai)) raiReason = rai.GetString();

            if (!string.IsNullOrWhiteSpace(b64))
            {
                images.Add(Convert.FromBase64String(b64));
            }
        }

        if (images.Count == 0)
        {
            var reason = string.IsNullOrWhiteSpace(raiReason)
                ? $"phản hồi không chứa ảnh. Response: {Truncate(body, 600)}"
                : $"bị bộ lọc an toàn loại bỏ (raiFilteredReason: {raiReason}).";
            throw new InvalidOperationException($"[{model}] Không có ảnh — {reason}");
        }

        return images;
    }

    private static List<ReferenceImage> NormalizeReferenceImages(IReadOnlyList<ReferenceImage> referenceImages)
    {
        var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["avatar"] = 0,
            ["product"] = 1,
            ["logo"] = 2,
            ["uniform"] = 3,
            ["scene"] = 4
        };

        return referenceImages
            .Where(x => x.Bytes?.Length > 0 || !string.IsNullOrWhiteSpace(x.Base64))
            .Select(x =>
            {
                if (string.IsNullOrWhiteSpace(x.Base64) && x.Bytes?.Length > 0)
                {
                    x.Base64 = Convert.ToBase64String(x.Bytes);
                }
                if (string.IsNullOrWhiteSpace(x.MimeType))
                {
                    x.MimeType = "image/png";
                }
                return x;
            })
            .OrderBy(x => x.Role is not null && order.TryGetValue(x.Role, out var n) ? n : 99)
            .ToList();
    }

    private static string BuildCapabilityPrompt(string prompt, IReadOnlyList<ReferenceImage> refs)
    {
        var lines = new List<string>();
        foreach (var item in refs.Select((image, index) => new { image, id = index + 1 }))
        {
            var role = item.image.Role ?? string.Empty;
            if (role.Equals("avatar", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Use the person/avatar reference [{item.id}] as the main identity guidance for the chibi character.");
            }
            else if (role.Equals("product", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Use the product reference [{item.id}] as a mandatory visual constraint. The final image must clearly include the same product, preserving its main shape, color, package design, and visible details. The character should hold it, stand next to it, or interact with it naturally. Do not replace it with a generic object.");
            }
            else if (role.Equals("logo", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Use the brand/logo style reference [{item.id}] only for brand colors or a small tasteful badge. Do not render transparent logo areas as a black square or dark background; preserve the transparent look or use a clean white/transparent-safe treatment.");
            }
            else if (role.Equals("uniform", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Use the uniform/clothing reference [{item.id}] for clothing shape, main colors, and brand identity details.");
            }
            else if (role.Equals("scene", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Use the background/scene style reference [{item.id}] only as simplified cinematic background inspiration.");
            }
            else
            {
                lines.Add($"Use reference image [{item.id}] as visual guidance.");
            }
        }

        return prompt.Trim() + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static object[] BuildCapabilityReferences(IReadOnlyList<ReferenceImage> refs)
    {
        return refs.Select((image, index) =>
        {
            var referenceImage = new { bytesBase64Encoded = image.Base64, mimeType = image.MimeType ?? "image/png" };
            var id = index + 1;
            var role = image.Role ?? string.Empty;

            if (role.Equals("avatar", StringComparison.OrdinalIgnoreCase))
            {
                return (object)new
                {
                    referenceType = "REFERENCE_TYPE_SUBJECT",
                    referenceId = id,
                    referenceImage,
                    subjectImageConfig = new
                    {
                        subjectDescription = "person in the avatar reference",
                        subjectType = "SUBJECT_TYPE_PERSON"
                    }
                };
            }

            if (role.Equals("product", StringComparison.OrdinalIgnoreCase))
            {
                return (object)new
                {
                    referenceType = "REFERENCE_TYPE_SUBJECT",
                    referenceId = id,
                    referenceImage,
                    subjectImageConfig = new
                    {
                        subjectDescription = "product in the product reference",
                        subjectType = "SUBJECT_TYPE_PRODUCT"
                    }
                };
            }

            return (object)new
            {
                referenceType = "REFERENCE_TYPE_STYLE",
                referenceId = id,
                referenceImage,
                styleImageConfig = new
                {
                    styleDescription = role.Equals("logo", StringComparison.OrdinalIgnoreCase)
                        ? "brand logo colors and simple badge style"
                        : role.Equals("uniform", StringComparison.OrdinalIgnoreCase)
                            ? "uniform and clothing style"
                            : "cinematic background style"
                }
            };
        }).ToArray();
    }

    private static bool SupportsReferenceImages(string model)
    {
        return model.Contains("capability", StringComparison.OrdinalIgnoreCase)
            || model.Contains("edit", StringComparison.OrdinalIgnoreCase)
            || model.Contains("imagegeneration@006", StringComparison.OrdinalIgnoreCase);
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
