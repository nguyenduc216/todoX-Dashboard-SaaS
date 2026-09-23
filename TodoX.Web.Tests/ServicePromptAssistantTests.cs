using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.PromptAssistant;
using TodoX.Web.Services.VideoRender;
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
        Assert.Equal("generated_json_invalid", error.Code);
    }

    [Fact]
    public void CanonicalJsonSerializationWritesVietnameseUnicodeWithoutChangingValues()
    {
        const string escapedJson = """
            {"voice":"\u0110\u1eb7c bi\u1ec7t ng\u01b0\u1eddi Vi\u1ec7t Nam","image_prompt":"Nh\u00e2n v\u1eadt \u0111\u1ee9ng gi\u1eefa con \u0111\u01b0\u1eddng","subtitle_lines":["\u0110\u00e3 s\u1ed1ng"]}
            """;

        var canonical = ServicePromptJson.Canonicalize(escapedJson);
        using var document = JsonDocument.Parse(canonical);

        Assert.Contains("Đặc biệt người Việt Nam", canonical, StringComparison.Ordinal);
        Assert.Contains("Nhân vật đứng giữa con đường", canonical, StringComparison.Ordinal);
        Assert.Contains("Đã sống", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0110", canonical, StringComparison.Ordinal);
        Assert.Equal("Đặc biệt người Việt Nam", document.RootElement.GetProperty("voice").GetString());
        Assert.Equal("Nhân vật đứng giữa con đường", document.RootElement.GetProperty("image_prompt").GetString());
        Assert.Equal("Đã sống", document.RootElement.GetProperty("subtitle_lines")[0].GetString());
    }

    [Fact]
    public void CanonicalJsonSerializationDoesNotDecodeLiteralDoubleEscapedUnicode()
    {
        const string doubleEscapedJson = """{"voice":"\\u0110\\u1eb7c"}""";

        var canonical = ServicePromptJson.Canonicalize(doubleEscapedJson);
        using var document = JsonDocument.Parse(canonical);

        Assert.Equal("\\u0110\\u1eb7c", document.RootElement.GetProperty("voice").GetString());
        Assert.Contains("\\\\u0110", canonical, StringComparison.Ordinal);
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
    public void OptionsUseTheConfiguredGommoEndpointAndLimits()
    {
        var options = new ServicePromptAssistantOptions();
        Assert.Equal("https://api.gommo.net/api/v2/chat", options.ApiUrl);
        Assert.Equal(TimeSpan.FromSeconds(300), options.Timeout);
        Assert.Equal(2_000_000, options.TemplateLimit);
        Assert.Equal(1_000_000, options.DescriptionLimit);
    }

    [Fact]
    public void GenerationPersistenceSqlCastsAllJsonbColumnsAndAllowsNullableGeneratedJson()
    {
        var sql = ServicePromptGenerationPersistenceContract.InsertSql;

        Assert.Contains("CAST(@RequestSnapshot AS jsonb)", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(@GeneratedJson AS jsonb)", sql, StringComparison.Ordinal);
        Assert.Contains("CAST(@ValidationErrors AS jsonb)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@RequestSnapshot, @RawResponse", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@GeneratedJson, @ValidationStatus", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationPersistenceAcceptsJsonbCompatibleSnapshotErrorsAndNullableGeneratedJson()
    {
        var model = CreatePersistence(
            requestSnapshot: """{"providerCode":"79ai","modelCode":"configured-model"}""",
            generatedJson: null,
            validationErrors: "[]");

        ServicePromptGenerationPersistenceContract.Validate(model);
    }

    [Fact]
    public void GenerationPersistenceAcceptsGeneratedJsonAndMultipleValidationErrors()
    {
        var model = CreatePersistence(
            requestSnapshot: """{"providerCode":"79ai","modelCode":"configured-model"}""",
            generatedJson: """{"title":"ok","scenes":[]}""",
            validationErrors: """[{"path":"$.scenes[0].motion","code":"missing_field","message":"motion is required"},{"path":"$.title","code":"empty","message":"title is required"}]""");

        ServicePromptGenerationPersistenceContract.Validate(model);
    }

    [Fact]
    public void GenerationPersistenceCarriesProviderTotalDurationToTheMetricsColumn()
    {
        var model = CreatePersistence(requestSnapshot: "{}", totalDurationMs: 321);

        ServicePromptGenerationPersistenceContract.Validate(model);

        Assert.Equal(321, model.TotalDurationMs);
        Assert.Contains("total_duration_ms", ServicePromptGenerationPersistenceContract.InsertSql, StringComparison.Ordinal);
        Assert.Contains("@TotalDurationMs", ServicePromptGenerationPersistenceContract.InsertSql, StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationPersistenceRejectsMalformedJsonBeforeInsert()
    {
        var malformedSnapshot = CreatePersistence(requestSnapshot: "{broken");
        var malformedErrors = CreatePersistence(validationErrors: "{broken");
        var malformedGenerated = CreatePersistence(generatedJson: "{broken");

        Assert.Throws<ArgumentException>(() => ServicePromptGenerationPersistenceContract.Validate(malformedSnapshot));
        Assert.Throws<ArgumentException>(() => ServicePromptGenerationPersistenceContract.Validate(malformedErrors));
        Assert.Throws<ArgumentException>(() => ServicePromptGenerationPersistenceContract.Validate(malformedGenerated));
    }

    [Fact]
    public void ReferenceEnrichmentLeavesPromptUnchangedWithoutUsableReference()
    {
        const string prompt = """{"scenes":[{"image_prompt":"A person in a store."}]}""";

        var noCharacterResult = VideoPromptReferenceEnricher.Enrich(prompt, new RVideoJobSettingsDto
        {
            SkipCharacter = true,
            CharacterMode = RVideoCharacterModes.None
        });
        var missingUploadResult = VideoPromptReferenceEnricher.Enrich(prompt, new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Upload
        });

        Assert.Equal(prompt, noCharacterResult);
        Assert.Equal(prompt, missingUploadResult);
    }

    [Fact]
    public void ReferenceEnrichmentPreservesVietnameseUnicodeInImagePrompt()
    {
        const string prompt = "{\"scenes\":[{\"image_prompt\":\"Nh\\u00e2n v\\u1eadt \\u0111\\u1ee9ng gi\\u1eefa con \\u0111\\u01b0\\u1eddng.\"}]}";
        var settings = new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = "{\"fileUrl\":\"https://media.example/reference.png\"}"
        };

        var result = VideoPromptReferenceEnricher.Enrich(prompt, settings);
        using var document = JsonDocument.Parse(result);
        var imagePrompt = document.RootElement.GetProperty("scenes")[0]
            .GetProperty("image_prompt").GetString();

        Assert.Contains("Nh\u00e2n v\u1eadt \u0111\u1ee9ng gi\u1eefa con \u0111\u01b0\u1eddng.", imagePrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", result, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceEnrichmentAddsInstructionToEverySceneOnlyInNormalReferenceMode()
    {
        const string prompt = """
            {"title":"Demo","scenes":[
              {"scene_index":1,"image_prompt":"A person in a store.","video_prompt":"Walk forward."},
              {"scene_index":2,"image_prompt":"A person outside.","video_prompt":"Turn around."}
            ]}
            """;
        var settings = new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """{"fileUrl":"https://media.example/reference.png"}""",
            UseReferenceImageForAllScenes = false
        };

        var result = VideoPromptReferenceEnricher.Enrich(prompt, settings);
        using var document = JsonDocument.Parse(result);
        var scenes = document.RootElement.GetProperty("scenes").EnumerateArray().ToArray();

        Assert.All(scenes, scene => Assert.Contains("Use the provided reference image", scene.GetProperty("image_prompt").GetString()));
        Assert.Equal("Walk forward.", scenes[0].GetProperty("video_prompt").GetString());
        Assert.Equal(1, scenes[0].GetProperty("scene_index").GetInt32());
        Assert.Equal(result, VideoPromptReferenceEnricher.Enrich(result, settings));
    }

    [Fact]
    public void ReferenceEnrichmentDoesNotAddAiImageInstructionForSharedReferenceMode()
    {
        const string prompt = """{"scenes":[{"image_prompt":"A person in a store."}]}""";
        var settings = new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """{"storageKey":"uploads/reference.png"}""",
            UseReferenceImageForAllScenes = true
        };

        Assert.Equal(prompt, VideoPromptReferenceEnricher.Enrich(prompt, settings));
    }

    [Theory]
    [InlineData("Use the reference image to preserve the character identity.")]
    [InlineData("MAIN CHARACTER IDENTITY is defined by the attached reference image.")]
    public void ReferenceEnrichmentPreservesExistingEquivalentInstruction(string imagePrompt)
    {
        var prompt = JsonSerializer.Serialize(new
        {
            scenes = new[] { new { image_prompt = imagePrompt } }
        });
        var settings = new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Library,
            SelectedCharacterId = 12,
            CharacterSnapshotJson = """{"masterImageUrl":"https://media.example/reference.png"}"""
        };

        var result = VideoPromptReferenceEnricher.Enrich(prompt, settings);
        using var document = JsonDocument.Parse(result);

        Assert.Equal(imagePrompt, document.RootElement.GetProperty("scenes")[0].GetProperty("image_prompt").GetString());
    }

    [Fact]
    public void ReferenceEnrichmentPreservesImportedInstructionWithoutDuplication()
    {
        const string imagePrompt = """
            REFERENCE IMAGE:
            Use the provided reference image as the main character source.

            SCENE:
            A person in a store.
            """;
        var prompt = JsonSerializer.Serialize(new { scenes = new[] { new { image_prompt = imagePrompt } } });
        var settings = new RVideoJobSettingsDto
        {
            CharacterMode = RVideoCharacterModes.Upload,
            CharacterSnapshotJson = """{"fileUrl":"https://media.example/reference.png"}"""
        };

        var result = VideoPromptReferenceEnricher.Enrich(prompt, settings);
        using var document = JsonDocument.Parse(result);
        var enriched = document.RootElement.GetProperty("scenes")[0].GetProperty("image_prompt").GetString();

        Assert.Equal(imagePrompt, enriched);
        Assert.Equal(1, enriched!.Split("REFERENCE IMAGE:", StringSplitOptions.None).Length - 1);
    }

    private static ServicePromptGenerationPersistence CreatePersistence(
        string requestSnapshot = "{}",
        string? generatedJson = """{"title":"ok"}""",
        string validationErrors = "[]",
        int? totalDurationMs = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ServiceId = Guid.NewGuid(),
            AssistantId = Guid.NewGuid(),
            TrainingVersionId = Guid.NewGuid(),
            ProviderCode = "79ai",
            ModelCode = "configured-model",
            RequestSnapshot = requestSnapshot,
            GeneratedJson = generatedJson,
            ValidationErrors = validationErrors,
            TotalDurationMs = totalDurationMs,
            CreatedAt = DateTime.UtcNow
        };

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
