using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.EditVideoTool;

public sealed class EditVideoAiOptions
{
    public const string SectionName = "EditVideoTool:Ai";

    public bool Enabled { get; set; } = true;

    /// <summary>Gommo REST base; empty → TimelapseProviderWorkers:Default79AiBaseUrl.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Gommo domain field; empty → the 79ai provider account's config "domain".</summary>
    public string? Domain { get; set; }

    public string ProjectId { get; set; } = "default";

    /// <summary>Gommo V2 jobs host (music: POST /ai/jobs/music/{model}, poll POST /ai/jobs/{id}?media=music).</summary>
    public string V2BaseUrl { get; set; } = "https://v2.api.gommo.net";

    /// <summary>What one Gommo credit costs TodoX, in VND.</summary>
    public decimal VndPerCredit { get; set; } = 1m;

    /// <summary>Multiplier on the Gommo cost before converting to points (1 = sell at cost; agreed rate 3).</summary>
    public decimal Markup { get; set; } = 3m;

    /// <summary>Value of one TodoX point in VND; empty → AiImageBilling:TodoXVndPerPoint.</summary>
    public decimal? VndPerPoint { get; set; }

    public int MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
    public int ModelsCacheSeconds { get; set; } = 60;
    public int StatusCacheSeconds { get; set; } = 5;
}

public sealed class EditVideoGommoException : Exception
{
    public EditVideoGommoException(string code, string message, HttpStatusCode? status = null) : base(message)
    {
        Code = code;
        Status = status;
    }

    public string Code { get; }
    public HttpStatusCode? Status { get; }
}

public interface IEditVideoGommoClient
{
    /// <summary>POSTs a form to Gommo with TodoX's own access token; the token never leaves this class.</summary>
    Task<JsonNode> PostAsync(string path, IReadOnlyDictionary<string, string?> fields, CancellationToken ct = default);

    /// <summary>JSON POST to a Gommo V2 path on the AI host (projects). Test doubles may leave it unsupported.</summary>
    Task<JsonNode> PostJsonAsync(string hostPath, IDictionary<string, object?> body, CancellationToken ct = default) =>
        throw new NotSupportedException("Gommo V2 JSON calls are not available.");

    /// <summary>Form POST to the Gommo V2 jobs host (Bearer token), e.g. /ai/jobs/music/suno-v5.5.</summary>
    Task<JsonNode> PostV2Async(string path, IReadOnlyDictionary<string, string?> fields, CancellationToken ct = default) =>
        throw new NotSupportedException("Gommo V2 jobs are not available.");
}

/// <summary>Thin form-POST client for Gommo (79AI) REST, authenticated with TodoX's stored 79ai credential.</summary>
public sealed class EditVideoGommoClient : IEditVideoGommoClient
{
    private readonly HttpClient _http;
    private readonly IProviderCredentialResolver _credentials;
    private readonly IProviderCredentialRepository _accounts;
    private readonly IOptionsMonitor<EditVideoAiOptions> _options;
    private readonly IConfiguration _configuration;

    public EditVideoGommoClient(HttpClient http, IProviderCredentialResolver credentials, IProviderCredentialRepository accounts,
        IOptionsMonitor<EditVideoAiOptions> options, IConfiguration configuration)
    {
        _http = http;
        _credentials = credentials;
        _accounts = accounts;
        _options = options;
        _configuration = configuration;
    }

    public async Task<JsonNode> PostAsync(string path, IReadOnlyDictionary<string, string?> fields, CancellationToken ct = default)
    {
        ResolvedProviderCredential credential;
        try
        {
            credential = await _credentials.ResolveAsync("79ai", "access_token", ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new EditVideoGommoException("not_configured", ex.Message);
        }

        var options = _options.CurrentValue;
        var baseUrl = FirstNonBlank(options.BaseUrl, _configuration["TimelapseProviderWorkers:Default79AiBaseUrl"])
            ?? throw new EditVideoGommoException("not_configured", "Gommo base URL is not configured.");
        var domain = FirstNonBlank(options.Domain, ReadDomain((await _accounts.GetAccountByIdAsync(credential.ProviderAccountId, ct))?.ConfigJson), "79ai.net")!;

        var form = new Dictionary<string, string> { ["access_token"] = credential.Secret, ["domain"] = domain };
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                form[key] = value;
            }
        }

