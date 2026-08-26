using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.DanceSell;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class DanceSellAi79ReferenceProviderTests
{
    private const string ExpectedPrompt = """
VIRTUAL TRY-ON – PREVIEW ONLY

Use IMAGE 1 as FIXED BASE BODY.
- Preserve exact body pose, limb angles, shoulder alignment, head tilt, camera angle
- Do NOT regenerate body, do NOT reinterpret pose
- Only replace clothing region

Apply clothing from IMAGE 2 with exact design, color, texture, pattern
- Clothing must conform to existing body pose
- No pose correction, no body adjustment, no camera shift

If conflict occurs between clothing and pose:
→ Prioritize BODY POSE from IMAGE 1 over clothing realism

Photorealistic, product preview quality.
""";

    [Fact]
    public async Task SubmitAsync_UsesVerifiedFashionTryOnFormPayload()
    {
        var client = new CapturingAi79TaskClient();
        var provider = new Ai79DanceSellReferenceProvider(
            client,
            new StaticCredentialResolver(),
            NullLogger<Ai79DanceSellReferenceProvider>.Instance);

        var result = await provider.SubmitAsync(new DanceSellReferenceProviderRequest
        {
            Route = new DanceSellProviderRouteDto
            {
                ProviderCode = "79ai",
                ModelName = "seedream_5_0",
                ConfigJson = "{}"
            },
            Prompt = "must be ignored",
            CharacterImageUrl = "https://cdn.example/model.png",
            ProductImageUrl = "https://cdn.example/product.png"
        }, CancellationToken.None);

        var request = Assert.IsType<Ai79TaskSubmitRequest>(client.LastRequest);
        Assert.Equal("79ai.net", request.Domain);
        Assert.Equal("imagegen_2_0", request.Model);
        Assert.Equal(ExpectedPrompt, request.Prompt);
        Assert.Empty(request.Images);
        Assert.Null(request.FirstImageField);
        Assert.Null(request.SecondImageField);
        Assert.Equal("create", request.Options["action_type"]);
        Assert.Equal("false", request.Options["sync"]);
        Assert.Equal("default", request.Options["project_id"]);
        Assert.Equal("16:9", request.Options["ratio"]);
        Assert.Equal("FASHION", request.Options["category"]);
        Assert.Equal("1k", request.Options["resolution"]);
        Assert.Equal("low", request.Options["mode"]);
        Assert.Equal("1", request.Options["num_outputs"]);
        Assert.Equal("VI", request.Options["language"]);
        Assert.Equal("https://cdn.example/model.png", request.Options["subjects[0][url]"]);
        Assert.Equal("https://cdn.example/product.png", request.Options["subjects[1][url]"]);
        Assert.DoesNotContain("base64Image", request.Options.Keys, StringComparer.Ordinal);
        Assert.DoesNotContain("image_2", request.Options.Keys, StringComparer.Ordinal);

        Assert.Equal("imagegen_2_0", result.ModelName);
        Assert.Contains("\"prompt\":\"VIRTUAL TRY-ON", result.RequestJson, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"https://cdn.example/model.png\"", result.RequestJson, StringComparison.Ordinal);
        Assert.Contains("\"url\":\"https://cdn.example/product.png\"", result.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("must be ignored", result.RequestJson, StringComparison.Ordinal);
        Assert.Equal("""{"imageInfo":{"id_base":"try-on-001"}}""", result.ResponseJson);
    }


    [Fact]
    public async Task SubmitAsync_PersonOnlyOmitsProductReference()
    {
        var client = new CapturingAi79TaskClient();
        var provider = new Ai79DanceSellReferenceProvider(
            client,
            new StaticCredentialResolver(),
            NullLogger<Ai79DanceSellReferenceProvider>.Instance);

        await provider.SubmitAsync(new DanceSellReferenceProviderRequest
        {
            Route = new DanceSellProviderRouteDto { ProviderCode = "79ai", ModelName = "imagegen_2_0", ConfigJson = "{}" },
            CharacterImageUrl = "https://cdn.example/model.png"
        }, CancellationToken.None);

        var request = Assert.IsType<Ai79TaskSubmitRequest>(client.LastRequest);
        Assert.Equal("https://cdn.example/model.png", request.Options["subjects[0][url]"]);
        Assert.DoesNotContain("subjects[1][url]", request.Options.Keys, StringComparer.Ordinal);
        Assert.Contains("PERSON ONLY REFERENCE IMAGE", request.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("IMAGE 2", request.Prompt, StringComparison.Ordinal);
    }
    private sealed class CapturingAi79TaskClient : IAi79TaskClient
    {
        public Ai79TaskSubmitRequest? LastRequest { get; private set; }

        public Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new Ai79TaskSubmitResult("try-on-001", """{"imageInfo":{"id_base":"try-on-001"}}"""));
        }

        public Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Ai79ProviderMediaListResult> ListImagesAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
            => Task.FromResult(new Ai79ProviderMediaListResult(Array.Empty<Ai79ProviderMediaItem>(), "{}"));

        public Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
            => Task.FromResult(new Ai79ProviderMediaListResult(Array.Empty<Ai79ProviderMediaItem>(), "{}"));

        public Task<Ai79TaskStatusResult> GetStatusAsync(Ai79TaskStatusRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class StaticCredentialResolver : IProviderCredentialResolver
    {
        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential
            {
                ProviderAccountId = Guid.NewGuid(),
                ProviderCode = providerCode,
                CredentialRole = credentialRole,
                Secret = "secret-token",
                MaskedHint = "****oken"
            });
    }
}
