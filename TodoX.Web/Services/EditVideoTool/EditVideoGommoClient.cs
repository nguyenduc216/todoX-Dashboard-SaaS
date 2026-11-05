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

    /// <summary>What one Gommo credit costs TodoX, in VND.</summary>
    public decimal VndPerCredit { get; set; } = 1m;

    /// <summary>Multiplier on the Gommo cost before converting to points (1 = sell at cost).</summary>
    public decimal Markup { get; set; } = 1m;

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
