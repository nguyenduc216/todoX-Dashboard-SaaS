using System.Net;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class GommoPromptAssistantTests
{
    [Fact]
    public async Task ClientSendsAgentBaseAndAggregatesOnlyAssistantSseContent()
    {
        var handler = new StubHandler(
            "event: thinking\ndata: {\"thinking\":\"hidden\"}\n\n" +
            $"data: {Chunk("{")}\n\n" +
            "event: usage\ndata: {\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3,\"total_tokens\":5}}\n\n" +
            $"data: {Chunk("\"scene\":\"demo\"}")}\n\n" +
            "data: [DONE]\n\n");
        var credentials = new StaticCredentialResolver("secret-token");
        var client = new ServicePrompt79AiClient(new HttpClient(handler), credentials,
            Options.Create(new ServicePromptAssistantOptions { TimeoutSeconds = 30 }));

        var result = await client.CompleteAsync(new ServicePromptProviderRequest(
            "https://api.gommo.net/api/v2/chat", "gommo_agent", "base-123", "make demo"));

        Assert.Equal("{\"scene\":\"demo\"}", result.Content);
        Assert.Equal(5, result.TotalTokens);
        Assert.NotNull(result.TotalDurationMs);
        Assert.True(result.TotalDurationMs >= 0);
        Assert.DoesNotContain("secret-token", result.SanitizedRawResponse);
        Assert.Equal("text/event-stream; charset=utf-8", result.Diagnostics?.HttpContentType);
        Assert.Equal(5, result.Diagnostics?.SseDataCount);
        Assert.True(result.Diagnostics?.DoneReceived);
        Assert.Equal(result.Content.Length, result.Diagnostics?.AssembledContentLength);
        Assert.False(result.Diagnostics?.RawResponseTruncated);
        Assert.Equal(("gommo_agent", "access_token"), credentials.LastResolve);
        Assert.Equal("base-123", handler.RequestBody!.RootElement.GetProperty("agent_id").GetString());
        Assert.NotEqual(handler.RequestBody.RootElement.GetProperty("user_message_id").GetString(), handler.RequestBody.RootElement.GetProperty("assistant_message_id").GetString());
    }

    [Fact]
    public async Task ClientMarksBoundedRawCaptureAsTruncated()
    {
        var largeDiagnosticEvent = System.Text.Json.JsonSerializer.Serialize(new { ignored = new string('x', 70 * 1024) });
        var handler = new StubHandler(
            $"data: {largeDiagnosticEvent}\n\n" +
            $"data: {Chunk("{\"title\":\"ok\"}")}\n\n" +
            "data: [DONE]\n\n");
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(CreateRequest());

        Assert.True(result.Diagnostics?.RawResponseTruncated);
        Assert.Equal(64 * 1024, result.Diagnostics?.RawResponseCapturedLength);
        Assert.Equal("{\"title\":\"ok\"}", result.Content);
    }

    [Fact]
    public async Task ClientPreservesSanitizedMalformedChunkEvidenceAndDiagnostics()
    {
        var handler = new StubHandler("data: {\"invalid\":\"secret-token\"\n\n");
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("malformed_sse_chunk", error.Code);
        Assert.DoesNotContain("secret-token", error.SanitizedResponse, StringComparison.Ordinal);
        Assert.Contains("***", error.SanitizedResponse, StringComparison.Ordinal);
        Assert.Equal(1, error.Diagnostics?.SseDataCount);
        Assert.False(error.Diagnostics?.DoneReceived);
        Assert.NotNull(error.ParserErrorLineNumber);
        Assert.NotNull(error.ParserErrorBytePositionInLine);
    }

    [Fact]
    public async Task ClientReportsProviderStreamErrorInsteadOfMissingContent()
    {
        var handler = new StubHandler(
            $"data: {Chunk("")}\n\n" +
            "event: error\ndata: {\"message\":\"Dịch vụ AI đang gặp lỗi tạm thời.\",\"error_code\":\"CHAT_STREAM_ERROR\"}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n");
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_error", error.Code);
        Assert.Equal("Dịch vụ AI đang gặp lỗi tạm thời. (CHAT_STREAM_ERROR)", error.Message);
        Assert.Contains("CHAT_STREAM_ERROR", error.SanitizedResponse, StringComparison.Ordinal);
        Assert.True(error.Diagnostics?.DoneReceived);
    }

    [Fact]
    public async Task ClientReportsProviderStreamErrorWhenStreamEndsWithoutDone()
    {
        var handler = new StubHandler("event: error\ndata: {\"message\":\"secret-token failed\"}\n\n");
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("gommo_stream_error", error.Code);
        Assert.Equal("*** failed", error.Message);
    }

    [Fact]
    public async Task ClientStillReportsMissingContentWithoutProviderError()
    {
        var handler = new StubHandler($"data: {Chunk("")}\n\ndata: [DONE]\n\n");
        var client = CreateClient(handler);

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() => client.CompleteAsync(CreateRequest()));

        Assert.Equal("missing_content", error.Code);
    }

    [Fact]
    public async Task ClientKeepsContentWhenErrorEventFollowsAssistantContent()
    {
        var handler = new StubHandler(
            $"data: {Chunk("{\"title\":\"ok\"}")}\n\n" +
            "event: error\ndata: {\"message\":\"late\",\"error_code\":\"CHAT_STREAM_ERROR\"}\n\n" +
            "data: [DONE]\n\n");
        var client = CreateClient(handler);

        var result = await client.CompleteAsync(CreateRequest());

        Assert.Equal("{\"title\":\"ok\"}", result.Content);
    }

    private static ServicePrompt79AiClient CreateClient(StubHandler handler)
        => new(
            new HttpClient(handler),
            new StaticCredentialResolver("secret-token"),
            Options.Create(new ServicePromptAssistantOptions { TimeoutSeconds = 30 }));

    private static ServicePromptProviderRequest CreateRequest()
        => new("https://api.gommo.net/api/v2/chat", "gommo_agent", "base-123", "make demo");

    private static string Chunk(string content) => System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content } } } });

    private sealed class StaticCredentialResolver(string secret) : IProviderCredentialResolver
    {
        public (string ProviderCode, string CredentialRole)? LastResolve { get; private set; }

        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
        {
            LastResolve = (providerCode, credentialRole);
            return Task.FromResult(new ResolvedProviderCredential { ProviderAccountId = Guid.NewGuid(), ProviderCode = providerCode, CredentialRole = credentialRole, Secret = secret });
        }
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public System.Text.Json.JsonDocument? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal("secret-token", request.Headers.GetValues("Gommo-Token").Single());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/event-stream") };
        }
    }
}
