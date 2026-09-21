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

    public ServicePrompt79AiClient(HttpClient http, IProviderCredentialResolver credentials, IOptions<ServicePromptAssistantOptions> options)
    { _http = http; _credentials = credentials; _options = options.Value; }

    public async Task<ServicePromptProviderResponse> CompleteAsync(ServicePromptProviderRequest request, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(request.ApiUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ServicePromptProviderException("invalid_api_url", "Prompt provider API URL is invalid.", "{}");
        if (string.IsNullOrWhiteSpace(request.AgentIdBase))
            throw new ServicePromptProviderException("missing_agent_id_base", "Gommo Agent ID Base is not configured.", "{}");

        var credential = await _credentials.ResolveAsync(request.ProviderCode, "access_token", ct);
        var payload = new
        {
            action = "chat",
            agent_id = request.AgentIdBase,
            messages = new[] { new { role = "user", text = request.UserInput } },
            user_message_id = Guid.NewGuid().ToString(),
            assistant_message_id = Guid.NewGuid().ToString(),
            chat_mode = "chat",
            platform = "web"
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, uri);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add("Gommo-Token", credential.Secret);
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.Timeout);

        HttpResponseMessage response;
        try { response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServicePromptProviderException("timeout", "Gommo Agent request timed out.", "{}"); }
        catch (HttpRequestException ex) { throw new ServicePromptProviderException("network_error", "Gommo Agent request failed.", "{}", ex); }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var content = new StringBuilder(); var raw = new StringBuilder();
        int? promptTokens = null, completionTokens = null, totalTokens = null; var sawSse = false;
        try
        {
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                raw.AppendLine(line);
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                sawSse = true; var data = line[5..].Trim();
                if (data is "" or "[DONE]") continue;
                TryReadChunk(data, content, ref promptTokens, ref completionTokens, ref totalTokens);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServicePromptProviderException("timeout", "Gommo Agent request timed out.", "{}"); }

        var sanitized = Sanitize(raw.ToString(), credential.Secret);
        if (!response.IsSuccessStatusCode) throw new ServicePromptProviderException($"http_{(int)response.StatusCode}", $"Gommo Agent returned HTTP {(int)response.StatusCode}.", sanitized);
        if (!sawSse || !string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw new ServicePromptProviderException("invalid_sse", "Gommo Agent response is not SSE.", sanitized);
        if (content.Length == 0) throw new ServicePromptProviderException("missing_content", "Gommo Agent response contains no assistant content.", sanitized);
        return new ServicePromptProviderResponse(content.ToString(), promptTokens, completionTokens, totalTokens, sanitized);
    }

    private static void TryReadChunk(string data, StringBuilder content, ref int? promptTokens, ref int? completionTokens, ref int? totalTokens)
    {
        try
        {
            using var document = JsonDocument.Parse(data); var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var token) && token.ValueKind == JsonValueKind.String) content.Append(token.GetString());
            }
            if (root.TryGetProperty("usage", out var usage)) { promptTokens = ReadInt(usage, "prompt_tokens"); completionTokens = ReadInt(usage, "completion_tokens"); totalTokens = ReadInt(usage, "total_tokens"); }
        }
        catch (JsonException) { }
    }

    private static int? ReadInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static string Sanitize(string raw, string secret) => string.IsNullOrWhiteSpace(secret) ? raw : raw.Replace(secret, "***", StringComparison.Ordinal);
}
