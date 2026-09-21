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
        Assert.DoesNotContain("secret-token", result.SanitizedRawResponse);
        Assert.Equal(("79ai", "access_token"), credentials.LastResolve);
        Assert.Equal("base-123", handler.RequestBody!.RootElement.GetProperty("agent_id").GetString());
        Assert.NotEqual(handler.RequestBody.RootElement.GetProperty("user_message_id").GetString(), handler.RequestBody.RootElement.GetProperty("assistant_message_id").GetString());
    }

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