        using var body = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path.TrimStart('/')), body, ct);
        var text = (await response.Content.ReadAsStringAsync(ct)).Replace(credential.Secret, "***", StringComparison.Ordinal);
        JsonNode? json;
        try
        {
            json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            json = null;
        }

        if (!response.IsSuccessStatusCode || json is null)
        {
            throw new EditVideoGommoException(
                $"http_{(int)response.StatusCode}",
                EditVideoGommoJson.ErrorMessage(json) ?? $"Gommo returned HTTP {(int)response.StatusCode}.",
                response.StatusCode);
        }

        return json;
    }

    /// <summary>
    /// JSON call to a Gommo V2 path on the same host as the AI base URL (for example /api/v2/ai-projects). The token
    /// goes both as Bearer and as access_token, the two ways Gommo's V2 endpoints accept it.
    /// </summary>
    public async Task<JsonNode> PostJsonAsync(string hostPath, IDictionary<string, object?> body, CancellationToken ct = default)
    {
        ResolvedProviderCredential credential;
        try
        {
            credential = await _credentials.ResolveAsync("79ai", "access_token", ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new EditVideoGommoException("not_configured", ex.Message);
        }

        var options = _options.CurrentValue;
        var baseUrl = FirstNonBlank(options.BaseUrl, _configuration["TimelapseProviderWorkers:Default79AiBaseUrl"])
            ?? throw new EditVideoGommoException("not_configured", "Gommo base URL is not configured.");
        var host = new Uri(baseUrl).GetLeftPart(UriPartial.Authority);
        var payload = new Dictionary<string, object?>(body) { ["access_token"] = credential.Secret };
        using var request = new HttpRequestMessage(HttpMethod.Post, host + "/" + hostPath.TrimStart('/'))
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Secret);
        using var response = await _http.SendAsync(request, ct);
        var text = (await response.Content.ReadAsStringAsync(ct)).Replace(credential.Secret, "***", StringComparison.Ordinal);
        JsonNode? json;
        try
        {
            json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            json = null;
        }

        if (!response.IsSuccessStatusCode || json is null)
        {
            throw new EditVideoGommoException($"http_{(int)response.StatusCode}",
                EditVideoGommoJson.ErrorMessage(json) ?? $"Gommo returned HTTP {(int)response.StatusCode}.", response.StatusCode);
        }

        return json;
    }

    public async Task<JsonNode> PostV2Async(string path, IReadOnlyDictionary<string, string?> fields, CancellationToken ct = default)
    {
        ResolvedProviderCredential credential;
        try
        {
            credential = await _credentials.ResolveAsync("79ai", "access_token", ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new EditVideoGommoException("not_configured", ex.Message);
        }

        var options = _options.CurrentValue;
        var domain = FirstNonBlank(options.Domain, ReadDomain((await _accounts.GetAccountByIdAsync(credential.ProviderAccountId, ct))?.ConfigJson), "79ai.net")!;
        var form = new Dictionary<string, string> { ["access_token"] = credential.Secret, ["domain"] = domain };
        foreach (var (key, value) in fields)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                form[key] = value;
            }
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, (options.V2BaseUrl ?? "https://v2.api.gommo.net").TrimEnd('/') + "/" + path.TrimStart('/'))
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Secret);
        using var response = await _http.SendAsync(request, ct);
        var text = (await response.Content.ReadAsStringAsync(ct)).Replace(credential.Secret, "***", StringComparison.Ordinal);
        JsonNode? json;
        try
        {
            json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            json = null;
        }

        if (!response.IsSuccessStatusCode || json is null || json["success"] is JsonValue ok && ok.TryGetValue<bool>(out var success) && !success)
        {
            throw new EditVideoGommoException($"http_{(int)response.StatusCode}",
                EditVideoGommoJson.ErrorMessage(json) ?? $"Gommo returned HTTP {(int)response.StatusCode}.", response.StatusCode);
        }

        return json;
    }

    private static string? ReadDomain(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json)?["domain"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
