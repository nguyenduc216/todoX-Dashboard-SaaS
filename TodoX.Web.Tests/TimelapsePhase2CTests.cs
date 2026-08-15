using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public class TimelapsePhase2CTests
{
    [Fact]
    public void Workers_AreRegisteredAndUseRenderQueueGate()
    {
        var program = ReadSource("TodoX.Web", "Program.cs");
        var workers = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderWorkers.cs");

        Assert.Contains("AddHostedService<TodoX.Web.Services.Timelapse.TimelapseImageWorker>", program);
        Assert.Contains("AddHostedService<TodoX.Web.Services.Timelapse.TimelapseVideoWorker>", program);
        Assert.Contains("AddHostedService<TodoX.Web.Services.Timelapse.TimelapseFinalizerWorker>", program);
        Assert.Contains("RenderQueue:Enabled", workers);
        Assert.Contains("TimelapseProviderWorkerOptions", workers);
        Assert.Contains("\"TimelapseProviderWorkers\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"DefaultImageSubmitPath\": \"/generateImage\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"DefaultVideoSubmitPath\": \"/create-video\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"ProviderCode\": \"79ai\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"ImageCapabilityCode\": \"image_generation\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"ImageModelName\": \"seedream_5_0\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"VideoCapabilityCode\": \"image_to_video\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"VideoModelName\": \"seedance_20_pro\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"DefaultVideoResolution\": \"720p\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
    }

    [Fact]
    public void Repository_ClaimsUseSkipLockedAndPersistWorkerClaim()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("FOR UPDATE SKIP LOCKED", source);
        Assert.Contains("worker_claim", source);
        Assert.Contains("provider_task_id", source);
        Assert.Contains("AdvanceAfterImageCompletedAsync", source);
        Assert.Contains("AdvanceAfterVideoCompletedAsync", source);
        Assert.Contains("start_img.status='COMPLETED'", source, StringComparison.Ordinal);
        Assert.Contains("end_img.status='COMPLETED'", source, StringComparison.Ordinal);
        Assert.Contains("requireVideoConfirmation", source, StringComparison.Ordinal);
        Assert.Contains("videoRenderConfirmed", source, StringComparison.Ordinal);
        Assert.DoesNotContain("c.status IN ('WAITING','FAILED','INVALIDATED')", source, StringComparison.Ordinal);
        Assert.Contains("statusCounts.Active == 0 && statusCounts.Failed > 0", source, StringComparison.Ordinal);
        Assert.Contains("status <> 'COMPLETED'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_Uses79AiCredentialResolverAndDoesNotUseYEScale()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("_credentials.ResolveAsync(option.ProviderCode, \"access_token\"", source);
        Assert.Contains("GetEnabledProviderModelAsync", source);
        Assert.Contains("/generateImage", source);
        Assert.Contains("/create-video", source);
        Assert.Contains("/image", source);
        Assert.Contains("/video", source);
        Assert.DoesNotContain("ResolveProviderForCapabilityAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AiProviderCatalog.SceneImageGeneration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("YEScale", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SubmitAndWaitAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_ResolvesOnlyExplicitEnabled79AiModels()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var options = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderWorkerOptions.cs");
        var repository = ReadSource("TodoX.Web", "Services", "AiProviders", "AiProviderRepository.cs");

        Assert.Contains("ProviderCode { get; set; } = \"79ai\"", options, StringComparison.Ordinal);
        Assert.Contains("ImageCapabilityCode { get; set; } = \"image_generation\"", options, StringComparison.Ordinal);
        Assert.Contains("ImageModelName { get; set; } = \"seedream_5_0\"", options, StringComparison.Ordinal);
        Assert.Contains("VideoCapabilityCode { get; set; } = \"image_to_video\"", options, StringComparison.Ordinal);
        Assert.Contains("VideoModelName { get; set; } = \"seedance_20_pro\"", options, StringComparison.Ordinal);

        Assert.Contains("p.provider_code = @providerCode", repository, StringComparison.Ordinal);
        Assert.Contains("c.capability_code = @capabilityCode", repository, StringComparison.Ordinal);
        Assert.Contains("c.model_name = @modelName", repository, StringComparison.Ordinal);
        Assert.Contains("p.enabled = true", repository, StringComparison.Ordinal);
        Assert.Contains("c.enabled = true", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDefaultAsync", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("GetFirstByPriorityAsync", runtime, StringComparison.Ordinal);

        Assert.Contains("Chưa cấu hình model Seedream cho Timelapse.", runtime, StringComparison.Ordinal);
        Assert.Contains("Chưa cấu hình model Seedance cho Timelapse.", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("google_image_gen_banana_2_cheap", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageAICreativeRender", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("grok", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("veo", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("omni", runtime, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimelapseVideoContract_MapsCustomerModesAndKeepsSixSecondDuration()
    {
        Assert.Equal("fast", TodoX.Web.Models.Timelapse.TimelapseRequestRules.FastMode);
        Assert.Equal("professional", TodoX.Web.Models.Timelapse.TimelapseRequestRules.ProfessionalMode);
        Assert.Equal(
            TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Standard,
            TodoX.Web.Models.Timelapse.TimelapseSellPricing.QualityTierForMode(TodoX.Web.Models.Timelapse.TimelapseRequestRules.FastMode));
        Assert.Equal(
            TodoX.Web.Models.Catalog.ServiceSellPriceQualityTiers.Premium,
            TodoX.Web.Models.Timelapse.TimelapseSellPricing.QualityTierForMode(TodoX.Web.Models.Timelapse.TimelapseRequestRules.ProfessionalMode));
        Assert.Equal(6, TodoX.Web.Models.Timelapse.TimelapseRequestRules.RuntimeClipDurationSeconds);
    }

    [Theory]
    [InlineData("480p", "480p")]
    [InlineData("720p", "720p")]
    [InlineData("1080p", "1080p")]
    [InlineData(" 720P ", "720p")]
    public void VideoResolution_NormalizesSupportedProviderValues(string input, string expected)
    {
        Assert.Equal(expected, TimelapseProviderWorkerOptions.NormalizeVideoResolution(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("4k")]
    [InlineData("hd")]
    public void VideoResolution_RejectsInvalidConfigurationBeforeSubmit(string? input)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => TimelapseProviderWorkerOptions.NormalizeVideoResolution(input));

        Assert.Contains("480p, 720p, 1080p", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeedanceVideoSubmit_IncludesValidatedResolutionAndSanitizedDiagnostics()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var options = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderWorkerOptions.cs");
        var submitStart = runtime.IndexOf("private async Task SubmitVideoAsync", StringComparison.Ordinal);
        var submitEnd = runtime.IndexOf("private string ResolveVideoResolution", submitStart, StringComparison.Ordinal);
        var submit = runtime[submitStart..submitEnd];

        Assert.Contains("DefaultVideoResolution { get; set; } = \"720p\"", options, StringComparison.Ordinal);
        Assert.Contains("var resolution = ResolveVideoResolution(item.VideoMode);", submit, StringComparison.Ordinal);
        Assert.Contains("[\"duration\"] = item.DurationSeconds.ToString()", submit, StringComparison.Ordinal);
        Assert.Contains("[\"mode\"] = item.VideoMode", submit, StringComparison.Ordinal);
        Assert.Contains("[\"ratio\"] = NormalizeRatio(item.Ratio)", submit, StringComparison.Ordinal);
        Assert.Contains("[\"resolution\"] = resolution", submit, StringComparison.Ordinal);
        Assert.Contains("[item.StartPublicUrl!, item.EndPublicUrl!]", submit, StringComparison.Ordinal);
        Assert.Contains("_options.DefaultVideoStartImageField", submit, StringComparison.Ordinal);
        Assert.Contains("_options.DefaultVideoEndImageField", submit, StringComparison.Ordinal);
        Assert.Contains("request.SanitizedJson", submit, StringComparison.Ordinal);

        var validationIndex = submit.IndexOf("ResolveVideoResolution(item.VideoMode)", StringComparison.Ordinal);
        var providerSubmitIndex = submit.IndexOf("_taskClient.SubmitAsync", StringComparison.Ordinal);
        Assert.True(validationIndex >= 0 && validationIndex < providerSubmitIndex);

        var diagnosticsStart = runtime.IndexOf("private SubmitRequestEnvelope BuildSubmitRequest", StringComparison.Ordinal);
        var diagnosticsEnd = runtime.IndexOf("private async Task<ImageReferencePayload>", diagnosticsStart, StringComparison.Ordinal);
        var diagnostics = runtime[diagnosticsStart..diagnosticsEnd];
        Assert.Contains("provider.ProviderCode", diagnostics, StringComparison.Ordinal);
        Assert.Contains("provider.Model", diagnostics, StringComparison.Ordinal);
        Assert.Contains("images,", diagnostics, StringComparison.Ordinal);
        Assert.Contains("options", diagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("provider.Credential.Secret", diagnostics[diagnostics.IndexOf("var sanitized", StringComparison.Ordinal)..], StringComparison.Ordinal);
    }

    [Fact]
    public void SeedanceVideoRetry_ReentersResolutionAwareSubmitPath()
    {
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("RetryVideoAsync(Guid jobId", workflow, StringComparison.Ordinal);
        Assert.Contains("StartReadyVideosAsync(conn, tx, jobId)", workflow, StringComparison.Ordinal);
        Assert.Contains("if (string.IsNullOrWhiteSpace(item.ProviderTaskId))", runtime, StringComparison.Ordinal);
        Assert.Contains("SubmitVideoAsync(item, ct)", runtime, StringComparison.Ordinal);
        Assert.Contains("[\"resolution\"] = resolution", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void SeedanceMigration_IsIdempotentInternalAndDoesNotChangeGlobalDefaults()
    {
        var migration = ReadSource("database", "manual", "ai-provider-catalog", "05_seed_79ai_seedance_timelapse_capability.sql");

        Assert.Contains("WHERE provider_code = '79ai'", migration, StringComparison.Ordinal);
        Assert.Contains("capability_code = 'image_to_video'", migration, StringComparison.Ordinal);
        Assert.Contains("model_name = 'seedance_20_pro'", migration, StringComparison.Ordinal);
        Assert.Contains("IF NOT FOUND THEN", migration, StringComparison.Ordinal);
        Assert.Contains("'Seedance 2.0'", migration, StringComparison.Ordinal);
        Assert.Contains("'/create-video'", migration, StringComparison.Ordinal);
        Assert.Contains("'poll_path', '/video'", migration, StringComparison.Ordinal);
        Assert.Contains("'fast', 'fast_2', 'professional', 'professional_2'", migration, StringComparison.Ordinal);
        Assert.Contains("ARRAY[4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15]", migration, StringComparison.Ordinal);
        Assert.Contains("'480p', '720p', '1080p'", migration, StringComparison.Ordinal);
        Assert.Contains("is_default = false", migration, StringComparison.Ordinal);
        Assert.Contains("false, true, false", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("unit_cost_points = EXCLUDED", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageRetry_PreservesJobAndCreatesANewAttemptForExplicitRuntime()
    {
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var workerRepository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("RetryImageAsync(Guid jobId", workflow, StringComparison.Ordinal);
        Assert.Contains("active_attempt=active_attempt+1", workflow, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO timelapse.timelapse_image_stage_versions", workflow, StringComparison.Ordinal);
        Assert.Contains("provider_task_id=NULL", workerRepository, StringComparison.Ordinal);
        Assert.Contains("_options.ImageCapabilityCode", runtime, StringComparison.Ordinal);
        Assert.Contains("_options.ImageModelName", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_SubmitsWhenTaskMissingAndPollsExistingTask()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("if (string.IsNullOrWhiteSpace(item.ProviderTaskId))", source);
        Assert.Contains("SubmitImageAsync(item, ct)", source);
        Assert.Contains("SubmitVideoAsync(item, ct)", source);
        Assert.Contains("var status = await PollAsync(", source);
        Assert.Contains("item.ProviderCode,", source);
        Assert.Contains("item.ProviderTaskId,", source);
        Assert.Contains("ReleaseImageClaimAsync", source);
        Assert.Contains("ReleaseVideoClaimAsync", source);
    }

    [Fact]
    public void FailedSubmit_PersistsSanitizedRequestAndResponseWithoutProviderTaskId()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var repository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("catch (Ai79TaskSubmitException ex)", runtime, StringComparison.Ordinal);
        Assert.Contains("SaveImageSubmitFailedAsync(", runtime, StringComparison.Ordinal);
        Assert.Contains("SaveVideoSubmitFailedAsync(", runtime, StringComparison.Ordinal);
        Assert.Contains("request.SanitizedJson", runtime, StringComparison.Ordinal);
        Assert.Contains("ex.SanitizedResponseJson", runtime, StringComparison.Ordinal);
        Assert.Contains("TryAddSubmitFailureEventAsync", runtime, StringComparison.Ordinal);

        Assert.Contains("provider_task_id=CASE WHEN @clearProviderTaskId THEN NULL ELSE provider_task_id END", repository, StringComparison.Ordinal);
        Assert.Contains("request_json=CASE", repository, StringComparison.Ordinal);
        Assert.Contains("ELSE CAST(@requestJson AS jsonb)", repository, StringComparison.Ordinal);
        Assert.Contains("response_json=CAST(@responseJson AS jsonb)", repository, StringComparison.Ordinal);
        Assert.Contains("SaveImageSubmitFailedAsync", repository, StringComparison.Ordinal);
        Assert.Contains("SaveVideoSubmitFailedAsync", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageSubmitDiagnostics_ContainVerifiedSeedreamRequestContractWithoutSecret()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var options = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderWorkerOptions.cs");

        Assert.Contains("ImageModelName { get; set; } = \"seedream_5_0\"", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageReferenceField { get; set; } = \"base64Image\"", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageMode { get; set; } = \"vip\"", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageResolution { get; set; } = \"2k\"", options, StringComparison.Ordinal);
        Assert.Contains("provider.ProviderCode", runtime, StringComparison.Ordinal);
        Assert.Contains("provider.Model", runtime, StringComparison.Ordinal);
        Assert.Contains("provider.BaseUrl", runtime, StringComparison.Ordinal);
        Assert.Contains("endpointPath = provider.SubmitPath", runtime, StringComparison.Ordinal);
        Assert.Contains("provider.Domain", runtime, StringComparison.Ordinal);
        Assert.Contains("prompt,", runtime, StringComparison.Ordinal);
        Assert.Contains("action_type = \"create\"", runtime, StringComparison.Ordinal);
        Assert.Contains("editImage = true", runtime, StringComparison.Ordinal);
        Assert.Contains("subjects = Array.Empty<string>()", runtime, StringComparison.Ordinal);
        Assert.Contains("base64ImagePresent = true", runtime, StringComparison.Ordinal);
        Assert.Contains("base64ImageMime = reference.MimeType", runtime, StringComparison.Ordinal);
        Assert.Contains("base64ImageBytes = reference.Bytes", runtime, StringComparison.Ordinal);
        Assert.Contains("ResolveImageReferenceAsync", runtime, StringComparison.Ordinal);
        Assert.Contains("_media.ReadBytesAsync(media.Id", runtime, StringComparison.Ordinal);
        var imageBuilderStart = runtime.IndexOf("private SubmitRequestEnvelope BuildImageSubmitRequest", StringComparison.Ordinal);
        var imageBuilderEnd = runtime.IndexOf("private SubmitRequestEnvelope BuildSubmitRequest", imageBuilderStart, StringComparison.Ordinal);
        var imageBuilder = runtime[imageBuilderStart..imageBuilderEnd];
        var sanitizedStart = imageBuilder.IndexOf("var sanitized = JsonSerializer.Serialize", StringComparison.Ordinal);
        Assert.DoesNotContain("progress_percent", imageBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("provider.Credential.Secret", imageBuilder[sanitizedStart..], StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_PersistsProviderOutputsToTodoXMedia()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("DownloadAndSaveImageAtObjectKeyAsync", source);
        Assert.Contains("DownloadAndSaveBinaryAtObjectKeyAsync", source);
        Assert.Contains("SaveImageCompletedAsync", source);
        Assert.Contains("SaveVideoCompletedAsync", source);
        Assert.Contains("never used as final customer media", ReadSource("TodoX.Web", "docs", "timelapse-phase-2c-provider-workers.md"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Finalizer_UsesOrderedClipsAndFfmpegConcatCopy()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseFinalizerRuntime.cs");
        var repo = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("OrderBy(x => x.ClipIndex)", source);
        Assert.Contains("-f", source);
        Assert.Contains("concat", source);
        Assert.Contains("-c", source);
        Assert.Contains("copy", source);
        Assert.Contains("ORDER BY clip_index", repo);
        Assert.Contains("SaveFinalizerCompletedAsync", source);
        Assert.Contains("Storage:Provider", source);
        Assert.Contains("requires local media storage", source);
    }

    [Fact]
    public void Runtime_UsesProviderSpecificImageFieldsWithoutGenericImagesPayloadForTwoImages()
    {
        var client = ReadSource("TodoX.Web", "Services", "AiProviders", "Ai79TaskClient.cs");
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("DefaultVideoStartImageField", runtime, StringComparison.Ordinal);
        Assert.Contains("DefaultVideoEndImageField", runtime, StringComparison.Ordinal);
        Assert.Contains("request.Images.Count > 2", client, StringComparison.Ordinal);
        Assert.Contains("request.Operation == Ai79TaskOperation.Image ? \"id_base\" : \"task_id\"", client, StringComparison.Ordinal);
        Assert.Contains("FindImageIdBase", client, StringComparison.Ordinal);
        Assert.DoesNotContain("FindString(document.RootElement, \"task_id\", \"taskId\", \"request_id\", \"requestId\", \"id\")", client, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderStatusNormalizer_MapsKnownStatuses()
    {
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("SUCCESS"));
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("completed"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("FAILURE"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("error"));
        Assert.Equal(Ai79TaskStatusNormalizer.Running, Ai79TaskStatusNormalizer.Normalize("pending"));
        Assert.Equal(Ai79TaskStatusNormalizer.Running, Ai79TaskStatusNormalizer.Normalize(null));
    }

    [Fact]
    public void PromptResolver_UsesProfileSnapshotAndStageProgress()
    {
        var snapshot = new TodoX.Web.Models.Timelapse.TimelapseJobSnapshot
        {
            ProfileName = "Nhà phố",
            ProfileCode = "townhouse",
            Ratio = TodoX.Web.Models.Timelapse.TimelapseRequestRules.LandscapeRatio
        };

        var prompt = TimelapsePromptResolver.ResolveImagePrompt(snapshot, 70,
            """{"ProfileJson":"Use existing n8n construction prompt semantics."}""");

        Assert.Contains("Use existing n8n construction prompt semantics.", prompt);
        Assert.Contains("70%", prompt);
        Assert.Contains("Nhà phố", prompt);
    }

    [Fact]
    public void Report_DocumentsRequiredPhase2CContracts()
    {
        var report = ReadSource("TodoX.Web", "docs", "timelapse-phase-2c-provider-workers.md");

        Assert.Contains("image provider path", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("video provider path", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submit/poll", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker claiming", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restart", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("media persistence", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state advancement", report, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finalizer", report, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
