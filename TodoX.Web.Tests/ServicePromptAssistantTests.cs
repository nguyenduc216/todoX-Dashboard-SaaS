using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.PromptAssistant;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class ServicePromptAssistantTests
{
    private const string Template = """
        {"title":"","scenes":[{"scene_no":1,"voice":"","motion":""}]}
        """;

    [Fact]
    public void CompilerKeepsUserRequestOutOfSystemPrompt()
    {
        var compiler = new ServicePromptCompiler();
        var systemPrompt = compiler.Compile("service=demo", "secret user request", Template, "scenes are required");

        Assert.DoesNotContain("secret user request", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("JSON TEMPLATE", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("STRUCTURE DESCRIPTION", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void OutputParserAcceptsJsonFenceAndRejectsInvalidJson()
    {
        var parser = new ServicePromptOutputParser();
        using var parsed = parser.Parse("```json\n{\"title\":\"ok\"}\n```");
        Assert.Equal("ok", parsed.RootElement.GetProperty("title").GetString());

        var error = Assert.Throws<ServicePromptProviderException>(() => parser.Parse("{broken"));
        Assert.Equal("invalid_json", error.Code);
    }

    [Fact]
    public void ValidatorChecksNestedShapeAndAllowsVariableArrayLength()
    {
        var validator = new ServicePromptStructureValidator();
        var valid = validator.Validate(
            Template,
            """{"title":"demo","scenes":[{"scene_no":1,"voice":"a","motion":"b"},{"scene_no":2,"voice":"c","motion":"d"}]}""");
        Assert.Empty(valid);

        var invalid = validator.Validate(
            Template,
            """{"title":"demo","scenes":[{"scene_no":"1","voice":"a","creative_score":4}]}""");
        Assert.Contains(invalid, x => x.Code == "wrong_type" && x.Path == "$.scenes[0].scene_no");
        Assert.Contains(invalid, x => x.Code == "missing_field" && x.Path == "$.scenes[0].motion");
        Assert.Contains(invalid, x => x.Code == "unknown_field" && x.Path == "$.scenes[0].creative_score");
    }

    [Fact]
    public void ValidatorRejectsRootMismatchAndAllowsEmptyRepresentativeArray()
    {
        var validator = new ServicePromptStructureValidator();

        var rootMismatch = validator.Validate("""{"items":[]}""", "[]");
        Assert.Contains(rootMismatch, x => x.Path == "$" && x.Code == "wrong_type");

        var emptyArray = validator.Validate("""{"items":[]}""", """{"items":[{"new_field":"allowed only when no representative exists"}]}""");
        Assert.Empty(emptyArray);
    }

    [Fact]
    public void ValidatorRejectsUnknownFieldAndWrongNestedType()
    {
        var validator = new ServicePromptStructureValidator();
        var errors = validator.Validate(
            """{"config":{"enabled":true},"items":[{"id":1}]}""",
            """{"config":{"enabled":"yes"},"items":[{"id":1,"extra":true}]}""");

        Assert.Contains(errors, x => x.Path == "$.config.enabled" && x.Code == "wrong_type");
        Assert.Contains(errors, x => x.Path == "$.items[0].extra" && x.Code == "unknown_field");
    }

    [Fact]
    public void OptionsUseTheConfigured79AiEndpointAndLimits()
    {
        var options = new ServicePromptAssistantOptions();

        Assert.Equal("https://79ai.net/api/chat/completions", options.ApiUrl);
        Assert.Equal(2_000_000, options.TemplateLimit);
        Assert.Equal(1_000_000, options.DescriptionLimit);
        Assert.Equal(TimeSpan.FromSeconds(120), options.Timeout);
    }

    [Fact]
    public async Task ProviderClientRejectsInvalidUrlAndMissingContent()
    {
        var client = new ServicePrompt79AiClient(
            new HttpClient(new RecordingJsonHandler("""{"choices":[{"message":{}}]}""")),
            new FakeCredentialResolver("secret-token"),
            Options.Create(new ServicePromptAssistantOptions()));

        var invalidUrl = await Assert.ThrowsAsync<ServicePromptProviderException>(() =>
            client.CompleteAsync(new("not-a-url", "79ai", "model", "system", "user", null, null)));
        Assert.Equal("invalid_api_url", invalidUrl.Code);

        var missingContent = await Assert.ThrowsAsync<ServicePromptProviderException>(() =>
            client.CompleteAsync(new("https://79ai.example/chat", "79ai", "model", "system", "user", null, null)));
        Assert.Equal("missing_content", missingContent.Code);
    }

    [Fact]
    public async Task ProviderClientParsesUsageAndSanitizesProviderResponse()
    {
        var handler = new RecordingJsonHandler(
            """{"id":"req-1","choices":[{"message":{"content":"{\"title\":\"ok\"}"}}],"usage":{"prompt_tokens":4,"completion_tokens":7,"total_tokens":11}}""");
        var credentials = new FakeCredentialResolver("secret-token");
        var client = new ServicePrompt79AiClient(
            new HttpClient(handler),
            credentials,
            Options.Create(new ServicePromptAssistantOptions
            {
                TimeoutSeconds = 10
            }));

        var result = await client.CompleteAsync(new(
            "https://79ai.example/v1/chat/completions",
            "79ai",
            "configured-model",
            "system",
            "user",
            null,
            null));

        Assert.Equal("{\"title\":\"ok\"}", result.Content);
        Assert.Equal(4, result.PromptTokens);
        Assert.Equal(7, result.CompletionTokens);
        Assert.Equal(11, result.TotalTokens);
        Assert.Equal("req-1", result.ProviderRequestId);
        Assert.Contains("Bearer secret-token", handler.Requests.Single().Authorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderClientMissingUsageDoesNotFail()
    {
        var handler = new RecordingJsonHandler(
            """{"choices":[{"message":{"content":"{\"title\":\"ok\"}"}}]}""");
        var client = new ServicePrompt79AiClient(
            new HttpClient(handler),
            new FakeCredentialResolver("secret-token"),
            Options.Create(new ServicePromptAssistantOptions()));

        var result = await client.CompleteAsync(new(
            "https://79ai.example/v1/chat/completions",
            "79ai",
            "configured-model",
            "system",
            "user",
            null,
            null));

        Assert.Null(result.PromptTokens);
        Assert.Null(result.CompletionTokens);
        Assert.Null(result.TotalTokens);
    }

    [Fact]
    public async Task ProviderClientMissingChoicesIsControlledAndSanitized()
    {
        var handler = new RecordingJsonHandler("""{"error":"secret-token"}""", HttpStatusCode.BadRequest);
        var client = new ServicePrompt79AiClient(
            new HttpClient(handler),
            new FakeCredentialResolver("secret-token"),
            Options.Create(new ServicePromptAssistantOptions()));

        var error = await Assert.ThrowsAsync<ServicePromptProviderException>(() =>
            client.CompleteAsync(new(
                "https://79ai.example/v1/chat/completions",
                "79ai",
                "configured-model",
                "system",
                "user",
                null,
                null)));

        Assert.Equal("http_400", error.Code);
        Assert.DoesNotContain("secret-token", error.SanitizedResponse, StringComparison.Ordinal);
    }

    private sealed class FakeCredentialResolver : IProviderCredentialResolver
    {
        private readonly string _secret;

        public FakeCredentialResolver(string secret) => _secret = secret;

        public Task<ResolvedProviderCredential> ResolveAsync(
            string providerCode,
            string credentialRole,
            CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential
            {
                ProviderCode = providerCode,
                CredentialRole = credentialRole,
                Secret = _secret
            });
    }

    private sealed class RecordingJsonHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public RecordingJsonHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        public List<(string Uri, string Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
