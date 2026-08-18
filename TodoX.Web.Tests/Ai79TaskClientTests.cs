using System.Net;
using System.Net.Http.Headers;
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
    public async Task ImageSubmit_UsesSubjectsForProductReferenceWithoutImage2()
    {
        var handler = new RecordingJsonHandler("""{"imageInfo":{"id_base":"try-on-001"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/generateImage",
            "secret-token",
            "79ai.net",
            "imagegen_2_0",
            "VIRTUAL TRY-ON – PREVIEW ONLY",
            [],
            new Dictionary<string, string?>
            {
                ["action_type"] = "create",
                ["sync"] = "false",
                ["project_id"] = "default",
                ["subjects[0][url]"] = "https://cdn.example/model.png",
                ["subjects[1][url]"] = "https://cdn.example/product.png",
                ["ratio"] = "16:9",
                ["category"] = "FASHION",
                ["mode"] = "low",
                ["resolution"] = "1k",
                ["num_outputs"] = "1",
                ["language"] = "VI"
            },
            Ai79TaskOperation.Image));

        Assert.Equal("try-on-001", result.TaskId);
        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain("image_2", request.Body, StringComparison.Ordinal);
        Assert.Contains("subjects%5B0%5D%5Burl%5D=https%3A%2F%2Fcdn.example%2Fmodel.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("subjects%5B1%5D%5Burl%5D=https%3A%2F%2Fcdn.example%2Fproduct.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("model=imagegen_2_0", request.Body, StringComparison.Ordinal);
        Assert.Contains("prompt=VIRTUAL+TRY-ON+%E2%80%93+PREVIEW+ONLY", request.Body, StringComparison.Ordinal);
        Assert.Contains("sync=false", request.Body, StringComparison.Ordinal);
        Assert.Contains("category=FASHION", request.Body, StringComparison.Ordinal);
        Assert.Contains("resolution=1k", request.Body, StringComparison.Ordinal);
        Assert.Contains("mode=low", request.Body, StringComparison.Ordinal);
        Assert.Contains("num_outputs=1", request.Body, StringComparison.Ordinal);
        Assert.Contains("language=VI", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImageSubmit_RejectsSecondImageInsteadOfInventingImage2Field()
    {
        var handler = new RecordingJsonHandler("""{"imageInfo":{"id_base":"unused"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SubmitAsync(new Ai79TaskSubmitRequest(
            "https://api.gommo.net/ai",
            "/generateImage",
            "secret-token",
            "79ai.net",
            "imagegen_2_0",
            "prompt",
            ["data:image/jpeg;base64,CHARACTER_BYTES"],
            new Dictionary<string, string?> { ["subjects[0][url]"] = "https://cdn.example/model.png", ["subjects[1][url]"] = "https://cdn.example/product.png" },
            Ai79TaskOperation.Image,
            "base64Image",
            "image_2")));

        Assert.Contains("second image field", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
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

    [Fact]
    public async Task ImageUpload_UsesConstructionTimelapseOriginalImageContractAndParsesImageInfo()
    {
        var handler = new RecordingJsonHandler("""{"imageInfo":{"id_base":"original-id","url":"https://cdn.example/original.jpg","project_id":"default","file_name":"original.jpg"},"access_token":"secret-token"}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.UploadImageAsync(new Ai79ImageUploadRequest(
            "https://api.gommo.net/ai",
            "/image-upload",
            "secret-token",
            "79ai.net",
            "AQIDBA==",
            "default",
            "original.jpg",
            4));

        Assert.Equal("original-id", result.IdBase);
        Assert.Equal("https://cdn.example/original.jpg", result.Url);
        Assert.Equal("default", result.ProjectId);
        Assert.Equal("original.jpg", result.FileName);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson, StringComparison.Ordinal);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.gommo.net/ai/image-upload", request.Uri);
        Assert.Contains("access_token=secret-token", request.Body, StringComparison.Ordinal);
        Assert.Contains("domain=79ai.net", request.Body, StringComparison.Ordinal);
        Assert.Contains("data=AQIDBA%3D%3D", request.Body, StringComparison.Ordinal);
        Assert.Contains("project_id=default", request.Body, StringComparison.Ordinal);
        Assert.Contains("file_name=original.jpg", request.Body, StringComparison.Ordinal);
        Assert.Contains("size=4", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("data%3Aimage", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MediaUpload_UsesMultipartProviderAssetUploadAndParsesUrlAliases()
    {
        var handler = new RecordingJsonHandler(
            """{"data":{"asset_url":"https://cdn.79ai.net/assets/reference.png","id_base":"asset-img-001","file_name":"reference.png"}}""",
            """{"videoInfo":{"url":"https://cdn.79ai.net/assets/motion.mp4","id_base":"asset-video-001","file_name":"motion.mp4"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var image = await client.UploadMediaAsync(new Ai79MediaUploadRequest(
            "https://v2.api.gommo.net",
            "/ai/upload/image",
            "Bearer secret-token",
            "79ai.net",
            "default",
            "file",
            new Ai79MultipartFilePart("file", "reference.png", "image/png", 4, _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1, 2, 3, 4 })))));
        var video = await client.UploadMediaAsync(new Ai79MediaUploadRequest(
            "https://v2.api.gommo.net",
            "/ai/upload/video",
            "  Bearer   secret-token  ",
            "79ai.net",
            "default",
            "video_file",
            new Ai79MultipartFilePart("video_file", "motion.mp4", "video/mp4", 4, _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 5, 6, 7, 8 })))));

        Assert.Equal("https://cdn.79ai.net/assets/reference.png", image.Url);
        Assert.Equal("asset-img-001", image.IdBase);
        Assert.Equal("https://cdn.79ai.net/assets/motion.mp4", video.Url);
        Assert.Equal("asset-video-001", video.IdBase);
        Assert.DoesNotContain("secret-token", image.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", video.SanitizedResponseJson, StringComparison.Ordinal);

        Assert.Equal("https://v2.api.gommo.net/ai/upload/image", handler.Requests[0].Uri);
        Assert.Equal("https://v2.api.gommo.net/ai/upload/video", handler.Requests[1].Uri);
        Assert.Equal("Bearer", handler.Requests[0].Authorization?.Scheme);
        Assert.Equal("Bearer", handler.Requests[1].Authorization?.Scheme);
        Assert.Equal("secret-token", handler.Requests[0].Authorization?.Parameter);
        Assert.Equal("secret-token", handler.Requests[1].Authorization?.Parameter);
        Assert.StartsWith("multipart/form-data; boundary=", handler.Requests[0].ContentType, StringComparison.Ordinal);
        Assert.StartsWith("multipart/form-data; boundary=", handler.Requests[1].ContentType, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("name=\"file\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("filename=\"reference.png\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("Content-Type: image/png", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("name=\"video_file\"", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("filename=\"motion.mp4\"", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("Content-Type: video/mp4", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("name=\"domain\"", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("79ai.net", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("name=\"project_id\"", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("default", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MediaUpload_ParsesTopLevelDownloadUrlFromCurlCompatibleVideoResponse()
    {
        var handler = new RecordingJsonHandler(
            """{"message":"Upload video thành công","download_url":"https://ai-cdn.gommo.net/ai/videos/test.mp4"}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.UploadMediaAsync(new Ai79MediaUploadRequest(
            "https://v2.api.gommo.net",
            "/ai/upload/video",
            "Bearer secret-token",
            "79ai.net",
            "default",
            "video_file",
            new Ai79MultipartFilePart("video_file", "motion.mp4", "video/mp4", 4, _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 5, 6, 7, 8 })))));

        Assert.Equal("https://ai-cdn.gommo.net/ai/videos/test.mp4", result.Url);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://v2.api.gommo.net/ai/upload/video", request.Uri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);
        Assert.StartsWith("multipart/form-data; boundary=", request.ContentType, StringComparison.Ordinal);
        Assert.Contains("name=\"video_file\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("filename=\"motion.mp4\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("Content-Type: video/mp4", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=\"domain\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("79ai.net", request.Body, StringComparison.Ordinal);
        Assert.Contains("name=\"project_id\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("default", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MediaUpload_NormalizesRepeatedBearerPrefix()
    {
        var handler = new RecordingJsonHandler(
            """{"data":{"asset_url":"https://cdn.79ai.net/assets/reference.png","id_base":"asset-img-001"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        await client.UploadMediaAsync(new Ai79MediaUploadRequest(
            "https://v2.api.gommo.net",
            "/ai/upload/image",
            "Bearer Bearer secret-token",
            "79ai.net",
            "default",
            "file",
            new Ai79MultipartFilePart("file", "reference.png", "image/png", 4, _ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1, 2, 3, 4 })))));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);
        Assert.DoesNotContain("access_token=", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MotionControlSubmit_UsesProviderUploadedUrlsAndParsesIdBase()
    {
        var handler = new RecordingJsonHandler("""{"id_base":"motion-task-001","success":true,"echo":"secret-token"}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.SubmitMotionControlAsync(new Ai79MotionControlSubmitRequest(
            "https://v2.api.gommo.net",
            "/ai/jobs/video/kling_video_motion_3",
            "Bearer Bearer secret-token",
            "79ai.net",
            "default",
            "kling_video_motion_3",
            "",
            "https://cdn.79ai.net/assets/reference.png",
            "https://cdn.79ai.net/assets/motion.mp4",
            "standard",
            "default",
            "motion",
            "input_video"));

        Assert.Equal("motion-task-001", result.TaskId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://v2.api.gommo.net/ai/jobs/video/kling_video_motion_3", request.Uri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);
        Assert.DoesNotContain("secret-token", result.SanitizedResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=", request.Body, StringComparison.Ordinal);
        Assert.Contains("domain=79ai.net", request.Body, StringComparison.Ordinal);
        Assert.Contains("project_id=default", request.Body, StringComparison.Ordinal);
        Assert.Contains("model=kling_video_motion_3", request.Body, StringComparison.Ordinal);
        Assert.Contains("image_url=https%3A%2F%2Fcdn.79ai.net%2Fassets%2Freference.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("images%5B0%5D%5Burl%5D=https%3A%2F%2Fcdn.79ai.net%2Fassets%2Freference.png", request.Body, StringComparison.Ordinal);
        Assert.Contains("video_url=https%3A%2F%2Fcdn.79ai.net%2Fassets%2Fmotion.mp4", request.Body, StringComparison.Ordinal);
        Assert.Contains("subType=motion", request.Body, StringComparison.Ordinal);
        Assert.Contains("background_source=input_video", request.Body, StringComparison.Ordinal);
        Assert.Contains("mode=standard", request.Body, StringComparison.Ordinal);
        Assert.Contains("ratio=default", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", request.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("motion_video=", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("character_image=", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MotionControlSubmit_ReportsPlainText500AsHttpErrorNotInvalidJson()
    {
        var handler = new RecordingJsonHandler((HttpStatusCode.InternalServerError, "Service unavailable"));
        var client = new Ai79TaskClient(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<Ai79TaskSubmitException>(() => client.SubmitMotionControlAsync(new Ai79MotionControlSubmitRequest(
            "https://v2.api.gommo.net",
            "/ai/jobs/video/kling_video_motion_3",
            "secret-token",
            "79ai.net",
            "default",
            "kling_video_motion_3",
            "prompt",
            "https://cdn.example/reference.png",
            "https://cdn.example/motion.mp4",
            "standard",
            "default",
            "motion",
            "input_video")));

        Assert.Equal("http_500", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.InternalServerError, ex.HttpStatusCode);
        Assert.Contains("HTTP 500", ex.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid JSON", ex.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VideoStatus_BearerAuthUsesTaskIdPathAndProjectOnlyBody()
    {
        var handler = new RecordingJsonHandler("""{"videoInfo":{"status":"MEDIA_GENERATION_COMPLETED","download_url":"https://cdn.example/final.mp4"}}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.GetStatusAsync(new Ai79TaskStatusRequest(
            "https://v2.api.gommo.net",
            "/ai/jobs/{task_id}?media=video",
            "  Bearer   secret-token  ",
            "79ai.net",
            "motion-task-001",
            Ai79TaskOperation.Video,
            null,
            true,
            "default"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://v2.api.gommo.net/ai/jobs/motion-task-001?media=video", request.Uri);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Authorization?.Parameter);
        Assert.Contains("domain=79ai.net", request.Body, StringComparison.Ordinal);
        Assert.Contains("project_id=default", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("videoId=motion-task-001", request.Body, StringComparison.Ordinal);
        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal("https://cdn.example/final.mp4", result.OutputUrl);
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

    [Fact]
    public async Task VideoPoll_FallsBackToVideosListAndMatchesProviderTaskId()
    {
        var handler = new RecordingJsonHandler(
            """{"message":"single lookup did not include videoInfo"}""",
            """{"videoInfo":[{"id_base":"other","status":"MEDIA_GENERATION_STATUS_SUCCESSFUL","download_url":"https://cdn.example/other.mp4"},{"id_base":"abc123","generation_status":"MEDIA_GENERATION_COMPLETED","file_url":"https://cdn.example/final.mp4","url":"https://cdn.example/input-image.jpg"}]}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.GetStatusAsync(new Ai79TaskStatusRequest(
            "https://api.gommo.net/ai",
            "/video",
            "secret-token",
            "79ai.net",
            "abc123",
            Ai79TaskOperation.Video));

        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal("https://cdn.example/final.mp4", result.OutputUrl);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("https://api.gommo.net/ai/video", handler.Requests[0].Uri);
        Assert.Contains("videoId=abc123", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal("https://api.gommo.net/ai/videos", handler.Requests[1].Uri);
        Assert.DoesNotContain("videoId=abc123", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VideoPoll_MatchesVideoInfoArrayFromPrimaryResponseWithoutFallback()
    {
        var handler = new RecordingJsonHandler(
            """{"videoInfo":[{"id_base":"other","status":"MEDIA_GENERATION_STATUS_SUCCESSFUL","download_url":"https://cdn.example/other.mp4"},{"id_base":"abc123","status":"MEDIA_GENERATION_STATUS_SUCCESSFUL","download_url":"https://cdn.example/final.mp4"}]}""");
        var client = new Ai79TaskClient(new HttpClient(handler));

        var result = await client.GetStatusAsync(new Ai79TaskStatusRequest(
            "https://api.gommo.net/ai",
            "/video",
            "secret-token",
            "79ai.net",
            "abc123",
            Ai79TaskOperation.Video));

        Assert.Equal(Ai79TaskStatusNormalizer.Success, result.NormalizedStatus);
        Assert.Equal("https://cdn.example/final.mp4", result.OutputUrl);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("MEDIA_GENERATION_STATUS_SUCCESSFUL", Ai79TaskStatusNormalizer.Success)]
    [InlineData("MEDIA_GENERATION_COMPLETED", Ai79TaskStatusNormalizer.Success)]
    [InlineData("MEDIA_GENERATION_STATUS_FAILED", Ai79TaskStatusNormalizer.Failed)]
    [InlineData("MEDIA_GENERATION_FAILED", Ai79TaskStatusNormalizer.Failed)]
    [InlineData("REJECTED", Ai79TaskStatusNormalizer.Failed)]
    [InlineData("CANCELLED", Ai79TaskStatusNormalizer.Failed)]
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
            "base64Image")));

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
            "base64Image")));

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
        Assert.Null(request.Authorization);
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
        private readonly Queue<RecordedResponse> _responses;
        public List<RequestSnapshot> Requests { get; } = new();

        public RecordingJsonHandler(params string[] responses)
        {
            _responses = new Queue<RecordedResponse>(responses.Select(response => new RecordedResponse(HttpStatusCode.OK, response)));
        }

        public RecordingJsonHandler(params (HttpStatusCode Status, string Body)[] responses)
        {
            _responses = new Queue<RecordedResponse>(responses.Select(response => new RecordedResponse(response.Status, response.Body)));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(request.RequestUri!.ToString(), body, request.Headers.Authorization, request.Content?.Headers.ContentType?.ToString()));
            var response = _responses.Count == 0 ? new RecordedResponse(HttpStatusCode.OK, "{}") : _responses.Dequeue();
            return new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body)
            };
        }
    }

    private sealed record RequestSnapshot(string Uri, string Body, AuthenticationHeaderValue? Authorization, string? ContentType);
    private sealed record RecordedResponse(HttpStatusCode Status, string Body);
}
