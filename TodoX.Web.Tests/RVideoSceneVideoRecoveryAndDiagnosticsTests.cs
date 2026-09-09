using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoSceneVideoRecoveryAndDiagnosticsTests
{
    private static readonly BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

    [Fact]
    public void BuildSubmitRequestMetadata_IncludesSafeEffectiveFieldsAndImageMetadata()
    {
        var method = typeof(Ai79TaskClient).GetMethod("BuildSubmitRequestMetadata", NonPublicStatic);
        Assert.NotNull(method);

        var metadata = (string)method!.Invoke(null, new object?[]
        {
            "https://api.example.com/base",
            "/submitVideo",
            "79ai.net",
            "seedream_5_0",
            Ai79TaskOperation.Video,
            "vip",
            "12",
            "16:9",
            "16:9",
            "1080p",
            "motion",
            "project-1",
            "private",
            "yes",
            new[] { "https://cdn.example/video.jpg", "https://cdn.example/video.jpg" },
            "image",
            "image_2",
            new Dictionary<string, string?>
            {
                ["custom_flag"] = "on",
                ["access_token"] = "secret",
                ["Authorization"] = "Bearer token",
                ["credential"] = "hidden",
                ["ciphertext"] = "blocked"
            },
            2
        })!;

        using var doc = JsonDocument.Parse(metadata);
        Assert.Equal("vip", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal("12", doc.RootElement.GetProperty("duration").GetString());
        Assert.Equal("16:9", doc.RootElement.GetProperty("ratio").GetString());
        Assert.Equal("16:9", doc.RootElement.GetProperty("aspect_ratio").GetString());
        Assert.Equal("1080p", doc.RootElement.GetProperty("resolution").GetString());
        Assert.Equal("motion", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("project-1", doc.RootElement.GetProperty("project_id").GetString());
        Assert.Equal("private", doc.RootElement.GetProperty("privacy").GetString());
        Assert.Equal("yes", doc.RootElement.GetProperty("translate_to_en").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("imageCount").GetInt32());
        Assert.Equal("custom_flag", Assert.Single(doc.RootElement.GetProperty("extraFieldNames").EnumerateArray()).GetString());

        var images = doc.RootElement.GetProperty("images");
        Assert.True(images[0].GetProperty("present").GetBoolean());
        Assert.Equal("cdn.example", images[0].GetProperty("urlHost").GetString());
        Assert.Equal("/video.jpg", images[0].GetProperty("urlPath").GetString());
        Assert.Equal("https://cdn.example/video.jpg", images[0].GetProperty("sanitizedUrl").GetString());
        Assert.True(images[1].GetProperty("isImage2").GetBoolean());
        Assert.True(images[1].GetProperty("duplicateOfPrevious").GetBoolean());
        Assert.DoesNotContain("access_token", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ciphertext", metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildSubmitFailureDiagnostics_HandlesDirectAndWrappedSubmitExceptions(bool wrapped)
    {
        var metadataMethod = typeof(Ai79TaskClient).GetMethod("BuildSubmitRequestMetadata", NonPublicStatic);
        Assert.NotNull(metadataMethod);

        var metadata = (string)metadataMethod!.Invoke(null, new object?[]
        {
            "https://api.example.com/base",
            "/submitVideo",
            "79ai.net",
            "seedream_5_0",
            Ai79TaskOperation.Video,
            "vip",
            "12",
            "16:9",
            "16:9",
            "1080p",
            "motion",
            "project-1",
            "private",
            "yes",
            Array.Empty<string>(),
            null,
            null,
            new Dictionary<string, string?>(),
            0
        })!;

        var submitException = new Ai79TaskSubmitException(
            "79AI submit failed.",
            """{"ok":false}""",
            HttpStatusCode.BadRequest,
            "submit_failed",
            sanitizedRequestMetadataJson: metadata);

        Exception exception = wrapped
            ? new VideoProviderTransientException("wrapped", "submit_transient", submitException)
            : submitException;

        var diagnosticsMethod = typeof(SceneVideoWorkerHandler).GetMethod("BuildSubmitFailureDiagnostics", NonPublicStatic);
        Assert.NotNull(diagnosticsMethod);

        var diagnostics = diagnosticsMethod!.Invoke(null, new object?[] { exception });
        Assert.NotNull(diagnostics);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(diagnostics));
        Assert.Equal("submit_failed", doc.RootElement.GetProperty("providerErrorCode").GetString());
        Assert.Equal("79AI submit failed.", doc.RootElement.GetProperty("providerErrorMessage").GetString());
        var requestMetadata = doc.RootElement.GetProperty("sanitizedRequestMetadata").GetString();
        Assert.NotNull(requestMetadata);
        using var metadataDoc = JsonDocument.Parse(requestMetadata!);
        Assert.Equal("vip", metadataDoc.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public void SceneVideoJobWorkerPersistsAi79SubmitDiagnosticsWithoutSecrets()
    {
        var job = new RenderJobDto
        {
            ModelCode = "veo_omni",
            AttemptCount = 3,
            MaxAttempts = 3
        };
        var exception = new Ai79TaskSubmitException(
            "79AI video submit failed.",
            """{"error":"unavailable","accessToken":"raw-access-token","apiKey":"raw-api-key","Authorization":"Bearer raw-token"}""",
            HttpStatusCode.ServiceUnavailable,
            "provider_unavailable",
            sanitizedRequestMetadataJson: """{"endpoint":"/create-video","accessToken":"raw-access-token","apiKey":"raw-api-key","Authorization":"Bearer raw-token"}""");

        var method = typeof(SceneVideoJobWorker).GetMethod(
            "BuildAi79SubmitDiagnostics",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var diagnostics = method!.Invoke(null, new object[] { job, exception });
        Assert.NotNull(diagnostics);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(diagnostics));
        var root = document.RootElement;
        Assert.Equal("Ai79TaskSubmitException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("79ai", root.GetProperty("provider").GetString());
        Assert.Equal("veo_omni", root.GetProperty("model").GetString());
        Assert.Equal(503, root.GetProperty("httpStatusCode").GetInt32());
        Assert.Equal("provider_unavailable", root.GetProperty("providerErrorCode").GetString());
        Assert.Equal(3, root.GetProperty("attemptCount").GetInt32());
        Assert.Equal(3, root.GetProperty("maxAttempts").GetInt32());
        Assert.DoesNotContain("raw-access-token", root.GetProperty("sanitizedResponseJson").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-api-key", root.GetProperty("sanitizedResponseJson").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", root.GetProperty("sanitizedResponseJson").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw-access-token", root.GetProperty("sanitizedRequestMetadataJson").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-api-key", root.GetProperty("sanitizedRequestMetadataJson").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", root.GetProperty("sanitizedRequestMetadataJson").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SceneVideoJobWorkerFailureEventDataHasCompleteDiagnosticShape()
    {
        var job = new RenderJobDto
        {
            ModelCode = "veo_omni",
            AttemptCount = 3,
            MaxAttempts = 3
        };
        var exception = new Ai79TaskSubmitException(
            "79AI video submit failed.",
            """{"error":"unavailable"}""",
            HttpStatusCode.ServiceUnavailable,
            "provider_unavailable",
            sanitizedRequestMetadataJson: """{"endpoint":"/create-video"}""");

        var method = typeof(SceneVideoJobWorker).GetMethod(
            "BuildJobFailureEventData",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var eventData = method!.Invoke(null, new object[] { job, exception });
        Assert.NotNull(eventData);

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(eventData));
        var root = document.RootElement;
        Assert.Equal(
            new[]
            {
                "attemptCount",
                "exceptionType",
                "httpStatusCode",
                "maxAttempts",
                "model",
                "provider",
                "providerErrorCode",
                "sanitizedRequestMetadataJson",
                "sanitizedResponseJson"
            },
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("Ai79TaskSubmitException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("79ai", root.GetProperty("provider").GetString());
        Assert.Equal("veo_omni", root.GetProperty("model").GetString());
        Assert.Equal(503, root.GetProperty("httpStatusCode").GetInt32());
        Assert.Equal("provider_unavailable", root.GetProperty("providerErrorCode").GetString());
        Assert.Equal(3, root.GetProperty("attemptCount").GetInt32());
        Assert.Equal(3, root.GetProperty("maxAttempts").GetInt32());
        Assert.Equal("""{"error":"unavailable"}""", root.GetProperty("sanitizedResponseJson").GetString());
        Assert.Equal("""{"endpoint":"/create-video"}""", root.GetProperty("sanitizedRequestMetadataJson").GetString());
    }

    [Fact]
    public void SceneVideoPendingReconciliationEventDataKeepsAi79DiagnosticsAndRetryBudget()
    {
        var job = new RenderJobDto
        {
            ModelCode = "veo_omni",
            AttemptCount = 3,
            MaxAttempts = 3
        };
        var submitException = new Ai79TaskSubmitException(
            "79AI video submit failed.",
            """{"error":"unavailable","access_token":"secret-response"}""",
            HttpStatusCode.ServiceUnavailable,
            "provider_unavailable",
            sanitizedRequestMetadataJson: """{"endpoint":"/create-video","access_token":"secret-request"}""");
        var exception = new RenderJobPendingReconciliationException(
            "Video provider submit outcome is unknown.",
            new VideoProviderTransientException("wrapped", "submit_transient", submitException));

        var method = typeof(SceneVideoJobWorker).GetMethod(
            "BuildJobFailureEventData",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var eventData = method!.Invoke(null, new object[] { job, exception });

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(eventData));
        var root = document.RootElement;
        Assert.Equal("Ai79TaskSubmitException", root.GetProperty("exceptionType").GetString());
        Assert.Equal("79ai", root.GetProperty("provider").GetString());
        Assert.Equal("veo_omni", root.GetProperty("model").GetString());
        Assert.Equal(503, root.GetProperty("httpStatusCode").GetInt32());
        Assert.Equal("provider_unavailable", root.GetProperty("providerErrorCode").GetString());
        Assert.Equal(3, root.GetProperty("attemptCount").GetInt32());
        Assert.Equal(3, root.GetProperty("maxAttempts").GetInt32());
        Assert.DoesNotContain("secret-response", root.GetProperty("sanitizedResponseJson").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret-request", root.GetProperty("sanitizedRequestMetadataJson").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SceneVideoJobWorkerWritesAi79DiagnosticsBeforeRetryAndKeepsRetryPolicy()
    {
        var source = ReadRepoFile("Services", "Render", "SceneVideoJobWorker.cs");
        var diagnosticsIndex = source.IndexOf("AddAi79SubmitDiagnosticsAsync(jobs, job, ex, stoppingToken)", StringComparison.Ordinal);
        var retryIndex = source.IndexOf("var shouldRetry = job.AttemptCount < job.MaxAttempts;", StringComparison.Ordinal);

        Assert.True(diagnosticsIndex >= 0);
        Assert.True(retryIndex > diagnosticsIndex);
        Assert.Contains("await jobs.ScheduleRetryAsync(job.Id, delay, ex.GetType().Name, ex.Message, stoppingToken);", source);
        Assert.Contains("\"RVIDEO_79AI_SUBMIT_DIAGNOSTICS\"", source);
        Assert.Contains("BuildJobFailureEventData(job, ex)", source);
    }

    [Fact]
    public void RecoverableStuckDetection_RequiresFailedJobAndBlankProviderTask()
    {
        var service = (RVideoSceneVideoRecoveryService)FormatterServices.GetUninitializedObject(typeof(RVideoSceneVideoRecoveryService));

        var version = new SceneVideoVersionDto
        {
            Status = "queued",
            ProviderTaskId = null
        };
        var job = new RenderJobDto
        {
            JobType = RenderJobTypes.RenderSceneVideo,
            Status = RenderJobStatuses.Failed
        };

        Assert.True(service.IsRecoverableStuck(version, job));

        version.ProviderTaskId = "task-1";
        Assert.False(service.IsRecoverableStuck(version, job));

        version.ProviderTaskId = null;
        job.Status = RenderJobStatuses.Rendering;
        Assert.False(service.IsRecoverableStuck(version, job));

        job.Status = RenderJobStatuses.Failed;
        version.Status = "completed";
        Assert.False(service.IsRecoverableStuck(version, job));
    }

    [Fact]
    public void SceneVideoUnknownSubmitReusesThePendingVersionWithoutBlindResubmission()
    {
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var versions = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");

        Assert.Contains("IsUnknownSubmission(version, taskId)", worker);
        Assert.Contains("RVIDEO_VIDEO_SUBMIT_UNKNOWN", worker);
        Assert.Contains("RVIDEO_VIDEO_PENDING_RECONCILIATION", worker);
        Assert.Contains("throw new RenderJobPendingReconciliationException", worker);
        Assert.Contains("lower(status)='pending_reconciliation'", versions);
        Assert.Contains("provider_task_id IS NULL OR btrim(provider_task_id) = ''", versions);
    }

    [Fact]
    public void SceneVideoReconciliationCanRestartKnownTasksAfterTheOriginalJobFailed()
    {
        var repository = ReadRepoFile("Services", "VideoRender", "VideoRenderRepository.cs");
        var jobs = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("j.status NOT IN ('completed', 'cancelled')", repository);
        Assert.Contains("v.status IN ('submitted', 'processing', 'pending_reconciliation', 'rendering')", repository);
        Assert.Contains("'pending_reconciliation', 'failed'", jobs);
    }

    [Fact]
    public void SceneVideoProviderSuccessDownloadsAndCompletesBeforeTheParentCanAdvance()
    {
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var completion = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");

        var downloadIndex = worker.IndexOf("\"RVIDEO_VIDEO_DOWNLOAD_STARTED\"", StringComparison.Ordinal);
        var completeIndex = worker.IndexOf("\"RVIDEO_VIDEO_COMPLETED\"", StringComparison.Ordinal);
        Assert.True(downloadIndex >= 0);
        Assert.True(completeIndex > downloadIndex);
        Assert.Contains("RVIDEO_VIDEO_DOWNLOAD_COMPLETED", worker);
        Assert.Contains("CompleteSceneVideoVersionAsync", completion);
        Assert.Contains("selected_video_version_id=@versionId", ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs"));
    }

    [Fact]
    public void SceneVideoProviderEventsCarryPersistentCorrelationFields()
    {
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        foreach (var eventName in new[]
                 {
                     "RVIDEO_VIDEO_SUBMIT_STARTED",
                     "RVIDEO_VIDEO_SUBMITTED",
                     "RVIDEO_VIDEO_SUBMIT_UNKNOWN",
                     "RVIDEO_VIDEO_PENDING_RECONCILIATION",
                     "RVIDEO_VIDEO_POLL_STARTED",
                     "RVIDEO_VIDEO_POLL_COMPLETED",
                     "RVIDEO_VIDEO_DOWNLOAD_STARTED",
                     "RVIDEO_VIDEO_DOWNLOAD_COMPLETED",
                     "RVIDEO_VIDEO_COMPLETED",
                     "RVIDEO_VIDEO_DOWNLOAD_FAILED",
                     "RVIDEO_VIDEO_RECONCILIATION_STARTED",
                     "RVIDEO_VIDEO_RECONCILIATION_COMPLETED"
                 })
        {
            Assert.Contains(eventName, worker);
        }

        Assert.Contains("logicalRequestId", worker);
        Assert.Contains("providerCode", worker);
        Assert.Contains("modelCode", worker);
        Assert.Contains("providerTaskId", worker);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TodoX.Web",
            Path.Combine(parts)));
        return File.ReadAllText(path);
    }
}
