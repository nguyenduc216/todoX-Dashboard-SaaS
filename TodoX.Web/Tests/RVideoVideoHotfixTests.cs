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
    public void LegacySharedReferenceInputInfersSharedBaseImageMode()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveImageInputMode", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var legacyShared = new SceneVideoRenderWorkItemInput
        {
            UseSharedReferenceImage = true,
            ImageInputMode = VideoSceneImageInputMode.LegacySelectedSource
        };
        var legacySceneSource = new SceneVideoRenderWorkItemInput
        {
            UseSharedReferenceImage = false,
            ImageInputMode = VideoSceneImageInputMode.LegacySelectedSource
        };

        Assert.Equal(VideoSceneImageInputMode.SharedBaseImage, method!.Invoke(null, new object[] { legacyShared }));
        Assert.Equal(VideoSceneImageInputMode.SceneSource, method.Invoke(null, new object[] { legacySceneSource }));
    }

    [Fact]
    public void LegacyReferenceOnlyInputInfersSharedBaseImageMode()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveImageInputMode", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var legacyReferenceOnly = new SceneVideoRenderWorkItemInput
        {
            UseSharedReferenceImage = true,
            ImageInputMode = VideoSceneImageInputMode.ReferenceOnly
        };

        Assert.Equal(VideoSceneImageInputMode.SharedBaseImage, method!.Invoke(null, new object[] { legacyReferenceOnly }));
    }

    [Fact]
    public void ResolveFallbackCandidatesDropsCatalogRowsWithoutDurationContract()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveFallbackCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var input = new SceneVideoRenderWorkItemInput
        {
            ProviderCode = "79ai",
            DurationSeconds = 4
        };
        var catalog = new[]
        {
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "veo_omni",
                MediaType = "video",
                Enabled = true,
                IsDeprecated = false,
                SupportedModes = ["flash"],
                SupportedDurations = [4, 6, 8, 10]
            },
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "veo_3_1",
                MediaType = "video",
                Enabled = true,
                IsDeprecated = false,
                SupportedModes = ["fast", "lite", "quality"],
                SupportedDurations = []
            },
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "grok_video_heavy",
                MediaType = "video",
                Enabled = true,
                IsDeprecated = false,
                SupportedModes = []
            }
        };

        var resolved = (System.Collections.IEnumerable)method!.Invoke(null, new object[] { input, catalog })!;
        var policies = resolved.Cast<object>()
            .Select(item => item.GetType().GetProperty("Policy")!.GetValue(item)!)
            .Select(policy => (string)policy.GetType().GetProperty("Model")!.GetValue(policy)!)
            .ToArray();

        Assert.Equal(["veo_omni"], policies);
    }

    [Fact]
    public void ResolveFallbackCandidatesUsesSafeIntersectionAcrossKnownDurations()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveFallbackCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var input = new SceneVideoRenderWorkItemInput
        {
            ProviderCode = "79ai",
            DurationSeconds = 4
        };
        var catalog = new[]
        {
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "veo_omni",
                MediaType = "video",
                Enabled = true,
                IsDeprecated = false,
                SupportedModes = ["flash"],
                SupportedDurations = [4, 6, 8, 10]
            },
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "veo_3_1",
                MediaType = "video",
                Enabled = true,
                IsDeprecated = false,
                SupportedModes = ["fast", "lite", "quality"],
                SupportedDurations = [6, 10, 12, 15]
            }
        };

        var resolved = ((System.Collections.IEnumerable)method!.Invoke(null, new object[] { input, catalog })!)
            .Cast<object>()
            .Select(item =>
            {
                var policy = item.GetType().GetProperty("Policy")!.GetValue(item)!;
                return new
                {
                    Model = (string)policy.GetType().GetProperty("Model")!.GetValue(policy)!,
                    Mode = (string?)policy.GetType().GetProperty("Mode")!.GetValue(policy),
                    Duration = (int)item.GetType().GetProperty("ProviderDurationSeconds")!.GetValue(item)!
                };
            })
            .ToArray();

        Assert.Equal(3, resolved.Length);
        Assert.All(resolved, item => Assert.Equal(6, item.Duration));
        Assert.Equal(["veo_omni", "veo_3_1", "veo_3_1"], resolved.Select(x => x.Model));
    }

    [Fact]
    public void ResolveProviderDurationRoundsUpWithinSafeIntersection()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveProviderDuration", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var safeDurations = new HashSet<int> { 6, 10 };
        var resolved = method!.Invoke(null, new object[] { 4, safeDurations });

        Assert.Equal(6, resolved);
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
    public void SharedBasePromptGuardLocksVisualSetupAndAppendsOnce()
    {
        var prompt = "A worker enters the construction site, camera follows from a low angle.";

        var guarded = RVideoReferenceOnlyPromptGuard.Apply(prompt, useSharedReferenceImage: true);
        var guardedAgain = RVideoReferenceOnlyPromptGuard.Apply(guarded, useSharedReferenceImage: true);
        var unchanged = RVideoReferenceOnlyPromptGuard.Apply(prompt, useSharedReferenceImage: false);

        Assert.Contains("same exact person", guarded);
        Assert.Contains("same exact outfit", guarded);
        Assert.Contains("background, room/set", guarded);
        Assert.Contains("products, props, furniture, layout, lighting", guarded);
        Assert.Contains("camera framing", guarded);
        Assert.Contains("Animate only the subject's natural movements, expressions, gestures, speech, and product interaction", guarded);
        Assert.Contains("Do not show the supplied image as a frozen still or separate opening shot", guarded);
        Assert.Contains("Begin immediately with natural motion inside this exact setup", guarded);
        Assert.Contains("same exact person", guardedAgain);
        Assert.Contains("natural movements", guardedAgain);
        Assert.Equal(prompt, unchanged);
    }

    [Fact]
    public void SharedBasePromptGuardNeutralizesVisualConflictsButKeepsMotionAndDialogue()
    {
        var prompt = """
            move to another room, wearing a different outfit, and change background.
            raise the product and smile while saying hello.
            """;

        var guarded = RVideoSharedBaseImagePromptGuard.Apply(prompt, useSharedReferenceImage: true);

        Assert.Contains("same exact person", guarded);
        Assert.Contains("raise the product and smile", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("while saying hello", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("move to another room", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wearing a different outfit", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("change background", guarded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedReferenceSnapshotCarriesMediaMetadata()
    {
        var reference = new RVideoSceneImageReferenceSelection(
            true,
            CharacterId: 42,
            ObjectKey: "references/shared.png",
            Url: "https://example.invalid/shared.png",
            CharacterPrompt: "consistent character",
            Source: RVideoSceneImageReferenceSelection.LibrarySource)
        {
            MediaId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            FileName = "shared.png",
            MimeType = "image/png"
        };

        var snapshot = reference.ToSnapshot();

        Assert.Equal(reference.MediaId, snapshot.MediaId);
        Assert.Equal(reference.ObjectKey, snapshot.ObjectKey);
        Assert.Equal(reference.Url, snapshot.PublicUrl);
        Assert.Equal(reference.FileName, snapshot.FileName);
        Assert.Equal(reference.MimeType, snapshot.MimeType);
    }

    [Fact]
    public async Task RVideo79AiSharedBasePayloadContainsImageReference()
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
            SourceImageAsset: null,
            ReferenceImageAssets: new[]
            {
                new RVideo79AiProviderImageAsset(
                    "reference-base",
                    "project-1",
                    "https://example.test/reference-character.png",
                    "reference-character.png",
                    """{"ok":true}""")
            }));

        Assert.NotNull(client.LastSubmit);
        Assert.True(client.LastSubmit!.Options.TryGetValue("images", out var imagesJson));
        Assert.Contains("reference-character.png", imagesJson);
        Assert.Contains("Use the supplied image as the fixed visual base for this scene", client.LastSubmit.Prompt);

        var sanitized = JsonSerializer.Deserialize<JsonElement>((await service.SubmitAsync(new RVideo79AiVideoSubmitRequest(
            Create79AiRuntime(),
            RVideoVideoModelPolicy.GetInitial(),
            RVideoReferenceOnlyPromptGuard.Apply("Open directly on the described scene.", useSharedReferenceImage: true),
            "9:16",
            "720p",
            6,
            SourceImageAsset: null,
            ReferenceImageAssets: new[]
            {
                new RVideo79AiProviderImageAsset(
                    "reference-base",
                    "project-1",
                    "https://example.test/reference-character.png",
                    "reference-character.png",
                    """{"ok":true}""")
            }))).SanitizedRequestJson);
        Assert.Equal(JsonValueKind.Null, sanitized.GetProperty("sourceImage").ValueKind);
        Assert.Single(sanitized.GetProperty("referenceImages").EnumerateArray());
    }

    [Fact]
    public void SharedBaseImagePromptGuardDoesNotRewriteNormalMode()
    {
        var prompt = "Open directly on the described scene.";

        var guarded = RVideoSharedBaseImagePromptGuard.Apply(prompt, useSharedReferenceImage: false);

        Assert.Equal(prompt, guarded);
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
                """{"ok":true}"""),
            ReferenceImageAssets: Array.Empty<RVideo79AiProviderImageAsset>()));

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
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.NotNull(version);
        Assert.Equal(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), version!.Id);
    }

    [Fact]
    public async Task DirectSourceImageUrlCanSkipSceneImageVersion()
    {
        var worker = CreateWorker(null);

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            7L,
            null,
            false,
            "https://example.test/direct-source.png",
            null,
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.NotNull(version);
        Assert.Equal(Guid.Empty, version!.Id);
        Assert.Equal("https://example.test/direct-source.png", version.PublicUrl);
        Assert.False(version.IsSelected);
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
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.Null(version);
    }

    [Fact]
    public void SceneVideoVersionCreateAllowsNullSourceImageVersionId()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");

        Assert.Contains("@sourceImageVersionId", source);
        Assert.Contains("request.SourceImageVersionId", source);
        Assert.DoesNotContain("request.SourceImageVersionId == Guid.Empty", source);
    }

    [Fact]
    public void SceneVideoSourceEventsDoNotLogRawSourceImageUrl()
    {
        var handler = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var enqueueEvent = handler[
            handler.IndexOf("\"SCENE_VIDEO_CHILD_JOB_ENQUEUED\"", StringComparison.Ordinal)
            ..handler.IndexOf("}, ct);", handler.IndexOf("\"SCENE_VIDEO_CHILD_JOB_ENQUEUED\"", StringComparison.Ordinal), StringComparison.Ordinal)];
        var uploadEvent = worker[
            worker.IndexOf("\"RVIDEO_VIDEO_SOURCE_UPLOAD_BEGIN\"", StringComparison.Ordinal)
            ..worker.IndexOf("}, ct);", worker.IndexOf("\"RVIDEO_VIDEO_SOURCE_UPLOAD_BEGIN\"", StringComparison.Ordinal), StringComparison.Ordinal)];

        Assert.Contains("sourceImageType", enqueueEvent);
        Assert.Contains("hasSourceImage", enqueueEvent);
        Assert.DoesNotContain("sourceImageUrl =", enqueueEvent);
        Assert.Contains("sourceImageType", uploadEvent);
        Assert.Contains("hasSourceImage", uploadEvent);
        Assert.DoesNotContain("sourceImageUrl =", uploadEvent);
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

    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            Path.Combine(parts)));
        return File.ReadAllText(path);
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
