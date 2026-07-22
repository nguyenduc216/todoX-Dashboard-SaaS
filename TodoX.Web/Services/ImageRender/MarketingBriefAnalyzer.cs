using System.Text;
using System.Text.Json;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.ImageRender;

public sealed class MarketingBriefAnalyzerResult
{
    public string Provider { get; set; } = "rule_v1";
    public bool UsedFallback { get; set; }
    public string? RawResponse { get; set; }
    public string? Error { get; set; }
    public MarketingBriefAnalysis Analysis { get; set; } = new();
    public MarketingRenderPlan Plan { get; set; } = new();
}

public interface IMarketingBriefAnalyzer
{
    Task<MarketingBriefAnalyzerResult> AnalyzeAsync(MarketingImageRenderRequest request, CancellationToken ct = default);
}

public sealed class GeminiMarketingBriefAnalyzer : IMarketingBriefAnalyzer
{
    private readonly HttpClient _http;
    private readonly VertexClient _vertex;
    private readonly IAiProviderCredentialResolver _credentials;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiMarketingBriefAnalyzer> _logger;

    public GeminiMarketingBriefAnalyzer(HttpClient http, VertexClient vertex, IAiProviderCredentialResolver credentials, IConfiguration config,
        ILogger<GeminiMarketingBriefAnalyzer> logger)
    {
        _http = http;
        _vertex = vertex;
        _credentials = credentials;
        _config = config;
        _logger = logger;
    }

