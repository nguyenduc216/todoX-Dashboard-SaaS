using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services;
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
    public void SelectedCompletedImageVersionIsAcceptedAndGuidEmptyIsRejected()
    {
        var method = typeof(SceneVideoRenderHandler).GetMethod("IsCompletedSelectedImageVersion", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.True((bool)method!.Invoke(null, new object[]
        {
            new SceneImageVersionDto
            {
                Id = Guid.NewGuid(),
                IsSelected = true,
                Status = "completed"
            }
        })!);
        Assert.False((bool)method.Invoke(null, new object[]
        {
            new SceneImageVersionDto
            {
                Id = Guid.Empty,
                IsSelected = true,
                Status = "completed"
            }
        })!);
        Assert.False((bool)method.Invoke(null, new object[]
        {
            new SceneImageVersionDto
            {
                Id = Guid.NewGuid(),
                IsSelected = true,
                Status = "processing"
            }
        })!);
    }

    [Fact]
    public void SceneVideoWorkItemInputSerializesSourceImageVersionIdContract()
    {
        var sourceImageVersionId = Guid.Parse("70a7d49f-62b8-402a-8cc7-9b743af0ecda");
        var json = JsonSerializer.Serialize(new SceneVideoRenderWorkItemInput
        {
            SourceImageVersionId = sourceImageVersionId,
            SelectedSourceImageVersionId = sourceImageVersionId,
            ImageInputMode = VideoSceneImageInputMode.SceneSource
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(sourceImageVersionId, doc.RootElement.GetProperty("sourceImageVersionId").GetGuid());
        Assert.Equal(sourceImageVersionId, doc.RootElement.GetProperty("selectedSourceImageVersionId").GetGuid());
        Assert.Equal((int)VideoSceneImageInputMode.SceneSource, doc.RootElement.GetProperty("imageInputMode").GetInt32());
    }

    [Fact]
    public void ReferenceOnlyPromptGuardAppendsOnce()
    {
        var prompt = "A worker enters the construction site, camera follows from a low angle.";

        var guarded = RVideoReferenceOnlyPromptGuard.Apply(prompt, useSharedReferenceImage: true);
        var guardedAgain = RVideoReferenceOnlyPromptGuard.Apply(guarded, useSharedReferenceImage: true);
        var unchanged = RVideoReferenceOnlyPromptGuard.Apply(prompt, useSharedReferenceImage: false);

        Assert.Contains("reference image only for character/identity consistency", guarded);
        Assert.Contains("Do not use the reference image as the opening frame", guarded);
        Assert.Contains("Start the video directly in the scene", guarded);
        Assert.Equal(guarded, guardedAgain);
        Assert.Equal(prompt, unchanged);
    }

    [Fact]
    public async Task RVideo79AiReferenceOnlySubmitOmitsImagesOption()
    {
        var client = new CapturingAi79TaskClient();
        var service = Create79AiVideoService(client);

        await service.SubmitAsync(new RVideo79AiVideoSubmitRequest(
            Create79AiRuntime(),
            RVideoVideoModelPolicy.GetInitial(),
            RVideoReferenceOnlyPromptGuard.Apply("Open directly on the described scene.", useSharedReferenceImage: true),
            "9:16",
            "720p",
            6,
            SourceImageAsset: null));

        Assert.NotNull(client.LastSubmit);
        Assert.DoesNotContain("images", client.LastSubmit!.Options.Keys);
        Assert.Contains("Do not use the reference image as the opening frame", client.LastSubmit.Prompt);
    }

    [Fact]
    public async Task RVideo79AiSceneSourceSubmitKeepsImagesOption()
    {
        var client = new CapturingAi79TaskClient();
        var service = Create79AiVideoService(client);

        await service.SubmitAsync(new RVideo79AiVideoSubmitRequest(
            Create79AiRuntime(),
            RVideoVideoModelPolicy.GetInitial(),
            "Animate the generated scene image.",
            "9:16",
            "720p",
            6,
            new RVideo79AiProviderImageAsset(
                "scene-image-base",
                "project-1",
                "https://example.test/generated-scene.png",
                "generated-scene.png",
                """{"ok":true}""")));

        Assert.NotNull(client.LastSubmit);
        Assert.True(client.LastSubmit!.Options.TryGetValue("images", out var imagesJson));
        Assert.Contains("generated-scene.png", imagesJson);
    }

    [Fact]
    public async Task ExplicitSourceImageVersionWinsOverCurrentSelected()
    {
        var explicitVersionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var currentSelectedVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var worker = CreateWorker(
            new SceneImageVersionDto
            {
                Id = currentSelectedVersionId,
                IsSelected = true,
                Status = "completed",
                PublicUrl = "https://example.test/current.png",
                StorageKey = "scene/current.png"
            },
            new[]
            {
                new SceneImageVersionDto
                {
                    Id = explicitVersionId,
                    IsSelected = false,
                    Status = "completed",
                    PublicUrl = "https://example.test/explicit.png",
                    StorageKey = "scene/explicit.png"
                },
                new SceneImageVersionDto
                {
                    Id = currentSelectedVersionId,
                    IsSelected = true,
                    Status = "completed",
                    PublicUrl = "https://example.test/current.png",
                    StorageKey = "scene/current.png"
                }
            });

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            explicitVersionId,
            false,
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.NotNull(version);
        Assert.Equal(explicitVersionId, version!.Id);
        Assert.False(version.IsSelected);
    }

    [Fact]
    public async Task MissingExplicitSourceImageVersionDoesNotFallback()
    {
        var worker = CreateWorker(
            new SceneImageVersionDto
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                IsSelected = true,
                Status = "completed",
                PublicUrl = "https://example.test/current.png",
                StorageKey = "scene/current.png"
            },
            new[]
            {
                new SceneImageVersionDto
                {
                    Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    IsSelected = true,
                    Status = "completed",
                    PublicUrl = "https://example.test/current.png",
                    StorageKey = "scene/current.png"
                }
            });

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            false,
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.Null(version);
    }

    [Fact]
    public async Task LegacyMissingSourceImageVersionFallsBackToCurrentSelected()
    {
        var worker = CreateWorker(
            new SceneImageVersionDto
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                IsSelected = true,
                Status = "completed",
                PublicUrl = "https://example.test/current.png",
                StorageKey = "scene/current.png"
            });

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            null,
            false,
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.NotNull(version);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), version!.Id);
    }

    [Fact]
    public async Task ExplicitVersionMustBeCompleted()
    {
        var worker = CreateWorker(
            new SceneImageVersionDto
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                IsSelected = true,
                Status = "completed",
                PublicUrl = "https://example.test/current.png",
                StorageKey = "scene/current.png"
            },
            new[]
            {
                new SceneImageVersionDto
                {
                    Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    IsSelected = false,
                    Status = "processing",
                    PublicUrl = "https://example.test/processing.png",
                    StorageKey = "scene/processing.png"
                },
                new SceneImageVersionDto
                {
                    Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    IsSelected = true,
                    Status = "completed",
                    PublicUrl = "https://example.test/current.png",
                    StorageKey = "scene/current.png"
                }
            });

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            false,
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.Null(version);
    }

    [Fact]
    public async Task WorkerFallsBackToCurrentSelectedCompletedImageVersion()
    {
        var worker = CreateWorker(new SceneImageVersionDto
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            IsSelected = true,
            Status = "completed",
            PublicUrl = "https://example.test/image.png",
            StorageKey = "scene/image.png"
        });

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            null,
            false,
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.NotNull(version);
        Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), version!.Id);
        Assert.Equal("completed", version.Status);
    }

    [Fact]
    public async Task WorkerRejectsNonCompletedSelectedImageVersion()
    {
        var worker = CreateWorker(new SceneImageVersionDto
        {
            Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            IsSelected = true,
            Status = "processing",
            PublicUrl = "https://example.test/image.png",
            StorageKey = "scene/image.png"
        });

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            null,
            false,
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.Null(version);
    }

    [Fact]
    public async Task SceneVideoVersionCreateRejectsGuidEmptySourceImageVersionId()
    {
        var service = CreateSceneMediaVersioningService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateQueuedSceneVideoVersionAsync(
            new SceneVideoVersionCreateRequest(
                22,
                120,
                Guid.Empty,
                null,
                null,
                null,
                "logical-request",
                null,
                null,
                new { sceneId = 120 },
                new { mode = "test" }),
            CancellationToken.None));

        Assert.Equal("RVIDEO_VIDEO_SOURCE_IMAGE_VERSION_GUID_EMPTY", ex.Message);
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

    private static RVideo79AiVideoService Create79AiVideoService(CapturingAi79TaskClient client)
    {
#pragma warning disable SYSLIB0050
        var service = (RVideo79AiVideoService)FormatterServices.GetUninitializedObject(typeof(RVideo79AiVideoService));
#pragma warning restore SYSLIB0050
        typeof(RVideo79AiVideoService)
            .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(service, client);
        return service;
    }

    private static RVideo79AiRuntime Create79AiRuntime()
        => new(
            18,
            99,
            "79ai",
            "https://example.test/ai",
            "/create-video",
            "/video",
            "/image-upload",
            "79ai.net",
            "project-1",
            new ResolvedProviderCredential
            {
                ProviderAccountId = Guid.NewGuid(),
                ProviderCode = "79ai",
                CredentialRole = "access_token",
                Secret = "test-token"
            },
            null,
            null,
            42);

    private static SceneVideoWorkerHandler CreateWorker(
        SceneImageVersionDto? selectedImageVersion,
        IReadOnlyList<SceneImageVersionDto>? imageVersions = null)
    {
#pragma warning disable SYSLIB0050
        var handler = (SceneVideoWorkerHandler)FormatterServices.GetUninitializedObject(typeof(SceneVideoWorkerHandler));
#pragma warning restore SYSLIB0050
        var versionsProxy = DispatchProxy.Create<ISceneMediaVersioningService, SceneMediaVersioningServiceProxy>();
        var proxy = (SceneMediaVersioningServiceProxy)(object)versionsProxy;
        proxy.SelectedImageVersion = selectedImageVersion;
        proxy.ImageVersions = imageVersions ?? Array.Empty<SceneImageVersionDto>();

        typeof(SceneVideoWorkerHandler)
            .GetField("_versions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(handler, versionsProxy);

        return handler;
    }

    private static SceneMediaVersioningService CreateSceneMediaVersioningService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TodoXSaaS"] = "Host=127.0.0.1;Database=todox_test;Username=test;Password=test",
                ["TodoX:TenantId"] = "11111111-1111-1111-1111-111111111111"
            })
            .Build();

        return new SceneMediaVersioningService(
            new TodoXConnectionFactory(configuration),
            new TenantContext(new TodoXConnectionFactory(configuration), configuration));
    }

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

    public class SceneMediaVersioningServiceProxy : DispatchProxy
    {
        public SceneImageVersionDto? SelectedImageVersion { get; set; }
        public IReadOnlyList<SceneImageVersionDto> ImageVersions { get; set; } = Array.Empty<SceneImageVersionDto>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISceneMediaVersioningService.GetSelectedImageVersionAsync))
            {
                return Task.FromResult(SelectedImageVersion);
            }

            if (targetMethod?.Name == nameof(ISceneMediaVersioningService.ListImageVersionsAsync)
                && targetMethod.GetParameters().Length == 4)
            {
                return Task.FromResult(ImageVersions);
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }
}
