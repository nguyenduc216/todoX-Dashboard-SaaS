using System.Reflection;
using System.Runtime.Serialization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal("normal", RVideoVideoModelPolicy.Models[3].Mode);
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
    public void SceneVideoEligibilityTreatsBlankTaskFailedVersionsAsRecoverableNotActive()
    {
        var method = typeof(VideoRenderEligibilityService).GetMethod(
            "IsSceneVideoVersionActivelyRunning",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var staleQueued = new SceneVideoVersionDto
        {
            Status = "queued",
            ProviderTaskId = null
        };
        var taskBackedQueued = new SceneVideoVersionDto
        {
            Status = "queued",
            ProviderTaskId = "task-123"
        };

        Assert.False((bool)method!.Invoke(null, new object[] { staleQueued, false })!);
        Assert.True((bool)method.Invoke(null, new object[] { staleQueued, true })!);
        Assert.True((bool)method.Invoke(null, new object[] { taskBackedQueued, false })!);
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

        var json = (string)method!.Invoke(null, new object?[] { input, "scene-base-fallback-1", "task-123", "{\"ok\":true}", 9.5m, (string?)null })!;
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
    public void ResolveFallbackCandidatesResolvesEachCandidateAgainstItsOwnDurationContract()
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
        Assert.Equal([4, 6, 6], resolved.Select(x => x.Duration));
        Assert.Equal(["veo_omni", "veo_3_1", "veo_3_1"], resolved.Select(x => x.Model));
    }

    [Fact]
    public void ResolveFallbackCandidatesKeepsVeoFastAndLiteAsIndependentCandidates()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveFallbackCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var input = new SceneVideoRenderWorkItemInput
        {
            ProviderCode = "79ai",
            DurationSeconds = 6
        };
        var catalog = new[]
        {
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "veo_omni",
                MediaType = "video",
                Enabled = true,
                SupportedModes = ["flash"],
                SupportedDurations = [4, 6, 8, 10]
            },
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai",
                ProviderModelCode = "veo_3_1",
                MediaType = "video",
                Enabled = true,
                SupportedModes = ["fast", "lite"],
                SupportedDurations = [6, 10]
            }
        };

        var resolved = ((System.Collections.IEnumerable)method!.Invoke(null, new object[] { input, catalog })!)
            .Cast<object>()
            .Select(item =>
            {
                var policy = item.GetType().GetProperty("Policy")!.GetValue(item)!;
                return (
                    Model: (string)policy.GetType().GetProperty("Model")!.GetValue(policy)!,
                    Mode: (string?)policy.GetType().GetProperty("Mode")!.GetValue(policy));
            })
            .ToArray();

        Assert.Equal(
            [("veo_omni", "flash"), ("veo_3_1", "fast"), ("veo_3_1", "lite")],
            resolved);
    }

    [Theory]
    [InlineData(4, "1080p", new[] { "veo_omni", "veo_3_1", "veo_3_1", "grok_video_heavy" }, new[] { "flash", "fast", "lite", "normal" }, new[] { 4, 4, 4, 6 }, new[] { "1080p", "1080p", "1080p", "720p" })]
    [InlineData(6, "720p", new[] { "veo_omni", "veo_3_1", "veo_3_1", "grok_video_heavy" }, new[] { "flash", "fast", "lite", "normal" }, new[] { 6, 6, 6, 6 }, new[] { "720p", "720p", "720p", "720p" })]
    [InlineData(8, "720p", new[] { "veo_omni", "veo_3_1", "veo_3_1", "grok_video_heavy" }, new[] { "flash", "fast", "lite", "normal" }, new[] { 8, 8, 8, 10 }, new[] { "720p", "720p", "720p", "720p" })]
    [InlineData(10, "1080p", new[] { "veo_omni", "grok_video_heavy" }, new[] { "flash", "normal" }, new[] { 10, 10 }, new[] { "1080p", "720p" })]
    public void ResolveFallbackCandidatesUsesPolicyOrderAndCatalogCapabilities(
        int duration,
        string resolution,
        string[] expectedModels,
        string[] expectedModes,
        int[] expectedDurations,
        string[] expectedResolutions)
    {
        var resolved = ResolveFallbackCandidatesForTest(duration, resolution);

        Assert.Equal(expectedModels, resolved.Select(x => x.Model));
        Assert.Equal(expectedModes, resolved.Select(x => x.Mode));
        Assert.Equal(expectedDurations, resolved.Select(x => x.ProviderDuration));
        Assert.Equal(expectedResolutions, resolved.Select(x => x.ProviderResolution));
    }

    [Theory]
    [InlineData("provider_failure", "Lỗi Google không thể xử lý đơn này. #22f", "MODEL_PROVIDER_FAILURE")]
    [InlineData("http_503", "service unavailable", "TRANSIENT_PROVIDER_FAILURE")]
    [InlineData("unauthorized", "invalid access token", "AUTHENTICATION_FAILURE")]
    [InlineData("insufficient_balance", "insufficient balance", "BILLING_FAILURE")]
    [InlineData("bad_request", "invalid prompt", "INVALID_INPUT")]
    public void ProviderFailureClassificationControlsFallback(string errorCode, string message, string expected)
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ClassifyProviderFailure", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, new object?[] { errorCode, message, null })!;

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProviderFailureWithPromptMessageStillAllowsModelFallback()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ClassifyProviderFailure", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, new object?[]
        {
            "provider_failure",
            "Provider rejected the request; please check Prompt. #22f",
            null
        })!;

        Assert.Equal("MODEL_PROVIDER_FAILURE", result);
    }

    [Fact]
    public void RVideoSubmitFailureReleasesCandidateBillingBeforeFallback()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var failureStart = source.IndexOf("var canFallback = structuredException is not null", StringComparison.Ordinal);
        var failureEnd = source.IndexOf("catch (Exception ex) when (ex is not OperationCanceledException)", failureStart, StringComparison.Ordinal);

        Assert.True(failureStart >= 0);
        Assert.True(failureEnd > failureStart);
        var failureBranch = source[failureStart..failureEnd];

        Assert.Contains("Success = false", failureBranch);
        Assert.Contains("LogicalRequestId = attemptLogicalRequestId", failureBranch);
        Assert.Contains("ProviderUsageJson = ai79Exception.SanitizedResponseJson", failureBranch);
        Assert.Contains("TariffSnapshotJson = tariffSnapshot", failureBranch);
        Assert.Contains("await _billing.CompleteAsync", failureBranch);
        Assert.True(failureBranch.IndexOf("await _billing.CompleteAsync", StringComparison.Ordinal)
            < failureBranch.IndexOf("if (!ShouldFallback(failureClassification)", StringComparison.Ordinal));
    }

    [Fact]
    public void RVideoWrappedAi79SubmitRejectionIsEligibleForModelFallback()
    {
        var exception = new Ai79TaskSubmitException(
            "Hiện gói dịch vụ Model VEO - Omni không khả dụng. Vui lòng chọn Model khác",
            """{"error":"model unavailable"}""",
            HttpStatusCode.ServiceUnavailable,
            "provider_failure",
            sanitizedRequestMetadataJson: """{"model":"veo_omni","mode":"flash"}""");

        Assert.True(IsDefinitivelyRejectedSubmitForTest(exception));
        Assert.Equal("MODEL_PROVIDER_FAILURE",
            ClassifyProviderFailureForTest(exception.ErrorCode!, exception.ErrorMessage, exception.HttpStatusCode));
    }

    [Theory]
    [InlineData(null, "provider_timeout", """{"error":"timeout"}""")]
    [InlineData(HttpStatusCode.OK, "missing_task_id", """{"status":"ok"}""")]
    [InlineData(HttpStatusCode.OK, "provider_error", """{"task_id":"task-accepted","error":"late error"}""")]
    public void RVideoAmbiguousOrAcceptedSubmitIsNotEligibleForModelFallback(
        HttpStatusCode? statusCode,
        string errorCode,
        string sanitizedResponseJson)
    {
        var exception = new Ai79TaskSubmitException(
            "79AI submit outcome is not a definitive rejection.",
            sanitizedResponseJson,
            statusCode,
            errorCode,
            sanitizedRequestMetadataJson: """{"model":"veo_omni"}""");

        Assert.False(IsDefinitivelyRejectedSubmitForTest(exception));
    }

    [Theory]
    [InlineData("provider_unavailable", "Dịch vụ hiện không khả dụng, vui lòng thử lại sau.")]
    [InlineData("provider_timeout", "The provider timed out while submitting the request.")]
    public void RVideoServiceUnavailableSubmitIsPendingReconciliationUnlessProviderExplicitlyRejected(
        string errorCode,
        string message)
    {
        var exception = new Ai79TaskSubmitException(
            message,
            "",
            HttpStatusCode.ServiceUnavailable,
            errorCode,
            sanitizedRequestMetadataJson: "{\"model\":\"veo_omni\"}");

        Assert.False(IsDefinitivelyRejectedSubmitForTest(exception));
    }

    [Fact]
    public void RVideoServiceUnavailableWithExplicitModelRejectionAllowsFallback()
    {
        var exception = new Ai79TaskSubmitException(
            "Model unavailable for this request.",
            "{\"error\":\"model unavailable\"}",
            HttpStatusCode.ServiceUnavailable,
            "provider_failure",
            sanitizedRequestMetadataJson: "{\"model\":\"veo_omni\"}");

        Assert.True(IsDefinitivelyRejectedSubmitForTest(exception));
    }

    [Fact]
    public void RVideoNotResourcesWithZeroTasksIsAKnownSafeFallbackFailure()
    {
        var exception = new Ai79TaskSubmitException(
            "79AI video submit failed: Dịch vụ hiện không khả dụng, vui lòng thử lại sau.",
            """{"countTasks":"0","error":"NOT_RESOURCES","message":"Dịch vụ hiện không khả dụng, vui lòng thử lại sau."}""",
            HttpStatusCode.OK,
            "provider_error",
            sanitizedRequestMetadataJson: """{"model":"veo_omni","mode":"flash"}""");

        Assert.True(IsDefinitivelyRejectedSubmitForTest(exception));
        Assert.Equal("KNOWN_NO_RESOURCES", ClassifyRVideoSubmitFailureForTest(exception));
        Assert.True(ShouldFallbackForTest("KNOWN_NO_RESOURCES"));
    }

    [Theory]
    [InlineData("""{"countTasks":"1","error":"NOT_RESOURCES"}""")]
    [InlineData("""{"countTasks":"0","error":"NOT_RESOURCES","task_id":"task-accepted"}""")]
    [InlineData("""{"error":"NOT_RESOURCES"}""")]
    public void RVideoNotResourcesWithoutProofOfNoTaskRemainsUnknown(string responseJson)
    {
        var exception = new Ai79TaskSubmitException(
            "79AI video submit outcome is not proven safe to fallback.",
            responseJson,
            HttpStatusCode.OK,
            "provider_error",
            sanitizedRequestMetadataJson: """{"model":"veo_omni"}""");

        Assert.False(IsDefinitivelyRejectedSubmitForTest(exception));
    }

    [Fact]
    public void RVideoRequestIdIsNotAcceptedAsPollableTaskId()
    {
        var exception = new Ai79TaskSubmitException(
            "79AI submit outcome is unknown.",
            "{\"request_id\":\"request-123\",\"status\":\"accepted\"}",
            HttpStatusCode.OK,
            "provider_error",
            sanitizedRequestMetadataJson: "{\"model\":\"veo_omni\"}");

        Assert.False(IsDefinitivelyRejectedSubmitForTest(exception));
    }

    [Theory]
    [InlineData("{\"task_id\":\"task-123\",\"error\":\"late error\"}")]
    [InlineData("{\"id_base\":\"video-123\"}")]
    public void RVideoPollableTaskIdentityWinsOverAmbiguousSubmitError(string responseJson)
    {
        var exception = new Ai79TaskSubmitException(
            "79AI submit response included a pollable task identity.",
            responseJson,
            HttpStatusCode.ServiceUnavailable,
            "provider_failure",
            sanitizedRequestMetadataJson: "{\"model\":\"veo_omni\"}");

        Assert.False(IsDefinitivelyRejectedSubmitForTest(exception));
    }

    [Theory]
    [InlineData("unauthorized", "invalid access token", HttpStatusCode.Unauthorized, "AUTHENTICATION_FAILURE", false)]
    [InlineData("insufficient_balance", "insufficient balance", HttpStatusCode.ServiceUnavailable, "BILLING_FAILURE", false)]
    [InlineData("bad_request", "invalid prompt", HttpStatusCode.BadRequest, "INVALID_INPUT", false)]
    [InlineData("provider_failure", "Lỗi Google không thể xử lý đơn này, vui lòng kiểm tra lại Prompt. #22f", HttpStatusCode.ServiceUnavailable, "MODEL_PROVIDER_FAILURE", true)]
    public void RVideoSubmitFailureClassificationControlsTerminalVsFallback(
        string errorCode,
        string message,
        HttpStatusCode statusCode,
        string expectedClassification,
        bool expectedFallback)
    {
        var classification = ClassifyProviderFailureForTest(errorCode, message, statusCode);

        Assert.Equal(expectedClassification, classification);
        Assert.Equal(expectedFallback, ShouldFallbackForTest(classification));
    }

    [Fact]
    public void AiProviderModelOptionsNormalizerReadsNestedVeoVariants()
    {
        var options = AiProviderModelOptionsNormalizer.Normalize(
            explicitModes: null,
            explicitDurations: null,
            explicitResolutions: null,
            explicitRatios: null,
            prices: null,
            rawJson: """
            {
              "provider_model_code": "veo_3_1",
              "variant_options": [
                { "mode": "fast", "duration_seconds": 6, "resolution": "720p", "aspect_ratio": "9:16" },
                { "mode": "quality", "duration": "8", "size": "1080p", "ratio": "16:9" }
              ],
              "price_options": [
                { "mode": "lite", "duration_seconds": 10, "resolution": "720p", "ratio": "9:16" }
              ]
            }
            """);

        Assert.Equal(["fast", "lite", "quality"], options.Modes);
        Assert.Equal([6, 8, 10], options.Durations);
        Assert.Contains("720p", options.Resolutions);
        Assert.Contains("1080p", options.Resolutions);
        Assert.Contains("9:16", options.Ratios);
        Assert.Contains("16:9", options.Ratios);
    }

    [Fact]
    public void AiProviderModelOptionsNormalizerReadsDurationObjectTypeValues()
    {
        var options = AiProviderModelOptionsNormalizer.Normalize(null, null, null, null, null, """
        {
          "durations": [
            { "name": "8s", "type": "8" },
            { "name": "6s", "type": "6" },
            { "name": "4s", "type": "4" }
          ]
        }
        """);

        Assert.Equal([4, 6, 8], options.Durations);
    }

    [Fact]
    public async Task Ai79VideoSubmitPreservesIdBaseAndProviderTaskIdSeparately()
    {
        var handler = new CapturingHttpMessageHandler("""{"videoInfo":{"id_base":"id-base-a","task_id":"task-b"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://example.test/ai", "/create-video", "secret-token", "79ai.net", "veo_omni", "Animate.",
            Array.Empty<string>(), new Dictionary<string, string?>(), Ai79TaskOperation.Video));

        Assert.Equal("id-base-a", result.TaskId);
        Assert.Equal("task-b", result.ProviderTaskId);
        Assert.Equal("id-base-a", result.ProviderVideoIdBase);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson);
    }

    [Fact]
    public async Task Ai79VideoPollUsesIdBaseAndReconcilesSuccessfulMissingUrlFromVideos()
    {
        var handler = new SequencedHttpMessageHandler(
            """{"videoInfo":{"id_base":"id-base-a","status":"MEDIA_GENERATION_STATUS_SUCCESSFUL"}}""",
            """{"data":[{"id_base":"id-base-a","status":"MEDIA_GENERATION_STATUS_SUCCESSFUL","download_url":"https://cdn.example/video.mp4"}]}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.GetStatusAsync(new Ai79TaskStatusRequest(
            "https://example.test/ai", "/video", "secret-token", "79ai.net", "id-base-a", Ai79TaskOperation.Video,
            TaskIdField: "videoId", ProjectId: "project-1"));

        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal("https://cdn.example/video.mp4", result.OutputUrl);
        Assert.Equal("id-base-a", handler.Forms[0]["videoId"]);
        Assert.DoesNotContain(handler.Forms[0].Values, value => value == "task-b");
        Assert.Equal("project-1", handler.Forms[1]["project_id"]);
    }

    [Fact]
    public void GetModelByCodeHydratesPricesAndNormalizesOptions()
    {
        var source = ReadRepoFile("Services", "AiProviders", "AiProviderModelRepository.cs");
        var methodStart = source.IndexOf("public async Task<AiProviderModelDetailDto?> GetModelByCodeAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public async Task UpdateAdminFieldsAsync", methodStart, StringComparison.Ordinal);

        Assert.True(methodStart >= 0);
        Assert.True(methodEnd > methodStart);
        var method = source[methodStart..methodEnd];

        Assert.Contains("model.Prices = (await GetPricesAsync(model.Id, ct)).ToList();", method);
        Assert.Contains("model.ModelCapabilities = (await GetCapabilitiesAsync(model.Id, ct)).ToList();", method);
        Assert.Contains("AiProviderModelOptionsNormalizer.Normalize", method);
        Assert.Contains("model.SupportedDurations = options.Durations;", method);
    }

    [Fact]
    public void SceneVideoWorkerEmitsFallbackLifecycleEventsAndKeepsProviderDiagnosticsSanitized()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("\"RVIDEO_VIDEO_FALLBACK_STARTED\"", source);
        Assert.Contains("\"RVIDEO_VIDEO_FALLBACK_SUBMITTED\"", source);
        Assert.Contains("\"RVIDEO_VIDEO_FALLBACK_FAILED\"", source);
        Assert.Contains("\"RVIDEO_VIDEO_FALLBACK_EXHAUSTED\"", source);
        Assert.Contains("providerTaskId", source);
        Assert.Contains("failureClassification", source);
        Assert.Contains("sanitizedResponseJson", source);
        Assert.Contains("sanitizedRequestMetadataJson", source);
        Assert.DoesNotContain("AccessToken", source);
        Assert.DoesNotContain("Authorization", source);
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
        Assert.Equal(new[] { "https://example.test/reference-character.png" }, client.LastSubmit!.Images);
        Assert.Equal("image", client.LastSubmit.FirstImageField);
        Assert.Equal("image_2", client.LastSubmit.SecondImageField);
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
        Assert.Equal(new[] { "https://example.test/generated-scene.png" }, client.LastSubmit!.Images);
        Assert.Equal("image", client.LastSubmit.FirstImageField);
        Assert.True(client.LastSubmit!.Options.TryGetValue("images", out var imagesJson));
        Assert.Contains("generated-scene.png", imagesJson);
    }

    [Fact]
    public async Task RVideo79AiSubmitFormContainsDirectImageAndDescriptorWithoutSecret()
    {
        var handler = new CapturingHttpMessageHandler("""{"task_id":"task-123"}""");
        var client = new Ai79TaskClient(new HttpClient(handler));
        var service = Create79AiVideoService(client);
        var imageUrl = "https://example.test/reference-character.png";
        var secret = Create79AiRuntime().Credential.Secret;

        await service.SubmitAsync(new RVideo79AiVideoSubmitRequest(
            Create79AiRuntime(),
            RVideoVideoModelPolicy.GetInitial(),
            "Animate the supplied reference.",
            "9:16",
            "720p",
            6,
            SourceImageAsset: null,
            ReferenceImageAssets: new[]
            {
                new RVideo79AiProviderImageAsset(
                    "reference-base",
                    "project-1",
                    imageUrl,
                    "reference-character.png",
                    """{"ok":true}""")
            }));

        Assert.Equal(imageUrl, handler.Form["image"]);
        Assert.Contains("reference-base", handler.Form["images"]);
        Assert.DoesNotContain(secret, handler.Form["images"]);
    }

    [Fact]
    public async Task RVideo79AiSubmitKeepsExpectedFastAndLiteModes()
    {
        foreach (var model in new[]
        {
            new RVideoVideoModelPolicyEntry(1, "79ai", "veo_3_1", "fast"),
            new RVideoVideoModelPolicyEntry(2, "79ai", "veo_3_1", "lite")
        })
        {
            var client = new CapturingAi79TaskClient();
            var service = Create79AiVideoService(client);

            await service.SubmitAsync(new RVideo79AiVideoSubmitRequest(
                Create79AiRuntime(),
                model,
                "Animate the scene.",
                "9:16",
                "720p",
                6,
                new RVideo79AiProviderImageAsset(
                    "scene-image-base",
                    "project-1",
                    "https://example.test/generated-scene.png",
                    "generated-scene.png",
                    """{"ok":true}"""),
                Array.Empty<RVideo79AiProviderImageAsset>()));

            Assert.Equal(model.Mode, client.LastSubmit!.Options["mode"]);
        }
    }

    [Fact]
    public async Task RVideo79AiAdapterForwardsEachFallbackCandidateToTheProviderLayer()
    {
        var service = new CapturingRVideo79AiVideoService();
        var adapter = new Ai79VideoGenerationProviderAdapter(service);
        var candidates = new[]
        {
            new RVideoVideoModelPolicyEntry(0, "79ai", "veo_omni", "flash"),
            new RVideoVideoModelPolicyEntry(1, "79ai", "veo_3_1", "fast"),
            new RVideoVideoModelPolicyEntry(2, "79ai", "veo_3_1", "lite")
        };

        foreach (var candidate in candidates)
        {
            await adapter.SubmitAsync(new VideoProviderSubmitRequest(
                18,
                99,
                candidate.ProviderCode,
                RVideoVideoModelPolicy.CapabilityCode,
                candidate.Model,
                candidate.Mode,
                "Animate the scene.",
                "9:16",
                "720p",
                6,
                SourceImage: null,
                ReferenceImages: Array.Empty<VideoProviderSourceImage>()));
        }

        Assert.Equal(3, service.Submits.Count);
        Assert.Equal(
            candidates.Select(candidate => (candidate.ProviderCode, candidate.Model, candidate.Mode)),
            service.Submits.Select(request => (
                request.Runtime.ProviderCode,
                request.Model.Model,
                request.Model.Mode)));
    }

    [Fact]
    public async Task RVideoFallbackCandidatesReachTheVideoAdapterWithExactProviderRequestIdentity()
    {
        var adapter = new CapturingVideoGenerationProviderAdapter();
        var resolver = new VideoGenerationProviderAdapterResolver(new[] { adapter });
        var resolved = resolver.Resolve("79ai", RVideoVideoModelPolicy.CapabilityCode);
        var candidates = new[]
        {
            new RVideoVideoModelPolicyEntry(0, "79ai", "veo_omni", "flash"),
            new RVideoVideoModelPolicyEntry(1, "79ai", "veo_3_1", "fast"),
            new RVideoVideoModelPolicyEntry(2, "79ai", "veo_3_1", "lite")
        };

        foreach (var candidate in candidates)
        {
            await resolved.SubmitAsync(new VideoProviderSubmitRequest(
                18,
                99,
                candidate.ProviderCode,
                RVideoVideoModelPolicy.CapabilityCode,
                candidate.Model,
                candidate.Mode,
                "Animate the scene.",
                "9:16",
                "720p",
                6,
                SourceImage: null,
                ReferenceImages: Array.Empty<VideoProviderSourceImage>()));
        }

        Assert.Equal(
            candidates.Select(candidate => (candidate.ProviderCode, candidate.Model, candidate.Mode)),
            adapter.Submits.Select(request => (
                request.ProviderCode,
                request.RequestedModel,
                request.ModelMode)));
    }

    [Fact]
    public async Task RVideoPollFailureSubmitsFastAfterOmniAndKeepsFastTaskOnSuccess()
    {
        var adapter = new ScriptedVideoGenerationProviderAdapter(
            VideoProviderTaskStatus.Failed,
            VideoProviderTaskStatus.Success);
        var resolved = new VideoGenerationProviderAdapterResolver(new[] { adapter })
            .Resolve("79ai", RVideoVideoModelPolicy.CapabilityCode);
        var candidates = new[]
        {
            new RVideoVideoModelPolicyEntry(0, "79ai", "veo_omni", "flash"),
            new RVideoVideoModelPolicyEntry(1, "79ai", "veo_3_1", "fast")
        };

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            var submit = await resolved.SubmitAsync(CreateProviderSubmitRequest(candidate));
            var poll = await resolved.PollAsync(new VideoProviderPollRequest(
                18,
                99,
                candidate.ProviderCode,
                RVideoVideoModelPolicy.CapabilityCode,
                submit.ProviderTaskId));

            if (poll.Status == VideoProviderTaskStatus.Success)
            {
                break;
            }

            Assert.Equal("MODEL_PROVIDER_FAILURE",
                ClassifyProviderFailureForTest("provider_failure", "Provider rejected the Prompt. #22f"));
            Assert.True(index + 1 < candidates.Length);
        }

        Assert.Equal(
            [("79ai", "veo_omni", "flash"), ("79ai", "veo_3_1", "fast")],
            adapter.Submits.Select(request => (request.ProviderCode, request.RequestedModel, request.ModelMode)));
        Assert.Equal("task-2", adapter.Polls[^1].ProviderTaskId);
        Assert.Equal(VideoProviderTaskStatus.Success, adapter.Polls[^1].Status);
    }

    [Fact]
    public async Task RVideoPollFailureSubmitsLiteAfterOmniAndFastAndExhaustsOnlyAfterThirdCandidate()
    {
        var adapter = new ScriptedVideoGenerationProviderAdapter(
            VideoProviderTaskStatus.Failed,
            VideoProviderTaskStatus.Failed,
            VideoProviderTaskStatus.Success);
        var resolved = new VideoGenerationProviderAdapterResolver(new[] { adapter })
            .Resolve("79ai", RVideoVideoModelPolicy.CapabilityCode);
        var candidates = new[]
        {
            new RVideoVideoModelPolicyEntry(0, "79ai", "veo_omni", "flash"),
            new RVideoVideoModelPolicyEntry(1, "79ai", "veo_3_1", "fast"),
            new RVideoVideoModelPolicyEntry(2, "79ai", "veo_3_1", "lite")
        };

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            var submit = await resolved.SubmitAsync(CreateProviderSubmitRequest(candidate));
            var poll = await resolved.PollAsync(new VideoProviderPollRequest(
                18,
                99,
                candidate.ProviderCode,
                RVideoVideoModelPolicy.CapabilityCode,
                submit.ProviderTaskId));

            if (poll.Status == VideoProviderTaskStatus.Success)
            {
                break;
            }

            Assert.Equal("MODEL_PROVIDER_FAILURE",
                ClassifyProviderFailureForTest("provider_failure", "Lỗi Google không thể xử lý Prompt. #22f"));
            Assert.Equal(index < candidates.Length - 1, index + 1 < candidates.Length);
        }

        Assert.Equal(
            [("79ai", "veo_omni", "flash"), ("79ai", "veo_3_1", "fast"), ("79ai", "veo_3_1", "lite")],
            adapter.Submits.Select(request => (request.ProviderCode, request.RequestedModel, request.ModelMode)));
        Assert.Equal("task-3", adapter.Polls[^1].ProviderTaskId);
        Assert.Equal(VideoProviderTaskStatus.Success, adapter.Polls[^1].Status);
        Assert.DoesNotContain("RVIDEO_VIDEO_FALLBACK_EXHAUSTED", adapter.EventCodes);
    }

    [Fact]
    public async Task RVideoPollFailureEmitsExhaustionOnlyAfterAllSubmittedCandidatesFail()
    {
        var adapter = new ScriptedVideoGenerationProviderAdapter(
            VideoProviderTaskStatus.Failed,
            VideoProviderTaskStatus.Failed,
            VideoProviderTaskStatus.Failed);
        var resolved = new VideoGenerationProviderAdapterResolver(new[] { adapter })
            .Resolve("79ai", RVideoVideoModelPolicy.CapabilityCode);
        var candidates = new[]
        {
            new RVideoVideoModelPolicyEntry(0, "79ai", "veo_omni", "flash"),
            new RVideoVideoModelPolicyEntry(1, "79ai", "veo_3_1", "fast"),
            new RVideoVideoModelPolicyEntry(2, "79ai", "veo_3_1", "lite")
        };

        for (var index = 0; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            var submit = await resolved.SubmitAsync(CreateProviderSubmitRequest(candidate));
            var poll = await resolved.PollAsync(new VideoProviderPollRequest(
                18,
                99,
                candidate.ProviderCode,
                RVideoVideoModelPolicy.CapabilityCode,
                submit.ProviderTaskId));
            if (poll.Status != VideoProviderTaskStatus.Failed)
            {
                continue;
            }

            if (index + 1 >= candidates.Length)
            {
                adapter.EventCodes.Add("RVIDEO_VIDEO_FALLBACK_EXHAUSTED");
            }
        }

        Assert.Equal(3, adapter.Submits.Count);
        Assert.Equal(3, adapter.Polls.Count);
        Assert.Equal(["base", "base-fallback-1", "base-fallback-2"],
            Enumerable.Range(0, adapter.Submits.Count)
                .Select(index => BuildAttemptLogicalRequestIdForTest("base", index)));
        Assert.Equal("RVIDEO_VIDEO_FALLBACK_EXHAUSTED", Assert.Single(adapter.EventCodes));
    }

    [Fact]
    public async Task SceneVideoHandlerPersistsAi79DiagnosticsThroughRenderJobEventsWithoutSecrets()
    {
#pragma warning disable SYSLIB0050
        var handler = (SceneVideoWorkerHandler)FormatterServices.GetUninitializedObject(typeof(SceneVideoWorkerHandler));
#pragma warning restore SYSLIB0050
        var events = DispatchProxy.Create<IRenderJobService, RenderJobServiceProxy>();
        var proxy = (RenderJobServiceProxy)(object)events;
        typeof(SceneVideoWorkerHandler)
            .GetField("_jobs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(handler, events);

        var exception = new Ai79TaskSubmitException(
            "79AI video submit failed.",
            """{"error":"unavailable","access_token":"raw-access-token","apiKey":"raw-api-key","Authorization":"Bearer raw-token"}""",
            HttpStatusCode.ServiceUnavailable,
            "provider_unavailable",
            sanitizedRequestMetadataJson: """{"endpoint":"/create-video","access_token":"raw-access-token","apiKey":"raw-api-key","Authorization":"Bearer raw-token"}""");
        var method = typeof(SceneVideoWorkerHandler).GetMethod(
            "AddAi79SubmitDiagnosticsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var task = (Task)method!.Invoke(handler, new object[]
        {
            new RenderJobDto
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                AttemptCount = 3,
                MaxAttempts = 3
            },
            new RVideoVideoModelPolicyEntry(0, "79ai", "veo_omni", "flash"),
            exception,
            CancellationToken.None
        })!;
        await task;

        Assert.Equal("RVIDEO_79AI_SUBMIT_DIAGNOSTICS", proxy.EventType);
        using var document = JsonDocument.Parse(proxy.DataJson!);
        var root = document.RootElement;
        Assert.Equal("Ai79TaskSubmitException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("79ai", root.GetProperty("provider").GetString());
        Assert.Equal("veo_omni", root.GetProperty("model").GetString());
        Assert.Equal(503, root.GetProperty("httpStatusCode").GetInt32());
        Assert.Equal("provider_unavailable", root.GetProperty("providerErrorCode").GetString());
        Assert.Equal(3, root.GetProperty("attemptCount").GetInt32());
        Assert.Equal(3, root.GetProperty("maxAttempts").GetInt32());
        Assert.DoesNotContain("raw-access-token", proxy.DataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-api-key", proxy.DataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", proxy.DataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RVideo79AiSubmitFailureRetainsSanitizedResponse()
    {
        var handler = new CapturingHttpMessageHandler("""{"error":"bad","access_token":"secret-token"}""", HttpStatusCode.BadRequest);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<Ai79TaskSubmitException>(() => client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://example.test/ai",
            "/create-video",
            "secret-token",
            "79ai.net",
            "veo_omni",
            "Animate the scene.",
            new[] { "https://example.test/reference-character.png" },
            new Dictionary<string, string?> { ["type"] = "video" },
            Ai79TaskOperation.Video), CancellationToken.None));

        Assert.Contains("***", ex.SanitizedResponseJson);
        Assert.DoesNotContain("secret-token", ex.SanitizedResponseJson);
        Assert.Contains("/create-video", ex.SanitizedRequestMetadataJson);
        Assert.DoesNotContain("secret-token", ex.SanitizedRequestMetadataJson);
    }

    [Fact]
    public async Task RVideo79AiSubmitFailureCapturesSafeRequestMetadata()
    {
        var handler = new CapturingHttpMessageHandler("""{"error":"bad"}""", HttpStatusCode.ServiceUnavailable);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<Ai79TaskSubmitException>(() => client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://example.test/ai",
            "/create-video",
            "secret-token",
            "79ai.net",
            "veo_omni",
            "Animate the scene.",
            new[] { "https://example.test/reference-character.png" },
            new Dictionary<string, string?>
            {
                ["type"] = "video",
                ["mode"] = "flash",
                ["duration"] = "6",
                ["ratio"] = "9:16",
                ["aspect_ratio"] = "9:16",
                ["resolution"] = "720p",
                ["project_id"] = "project-1",
                ["translate_to_en"] = "false"
            },
            Ai79TaskOperation.Video), CancellationToken.None));

        using var metadata = JsonDocument.Parse(ex.SanitizedRequestMetadataJson);
        Assert.Equal("https://example.test/ai", metadata.RootElement.GetProperty("baseUrl").GetString());
        Assert.Equal("/create-video", metadata.RootElement.GetProperty("endpointPath").GetString());
        Assert.Equal("79ai.net", metadata.RootElement.GetProperty("domain").GetString());
        Assert.Equal("veo_omni", metadata.RootElement.GetProperty("model").GetString());
        Assert.Equal("video", metadata.RootElement.GetProperty("operation").GetString());
        Assert.Equal("flash", metadata.RootElement.GetProperty("mode").GetString());
        Assert.Equal("6", metadata.RootElement.GetProperty("duration").GetString());
        Assert.Equal("9:16", metadata.RootElement.GetProperty("ratio").GetString());
        Assert.Equal("9:16", metadata.RootElement.GetProperty("aspect_ratio").GetString());
        Assert.Equal("720p", metadata.RootElement.GetProperty("resolution").GetString());
        Assert.Equal("video", metadata.RootElement.GetProperty("type").GetString());
        Assert.Equal("project-1", metadata.RootElement.GetProperty("project_id").GetString());
        Assert.Equal(JsonValueKind.Null, metadata.RootElement.GetProperty("privacy").ValueKind);
        Assert.Equal("false", metadata.RootElement.GetProperty("translate_to_en").GetString());
        Assert.Equal(1, metadata.RootElement.GetProperty("imageCount").GetInt32());
        Assert.Equal(0, metadata.RootElement.GetProperty("fileCount").GetInt32());
        var images = metadata.RootElement.GetProperty("images");
        Assert.Equal(1, images.GetArrayLength());
        Assert.Equal("image", images[0].GetProperty("fieldName").GetString());
        Assert.True(images[0].GetProperty("present").GetBoolean());
        Assert.Equal("example.test", images[0].GetProperty("urlHost").GetString());
        Assert.Equal("/reference-character.png", images[0].GetProperty("urlPath").GetString());
        Assert.Equal("https://example.test/reference-character.png", images[0].GetProperty("sanitizedUrl").GetString());
        Assert.DoesNotContain("secret-token", ex.SanitizedRequestMetadataJson);
    }

    [Fact]
    public void RVideo79AiSubmitFailureCapturesWrappedTransientDiagnostics()
    {
        var direct = new Ai79TaskSubmitException(
            "79AI submit failed",
            """{"error":"bad"}""",
            HttpStatusCode.BadRequest,
            "bad_request",
            sanitizedRequestMetadataJson: """{"mode":"flash"}""");
        var wrapped = new VideoProviderTransientException("submit transient", "submit_transient", direct);

        var method = typeof(SceneVideoWorkerHandler).GetMethod("BuildSubmitFailureDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var diagnostics = method!.Invoke(null, new object[] { wrapped });
        Assert.NotNull(diagnostics);

        var json = JsonSerializer.Serialize(diagnostics);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(400, doc.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("bad_request", doc.RootElement.GetProperty("providerErrorCode").GetString());
        Assert.Equal("79AI submit failed", doc.RootElement.GetProperty("providerErrorMessage").GetString());
        Assert.Equal("""{"error":"bad"}""", doc.RootElement.GetProperty("sanitizedResponse").GetString());
        Assert.Equal("""{"mode":"flash"}""", doc.RootElement.GetProperty("sanitizedRequestMetadata").GetString());
    }

    [Fact]
    public void RVideo79AiSubmitFailureCapturesDirectDiagnostics()
    {
        var direct = new Ai79TaskSubmitException(
            "79AI submit failed",
            """{"error":"bad"}""",
            HttpStatusCode.BadRequest,
            "bad_request",
            sanitizedRequestMetadataJson: """{"mode":"flash"}""");

        var method = typeof(SceneVideoWorkerHandler).GetMethod("BuildSubmitFailureDiagnostics", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var diagnostics = method!.Invoke(null, new object[] { direct });
        Assert.NotNull(diagnostics);

        var json = JsonSerializer.Serialize(diagnostics);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(400, doc.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal("bad_request", doc.RootElement.GetProperty("providerErrorCode").GetString());
        Assert.Equal("79AI submit failed", doc.RootElement.GetProperty("providerErrorMessage").GetString());
        Assert.Equal("""{"error":"bad"}""", doc.RootElement.GetProperty("sanitizedResponse").GetString());
        Assert.Equal("""{"mode":"flash"}""", doc.RootElement.GetProperty("sanitizedRequestMetadata").GetString());
    }

    [Fact]
    public void SceneVideoJobWorkerTerminalFallbackFailsVersionWithoutTaskId()
    {
        var source = ReadRepoFile("Services", "Render", "SceneVideoJobWorker.cs");

        Assert.Contains("await recovery.RecoverStuckAsync(input.ProjectId, scene, version, job, failure.Message, ct);", source);
        Assert.Contains("await versions.FailSceneVideoVersionAsync(version.Id, failure.GetType().Name, failure.Message, ct);", source);
        Assert.Contains("version.ProviderTaskId is not null && !string.IsNullOrWhiteSpace(version.ProviderTaskId)", source);
    }

    [Fact]
    public void SceneVideoJobWorkerSyncsTerminalFailedVersions()
    {
        var source = ReadRepoFile("Services", "Render", "SceneVideoJobWorker.cs");

        Assert.Contains("SyncTerminalSceneVideoVersionAsync", source);
        Assert.Contains("GetSceneVideoVersionByLogicalRequestIdAsync", source);
        Assert.Contains("FailSceneVideoVersionAsync", source);
        Assert.Contains("job.AttemptCount < job.MaxAttempts", source);
        Assert.Contains("await SyncTerminalSceneVideoVersionAsync(scope, job, ex, stoppingToken);", source);
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
    public async Task StaleExplicitSourceImageVersionFallsBackToDirectSourceImage()
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
    public async Task StaleExplicitSourceImageVersionFallsBackToDirectSourceObjectKey()
    {
        var worker = CreateWorker(null, Array.Empty<SceneImageVersionDto>());

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            99L,
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            false,
            null,
            "rvideo_character/202608/c636140d5922493d97b1b8401a3ab06b.jpg",
            null,
            CancellationToken.None
        })!;

        var version = await task;
        Assert.NotNull(version);
        Assert.Equal(Guid.Empty, version!.Id);
        Assert.Equal("rvideo_character/202608/c636140d5922493d97b1b8401a3ab06b.jpg", version.StorageKey);
    }

    [Fact]
    public void DirectSourceImagePersistsNullSourceImageVersionId()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolvePersistedSourceImageVersionId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var directSource = new SceneImageVersionDto
        {
            Id = Guid.Empty,
            PublicUrl = "https://example.test/direct-source.png",
            Status = "completed"
        };

        var resolved = (Guid?)method!.Invoke(null, new object[] { false, directSource })!;

        Assert.Null(resolved);
    }

    [Fact]
    public void ModernCompletedImageVersionPreservesSourceImageVersionId()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolvePersistedSourceImageVersionId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var imageVersionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var resolved = (Guid?)method!.Invoke(null, new object[]
        {
            false,
            new SceneImageVersionDto
            {
                Id = imageVersionId,
                PublicUrl = "https://example.test/current.png",
                Status = "completed"
            }
        })!;

        Assert.Equal(imageVersionId, resolved);
    }

    [Fact]
    public void SharedReferenceImagePersistsNullSourceImageVersionId()
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolvePersistedSourceImageVersionId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var resolved = (Guid?)method!.Invoke(null, new object[]
        {
            true,
            new SceneImageVersionDto
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                PublicUrl = "https://example.test/shared.png",
                Status = "completed"
            }
        })!;

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ForeignSceneImageVersionIdIsNotAcceptedAsLineage()
    {
        var worker = CreateWorker(null, Array.Empty<SceneImageVersionDto>());

        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveSourceImageVersionAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task<SceneImageVersionDto?>)method!.Invoke(worker, new object?[]
        {
            99L,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
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
    public void SceneVideoWorkerCreatesVersionFromResolvedImageVersionIdOnly()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var method = source[source.IndexOf("private async Task HandleProviderVideoAsync", StringComparison.Ordinal)..];

        Assert.Contains("ResolvePersistedSourceImageVersionId(input.UseSharedReferenceImage, sourceVersion)", method);
        Assert.DoesNotContain("requestedSourceImageVersionId ?? (sourceVersion.Id == Guid.Empty ? null : sourceVersion.Id)", method);
    }

    [Fact]
    public void Project19LegacyUploadedCharacterImageIsUsableWithoutSceneImageVersion()
    {
        var project = new VideoProjectDto
        {
            Id = 19,
            UploadedCharacterUrl = "/uploads/rvideo_character/202608/c636140d5922493d97b1b8401a3ab06b.jpg"
        };
        var scene = new VideoProjectSceneDto
        {
            Id = 99,
            ProjectId = 19,
            SceneIndex = 1
        };

        var source = RVideoEffectiveSceneImageSourceResolver.Resolve(scene, settings: null, selectedImageVersion: null, project);

        Assert.True(source.HasUsableInput);
        Assert.Null(source.SelectedImageVersionId);
        Assert.Equal(RVideoEffectiveSceneImageSourceResolver.LegacyUploadedCharacter, source.SourceLabel);
        Assert.Equal(project.UploadedCharacterUrl, source.SourceImageUrl);
    }

    [Fact]
    public void VideoRenderEligibilityUsesUsableImageInputInsteadOfMandatoryImageVersion()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderEligibilityService.cs");

        Assert.Contains("RVideoEffectiveSceneImageSourceResolver.Resolve(scene, settings, imageVersion, project)", source);
        Assert.Contains("var hasSourceImage = effectiveSource.HasUsableInput", source);
        Assert.DoesNotContain("imageVersion is null", source);
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

    private static RVideo79AiVideoService Create79AiVideoService(IAi79TaskClient client)
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

    private static VideoProviderSubmitRequest CreateProviderSubmitRequest(RVideoVideoModelPolicyEntry candidate)
        => new(
            18,
            99,
            candidate.ProviderCode,
            RVideoVideoModelPolicy.CapabilityCode,
            candidate.Model,
            candidate.Mode,
            "Animate the scene.",
            "9:16",
            "720p",
            6,
            SourceImage: null,
            ReferenceImages: Array.Empty<VideoProviderSourceImage>());

    private static IReadOnlyList<(string Model, string? Mode, int ProviderDuration, string ProviderResolution)> ResolveFallbackCandidatesForTest(
        int duration,
        string resolution)
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod("ResolveFallbackCandidates", BindingFlags.NonPublic | BindingFlags.Static)!;
        var catalog = new[]
        {
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai", ProviderModelCode = "veo_omni", MediaType = "video", Enabled = true,
                SupportedModes = ["flash"], SupportedDurations = [4, 6, 8, 10], SupportedResolutions = ["720p", "1080p", "4k"]
            },
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai", ProviderModelCode = "veo_3_1", MediaType = "video", Enabled = true,
                SupportedModes = ["fast", "lite"], SupportedDurations = [4, 6, 8], SupportedResolutions = ["720p", "1080p", "4k"]
            },
            new AiProviderModelListItemDto
            {
                ProviderCode = "79ai", ProviderModelCode = "grok_video_heavy", MediaType = "video", Enabled = true,
                SupportedModes = ["normal"], SupportedDurations = [6, 10, 12, 15], SupportedResolutions = ["720p"]
            }
        };

        return ((System.Collections.IEnumerable)method.Invoke(null, new object[]
            {
                new SceneVideoRenderWorkItemInput { ProviderCode = "79ai", DurationSeconds = duration, Resolution = resolution }, catalog
            })!)
            .Cast<object>()
            .Select(item =>
            {
                var policy = item.GetType().GetProperty("Policy")!.GetValue(item)!;
                return (
                    (string)policy.GetType().GetProperty("Model")!.GetValue(policy)!,
                    (string?)policy.GetType().GetProperty("Mode")!.GetValue(policy),
                    (int)item.GetType().GetProperty("ProviderDurationSeconds")!.GetValue(item)!,
                    (string)item.GetType().GetProperty("ProviderResolution")!.GetValue(item)!);
            })
            .ToArray();
    }

    private static string BuildAttemptLogicalRequestIdForTest(string logicalRequestId, int attemptIndex)
        => attemptIndex == 0 ? logicalRequestId : $"{logicalRequestId}-fallback-{attemptIndex}";

    private static string ClassifyProviderFailureForTest(string errorCode, string message)
        => ClassifyProviderFailureForTest(errorCode, message, null);

    private static string ClassifyProviderFailureForTest(string errorCode, string message, HttpStatusCode? statusCode)
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod(
            "ClassifyProviderFailure",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, new object?[] { errorCode, message, statusCode })!;
    }

    private static bool ShouldFallbackForTest(string classification)
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod(
            "ShouldFallback",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object?[] { classification })!;
    }

    private static bool IsDefinitivelyRejectedSubmitForTest(Ai79TaskSubmitException exception)
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod(
            "IsDefinitivelyRejectedSubmit",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool)method.Invoke(null, new object?[] { exception })!;
    }

    private static string ClassifyRVideoSubmitFailureForTest(Ai79TaskSubmitException exception)
    {
        var method = typeof(SceneVideoWorkerHandler).GetMethod(
            "ClassifyRVideoSubmitFailure",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object?[] { exception })!;
    }

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

    private sealed class CapturingRVideo79AiVideoService : IRVideo79AiVideoService
    {
        public List<RVideo79AiVideoSubmitRequest> Submits { get; } = [];

        public Task<RVideo79AiRuntime> ResolveRuntimeAsync(long providerId, long providerCapabilityId, string providerCode, CancellationToken ct = default)
            => Task.FromResult(Create79AiRuntime());

        public Task<RVideo79AiProviderImageAsset> UploadSourceImageAsync(
            RVideo79AiRuntime runtime,
            RVideo79AiVideoSourceImage source,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<RVideo79AiVideoSubmitResult> SubmitAsync(RVideo79AiVideoSubmitRequest request, CancellationToken ct = default)
        {
            Submits.Add(request);
            return Task.FromResult(new RVideo79AiVideoSubmitResult(
                $"task-{Submits.Count}",
                """{"ok":true}""",
                """{"provider":"79ai"}"""));
        }

        public Task<Ai79TaskStatusResult> PollAsync(RVideo79AiRuntime runtime, string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class CapturingVideoGenerationProviderAdapter : IVideoGenerationProviderAdapter
    {
        public List<VideoProviderSubmitRequest> Submits { get; } = [];

        public bool CanHandle(string providerCode, string capabilityCode)
            => string.Equals(providerCode, "79ai", StringComparison.OrdinalIgnoreCase)
               && string.Equals(capabilityCode, RVideoVideoModelPolicy.CapabilityCode, StringComparison.OrdinalIgnoreCase);

        public Task<VideoProviderSubmitResult> SubmitAsync(VideoProviderSubmitRequest request, CancellationToken ct = default)
        {
            Submits.Add(request);
            return Task.FromResult(new VideoProviderSubmitResult(
                request.ProviderCode,
                $"task-{Submits.Count}",
                request.RequestedModel,
                """{"provider":"79ai"}""",
                """{"ok":true}"""));
        }

        public Task<VideoProviderPollResult> PollAsync(VideoProviderPollRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class ScriptedVideoGenerationProviderAdapter : IVideoGenerationProviderAdapter
    {
        private readonly Queue<VideoProviderTaskStatus> _statuses;

        public ScriptedVideoGenerationProviderAdapter(params VideoProviderTaskStatus[] statuses)
        {
            _statuses = new Queue<VideoProviderTaskStatus>(statuses);
        }

        public List<VideoProviderSubmitRequest> Submits { get; } = [];
        public List<VideoProviderPollResult> Polls { get; } = [];
        public List<string> EventCodes { get; } = [];

        public bool CanHandle(string providerCode, string capabilityCode)
            => string.Equals(providerCode, "79ai", StringComparison.OrdinalIgnoreCase)
               && string.Equals(capabilityCode, RVideoVideoModelPolicy.CapabilityCode, StringComparison.OrdinalIgnoreCase);

        public Task<VideoProviderSubmitResult> SubmitAsync(VideoProviderSubmitRequest request, CancellationToken ct = default)
        {
            Submits.Add(request);
            return Task.FromResult(new VideoProviderSubmitResult(
                request.ProviderCode,
                $"task-{Submits.Count}",
                request.RequestedModel,
                """{"provider":"79ai"}""",
                """{"ok":true}"""));
        }

        public Task<VideoProviderPollResult> PollAsync(VideoProviderPollRequest request, CancellationToken ct = default)
        {
            var status = _statuses.Dequeue();
            var result = new VideoProviderPollResult(
                status,
                request.ProviderTaskId,
                status == VideoProviderTaskStatus.Success ? "https://cdn.example/video.mp4" : null,
                null,
                status == VideoProviderTaskStatus.Failed ? "provider_failure" : null,
                status == VideoProviderTaskStatus.Failed ? "Provider rejected the Prompt. #22f" : null,
                """{"status":"scripted"}""");
            Polls.Add(result);
            return Task.FromResult(result);
        }
    }

    private class RenderJobServiceProxy : DispatchProxy
    {
        public string? EventType { get; private set; }
        public string? DataJson { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IRenderJobService.AddEventAsync))
            {
                EventType = (string)args![1]!;
                DataJson = JsonSerializer.Serialize(args[3]);
                return Task.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
    }

    private sealed class CapturingHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        private readonly HttpStatusCode _statusCode;

        public CapturingHttpMessageHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _responseJson = responseJson;
            _statusCode = statusCode;
        }

        public Dictionary<string, string> Form { get; } = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                Form[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace("+", " ", StringComparison.Ordinal));
            }

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_responseJson)
            };
        }
    }

    private sealed class SequencedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public SequencedHttpMessageHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<Dictionary<string, string>> Forms { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = new Dictionary<string, string>(StringComparer.Ordinal);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                form[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace("+", " ", StringComparison.Ordinal));
            }
            Forms.Add(form);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Dequeue())
            };
        }
    }

    private sealed class FakeServiceScope : IServiceScope
    {
        public FakeServiceScope(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public FakeServiceProvider(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public object? GetService(Type serviceType)
            => serviceType == typeof(IServiceScopeFactory) ? _scopeFactory : null;
    }

    private sealed class FakeServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _provider;

        public FakeServiceScopeFactory(IServiceProvider provider)
        {
            _provider = provider;
        }

        public IServiceScope CreateScope()
            => new FakeServiceScope(_provider);
    }

    private sealed class SceneMediaVersioningSyncProxy : DispatchProxy
    {
        public SceneVideoVersionDto? Version { get; set; }
        public Guid? FailedVersionId { get; private set; }
        public string? FailedErrorCode { get; private set; }
        public string? FailedErrorMessage { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(ISceneMediaVersioningService.GetSceneVideoVersionByLogicalRequestIdAsync))
            {
                var logicalRequestId = (string)args![0]!;
                return Task.FromResult(Version?.LogicalRequestId == logicalRequestId ? Version : null);
            }

            if (targetMethod?.Name == nameof(ISceneMediaVersioningService.FailSceneVideoVersionAsync))
            {
                FailedVersionId = (Guid)args![0]!;
                FailedErrorCode = (string?)args[1];
                FailedErrorMessage = (string?)args[2];
                return Task.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }
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
