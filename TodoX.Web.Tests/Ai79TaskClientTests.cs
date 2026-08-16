using System.Net;
using TodoX.Web.Services.AiProviders;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class Ai79TaskClientTests
{
    [Theory]
    [InlineData("""{"imageInfo":{"id_base":"abc123"}}""")]
    [InlineData("""{"data":{"imageInfo":{"id_base":"abc123"}}}""")]
    public async Task ImageSubmit_UsesVerifiedGenerateImageContractAndParsesIdBase(string responseJson)
    {
        var handler = new RecordingJsonHandler(responseJson);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/generateImage",
            "secret-token",
            "79ai.net",
            "seedream_5_0",
            "construction progress",
            ["data:image/jpeg;base64,AQID"],
            new Dictionary<string, string?>
            {
                ["action_type"] = "create",
                ["editImage"] = "true",
                ["project_id"] = "default",
                ["subjects"] = "[]",
                ["ratio"] = "9_16",
                ["mode"] = "vip",
                ["resolution"] = "2k"
            },
            Ai79TaskOperation.Image,
            "base64Image"));

        Assert.Equal("abc123", result.TaskId);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson, StringComparison.Ordinal);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.gommo.net/ai/generateImage", request.Uri);
        Assert.Contains("access_token=secret-token", request.Body, StringComparison.Ordinal);
        Assert.Contains("domain=79ai.net", request.Body, StringComparison.Ordinal);
        Assert.Contains("model=seedream_5_0", request.Body, StringComparison.Ordinal);
        Assert.Contains("prompt=construction+progress", request.Body, StringComparison.Ordinal);
        Assert.Contains("action_type=create", request.Body, StringComparison.Ordinal);
        Assert.Contains("editImage=true", request.Body, StringComparison.Ordinal);
        Assert.Contains("base64Image=data%3Aimage%2Fjpeg%3Bbase64%2CAQID", request.Body, StringComparison.Ordinal);
        Assert.Contains("project_id=default", request.Body, StringComparison.Ordinal);
        Assert.Contains("subjects=%5B%5D", request.Body, StringComparison.Ordinal);
        Assert.Contains("ratio=9_16", request.Body, StringComparison.Ordinal);
        Assert.Contains("mode=vip", request.Body, StringComparison.Ordinal);
        Assert.Contains("resolution=2k", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("image=https", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("images=", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VideoSubmit_UsesVerifiedCreateVideoContractWithStartAndEndImageDescriptors()
    {
        var handler = new RecordingJsonHandler("""{"data":{"request_id":"video-task-001"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));
        var imagesJson = """[{"id_base":"start-id","project_id":"default","url":"https://cdn.example/start.png","file_name":"start.png"},{"id_base":"end-id","project_id":"default","url":"https://cdn.example/end.png","file_name":"end.png"}]""";

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/create-video",
            "secret-token",
            "79ai.net",
            "seedance_20_pro",
            "transition prompt",
            [],
            new Dictionary<string, string?>
            {
                ["privacy"] = "PRIVATE",
                ["translate_to_en"] = "false",
                ["project_id"] = "default",
                ["mode"] = "fast",
                ["duration"] = "6",
                ["ratio"] = "16:9",
                ["resolution"] = "720p",
                ["images"] = imagesJson
            },
            Ai79TaskOperation.Video));

        Assert.Equal("video-task-001", result.TaskId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.gommo.net/ai/create-video", request.Uri);
        Assert.Contains("images=", request.Body, StringComparison.Ordinal);
        Assert.Contains("id_base", Uri.UnescapeDataString(request.Body), StringComparison.Ordinal);
        Assert.Contains("start-id", Uri.UnescapeDataString(request.Body), StringComparison.Ordinal);
        Assert.Contains("end-id", Uri.UnescapeDataString(request.Body), StringComparison.Ordinal);
        Assert.Contains("privacy=PRIVATE", request.Body, StringComparison.Ordinal);
        Assert.Contains("translate_to_en=false", request.Body, StringComparison.Ordinal);
        Assert.Contains("project_id=default", request.Body, StringComparison.Ordinal);
        Assert.Contains("mode=fast", request.Body, StringComparison.Ordinal);
        Assert.Contains("duration=6", request.Body, StringComparison.Ordinal);
        Assert.Contains("ratio=16%3A9", request.Body, StringComparison.Ordinal);
        Assert.Contains("resolution=720p", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("image=https", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("image_2=", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"success":true,"id_base":"45d9f48d8741edb7","message":"Gửi yêu cầu tạo video thành công, chờ hoàn thành trong ít phút.","videoInfo":{"mode":"fast","model":"seedance_20_pro"}}""")]
    [InlineData("""{"success":true,"videoInfo":{"id_base":"45d9f48d8741edb7","mode":"fast","model":"seedance_20_pro"},"message":"Gửi yêu cầu tạo video thành công, chờ hoàn thành trong ít phút."}""")]
    [InlineData("""{"success":true,"data":{"id_base":"45d9f48d8741edb7"},"message":"Gửi yêu cầu tạo video thành công, chờ hoàn thành trong ít phút."}""")]
    public async Task VideoSubmit_ParsesIdBaseAndDoesNotTreatSuccessMessageAsError(string responseJson)
    {
        var handler = new RecordingJsonHandler(responseJson);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/create-video",
            "secret-token",
            "79ai.net",
            "seedance_20_pro",
            "transition prompt",
            ["https://cdn.example/start.png", "https://cdn.example/end.png"],
            new Dictionary<string, string?> { ["mode"] = "fast", ["duration"] = "6", ["ratio"] = "16:9", ["resolution"] = "720p" },
            Ai79TaskOperation.Video,
            "image",
            "image_2"));

        Assert.Equal("45d9f48d8741edb7", result.TaskId);
        Assert.Contains("message", result.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"task_id":"video-task-001"}""", "video-task-001")]
    [InlineData("""{"data":{"request_id":"video-request-001"}}""", "video-request-001")]
    public async Task VideoSubmit_KeepsLegacyAsyncTaskAliases(string responseJson, string expectedTaskId)
    {
        var handler = new RecordingJsonHandler(responseJson);
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
            Ai79TaskOperation.Video,
            "image",
            "image_2"));

        Assert.Equal(expectedTaskId, result.TaskId);
    }

    [Fact]
    public async Task VideoSubmit_ProviderErrorStillThrowsWhenResolutionIsMissing()
    {
        var handler = new RecordingJsonHandler("""{"error":600,"message":"Thiếu tùy chọn bắt buộc \"resolution\"."}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<Ai79TaskSubmitException>(() => client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/create-video",
            "secret-token",
            "79ai.net",
            "seedance_20_pro",
            "transition prompt",
            ["https://cdn.example/start.png", "https://cdn.example/end.png"],
            new Dictionary<string, string?> { ["mode"] = "fast", ["duration"] = "6", ["ratio"] = "16:9" },
            Ai79TaskOperation.Video,
            "image",
            "image_2")));

        Assert.Equal("provider_error", ex.ErrorCode);
        Assert.Contains("resolution", ex.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"data":{"task_id":"img-task-001","status":"RUNNING"}}""")]
    [InlineData("""{"task":{"request_id":"img-task-001","state":"processing"}}""")]
    public async Task ImagePoll_RunningFixtureNormalizesToRunning(string json)
    {
        var result = await PollAsync("/image", json, Ai79TaskOperation.Image);

        Assert.Equal(Ai79TaskStatusNormalizer.Running, result.NormalizedStatus);
        Assert.Null(result.OutputUrl);
    }

    [Theory]
    [InlineData("""{"status":"SUCCESS","url":"https://cdn.example/out.png"}""")]
    [InlineData("""{"imageInfo":{"status":"SUCCESS","url":"https://cdn.example/out.png"}}""")]
    public async Task ImagePoll_SuccessFixturesUseIdBaseAndExtractOutput(string json)
    {
        var result = await PollAsync("/image", json, Ai79TaskOperation.Image);

        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal("https://cdn.example/out.png", result.OutputUrl);
    }

    [Theory]
    [InlineData("""{"videoInfo":{"status":"MEDIA_GENERATION_STATUS_SUCCESSFUL","download_url":"https://cdn.example/out.mp4","url":"https://cdn.example/fallback.mp4"}}""", "https://cdn.example/out.mp4")]
    [InlineData("""{"data":{"videoInfo":{"status":"MEDIA_GENERATION_COMPLETED","downloadUrl":"https://cdn.example/out2.mp4"}}}""", "https://cdn.example/out2.mp4")]
    public async Task VideoPoll_SuccessFixtureExtractsOutputUrl(string json, string expectedUrl)
    {
        var result = await PollAsync("/video", json, Ai79TaskOperation.Video);

        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal(expectedUrl, result.OutputUrl);
    }

    [Theory]
    [InlineData("MEDIA_GENERATION_STATUS_SUCCESSFUL", Ai79TaskStatusNormalizer.Success)]
    [InlineData("MEDIA_GENERATION_COMPLETED", Ai79TaskStatusNormalizer.Success)]
    [InlineData("MEDIA_GENERATION_STATUS_FAILED", Ai79TaskStatusNormalizer.Failed)]
    [InlineData("MEDIA_GENERATION_FAILED", Ai79TaskStatusNormalizer.Failed)]
    [InlineData("MEDIA_GENERATION_STATUS_PROCESSING", Ai79TaskStatusNormalizer.Running)]
    public void ProviderStatusNormalizer_Maps79AiMediaGenerationStatuses(string status, string expected)
    {
        Assert.Equal(expected, Ai79TaskStatusNormalizer.Normalize(status));
    }

    [Theory]
    [InlineData("""{"error":{"code":"bad_prompt","message":"Prompt rejected"},"data":{"status":"FAILED"}}""", "bad_prompt", "Prompt rejected")]
    [InlineData("""{"response":{"state":"ERROR","errorCode":"provider_error","errorMessage":"Provider failed"}}""", "provider_error", "Provider failed")]
    public async Task Poll_FailureFixtureExtractsSafeError(string json, string code, string message)
    {
        var result = await PollAsync("/image", json, Ai79TaskOperation.Image);

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
            Ai79TaskOperation.Image,
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
            Ai79TaskOperation.Image,
            "image")));

        Assert.Equal("invalid_model", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.OK, ex.HttpStatusCode);
        Assert.Equal("79AI image submit failed: Model is unavailable", ex.ErrorMessage);
        Assert.Contains("invalid_model", ex.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", ex.SanitizedResponseJson, StringComparison.Ordinal);
    }

    private static async Task<Ai79TaskStatusResult> PollAsync(string path, string json, Ai79TaskOperation operation)
    {
        var handler = new RecordingJsonHandler(json);
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.GetStatusAsync(new Ai79TaskStatusRequest(
            "https://api.gommo.net/ai",
            path,
            "secret-token",
            "79ai.net",
            "abc123",
            operation));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"https://api.gommo.net/ai{path}", request.Uri);
        if (operation == Ai79TaskOperation.Image)
        {
            Assert.Contains("id_base=abc123", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("task_id=abc123", request.Body, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("videoId=abc123", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("task_id=abc123", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("id_base=abc123", request.Body, StringComparison.Ordinal);
        }

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