    public async Task<MarketingBriefAnalyzerResult> AnalyzeAsync(MarketingImageRenderRequest request, CancellationToken ct = default)
    {
        var project = _config["Vertex:ProjectId"] ?? throw new InvalidOperationException("Missing Vertex:ProjectId");
        var location = _config["Vertex:Location"] ?? "us-central1";
        var model = _config["Vertex:GeminiModel"] ?? "gemini-2.5-flash";
        var credential = await _credentials.ResolveDefaultAsync("google-vertex-ai", ct: ct);
        var token = await _vertex.AcquireAccessTokenAsync(credential, ct);
        var prompt = BuildInstruction(request);
        var url = $"https://{location}-aiplatform.googleapis.com/v1/projects/{project}/locations/{location}/publishers/google/models/{model}:generateContent";
        var payload = new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.35, responseMimeType = "application/json" }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Gemini analyzer HTTP {(int)resp.StatusCode}: {Truncate(body, 500)}");
        }

        var text = ExtractText(body);
        var parsed = ParseAnalyzerJson(text);
        parsed.Provider = $"gemini:{model}";
        parsed.RawResponse = text;
        ValidateAgainstUserBrief(parsed.Plan, request);
        return parsed;
    }

    private static string BuildInstruction(MarketingImageRenderRequest request)
    {
        return $$"""
        You are the marketing image planning brain for TodoX.
        Analyze the user's brief and produce a STRICT JSON object. Do not render an image.

        Hard rules:
        - Do not add TikTok, Facebook, Reup, cross-posting, or social network flow unless the user brief explicitly mentions them.
        - If a brand robot is present or preserveFixedAssets is true, assetPolicy.brandRobotSentToModel must be false.
        - If preserving robot, plan the background only and leave center/bottom-center empty for code composite.
        - Keep text short and readable.
        - Return JSON only, no markdown.

        Input:
        {
          "serviceName": {{JsonSerializer.Serialize(request.ServiceName)}},
          "serviceCategory": {{JsonSerializer.Serialize(request.ServiceCategory)}},
          "shortDescription": {{JsonSerializer.Serialize(request.ShortDescription)}},
          "brief": {{JsonSerializer.Serialize(request.Brief)}},
          "tone": {{JsonSerializer.Serialize(request.Tone)}},
          "aspectRatio": {{JsonSerializer.Serialize(request.AspectRatio)}},
          "hasBrandRobot": {{(!string.IsNullOrWhiteSpace(request.BrandRobotImageUrl)).ToString().ToLowerInvariant()}},
          "preserveFixedAssets": {{request.PreserveFixedAssets.ToString().ToLowerInvariant()}}
        }

        Required JSON shape:
        {
          "analysis": {
            "serviceName": "string",
            "serviceCategory": "string",
            "detectedServiceType": "string",
            "confidence": 0.0,
            "classificationReason": "string",
            "excludedServiceTypes": ["string"],
            "mentionsTikTok": false,
            "mentionsFacebook": false,
            "mentionsReup": false
          },
          "renderPlan": {
            "planVersion": "1.0",
            "serviceName": "string",
            "serviceCategory": "string",
            "serviceType": "string",
            "aspectRatio": "9:16",
            "theme": "yellow_black",
            "headline": "string",
            "subheadline": "string",
            "footer": "string",
            "benefitBullets": ["string"],
            "visualElements": ["string"],
            "assetPolicy": {
              "preserveBrandRobot": true,
              "brandRobotSentToModel": false,
              "robotRole": "brand_robot",
              "pipeline": "background_then_composite"
            },
            "forbiddenTerms": ["string"],
            "requiredConcepts": ["string"],
            "negativePrompt": "string"
          }
        }
        """;
    }

    private static MarketingBriefAnalyzerResult ParseAnalyzerJson(string text)
    {
        var cleaned = CleanJson(text);
        using var doc = JsonDocument.Parse(cleaned);
        var root = doc.RootElement;
        var analysis = JsonSerializer.Deserialize<MarketingBriefAnalysis>(
            root.GetProperty("analysis").GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new MarketingBriefAnalysis();
        var plan = JsonSerializer.Deserialize<MarketingRenderPlan>(
            root.GetProperty("renderPlan").GetRawText(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new MarketingRenderPlan();
        return new MarketingBriefAnalyzerResult
        {
            Provider = "gemini",
            Analysis = analysis,
            Plan = plan
        };
    }

    private static void ValidateAgainstUserBrief(MarketingRenderPlan plan, MarketingImageRenderRequest request)
    {
        var brief = $"{request.ServiceName} {request.ServiceCategory} {request.ShortDescription} {request.Brief}".ToLowerInvariant();
        var mentionsTikTok = brief.Contains("tiktok");
        var mentionsFacebook = brief.Contains("facebook") || brief.Contains("fb ");
        var mentionsReup = brief.Contains("reup") || brief.Contains("đăng lại") || brief.Contains("dang lai");

        if (!mentionsTikTok && ContainsAny(plan, "tiktok"))
        {
            throw new InvalidOperationException("AI plan added TikTok although the brief did not mention TikTok.");
        }

        if (!mentionsFacebook && ContainsAny(plan, "facebook"))
        {
            throw new InvalidOperationException("AI plan added Facebook although the brief did not mention Facebook.");
        }

        if (!mentionsReup && ContainsAny(plan, "reup"))
        {
            throw new InvalidOperationException("AI plan added Reup although the brief did not mention Reup.");
        }

        if (request.PreserveFixedAssets && plan.AssetPolicy.BrandRobotSentToModel)
        {
            throw new InvalidOperationException("AI plan attempted to send brand robot to model.");
        }
    }

    private static bool ContainsAny(MarketingRenderPlan plan, string term)
    {
        var haystack = string.Join(" ", new[]
        {
            plan.Headline,
            plan.Subheadline,
            plan.Footer,
            plan.ServiceType,
            string.Join(" ", plan.VisualElements),
            string.Join(" ", plan.RequiredConcepts)
        }).ToLowerInvariant();
        return haystack.Contains(term, StringComparison.OrdinalIgnoreCase);
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

    private static string CleanJson(string text)
    {
        var cleaned = text.Trim().Trim('`');
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            cleaned = cleaned[start..(end + 1)];
        }

        return cleaned;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
