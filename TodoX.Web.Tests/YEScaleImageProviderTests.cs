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
    public async Task TaskClient_SubmitPollFailure_ReturnsFailureStatus()
    {
        var handler = new QueueHandler(
            Json(HttpStatusCode.OK, """{"task_id":"task-2"}"""),
            Json(HttpStatusCode.OK, """{"task_id":"task-2","status":"FAILURE","error":{"code":"bad_prompt"}}"""));
        var client = CreateClient(handler);

        var result = await client.SubmitAndWaitAsync(new YEScaleTaskSubmitRequest
        {
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
            Model = "nano-banana-2",
            Prompt = "hello",
            Config = new YEScaleImageTaskConfig { Size = "1K" }
        });

        Assert.Equal("task-timeout-ok", result.TaskId);
        Assert.Equal(2, handler.Requests);
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

    private static YEScaleTaskClient CreateClient(HttpMessageHandler handler, int retryCount = 0, int requestTimeoutSeconds = 15, int pollTimeoutSeconds = 30)
        => new(
            new HttpClient(handler),
            new StaticOptionsMonitor<YEScaleOptions>(new YEScaleOptions
            {
                Enabled = true,
                BaseUrl = "https://api.yescale.io",
                AccessKey = "test-key",
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

        public FakeTaskClient(params object[] results)
        {
            _results = new Queue<object>(results);
        }

        public Task<YEScaleTaskSubmitResponse> SubmitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<YEScaleTaskStatusResponse> GetStatusAsync(string taskId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<YEScaleTaskResult> SubmitAndWaitAsync(YEScaleTaskSubmitRequest request, CancellationToken ct = default)
        {
            SubmitCalls++;
            var next = _results.Dequeue();
            if (next is Exception ex) throw ex;
            return Task.FromResult((YEScaleTaskResult)next);
        }
    }
}
