using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoVideoHotfixTests
{
    [Fact]
    public void RVideoVideoPolicyIs79AiOnly()
    {
        Assert.All(RVideoVideoModelPolicy.Models, model =>
        {
            Assert.Equal(RVideoVideoModelPolicy.ProviderCode, model.ProviderCode);
        });
        Assert.Equal("veo_omni", RVideoVideoModelPolicy.GetInitial().Model);
        Assert.Equal("flash", RVideoVideoModelPolicy.GetInitial().Mode);
        Assert.True(RVideoVideoModelPolicy.Is79AiProvider("79ai"));
        Assert.True(RVideoVideoModelPolicy.Is79AiProvider("79ai_video"));
        Assert.False(RVideoVideoModelPolicy.Is79AiProvider("yescale_task_video"));
        Assert.Equal(4, RVideoVideoModelPolicy.Models.Count);
        Assert.Null(RVideoVideoModelPolicy.GetNext(3));
    }

    [Fact]
    public void BuildAttemptLogicalRequestIdKeepsAttemptZeroStable()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("BuildAttemptLogicalRequestId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal("base", method!.Invoke(null, new object[] { "base", 0 }));
        Assert.Equal("base-fallback-2", method.Invoke(null, new object[] { "base", 2 }));
    }

    [Fact]
    public void ResolveNextAttemptIndexReusesActiveAttemptAndSkipsFailedOne()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveNextAttemptIndex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var active = new[]
        {
            new SceneVideoVersionDto { LogicalRequestId = "scene-base", Status = "submitted" }
        };
        var failed = new[]
        {
            new SceneVideoVersionDto { LogicalRequestId = "scene-base", Status = "failed" }
        };
        var fallback = new[]
        {
            new SceneVideoVersionDto { LogicalRequestId = "scene-base", Status = "failed" },
            new SceneVideoVersionDto { LogicalRequestId = "scene-base-fallback-1", Status = "failed" }
        };

        Assert.Equal(0, method!.Invoke(null, new object[] { "scene-base", active }));
        Assert.Equal(1, method.Invoke(null, new object[] { "scene-base", failed }));
        Assert.Equal(2, method.Invoke(null, new object[] { "scene-base", fallback }));
    }

    [Fact]
    public void BuildUsageMetadataCarriesAttemptLogicalRequestId()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("BuildUsageMetadata", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var input = new SceneVideoRenderWorkItemInput
        {
            ParentJobId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ProjectId = 42,
            SceneId = 7,
            SceneIndex = 3,
            CustomerId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            DurationSeconds = 12,
            AspectRatio = "9:16",
            Resolution = "720P",
            EstimatedUsd = 1.25m,
            CostSource = "configured_tariff",
            PricingMode = "fixed",
            PricingRuleKey = "rule-1"
        };

        var json = (string)method!.Invoke(null, new object[] { input, "scene-base-fallback-1", "task-123", "{\"ok\":true}", 9.5m })!;
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("scene-base-fallback-1", doc.RootElement.GetProperty("logicalRequestId").GetString());
        Assert.Equal("task-123", doc.RootElement.GetProperty("providerTaskId").GetString());
    }

    [Fact]
    public void VideoRenderPricingUsesPositiveCapabilityTariffBeforeUnitCostFallback()
    {
        var resolver = new VideoRenderPricingResolver();
        var option = new ProviderOptionDto
        {
            ProviderId = 18,
            ProviderCapabilityId = 99,
            ProviderCode = "79ai",
            CapabilityCode = RVideoVideoModelPolicy.CapabilityCode,
            ModelName = "veo_omni",
            UnitCostPoints = 0
        };
        var capability = new AiProviderCapabilityDto
        {
            Id = option.ProviderCapabilityId,
            ProviderId = option.ProviderId,
            ProviderCode = option.ProviderCode,
            CapabilityCode = option.CapabilityCode,
            ConfigJson = """
                {
                  "pricing": {
                    "rules": [
                      {
                        "match": { "model": "veo_omni", "mode": "flash", "duration": 6 },
                        "chargedPoints": 42,
                        "costSource": "catalog_todox_ai_model_price"
                      }
                    ]
                  }
                }
                """
        };

        var resolved = resolver.Resolve(option, capability, RVideoVideoModelPolicy.GetInitial(), "9:16", "720p", 6);

        Assert.Equal(42, resolved.ChargedPoints);
        Assert.Equal("catalog_todox_ai_model_price", resolved.CostSource);
    }

    [Fact]
    public void TrustedBackgroundCustomerPayerResolvesWithoutHttpSession()
    {
        var customerId = Guid.NewGuid();
        var payer = AiBillingPayerResolver.ResolveCore(null, new AiBillingPayerResolveRequest(
            customerId,
            UserId: null,
            FeatureCode: "render_job_scene_video",
            CapabilityCode: RVideoVideoModelPolicy.CapabilityCode,
            Metadata: null,
            TrustedContext: new AiBillingTrustedPayerContext(
                AiBillingPayerTypes.Customer,
                customerId,
                UserId: null,
                SystemWalletCode: null,
                Source: "background_job")));

        Assert.Equal(AiBillingPayerTypes.Customer, payer.PayerType);
        Assert.Equal(customerId, payer.PayerCustomerId);
        Assert.Equal("background_job", payer.ResolutionSource);
    }

    [Fact]
    public async Task RVideoUploadImageRenderSubmitsReferenceWithoutCharacterId()
    {
        var client = new CapturingAi79TaskClient();
        var service = Create79AiImageService(client);

        var response = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Model = RVideoImageModelPolicy.GetInitial().Model,
            RequestedModel = RVideoImageModelPolicy.GetInitial().Model,
            Prompt = "scene prompt",
            AspectRatio = "9:16",
            ReferenceImageBase64 = "data:image/jpeg;base64,abc123"
        });

        Assert.Equal(AiProviderExecutionState.Pending, response.ExecutionState);
        Assert.NotNull(client.LastSubmit);
        Assert.Equal("true", client.LastSubmit!.Options["editImage"]);
        Assert.Equal("data:image/jpeg;base64,abc123", client.LastSubmit.Options["base64Image"]);
    }

    [Fact]
    public async Task RVideoNoneImageRenderSubmitsTextToImageWithoutReference()
    {
        var client = new CapturingAi79TaskClient();
        var service = Create79AiImageService(client);

        var response = await service.GenerateImageAsync(new OpenRouterImageRequest
        {
            Model = RVideoImageModelPolicy.GetInitial().Model,
            RequestedModel = RVideoImageModelPolicy.GetInitial().Model,
            Prompt = "scene prompt",
            AspectRatio = "9:16"
        });

        Assert.Equal(AiProviderExecutionState.Pending, response.ExecutionState);
        Assert.NotNull(client.LastSubmit);
        Assert.Equal("false", client.LastSubmit!.Options["editImage"]);
        Assert.DoesNotContain("base64Image", client.LastSubmit.Options.Keys);
    }

    private static Gommo79AiImageService Create79AiImageService(CapturingAi79TaskClient client)
        => new(
            client,
            new StaticCredentialResolver(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TimelapseProviderWorkers:Default79AiBaseUrl"] = "https://example.test/ai"
            }).Build(),
            NullLogger<Gommo79AiImageService>.Instance);

    private sealed class StaticCredentialResolver : IProviderCredentialResolver
    {
        public Task<ResolvedProviderCredential> ResolveAsync(string providerCode, string credentialRole, CancellationToken ct = default)
            => Task.FromResult(new ResolvedProviderCredential
            {
                ProviderAccountId = Guid.NewGuid(),
                ProviderCode = providerCode,
                CredentialRole = credentialRole,
                Secret = "test-token"
            });
    }

    private sealed class CapturingAi79TaskClient : IAi79TaskClient
    {
        public Ai79TaskSubmitRequest? LastSubmit { get; private set; }

        public Task<Ai79TaskSubmitResult> SubmitAsync(Ai79TaskSubmitRequest request, CancellationToken ct = default)
        {
            LastSubmit = request;
            return Task.FromResult(new Ai79TaskSubmitResult("task-123", """{"id":"task-123"}"""));
        }

        public Task<Ai79TaskSubmitResult> SubmitMultipartAsync(Ai79MultipartTaskSubmitRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79MediaUploadResult> UploadMediaAsync(Ai79MediaUploadRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79ProviderMediaListResult> ListImagesAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79ProviderMediaListResult> ListVideosAsync(Ai79ProviderMediaListRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79TaskSubmitResult> SubmitMotionControlAsync(Ai79MotionControlSubmitRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79ImageUploadResult> UploadImageAsync(Ai79ImageUploadRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Ai79TaskStatusResult> GetStatusAsync(Ai79TaskStatusRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
