using System.Net;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class Ai79TaskClientTests
{
    [Fact]
    public async Task ImageSubmit_UsesVerifiedGenerateImageContractAndParsesNestedTaskId()
    {
        var handler = new RecordingJsonHandler("""
            {"code":0,"data":{"task_id":"img-task-001","status":"SUBMITTED","access_token":"secret-token"}}
            """);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/generateImage",
            "secret-token",
            "79ai.net",
            "image-model",
            "construction progress",
            ["https://cdn.example/source.png"],
            new Dictionary<string, string?> { ["ratio"] = "9:16" },
            "image"));

        Assert.Equal("img-task-001", result.TaskId);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson, StringComparison.Ordinal);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.gommo.net/ai/generateImage", request.Uri);
        Assert.Contains("access_token=secret-token", request.Body, StringComparison.Ordinal);
        Assert.Contains("domain=79ai.net", request.Body, StringComparison.Ordinal);
        Assert.Contains("model=image-model", request.Body, StringComparison.Ordinal);
        Assert.Contains("prompt=construction+progress", request.Body, StringComparison.Ordinal);
        Assert.Contains("image=https%3A%2F%2Fcdn.example%2Fsource.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("ratio=9%3A16", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("images=", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VideoSubmit_UsesVerifiedCreateVideoContractWithStartAndEndImages()
    {
        var handler = new RecordingJsonHandler("""{"data":{"request_id":"video-task-001"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/create-video",
            "secret-token",
            "79ai.net",
            "seedance_20_pro",
            "transition prompt",
            ["https://cdn.example/start.png", "https://cdn.example/end.png"],
            new Dictionary<string, string?> { ["mode"] = "fast", ["duration"] = "6", ["ratio"] = "16:9" },
            "image",
            "image_2"));

        Assert.Equal("video-task-001", result.TaskId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.gommo.net/ai/create-video", request.Uri);
        Assert.Contains("image=https%3A%2F%2Fcdn.example%2Fstart.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("image_2=https%3A%2F%2Fcdn.example%2Fend.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("mode=fast", request.Body, StringComparison.Ordinal);
        Assert.Contains("duration=6", request.Body, StringComparison.Ordinal);
        Assert.Contains("ratio=16%3A9", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("images=", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"data":{"task_id":"img-task-001","status":"RUNNING"}}""")]
    [InlineData("""{"task":{"request_id":"img-task-001","state":"processing"}}""")]
    public async Task ImagePoll_RunningFixtureNormalizesToRunning(string json)
    {
        var result = await PollAsync("/image", json);

        Assert.Equal(Ai79TaskStatusNormalizer.Running, result.NormalizedStatus);
        Assert.Null(result.OutputUrl);
    }

    [Theory]
    [InlineData("""{"data":{"task_id":"img-task-001","status":"SUCCESS","image_url":"https://cdn.example/out.png"}}""", "https://cdn.example/out.png")]
    [InlineData("""{"result":{"task_id":"video-task-001","task_status":"COMPLETED","video_url":"https://cdn.example/out.mp4"}}""", "https://cdn.example/out.mp4")]
    public async Task Poll_SuccessFixtureExtractsOutputUrl(string json, string expectedUrl)
    {
        var result = await PollAsync("/video", json);

        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal(expectedUrl, result.OutputUrl);
    }

    [Theory]
    [InlineData("""{"error":{"code":"bad_prompt","message":"Prompt rejected"},"data":{"status":"FAILED"}}""", "bad_prompt", "Prompt rejected")]
    [InlineData("""{"response":{"state":"ERROR","errorCode":"provider_error","errorMessage":"Provider failed"}}""", "provider_error", "Provider failed")]
    public async Task Poll_FailureFixtureExtractsSafeError(string json, string code, string message)
    {
        var result = await PollAsync("/image", json);

        Assert.Equal(Ai79TaskStatusNormalizer.Failed, result.NormalizedStatus);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(message, result.ErrorMessage);
    }

    [Fact]
    public async Task Submit_DoesNotTreatUnrelatedIdAsTaskId()
    {
        var handler = new RecordingJsonHandler("""{"data":{"id":"model-row-1","status":"ok","access_token":"secret-token"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<Ai79TaskSubmitException>(() => client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/generateImage",
            "secret-token",
            "79ai.net",
            "image-model",
            "prompt",
            ["https://cdn.example/source.png"],
            new Dictionary<string, string?>(),
            "image")));

        Assert.Equal("missing_task_id", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ex.HttpStatusCode);
        Assert.Contains("missing async task identifier", ex.Message, StringComparison.Ordinal);
        Assert.Contains("model-row-1", ex.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", ex.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.Contains("***", ex.SanitizedResponseJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submit_Http200ProviderErrorSurfacesSafeMessageAndRetainsResponse()
    {
        var handler = new RecordingJsonHandler("""
            {"success":false,"error":{"code":"invalid_model","message":"Model is unavailable"},"access_token":"secret-token"}
            """);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<Ai79TaskSubmitException>(() => client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/generateImage",
            "secret-token",
            "79ai.net",
            "seedream_5_0",
            "prompt",
            ["https://cdn.example/source.png"],
            new Dictionary<string, string?> { ["ratio"] = "16:9" },
            "image")));

        Assert.Equal("invalid_model", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ex.HttpStatusCode);
        Assert.Equal("79AI image submit failed: Model is unavailable", ex.ErrorMessage);
        Assert.Contains("invalid_model", ex.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", ex.SanitizedResponseJson, StringComparison.Ordinal);
    }

    private static async Task<Ai79TaskStatusResult> PollAsync(string path, string json)
    {
        var handler = new RecordingJsonHandler(json);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.GetStatusAsync(new Ai79TaskStatusRequest(
            "https://api.gommo.net/ai",
            path,
            "secret-token",
            "79ai.net",
            "task-001"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"https://api.gommo.net/ai{path}", request.Uri);
        Assert.Contains("task_id=task-001", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson, StringComparison.Ordinal);
        return result;
    }

    private sealed class RecordingJsonHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;
        public List<RequestSnapshot> Requests { get; } = new();

        public RecordingJsonHandler(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(request.RequestUri!.ToString(), body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responses.Count == 0 ? "{}" : _responses.Dequeue())
            };
        }
    }

    private sealed record RequestSnapshot(string Uri, string Body);
}
