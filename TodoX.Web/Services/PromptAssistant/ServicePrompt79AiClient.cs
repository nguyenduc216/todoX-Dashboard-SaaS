using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiProviders;

namespace TodoX.Web.Services.PromptAssistant;

public interface IServicePromptProviderClient
{
    Task<ServicePromptProviderResponse> CompleteAsync(ServicePromptProviderRequest request, CancellationToken ct = default);
}

public sealed class ServicePrompt79AiClient : IServicePromptProviderClient
{
    private readonly HttpClient _http;
    private readonly IProviderCredentialResolver _credentials;
    private readonly ServicePromptAssistantOptions _options;

    public ServicePrompt79AiClient(
        HttpClient http,
        IProviderCredentialResolver credentials,
        IOptions<ServicePromptAssistantOptions> options)
    {
        _http = http;
        _credentials = credentials;
        _options = options.Value;
    }

    public async Task<ServicePromptProviderResponse> CompleteAsync(
        ServicePromptProviderRequest request,
        CancellationToken ct = default)
    {
        if (!Uri.TryCreate(request.ApiUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ServicePromptProviderException("invalid_api_url", "Prompt provider API URL is invalid.", "{}");
        }

        var credential = await _credentials.ResolveAsync(request.ProviderCode, "access_token", ct);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.ModelCode,
            ["messages"] = new object[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserInput }
            },
            ["stream"] = false
        };
        if (request.Temperature is not null) payload["temperature"] = request.Temperature;
        if (request.MaxTokens is not null) payload["max_tokens"] = request.MaxTokens;

        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret);
        message.Content = new StringContent(
            JsonSerializer.Serialize(payload, ServicePromptJson.Options),
            Encoding.UTF8,
            "application/json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.Timeout);
        HttpResponseMessage response;
        string raw;
        try
        {
            response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            raw = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ServicePromptProviderException("timeout", "Prompt provider request timed out.", "{}");
        }
        catch (HttpRequestException ex)
        {
            throw new ServicePromptProviderException("network_error", "Prompt provider request failed.", "{}", ex);
        }

        var sanitized = Sanitize(raw, credential.Secret);
        if (!response.IsSuccessStatusCode)
        {
            throw new ServicePromptProviderException(
                $"http_{(int)response.StatusCode}",
                $"Prompt provider returned HTTP {(int)response.StatusCode}.",
                sanitized);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var content = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ServicePromptProviderException("missing_content", "Prompt provider response is missing content.", sanitized);
            }

            var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : default;
            return new ServicePromptProviderResponse(
                content,
                ReadInt(usage, "prompt_tokens"),
                ReadInt(usage, "completion_tokens"),
                ReadInt(usage, "total_tokens"),
                sanitized,
                ReadString(root, "id"));
        }
        catch (ServicePromptProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new ServicePromptProviderException("invalid_response", "Prompt provider response is invalid.", sanitized, ex);
        }
    }

    private static int? ReadInt(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(property, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Sanitize(string raw, string secret)
        => string.IsNullOrWhiteSpace(secret)
            ? raw
            : raw.Replace(secret, "***", StringComparison.Ordinal);
}
