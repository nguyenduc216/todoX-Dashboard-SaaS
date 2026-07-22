using System.Text;
using System.Text.Json;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.ImageRender;

namespace TodoX.Web.Services.Profile;

/// <summary>
/// Uses Vertex Gemini (gemini-2.5-flash) to turn one base chibi prompt into N distinct
/// scenario prompts, so the N generated images differ meaningfully. Reuses the Vertex SA credential.
/// </summary>
public sealed class GeminiPromptService
{
    private readonly HttpClient _http;
    private readonly VertexClient _vertex;
    private readonly IAiProviderCredentialResolver _credentials;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiPromptService> _logger;

    public GeminiPromptService(HttpClient http, VertexClient vertex, IAiProviderCredentialResolver credentials, IConfiguration config,
        ILogger<GeminiPromptService> logger)
    {
        _http = http;
        _vertex = vertex;
        _credentials = credentials;
        _config = config;
        _logger = logger;
    }

    public string ModelCode => _config["Vertex:GeminiModel"] ?? "gemini-2.5-flash";

    /// <summary>
    /// Ask Gemini for <paramref name="count"/> distinct variations of the base prompt.
    /// Throws on failure (caller decides fallback). Never fabricates images.
    /// </summary>
    public async Task<List<string>> GenerateVariationsAsync(string basePrompt, int count, CancellationToken ct = default)
    {
        var project = _config["Vertex:ProjectId"] ?? throw new InvalidOperationException("Missing Vertex:ProjectId");
        var location = _config["Vertex:Location"] ?? "us-central1";
        var model = ModelCode;
        var credential = await _credentials.ResolveDefaultAsync("google-vertex-ai", ct: ct);
        var token = await _vertex.AcquireAccessTokenAsync(credential, ct);

        var instruction =
            $"You are a prompt engineer for an AI image generator (Imagen). Based on the base prompt below, " +
            $"produce exactly {count} DISTINCT variation prompts for a 3D chibi avatar. Each variation must keep the " +
            $"same facial identity/style but differ in angle, pose, outfit, or background. Return ONLY a JSON array " +
            $"of {count} strings, no markdown, no commentary.\n\nBASE PROMPT:\n{basePrompt}";

        var url = $"https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}/publishers/google/models/{model}:generateContent";
        var payload = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = instruction } } } },
            generationConfig = new { temperature = 1.0, responseMimeType = "application/json" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini HTTP {(int)resp.StatusCode}: {Truncate(body, 400)}");
        }

        var text = ExtractText(body);
        var variations = ParseJsonArray(text);

        // Normalize to exactly `count` items.
        if (variations.Count == 0)
        {
            throw new InvalidOperationException("Gemini returned no usable variations.");
        }
        while (variations.Count < count) variations.Add(variations[variations.Count % variations.Count]);
        return variations.Take(count).ToList();
    }

    private static string ExtractText(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("candidates", out var cands) && cands.GetArrayLength() > 0)
        {
            var first = cands[0];
            if (first.TryGetProperty("content", out var content)
                && content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0
                && parts[0].TryGetProperty("text", out var t))
            {
                return t.GetString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    private static List<string> ParseJsonArray(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        // Strip markdown fences if present.
        var cleaned = text.Trim().Trim('`');
        var start = cleaned.IndexOf('[');
        var end = cleaned.LastIndexOf(']');
        if (start >= 0 && end > start)
        {
            cleaned = cleaned.Substring(start, end - start + 1);
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) result.Add(s!.Trim());
                }
            }
        }
        catch
        {
            // Not valid JSON; treat the whole text as a single prompt.
            result.Add(text.Trim());
        }
        return result;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
