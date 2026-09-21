using System.Net.Http.Headers;
using System.Diagnostics;
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

        ResolvedProviderCredential credential;
        try
        {
            credential = await _credentials.ResolveAsync(request.ProviderCode, "access_token", ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ServicePromptProviderException("gommo_timeout", "Gommo Agent credential lookup timed out.", "{}");
        }
        catch (InvalidOperationException ex)
        {
            throw new ServicePromptProviderException("missing_gommo_token", "Gommo token is not configured.", "{}", ex);
        }
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

        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try { response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServicePromptProviderException("gommo_timeout", "Gommo Agent request timed out.", "{}"); }
        catch (HttpRequestException ex) { throw new ServicePromptProviderException("gommo_http_error", "Gommo Agent request failed.", "{}", ex); }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await ReadBoundedBodyAsync(response, timeout.Token);
            var sanitizedError = Sanitize(errorBody, credential.Secret);
            var code = response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
                ? "gommo_authentication_failed"
                : "gommo_http_error";
            throw new ServicePromptProviderException(code, $"Gommo Agent returned HTTP {(int)response.StatusCode}.", sanitizedError);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var reader = new StreamReader(stream);
        var content = new StringBuilder(); var raw = new StringBuilder();
        int? promptTokens = null, outputTokens = null, completionTokens = null, totalTokens = null; decimal? credit = null; string? runtimeProvider = null;
        var sawSse = false; var firstEventMs = (int?)null; var firstContentMs = (int?)null; var done = false;
        try
        {
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (raw.Length < 64 * 1024) raw.AppendLine(line);
                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                sawSse = true; firstEventMs ??= (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var data = line[5..].Trim();
                if (string.IsNullOrWhiteSpace(data)) continue;
                if (string.Equals(data, "[DONE]", StringComparison.Ordinal)) { done = true; break; }
                TryReadChunk(data, content, ref promptTokens, ref outputTokens, ref completionTokens, ref totalTokens, ref credit, ref runtimeProvider, ref firstContentMs, started);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ServicePromptProviderException("gommo_timeout", "Gommo Agent request timed out.", "{}"); }

        var sanitized = Sanitize(raw.ToString(), credential.Secret);
        if (!sawSse || !done || !string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw new ServicePromptProviderException("invalid_sse", "Gommo Agent response is not SSE.", sanitized);
        if (content.Length == 0) throw new ServicePromptProviderException("missing_content", "Gommo Agent response contains no assistant content.", sanitized);
        var totalDurationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var effectiveOutputTokens = outputTokens ?? completionTokens;
        return new ServicePromptProviderResponse(content.ToString(), promptTokens, effectiveOutputTokens, totalTokens, sanitized,
            Credit: credit, RuntimeProvider: runtimeProvider, FirstEventMs: firstEventMs, FirstContentMs: firstContentMs,
            TotalDurationMs: totalDurationMs,
            StreamingDurationMs: firstContentMs is null ? null : Math.Max(0, totalDurationMs - firstContentMs.Value));
    }

    private static void TryReadChunk(string data, StringBuilder content, ref int? promptTokens, ref int? outputTokens, ref int? completionTokens, ref int? totalTokens, ref decimal? credit, ref string? runtimeProvider, ref int? firstContentMs, long started)
    {
        try
        {
            using var document = JsonDocument.Parse(data); var root = document.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var token) && token.ValueKind == JsonValueKind.String)
                { firstContentMs ??= (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds; content.Append(token.GetString()); }
            }
            if (root.TryGetProperty("usage", out var usage))
            {
                promptTokens = ReadInt(usage, "prompt_tokens") ?? promptTokens;
                outputTokens = ReadInt(usage, "output_tokens") ?? outputTokens;
                completionTokens = ReadInt(usage, "completion_tokens") ?? completionTokens;
                totalTokens = ReadInt(usage, "total_tokens") ?? totalTokens;
                credit = ReadDecimal(usage, "credit") ?? credit;
                runtimeProvider = ReadString(usage, "provider") ?? runtimeProvider;
            }
        }
        catch (JsonException ex) { throw new ServicePromptProviderException("malformed_sse_chunk", "Gommo Agent returned malformed SSE data.", "{}", ex); }
    }

    private static int? ReadInt(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static decimal? ReadDecimal(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.TryGetDecimal(out var result) ? result : null;
    private static string? ReadString(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string Sanitize(string raw, string secret) => string.IsNullOrWhiteSpace(secret) ? raw : raw.Replace(secret, "***", StringComparison.Ordinal);

    private static async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[64 * 1024];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (count == 0) break;
            read += count;
        }

        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
