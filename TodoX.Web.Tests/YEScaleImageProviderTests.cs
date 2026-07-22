using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public class YEScaleImageProviderTests
{
    [Fact]
    public void Mapper_BuildsNanoBananaPayload_WithOnlySupportedFields()
    {
        var payload = YEScaleImageModelMapper.BuildSubmitRequest(Request(), "nano-banana-2",
            new YEScaleImageRoutingConfig { AdapterProfile = "nano_banana_2", Size = "0.5K", GoogleSearch = "disable", Thinking = "minimal" });
        var json = JsonSerializer.Serialize(payload, JsonOptions());

        Assert.Contains("\"model\":\"nano-banana-2\"", json);
        Assert.Contains("\"images\":[\"https://cdn.example/ref.png\"]", json);
        Assert.Contains("\"aspect_ratio\":\"1:1\"", json);
        Assert.Contains("\"size\":\"0.5K\"", json);
        Assert.Contains("\"google_search\":\"disable\"", json);
        Assert.Contains("\"thinking\":\"minimal\"", json);
        Assert.DoesNotContain("\"quality\"", json);
        Assert.DoesNotContain("\"background\"", json);
    }

    [Fact]
    public void Mapper_BuildsGptImagePayload_WithOnlySupportedFields()
    {
        var payload = YEScaleImageModelMapper.BuildSubmitRequest(Request(resolution: "1024x1024"), "gpt-image",
            new YEScaleImageRoutingConfig { AdapterProfile = "gpt_image", Quality = "low", Background = "transparent" });
        var json = JsonSerializer.Serialize(payload, JsonOptions());

        Assert.Contains("\"model\":\"gpt-image\"", json);
        Assert.Contains("\"images\":[\"https://cdn.example/ref.png\"]", json);
        Assert.Contains("\"size\":\"1024x1024\"", json);
        Assert.Contains("\"quality\":\"low\"", json);
        Assert.Contains("\"background\":\"transparent\"", json);
        Assert.DoesNotContain("\"google_search\"", json);
        Assert.DoesNotContain("\"thinking\"", json);
        Assert.DoesNotContain("\"aspect_ratio\"", json);
    }

    [Fact]
    public void Mapper_BuildsSeedreamPayload_WithOnlySupportedFields()
    {
        var payload = YEScaleImageModelMapper.BuildSubmitRequest(Request(resolution: "2K"), "seedream-5", new YEScaleImageRoutingConfig { AdapterProfile = "seedream_5" });
        var json = JsonSerializer.Serialize(payload, JsonOptions());

        Assert.Contains("\"model\":\"seedream-5\"", json);
        Assert.Contains("\"images\":[\"https://cdn.example/ref.png\"]", json);
        Assert.Contains("\"size\":\"2K\"", json);
        Assert.DoesNotContain("\"quality\"", json);
        Assert.DoesNotContain("\"background\"", json);
        Assert.DoesNotContain("\"google_search\"", json);
        Assert.DoesNotContain("\"thinking\"", json);
        Assert.DoesNotContain("\"aspect_ratio\"", json);
    }

    [Fact]
    public void Mapper_RejectsUnsupportedModelSpecificSize()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            YEScaleImageModelMapper.BuildSubmitRequest(Request(resolution: "8K"), "nano-banana-2", new YEScaleImageRoutingConfig { AdapterProfile = "nano_banana_2" }));
        Assert.Contains("size '8K'", ex.Message);
    }

    [Fact]
    public void Mapper_RequiresAdapterProfile_FromConfig()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            YEScaleImageModelMapper.BuildSubmitRequest(Request(), "nano-banana-2", new YEScaleImageRoutingConfig()));
        Assert.Contains("adapter_profile", ex.Message);
    }

    [Fact]
    public async Task TaskClient_SubmitPollSuccess_ReturnsTerminalResult()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"task_id":"task-1"}"""),
            Json(HttpStatusCode.OK, """{"task_id":"task-1","status":"SUCCESS","output":{"url":"https://cdn.example/out.png"}}"""));
        var client = CreateClient(handler);

        var result = await client.SubmitAndWaitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        });

        Assert.Equal("task-1", result.TaskId);
        Assert.Equal("SUCCESS", result.Status);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/task/task-1", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.True(handler.Requests.All(r => r.Headers.Authorization?.Scheme == "Bearer"));
    }

    [Fact]
    public async Task TaskClient_SubmitMissingTaskId_ThrowsWithoutPolling()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"status":"submitted"}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<YEScaleTaskException>(() => client.SubmitAndWaitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        }));

        Assert.Contains("missing task_id", ex.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TaskClient_Disabled_DoesNotSendHttpRequest()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"task_id":"should-not-send"}"""));
        var client = CreateClient(handler, enabled: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        }));

        Assert.Contains("YEScale đang bị tắt", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TaskClient_MissingProviderAccountCredential_DoesNotSendHttpRequest()
    {
        var handler = new QueueHandler(Json(HttpStatusCode.OK, """{"task_id":"should-not-send"}"""));
        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync(new YEScaleTaskSubmitRequest
        {
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        }));

        Assert.Contains("YESCALE_PROVIDER_ACCOUNT_CREDENTIAL_REQUIRED", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TaskClient_SubmitPollFailure_ReturnsFailureStatus()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"task_id":"task-2"}"""),
            Json(HttpStatusCode.OK, """{"task_id":"task-2","status":"FAILURE","error":{"code":"bad_prompt"}}"""));
        var client = CreateClient(handler);

        var result = await client.SubmitAndWaitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "gpt-image",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1024x1024" }
        });

        Assert.Equal("FAILURE", result.Status);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TaskClient_PollTransientError_ContinuesSameTaskIdWithoutResubmit()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"task_id":"task-3"}"""),
            Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limit"}}"""),
            Json(HttpStatusCode.OK, """{"task_id":"task-3","status":"SUCCESS","output":{"url":"https://cdn.example/out.png"}}"""));
        var client = CreateClient(handler, retryCount: 1);

        var result = await client.SubmitAndWaitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "seedream-5",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "2K" }
        });

        Assert.Equal("SUCCESS", result.Status);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
        Assert.Equal(2, handler.Requests.Count(r => r.Method == HttpMethod.Get));
        Assert.All(handler.Requests.Where(r => r.Method == HttpMethod.Get), r => Assert.Equal("/task/task-3", r.RequestUri!.AbsolutePath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task TaskClient_AuthErrors_DoNotRetry(HttpStatusCode statusCode)
    {
        var handler = new QueueHandler(Json(statusCode, """{"error":{"code":"auth"}}"""));
        var client = CreateClient(handler, retryCount: 3);

        await Assert.ThrowsAsync<YEScaleTaskException>(() => client.SubmitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        }));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task TaskClient_RequestTimeout_IsTransientAndRetries()
    {
        var handler = new SlowThenSuccessHandler();
        var client = CreateClient(handler, retryCount: 1, requestTimeoutSeconds: 1);

        var result = await client.SubmitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        });

        Assert.Equal("task-timeout-ok", result.TaskId);
        Assert.Equal(2, handler.Requests);
    }

    [Fact]
    public async Task TaskClient_RetryAfter_IsHonored()
    {
        var retry = Json(HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limit"}}""");
        retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        var handler = new QueueHandler(retry, Json(HttpStatusCode.OK, """{"task_id":"task-after"}"""));
        var client = CreateClient(handler, retryCount: 1);
        var started = DateTimeOffset.UtcNow;

        var result = await client.SubmitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        });

        Assert.Equal("task-after", result.TaskId);
        Assert.True(DateTimeOffset.UtcNow - started >= TimeSpan.FromMilliseconds(900));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task TaskClient_PollTimeout_ThrowsTransientWithTaskId()
    {
        var handler = new PendingForeverHandler();
        var client = CreateClient(handler, pollTimeoutSeconds: 1);

        var ex = await Assert.ThrowsAsync<YEScaleTaskException>(() => client.SubmitAndWaitAsync(new YEScaleTaskSubmitRequest
        {
            ApiKey = "test-key",
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        }));

        Assert.True(ex.IsTransient);
        Assert.Equal("task-timeout", ex.TaskId);
        Assert.Single(handler.Requests, r => r.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task ImageAdapter_FallsBackOnTransientFailure_AndReturnsSecondModel()
    {
        var fake = new FakeTaskClient(new YEScaleTaskException("rate", 429, "rate_limit", transient: true), new YEScaleTaskResult
        {
            TaskId = "task-ok",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status("""{"status":"SUCCESS","output":{"url":"https://cdn.example/fallback.png"}}"""),
            TerminalResponseJson = """{"status":"SUCCESS","output":{"url":"https://cdn.example/fallback.png"}}"""
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(model: "nano-banana-2", capabilityConfigJson: """{"adapter_profile":"nano_banana_2","model_profiles":{"seedream-5":"seedream_5"},"model_sizes":{"seedream-5":"2K"},"fallback_models":["seedream-5"],"size":"1K"}"""));

        Assert.True(response.Success);
        Assert.Equal("seedream-5", response.ModelName);
        Assert.Equal("https://cdn.example/fallback.png", response.ImageUrl);
        Assert.Equal(2, fake.SubmitCalls);
    }

    [Fact]
    public async Task ImageAdapter_TerminalTransientFailure_FallsBackAndRecordsTrail()
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-rate",
            Status = "FAILURE",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status("""{"task_id":"task-rate","status":"FAILURE","error":{"code":"rate_limit","type":"quota","message":"Rate limited"}}"""),
            TerminalResponseJson = """{"task_id":"task-rate","status":"FAILURE","error":{"code":"rate_limit","type":"quota","message":"Rate limited"}}"""
        }, SuccessResult("task-ok", "https://cdn.example/fallback.png"));
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleFallbackConfig("""["rate_limit"]""")));

        Assert.True(response.Success);
        Assert.Equal("seedream-5", response.ModelName);
        Assert.Contains("\"taskId\":\"task-rate\"", response.UsageJson);
        Assert.Contains("\"errorCode\":\"rate_limit\"", response.UsageJson);
        Assert.Equal(2, fake.SubmitCalls);
    }

    [Theory]
    [InlineData("bad_prompt")]
    [InlineData("mystery_error")]
    public async Task ImageAdapter_TerminalNonTransientFailure_DoesNotFallback(string errorCode)
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-fail",
            Status = "FAILURE",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(FailureJson("task-fail", errorCode, "No retry")),
            TerminalResponseJson = FailureJson("task-fail", errorCode, "No retry")
        }, SuccessResult("task-should-not-run", "https://cdn.example/unused.png"));
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleFallbackConfig("""["rate_limit"]""")));

        Assert.False(response.Success);
        Assert.Equal("nano-banana-2", response.ModelName);
        Assert.Equal(1, fake.SubmitCalls);
    }

    [Fact]
    public async Task ImageAdapter_TerminalTransientFailureAtEndOfChain_ReturnsFailure()
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-last",
            Status = "FAILURE",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status("""{"task_id":"task-last","status":"FAILURE","error":{"code":"rate_limit","message":"Rate limited"}}"""),
            TerminalResponseJson = """{"task_id":"task-last","status":"FAILURE","error":{"code":"rate_limit","message":"Rate limited"}}"""
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(model: "seedream-5", resolution: "2K", capabilityConfigJson: """{"adapter_profile":"seedream_5","transient_terminal_error_codes":["rate_limit"],"size":"2K"}"""));

        Assert.False(response.Success);
        Assert.Equal("seedream-5", response.ModelName);
        Assert.Equal(1, fake.SubmitCalls);
    }

    [Fact]
    public async Task ImageAdapter_Cancellation_DoesNotFallback()
    {
        var fake = new FakeTaskClient(new OperationCanceledException());
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleFallbackConfig("""["rate_limit"]"""))));

        Assert.Equal(1, fake.SubmitCalls);
    }

    [Fact]
    public async Task ImageAdapter_SuccessWithUrl_ReturnsImageUrl()
    {
        var fake = new FakeTaskClient(SuccessResult("task-url", "https://cdn.example/out.png"));
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.True(response.Success);
        Assert.Equal("https://cdn.example/out.png", response.ImageUrl);
    }

    [Fact]
    public async Task ImageAdapter_TaskResultUrl_ReturnsImageUrlAndTaskMetadata()
    {
        const string json = """
        {
          "finish_time": 1784241504,
          "message": "YEScale - YESCALE_MEDIA - nano-banana-2 - Task Result",
          "progress": "100%",
          "status": "SUCCESS",
          "submit_time": 1784241488,
          "task_id": "yescale-nano-banana-2-prod",
          "task_result": {
            "note": "temporary link",
            "url": "https://cdn.yescale.vip/yescale-nano-banana-2-prod.png",
            "url_expires_at": "2026-08-01 05:38:24"
          }
        }
        """;
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "yescale-nano-banana-2-prod",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(json),
            TerminalResponseJson = json
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.True(response.Success);
        Assert.Equal("https://cdn.yescale.vip/yescale-nano-banana-2-prod.png", response.ImageUrl);
        Assert.Contains("\"taskId\":\"yescale-nano-banana-2-prod\"", response.UsageJson);
        Assert.Contains("\"finishTime\":1784241504", response.UsageJson);
        Assert.Contains("\"submitTime\":1784241488", response.UsageJson);
        Assert.Contains("\"urlExpiresAt\":\"2026-08-01 05:38:24\"", response.UsageJson);
        Assert.Contains("\"model\":\"nano-banana-2\"", response.UsageJson);
        Assert.Contains("\"providerCode\":\"yescale_task_image\"", response.UsageJson);
    }

    [Theory]
    [InlineData("""{"status":"SUCCESS","task_id":"task-query","task_result":{"url":"https://cdn.yescale.vip/signed-output?token=abc123"}}""", "https://cdn.yescale.vip/signed-output?token=abc123")]
    [InlineData("""{"status":"SUCCESS","task_id":"task-output","output":{"url":"https://cdn.example/output-without-extension?sig=1"}}""", "https://cdn.example/output-without-extension?sig=1")]
    [InlineData("""{"status":"SUCCESS","task_id":"task-result","result":{"url":"https://cdn.example/result.png"}}""", "https://cdn.example/result.png")]
    [InlineData("""{"status":"SUCCESS","task_id":"task-data","data":{"url":"https://cdn.example/data.webp"}}""", "https://cdn.example/data.webp")]
    [InlineData("""{"status":"SUCCESS","task_id":"task-images","task_result":{"images":["https://cdn.example/image-list?sig=2"]}}""", "https://cdn.example/image-list?sig=2")]
    public async Task ImageAdapter_SupportedOutputShapes_ReturnImageUrl(string json, string expectedUrl)
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-shape",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(json),
            TerminalResponseJson = json
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.True(response.Success);
        Assert.Equal(expectedUrl, response.ImageUrl);
    }

    [Fact]
    public async Task ImageAdapter_NonOutputUrls_AreNotMistakenForOutputImage()
    {
        const string json = """
        {
          "status": "SUCCESS",
          "task_id": "task-non-output",
          "response": {
            "callback_url": "https://example.com/callback.png",
            "metadata_url": "https://example.com/meta.png",
            "documentation_url": "https://example.com/docs.png",
            "input_image_url": "https://example.com/input.png",
            "message": "Render succeeded, see https://example.com/not-output.png"
          }
        }
        """;
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-non-output",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(json),
            TerminalResponseJson = json
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.False(response.Success);
        Assert.Null(response.ImageUrl);
        Assert.Contains("task_id=task-non-output", response.ErrorMessage);
    }

    [Fact]
    public async Task ImageAdapter_SuccessWithBase64_ReturnsImageBytes()
    {
        var png = Convert.ToBase64String(new byte[] { 137, 80, 78, 71 });
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-b64",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(Base64SuccessJson(png)),
            TerminalResponseJson = Base64SuccessJson(png)
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.True(response.Success);
        Assert.NotNull(response.ImageBytes);
        Assert.Equal("image/png", response.MimeType);
    }

    [Fact]
    public async Task ImageAdapter_SuccessWithoutOutputImage_ReturnsFailure()
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-empty",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status("""{"status":"SUCCESS","output":{"text":"done"}}"""),
            TerminalResponseJson = """{"status":"SUCCESS","output":{"text":"done"}}"""
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.False(response.Success);
        Assert.Contains("không tìm thấy", response.ErrorMessage);
    }

    [Fact]
    public async Task ImageAdapter_MetadataUrl_IsNotMistakenForOutputImage()
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-meta",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status("""{"status":"SUCCESS","metadata":{"callback_url":"https://example.com/callback.png"}}"""),
            TerminalResponseJson = """{"status":"SUCCESS","metadata":{"callback_url":"https://example.com/callback.png"}}"""
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.False(response.Success);
        Assert.Null(response.ImageUrl);
    }

    [Fact]
    public async Task ImageAdapter_OutputMetadataUrl_IsNotMistakenForOutputImage()
    {
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-output-meta",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status("""{"status":"SUCCESS","output":{"metadata":{"url":"https://example.com/docs.png"}}}"""),
            TerminalResponseJson = """{"status":"SUCCESS","output":{"metadata":{"url":"https://example.com/docs.png"}}}"""
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.False(response.Success);
        Assert.Null(response.ImageUrl);
    }

    [Fact]
    public async Task ImageAdapter_RawRequest_DoesNotContainAccessKey()
    {
        var fake = new FakeTaskClient(SuccessResult("task-url", "https://cdn.example/out.png"));
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.DoesNotContain("test-key", response.RawRequestJson);
        Assert.DoesNotContain("Bearer", response.RawRequestJson);
    }

    [Fact]
    public async Task ImageAdapter_RawResponse_RedactsSensitiveFields()
    {
        const string json = """
        {
          "status": "SUCCESS",
          "task_id": "task-secret",
          "task_result": { "url": "https://cdn.example/out.png" },
          "authorization": "Bearer should-not-leak",
          "nested": { "access_key": "secret-value", "apiKey": "api-secret" }
        }
        """;
        var fake = new FakeTaskClient(new YEScaleTaskResult
        {
            TaskId = "task-secret",
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(json),
            TerminalResponseJson = json
        });
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var response = await service.GenerateImageAsync(Request(capabilityConfigJson: YEScaleBaseConfig()));

        Assert.True(response.Success);
        Assert.DoesNotContain("should-not-leak", response.RawResponseJson);
        Assert.DoesNotContain("secret-value", response.RawResponseJson);
        Assert.DoesNotContain("api-secret", response.RawResponseJson);
        Assert.Contains("[redacted]", response.RawResponseJson);
    }

    [Fact]
    public async Task RecoverImageAsync_GetsExistingSuccessfulTaskWithoutSubmit()
    {
        const string json = """
        {"status":"SUCCESS","task_id":"task-recover","task_result":{"url":"https://cdn.yescale.vip/recover?sig=1"}}
        """;
        var fake = new FakeTaskClient(Status(json), Status(json));
        var service = new YEScaleImageService(fake, NullLogger<YEScaleImageService>.Instance);

        var first = await service.RecoverImageAsync("task-recover", "nano-banana-2", "test-key");
        var second = await service.RecoverImageAsync("task-recover", "nano-banana-2", "test-key");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("https://cdn.yescale.vip/recover?sig=1", first.ImageUrl);
        Assert.Equal("https://cdn.yescale.vip/recover?sig=1", second.ImageUrl);
        Assert.Equal(0, fake.SubmitCalls);
        Assert.Equal(2, fake.GetStatusCalls);
    }

    private static OpenRouterImageRequest Request(string model = "nano-banana-2", string resolution = "1K", string? capabilityConfigJson = null)
        => new()
        {
            Model = model,
            Prompt = "Draw a clean product image",
            AspectRatio = "1:1",
            Resolution = resolution,
            Quality = "high",
            ReferenceImageUrls = new[] { "https://cdn.example/ref.png" },
            CapabilityConfigJson = capabilityConfigJson
        };

    private static YEScaleTaskClient CreateClient(HttpMessageHandler handler, int retryCount = 0, int requestTimeoutSeconds = 15, int pollTimeoutSeconds = 30, bool enabled = true)
        => new(
            new HttpClient(handler),
            new StaticOptionsMonitor<YEScaleOptions>(new YEScaleOptions
            {
                Enabled = enabled,
                BaseUrl = "https://api.yescale.io",
                PollIntervalSeconds = 1,
                PollTimeoutSeconds = pollTimeoutSeconds,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                MaxTransientRetries = retryCount
            }),
            NullLogger<YEScaleTaskClient>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static YEScaleTaskStatusResponse Status(string json)
        => JsonSerializer.Deserialize<YEScaleTaskStatusResponse>(json, JsonOptions())!;

    private static YEScaleTaskResult SuccessResult(string taskId, string url)
        => new()
        {
            TaskId = taskId,
            Status = "SUCCESS",
            Duration = TimeSpan.FromMilliseconds(5),
            TerminalResponse = Status(SuccessJson(taskId, url)),
            TerminalResponseJson = SuccessJson(taskId, url)
        };

    private static string SuccessJson(string taskId, string url)
        => JsonSerializer.Serialize(new { task_id = taskId, status = "SUCCESS", output = new { url } }, JsonOptions());

    private static string Base64SuccessJson(string base64)
        => JsonSerializer.Serialize(new { status = "SUCCESS", output = new { b64_json = $"data:image/png;base64,{base64}" } }, JsonOptions());

    private static string FailureJson(string taskId, string errorCode, string message)
        => JsonSerializer.Serialize(new { task_id = taskId, status = "FAILURE", error = new { code = errorCode, message } }, JsonOptions());

    private static string YEScaleBaseConfig()
        => """{"adapter_profile":"nano_banana_2","size":"1K"}""";

    private static string YEScaleFallbackConfig(string transientCodes)
        => $$"""{"adapter_profile":"nano_banana_2","model_profiles":{"seedream-5":"seedream_5"},"model_sizes":{"seedream-5":"2K"},"fallback_models":["seedream-5"],"transient_terminal_error_codes":{{transientCodes}},"size":"1K"}""";

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class QueueHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public List<HttpRequestMessage> Requests { get; } = new();

        public QueueHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Headers.Authorization is AuthenticationHeaderValue auth)
            {
                clone.Headers.Authorization = auth;
            }

            if (request.Content is not null)
            {
                clone.Content = new StringContent(await request.Content.ReadAsStringAsync(cancellationToken), Encoding.UTF8, "application/json");
            }

            Requests.Add(clone);
            return _responses.Dequeue();
        }
    }

    private sealed class SlowThenSuccessHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Requests == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return Json(HttpStatusCode.OK, """{"task_id":"task-timeout-ok"}""");
        }
    }

    private sealed class PendingForeverHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            var json = request.Method == HttpMethod.Post
                ? """{"task_id":"task-timeout"}"""
                : """{"task_id":"task-timeout","status":"PENDING"}""";
            return Task.FromResult(Json(HttpStatusCode.OK, json));
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value) => CurrentValue = value;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class FakeTaskClient : IYEScaleTaskClient
    {
        private readonly Queue<object> _results;
        public int SubmitCalls { get; private set; }
        public int GetStatusCalls { get; private set; }

        public FakeTaskClient(params object[] results)
        {
            _results = new Queue<object>(results);
        }

        public Task<YEScaleTaskSubmitResponse> SubmitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<YEScaleTaskStatusResponse> GetStatusAsync(string taskId, CancellationToken ct = default)
            => GetStatusAsync(taskId, apiKey: "test-key", ct);

        public Task<YEScaleTaskStatusResponse> GetStatusAsync(string taskId, string? apiKey, CancellationToken ct = default)
        {
            GetStatusCalls++;
            var next = _results.Dequeue();
            if (next is Exception ex) throw ex;
            return Task.FromResult((YEScaleTaskStatusResponse)next);
        }

        public Task<YEScaleTaskResult> SubmitAndWaitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default)
        {
            SubmitCalls++;
            var next = _results.Dequeue();
            if (next is Exception ex) throw ex;
            return Task.FromResult((YEScaleTaskResult)next);
        }
    }
}
