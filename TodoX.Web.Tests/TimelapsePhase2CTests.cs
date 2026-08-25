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
        Assert.Contains("\"DefaultImageUploadPath\": \"/image-upload\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"DefaultVideoSubmitPath\": \"/create-video\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"ProviderCode\": \"79ai\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"ImageCapabilityCode\": \"image_generation\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("\"ImageModelName\": \"google_image_gen_banana_2\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
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
    public void VideoCompletion_AutomaticallyStartsFinalizerWhenAllCurrentClipsComplete()
    {
        var repository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");

        var advanceStart = repository.IndexOf("public async Task<bool> AdvanceAfterVideoCompletedAsync", StringComparison.Ordinal);
        var advanceEnd = repository.IndexOf("private async Task<bool> TryStartFinalizerIfReadyAsync", advanceStart, StringComparison.Ordinal);
        Assert.True(advanceStart >= 0 && advanceEnd > advanceStart, "Expected video completion advancement method.");
        var advance = repository[advanceStart..advanceEnd];

        Assert.Contains("await LockJobAsync(conn, tx, jobId);", advance, StringComparison.Ordinal);
        Assert.Contains("statusCounts.TotalVideos > 0 && statusCounts.IncompleteVideos == 0", advance, StringComparison.Ordinal);
        Assert.Contains("finalizerStarted = await TryStartFinalizerIfReadyAsync(conn, tx, jobId)", advance, StringComparison.Ordinal);
        Assert.Contains("return finalizerStarted;", advance, StringComparison.Ordinal);
        Assert.Contains("finalizing = TimelapseParentStatuses.Finalizing", advance, StringComparison.Ordinal);
        Assert.Contains("completed = TimelapseParentStatuses.Completed", advance, StringComparison.Ordinal);

        Assert.Contains("var finalizerStarted = await _repo.AdvanceAfterVideoCompletedAsync(item.JobId, ct);", runtime, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_FINALIZER_AUTO_STARTED", runtime, StringComparison.Ordinal);
        Assert.Contains("queued automatically after all Timelapse video clips completed", runtime, StringComparison.Ordinal);

        Assert.DoesNotContain("Hoàn thành video", razor, StringComparison.Ordinal);
        Assert.Contains("Đang hoàn thiện video...", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void AutoFinalizerReadiness_IsIdempotentAndVersionedForRetry()
    {
        var repository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");

        var helperStart = repository.IndexOf("private async Task<bool> TryStartFinalizerIfReadyAsync", StringComparison.Ordinal);
        var helperEnd = repository.IndexOf("private async Task UpdateStageSubmittedAsync", helperStart, StringComparison.Ordinal);
        Assert.True(helperStart >= 0 && helperEnd > helperStart, "Expected auto-finalizer helper.");
        var helper = repository[helperStart..helperEnd];

        Assert.Contains("TotalVideos == 0", helper, StringComparison.Ordinal);
        Assert.Contains("CompletedVideos != readiness.TotalVideos", helper, StringComparison.Ordinal);
        Assert.Contains("HasRenderingFinal", helper, StringComparison.Ordinal);
        Assert.Contains("HasCompletedFinal", helper, StringComparison.Ordinal);
        Assert.Contains("COALESCE((", helper, StringComparison.Ordinal);
        Assert.Contains("SELECT max(version) + 1", helper, StringComparison.Ordinal);
        Assert.Contains("startedBy = \"auto_video_completion\"", helper, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO timelapse.timelapse_final_outputs", helper, StringComparison.Ordinal);
        Assert.Contains("status=@status", helper, StringComparison.Ordinal);
        Assert.Contains("status = TimelapseParentStatuses.Finalizing", helper, StringComparison.Ordinal);

        var retryStart = workflow.IndexOf("public async Task<TimelapseWorkflowState> RetryVideoAsync", StringComparison.Ordinal);
        var retryEnd = workflow.IndexOf("public async Task<TimelapseWorkflowState> StartFinalizerAsync", retryStart, StringComparison.Ordinal);
        var retry = workflow[retryStart..retryEnd];
        Assert.Contains("await CancelFinalizerAsync(conn, tx, jobId);", retry, StringComparison.Ordinal);
        Assert.Contains("await InvalidateFinalAsync(conn, tx, jobId);", retry, StringComparison.Ordinal);
        Assert.Contains("StartReadyVideosAsync(conn, tx, jobId)", retry, StringComparison.Ordinal);
    }

    [Fact]
    public void FinalizerClaimSql_DoesNotReferenceUpdateAliasInsideJoinOn()
    {
        var source = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var methodStart = source.IndexOf("public async Task<TimelapseFinalizerWorkItem?> ClaimFinalizerAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("public async Task<bool> SaveFinalizerCompletedAsync", methodStart, StringComparison.Ordinal);
        var claimSql = source[methodStart..methodEnd];

        Assert.Contains("WITH candidate AS", claimSql, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE SKIP LOCKED", claimSql, StringComparison.Ordinal);
        Assert.Contains("UPDATE timelapse.timelapse_final_outputs f", claimSql, StringComparison.Ordinal);
        Assert.Contains("request_json=jsonb_set", claimSql, StringComparison.Ordinal);
        Assert.Contains("'{worker_claim}'", claimSql, StringComparison.Ordinal);
        Assert.Contains("started_at=COALESCE(f.started_at, now())", claimSql, StringComparison.Ordinal);
        Assert.Contains("FROM render.render_jobs j", claimSql, StringComparison.Ordinal);
        Assert.Contains("candidate c", claimSql, StringComparison.Ordinal);
        Assert.Contains("WHERE c.id=f.id", claimSql, StringComparison.Ordinal);
        Assert.Contains("AND j.id=f.job_id", claimSql, StringComparison.Ordinal);
        Assert.Contains("RETURNING f.id AS Id", claimSql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN candidate c ON c.id=f.id", claimSql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN candidate c ON c.id = f.id", claimSql, StringComparison.Ordinal);
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
        Assert.Contains("ImageModelName { get; set; } = NanoBanana2ModelName", options, StringComparison.Ordinal);
        Assert.Contains("VideoCapabilityCode { get; set; } = \"image_to_video\"", options, StringComparison.Ordinal);
        Assert.Contains("VideoModelName { get; set; } = \"seedance_20_pro\"", options, StringComparison.Ordinal);

        Assert.Contains("p.provider_code = @providerCode", repository, StringComparison.Ordinal);
        Assert.Contains("c.capability_code = @capabilityCode", repository, StringComparison.Ordinal);
        Assert.Contains("c.model_name = @modelName", repository, StringComparison.Ordinal);
        Assert.Contains("p.enabled = true", repository, StringComparison.Ordinal);
        Assert.Contains("c.enabled = true", repository, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDefaultAsync", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("GetFirstByPriorityAsync", runtime, StringComparison.Ordinal);

        Assert.Contains("$\"Chưa cấu hình model {model} cho Timelapse image.\"", runtime, StringComparison.Ordinal);
        Assert.Contains("FirstNonBlank(option.ModelName, capability.ModelName)", runtime, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_IMAGE_PROVIDER_RESOLVE_FAILED", runtime, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(null, "2k")]
    [InlineData("", "2k")]
    [InlineData("1k", "2k")]
    [InlineData(" 1K ", "2k")]
    [InlineData("2k", "2k")]
    [InlineData("4K", "4k")]
    public void SeedreamImageResolution_NormalizesLiveProviderValuesBeforeSubmit(string? input, string expected)
    {
        Assert.Equal(expected, TimelapseProviderWorkerOptions.NormalizeImageResolution("seedream_5_0", input));
    }

    [Theory]
    [InlineData("720p")]
    [InlineData("8k")]
    [InlineData("hd")]
    public void SeedreamImageResolution_RejectsUnsupportedProviderValuesBeforeSubmit(string input)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => TimelapseProviderWorkerOptions.NormalizeImageResolution("seedream_5_0", input));

        Assert.Contains("2k, 4k", error.Message, StringComparison.Ordinal);
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
        Assert.Contains("var startDescriptor = await BuildVideoImageDescriptorAsync", submit, StringComparison.Ordinal);
        Assert.Contains("var endDescriptor = await BuildVideoImageDescriptorAsync", submit, StringComparison.Ordinal);
        Assert.Contains("JsonSerializer.Serialize(new[] { startDescriptor, endDescriptor }", submit, StringComparison.Ordinal);
        Assert.Contains("[\"privacy\"] = \"PRIVATE\"", submit, StringComparison.Ordinal);
        Assert.Contains("[\"translate_to_en\"] = \"false\"", submit, StringComparison.Ordinal);
        Assert.Contains("[\"project_id\"] = _options.DefaultImageProjectId", submit, StringComparison.Ordinal);
        Assert.Contains("[\"images\"] = imagesJson", submit, StringComparison.Ordinal);
        Assert.Contains("Ai79TaskOperation.Video, null, null", submit, StringComparison.Ordinal);
        Assert.DoesNotContain("[item.StartPublicUrl!, item.EndPublicUrl!]", submit, StringComparison.Ordinal);
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
        Assert.Contains("SelectImageModel(item.ProviderModel", runtime, StringComparison.Ordinal);
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

        Assert.Contains("ImageModelName { get; set; } = NanoBanana2ModelName", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageUploadPath { get; set; } = \"/image-upload\"", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageReferenceField { get; set; } = \"base64Image\"", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageMode { get; set; } = \"vip\"", options, StringComparison.Ordinal);
        Assert.Contains("DefaultImageResolution { get; set; } = \"2k\"", options, StringComparison.Ordinal);
        Assert.Contains("\"DefaultImageResolution\": \"2k\"", ReadSource("TodoX.Web", "appsettings.json"), StringComparison.Ordinal);
        Assert.Contains("NormalizeImageResolution(model, configuredImageResolution)", runtime, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_IMAGE_RESOLUTION_NORMALIZED", runtime, StringComparison.Ordinal);
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
        Assert.Contains("[\"resolution\"] = resolution", imageBuilder, StringComparison.Ordinal);
        Assert.Contains("resolution,", imageBuilder, StringComparison.Ordinal);
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
        var options = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderWorkerOptions.cs");
        var appsettings = ReadSource("TodoX.Web", "appsettings.json");

        Assert.Contains("OrderBy(x => x.ClipIndex)", source);
        Assert.Contains("-f", source);
        Assert.Contains("concat", source);
        Assert.Contains("-c", source);
        Assert.Contains("copy", source);
        Assert.Contains("ORDER BY clip_index", repo);
        Assert.Contains("SaveFinalizerCompletedAsync", source);
        Assert.Contains("Storage:Provider", source);
        Assert.Contains("requires local media storage", source);
        Assert.Contains("IOptions<TimelapseProviderWorkerOptions>", source, StringComparison.Ordinal);
        Assert.Contains("FinalizerFfmpegTimeoutSeconds { get; set; } = 120", options, StringComparison.Ordinal);
        Assert.Contains("\"FinalizerFfmpegTimeoutSeconds\": 120", appsettings, StringComparison.Ordinal);
        Assert.Contains("Math.Max(1, _options.FinalizerFfmpegTimeoutSeconds)", source, StringComparison.Ordinal);
        Assert.Contains("new CancellationTokenSource(timeout)", source, StringComparison.Ordinal);
        Assert.Contains("process.Kill(entireProcessTree: true)", source, StringComparison.Ordinal);
        Assert.Contains("TimeoutException", source, StringComparison.Ordinal);
        Assert.Contains("FFmpeg concat timed out after", source, StringComparison.Ordinal);
        Assert.Contains("stderr={Stderr}", source, StringComparison.Ordinal);
        Assert.Contains("SaveFinalizerFailedAsync", source, StringComparison.Ordinal);
        Assert.Contains("Utf8NoBom", source, StringComparison.Ordinal);
        Assert.Contains("new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)", source, StringComparison.Ordinal);
        Assert.Contains("BuildConcatFile(paths), Utf8NoBom", source, StringComparison.Ordinal);
        Assert.Contains("Path.GetFullPath(path)", source, StringComparison.Ordinal);
        Assert.Contains(".Replace('\\\\', '/')", source, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_FINALIZER_FFMPEG_START", source, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_FINALIZER_FFMPEG_CLIP", source, StringComparison.Ordinal);
        Assert.Contains("BuildFfmpegFailureMessage", source, StringComparison.Ordinal);
        Assert.Contains("ExtractUsefulFfmpegTail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildConcatFile(paths), Encoding.UTF8", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalizerConcatFile_UsesUtf8NoBomAndNormalizedFfmpegPaths()
    {
        var concat = TimelapseFinalizerRuntime.BuildConcatFile(new[]
        {
            @"H:\folder\clip-1.mp4",
            @"H:\folder\clip-2.mp4",
            @"H:\O'Brien\clip-3.mp4"
        });

        var lines = concat.Split(Environment.NewLine);
        Assert.Equal("file 'H:/folder/clip-1.mp4'", lines[0]);
        Assert.Equal("file 'H:/folder/clip-2.mp4'", lines[1]);
        Assert.Equal("file 'H:/O'\\''Brien/clip-3.mp4'", lines[2]);
        Assert.All(lines, line => Assert.StartsWith("file '", line, StringComparison.Ordinal));
        Assert.DoesNotContain(@"H:\folder", concat, StringComparison.Ordinal);

        var concatPath = Path.Combine(Path.GetTempPath(), $"todox-concat-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(concatPath, concat, TimelapseFinalizerRuntime.Utf8NoBom);
            var bytes = await File.ReadAllBytesAsync(concatPath);

            Assert.True(bytes.Length > 3);
            Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            Assert.Empty(TimelapseFinalizerRuntime.Utf8NoBom.GetPreamble());
        }
        finally
        {
            if (File.Exists(concatPath))
            {
                File.Delete(concatPath);
            }
        }
    }

    [Fact]
    public void FinalizerFfmpegErrorMessage_RemovesBannerAndKeepsUsefulTail()
    {
        var stderr = string.Join('\n', new[]
        {
            "ffmpeg version 7.1-full_build-www.gyan.dev Copyright",
            "built with gcc 14.2.0",
            "configuration: --enable-gpl --enable-version3 --enable-static",
            "libavutil      59. 39.100 / 59. 39.100",
            "libavcodec     61. 19.100 / 61. 19.100",
            "[concat @ 000001] Impossible to open 'H:/folder/clip.mp4'",
            "concat.txt: Invalid data found when processing input"
        });

        var sanitized = TimelapseFinalizerRuntime.BuildFfmpegFailureMessage("FFmpeg concat failed.", stderr);

        Assert.DoesNotContain("ffmpeg version", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("configuration:", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("libavcodec", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Impossible to open", sanitized, StringComparison.Ordinal);
        Assert.Contains("Invalid data found when processing input", sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length <= 2000);
    }

    [Fact]
    public void Runtime_UsesProviderSpecificImageFieldsWithoutGenericImagesPayloadForTwoImages()
    {
        var client = ReadSource("TodoX.Web", "Services", "AiProviders", "Ai79TaskClient.cs");
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        Assert.Contains("[\"images\"] = imagesJson", runtime, StringComparison.Ordinal);
        Assert.Contains("BuildVideoImageDescriptorAsync", runtime, StringComparison.Ordinal);
        Assert.Contains("UploadImageAsync", runtime, StringComparison.Ordinal);
        Assert.Contains("DefaultImageUploadPath", runtime, StringComparison.Ordinal);
        Assert.Contains("request.Images.Count > 2", client, StringComparison.Ordinal);
        Assert.Contains("request.Operation == Ai79TaskOperation.Image ? \"id_base\" : \"videoId\"", client, StringComparison.Ordinal);
        Assert.Contains("FindImageIdBase", client, StringComparison.Ordinal);
        Assert.DoesNotContain("FindString(document.RootElement, \"task_id\", \"taskId\", \"request_id\", \"requestId\", \"id\")", client, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_AuditsN8nVideoContractAndPortsSubmitPollCancellation()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var repo = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");
        var client = ReadSource("TodoX.Web", "Services", "AiProviders", "Ai79TaskClient.cs");
        var report = ReadSource("docs", "core-platform", "reports", "construction-video-n8n-contract-port-report.md");

        Assert.Contains("todox_timelapse_05_video_submit_v4.4_worker_anchor_prompt.json", report, StringComparison.Ordinal);
        Assert.DoesNotContain("todoX-rendervideo-04-video-worker", report, StringComparison.Ordinal);
        Assert.Contains("POST /create-video", report, StringComparison.Ordinal);
        Assert.Contains("POST /video", report, StringComparison.Ordinal);
        Assert.Contains("POST /videos", report, StringComparison.Ordinal);
        Assert.Contains("POST /image-upload", report, StringComparison.Ordinal);
        Assert.Contains("images JSON descriptor", report, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("start_v.response_json::text AS StartResponseJson", repo, StringComparison.Ordinal);
        Assert.Contains("end_v.response_json::text AS EndResponseJson", repo, StringComparison.Ordinal);
        Assert.Contains("string? StartResponseJson", repo, StringComparison.Ordinal);
        Assert.Contains("string? EndResponseJson", repo, StringComparison.Ordinal);
        Assert.Contains("start_img.prompt_snapshot_json::text AS StartPromptSnapshotJson", repo, StringComparison.Ordinal);
        Assert.Contains("end_img.prompt_snapshot_json::text AS EndPromptSnapshotJson", repo, StringComparison.Ordinal);

        Assert.Contains("[\"images\"] = imagesJson", runtime, StringComparison.Ordinal);
        Assert.Contains("new[] { startDescriptor, endDescriptor }", runtime, StringComparison.Ordinal);
        Assert.Contains("ExtractImageIdBase", runtime, StringComparison.Ordinal);
        Assert.Contains("UploadImageAsync", runtime, StringComparison.Ordinal);
        Assert.Contains("ResolveProviderImageUrl", runtime, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_VIDEO_POLL_TRANSIENT", runtime, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException ex)", runtime, StringComparison.Ordinal);
        Assert.Contains("ReleaseVideoClaimAsync(item.Id, item.Attempt, CancellationToken.None)", runtime, StringComparison.Ordinal);

        Assert.Contains("\"videoId\"", client, StringComparison.Ordinal);
        Assert.Contains("ResolveVideosListPath", client, StringComparison.Ordinal);
        Assert.Contains("TryFindVideoInfoById", client, StringComparison.Ordinal);
        Assert.Contains("FindVideoOutputUrl", client, StringComparison.Ordinal);
        Assert.Contains("videoInfo", client, StringComparison.Ordinal);
        Assert.Contains("MEDIA_GENERATION_STATUS_SUCCESSFUL", client, StringComparison.Ordinal);
        Assert.Contains("MEDIA_GENERATION_COMPLETED", client, StringComparison.Ordinal);
        Assert.Contains("MEDIA_GENERATION_STATUS_FAILED", client, StringComparison.Ordinal);
        Assert.Contains("MEDIA_GENERATION_FAILED", client, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoPrompt_UsesStrictConstructionContinuityRulesAndSixSceneMapping()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var models = ReadSource("TodoX.Web", "Models", "Timelapse", "TimelapseModels.cs");

        Assert.Contains("Use @image1 as the exact starting frame and @image2 as the exact ending frame.", runtime, StringComparison.Ordinal);
        Assert.Contains("same building, architecture, footprint, floor count, window/opening layout, roof geometry, camera, lens, perspective, framing, and environment", runtime, StringComparison.Ordinal);
        Assert.Contains("Never remove permanent elements visible in @image1.", runtime, StringComparison.Ordinal);
        Assert.Contains("Do not demolish, reset, rebuild from scratch", runtime, StringComparison.Ordinal);
        Assert.Contains("Only add or advance work necessary to reach @image2.", runtime, StringComparison.Ordinal);
        Assert.Contains("The final frame must converge visually to @image2.", runtime, StringComparison.Ordinal);
        Assert.Contains("Workers may move naturally", runtime, StringComparison.Ordinal);
        Assert.Contains("6 => [0, 25, 40, 55, 70, 75, 90, 100]", models, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoPrompt_DoesNotSerializeProfileMetadataAndFitsProviderBudget()
    {
        var largeMetadata = new string('x', 5600);
        var profileSnapshot = """
            {
              "profileJson": {
                "id": 1,
                "enabled": true,
                "category": "construction_exterior",
                "select_no": 1,
                "created_at": "2026-08-16T00:00:00Z",
                "updated_at": "2026-08-16T01:00:00Z",
                "metadata_blob": "__LARGE_METADATA__",
                "phase_rules": [
                  {
                    "min_progress": 75,
                    "max_progress": 100,
                    "phase_goal": "finish exterior envelope and final facade details",
                    "prompt_fragment": "Install roof panels carefully and finish facade surfaces.",
                    "worker_actions": "installing roof panels, sealing facade joints",
                    "must_exist": ["finished roof", "clean facade"],
                    "must_not_exist": ["demolition debris"]
                  }
                ],
                "continuity_rules": {
                  "must_preserve": ["same facade rhythm", "same roof pitch"],
                  "must_avoid": ["new windows", "camera jump"]
                },
                "video_generation": {
                  "video_clip_prompt_template": "Profile detail: {{phase_goal}}. Actions: {{worker_actions}}. Preserve: {{must_preserve}}. Avoid: {{must_avoid}}. {{prompt_fragment}}"
                }
              }
            }
            """.Replace("__LARGE_METADATA__", largeMetadata, StringComparison.Ordinal);
        var snapshot = new TodoX.Web.Models.Timelapse.TimelapseJobSnapshot
        {
            ProfileName = "Construction Exterior",
            SceneCount = 6,
            ProgressMapping = TodoX.Web.Models.Timelapse.TimelapseRequestRules.GetProgressMapping(6)
        };

        var oldRepresentativeLength = profileSnapshot.Length
                                      + TimelapsePromptResolver.ResolveVideoPrompt(snapshot, 4, 75, 100).Length;
        var prompt = TimelapsePromptResolver.ResolveVideoPromptEnvelope(snapshot, 4, 75, 100, profileSnapshot);

        Assert.True(oldRepresentativeLength > 5000);
        Assert.True(prompt.Prompt.Length <= TimelapsePromptResolver.MaxProviderPromptLength);
        Assert.Contains("Install roof panels carefully", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("finish exterior envelope", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("Use @image1 as the exact starting frame and @image2 as the exact ending frame.", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("Never remove permanent elements visible in @image1.", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("Do not demolish, reset, rebuild from scratch", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("The final frame must converge visually to @image2.", prompt.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\": 1", prompt.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"enabled\": true", prompt.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"created_at\"", prompt.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"updated_at\"", prompt.Prompt, StringComparison.Ordinal);
        Assert.False(prompt.ProfilePromptTruncated);
    }

    [Fact]
    public void VideoPrompt_TrimsOnlyOptionalProfileTextAndDiagnosticsArePersisted()
    {
        var longFragment = new string('a', 8000);
        var profileSnapshot = """
            {
              "ProfileJson": {
                "phase_rules": [
                  {
                    "min_progress": 0,
                    "max_progress": 100,
                    "phase_goal": "advance construction",
                    "prompt_fragment": "__LONG_FRAGMENT__",
                    "worker_actions": "installing materials"
                  }
                ],
                "video_generation": {
                  "video_clip_prompt_template": "{{prompt_fragment}}"
                }
              }
            }
            """.Replace("__LONG_FRAGMENT__", longFragment, StringComparison.Ordinal);
        var snapshot = new TodoX.Web.Models.Timelapse.TimelapseJobSnapshot
        {
            ProfileName = "Construction Exterior",
            SceneCount = 6
        };
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");

        var prompt = TimelapsePromptResolver.ResolveVideoPromptEnvelope(snapshot, 4, 75, 100, profileSnapshot);

        Assert.True(prompt.Prompt.Length <= TimelapsePromptResolver.MaxProviderPromptLength);
        Assert.True(prompt.ProfilePromptTruncated);
        Assert.Contains("Use @image1 as the exact starting frame and @image2 as the exact ending frame.", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("The final frame must converge visually to @image2.", prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("prompt_length = prompt.Prompt.Length", runtime, StringComparison.Ordinal);
        Assert.Contains("profile_prompt_length = prompt.ProfilePromptLength", runtime, StringComparison.Ordinal);
        Assert.Contains("profile_prompt_truncated = prompt.ProfilePromptTruncated", runtime, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoProfilePrompt_DoesNotUseRawJsonFallback()
    {
        var runtime = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseProviderRuntime.cs");
        var videoExtractorStart = runtime.IndexOf("private static string ExtractVideoProfilePrompt", StringComparison.Ordinal);
        var videoExtractorEnd = runtime.IndexOf("private static JsonElement? ExtractProfileJsonElement", videoExtractorStart, StringComparison.Ordinal);
        var videoExtractor = runtime[videoExtractorStart..videoExtractorEnd];

        Assert.DoesNotContain("GetRawText", videoExtractor, StringComparison.Ordinal);
        Assert.DoesNotContain("metadata_blob", videoExtractor, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseDetailUi_RemovesVideoInputThumbnailsAndKeepsPreviewConstrained()
    {
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");
        var mediaCss = ReadSource("TodoX.Web", "Components", "Shared", "RenderMediaFrame.razor.css");

        Assert.Contains("video-stage-card", razor, StringComparison.Ordinal);
        Assert.Contains("PreviewClass(\"video-preview\"", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"clip-input-thumbnails\"", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderImageThumb", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("clip-input-image", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("clip-input-placeholder", razor, StringComparison.Ordinal);

        Assert.Contains(".timelapse-stage-grid > *", css, StringComparison.Ordinal);
        Assert.Contains("repeat(auto-fill, minmax(260px, 320px))", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 0;", css, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%;", css, StringComparison.Ordinal);
        Assert.Contains("overflow: hidden;", css, StringComparison.Ordinal);
        Assert.Contains("object-fit: contain;", mediaCss, StringComparison.Ordinal);
        Assert.Contains("aspect-ratio: 16 / 9;", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".clip-input-thumbnails", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".clip-input-reference", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseProcessingOverlay_UsesIsolatedFullFrameAnimationAndPreservesReducedMotion()
    {
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var pageCss = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");
        var sharedMediaCss = ReadSource("TodoX.Web", "Components", "Shared", "RenderMediaFrame.razor.css");
        var overlay = ReadSource("TodoX.Web", "Components", "Timelapse", "TimelapseProcessingOverlay.razor");
        var overlayCss = ReadSource("TodoX.Web", "Components", "Timelapse", "TimelapseProcessingOverlay.razor.css");

        Assert.Contains("ResolveTimelapseMediaState", razor, StringComparison.Ordinal);
        Assert.Contains("<RenderMediaFrame IsVideo=\"false\"", razor, StringComparison.Ordinal);
        Assert.Contains("<RenderMediaFrame IsVideo=\"true\"", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderProcessingOverlay", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("RenderTreeBuilder", razor, StringComparison.Ordinal);
        Assert.DoesNotContain(".tl-processing-mask", pageCss, StringComparison.Ordinal);
        Assert.DoesNotContain("@keyframes tl-processing-beam-travel", pageCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".clip-input-thumbnails", pageCss, StringComparison.Ordinal);

        Assert.Contains("class=\"tl-processing-mask @(IsVideo ? \"is-video\" : \"is-image\")\"", overlay, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", overlay, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@(IsVideo ? \"Video processing\" : \"Image processing\")\"", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-processing-flash", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-processing-beam", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-processing-scan", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-processing-spinner", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-processing-dots", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-processing-wave", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MudIcon", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("MovieCreation", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoAwesome", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("tl-processing-text", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0110ang t\u1ea1o video", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0110ang t\u1ea1o \u1ea3nh", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Äang táº¡o video", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Äang táº¡o áº£nh", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Ã„Âang tÃ¡ÂºÂ¡o video", overlay, StringComparison.Ordinal);
        Assert.DoesNotContain("Ã„Âang tÃ¡ÂºÂ¡o Ã¡ÂºÂ£nh", overlay, StringComparison.Ordinal);

        Assert.Contains(".tl-processing-mask", overlayCss, StringComparison.Ordinal);
        Assert.Contains(".tl-processing-mask.is-video", overlayCss, StringComparison.Ordinal);
        Assert.Contains(".tl-processing-beam", overlayCss, StringComparison.Ordinal);
        Assert.Contains("transform: translateX(-180%) skewX(-15deg);", overlayCss, StringComparison.Ordinal);
        Assert.Contains("transform: translateX(520%) skewX(-15deg);", overlayCss, StringComparison.Ordinal);
        Assert.Contains("top: -5%;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("top: 105%;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("left: -5%;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("left: 105%;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-mask-pulse", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-beam-travel", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-scan-down", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-scan-across", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-spin", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-dots", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-processing-wave", overlayCss, StringComparison.Ordinal);
        Assert.Contains("animation: tl-processing-spin 0.75s linear infinite;", overlayCss, StringComparison.Ordinal);
        Assert.DoesNotContain("tl-processing-icon-pulse", overlayCss, StringComparison.Ordinal);
        Assert.DoesNotContain("tl-processing-text-shimmer", overlayCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".tl-processing-text", overlayCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-icon-root", overlayCss, StringComparison.Ordinal);
        Assert.Contains("transform: rotate(360deg);", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", sharedMediaCss, StringComparison.Ordinal);

        var reducedMotionStart = overlayCss.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal);
        var reducedMotion = overlayCss[reducedMotionStart..];
        var blanketDisableEnd = reducedMotion.IndexOf("animation: none !important;", StringComparison.Ordinal);
        var blanketDisable = reducedMotion[..blanketDisableEnd];

        Assert.DoesNotContain(".tl-processing-mask,", blanketDisable, StringComparison.Ordinal);
        Assert.DoesNotContain(".tl-processing-spinner,", blanketDisable, StringComparison.Ordinal);
        Assert.DoesNotContain(".tl-processing-dots i,", blanketDisable, StringComparison.Ordinal);
        Assert.Contains(".tl-processing-beam", blanketDisable, StringComparison.Ordinal);
        Assert.Contains(".tl-processing-scan", blanketDisable, StringComparison.Ordinal);
        Assert.Contains(".tl-processing-wave i", blanketDisable, StringComparison.Ordinal);
        Assert.Contains("animation: tl-processing-mask-pulse 2.4s ease-in-out infinite !important;", reducedMotion, StringComparison.Ordinal);
        Assert.Contains("animation: tl-processing-spin 2.4s linear infinite !important;", reducedMotion, StringComparison.Ordinal);
        Assert.Contains("animation: tl-processing-dots 1.6s ease-in-out infinite !important;", reducedMotion, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseFinalizingOverlay_AnimatesVideoPiecesAndPreservesCompletedOutput()
    {
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var pageCss = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");
        var overlay = ReadSource("TodoX.Web", "Components", "Timelapse", "TimelapseFinalizingOverlay.razor");
        var overlayCss = ReadSource("TodoX.Web", "Components", "Timelapse", "TimelapseFinalizingOverlay.razor.css");

        Assert.Contains("IsFinalOutputLoading", razor, StringComparison.Ordinal);
        Assert.Contains("TimelapseParentStatuses.Finalizing", razor, StringComparison.Ordinal);
        Assert.Contains("<TimelapseFinalizingOverlay Title=\"@FinalLoadingTitle\" Subtitle=\"@FinalLoadingSubtitle\" />", razor, StringComparison.Ordinal);
        Assert.Contains("<video src=\"@_job.Workflow.FinalOutput.PublicUrl\" controls playsinline class=\"final-video\"></video>", razor, StringComparison.Ordinal);
        Assert.Contains("_job.Workflow.FinalOutput?.Status == TimelapseOperationStatuses.Completed", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"tl-final-loading", razor, StringComparison.Ordinal);
        Assert.DoesNotContain(".tl-final-loading", pageCss, StringComparison.Ordinal);

        Assert.Contains("class=\"tl-finalizing-shell\"", overlay, StringComparison.Ordinal);
        Assert.Contains("role=\"status\"", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-stage", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-piece piece-1", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-piece piece-2", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-piece piece-3", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-piece piece-4", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-frame", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-glow", overlay, StringComparison.Ordinal);
        Assert.Contains("tl-finalizing-progress", overlay, StringComparison.Ordinal);
        Assert.Contains("@Title", overlay, StringComparison.Ordinal);
        Assert.Contains("@Subtitle", overlay, StringComparison.Ordinal);

        Assert.Contains(".tl-finalizing-shell", overlayCss, StringComparison.Ordinal);
        Assert.Contains(".tl-finalizing-piece", overlayCss, StringComparison.Ordinal);
        Assert.Contains(".piece-1", overlayCss, StringComparison.Ordinal);
        Assert.Contains(".piece-4", overlayCss, StringComparison.Ordinal);
        Assert.Contains("--merge-x", overlayCss, StringComparison.Ordinal);
        Assert.Contains("--merge-y", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-piece-merge", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-frame-lock", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-glow", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-scan", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-progress-scan", overlayCss, StringComparison.Ordinal);
        Assert.Contains("animation: tl-finalizing-progress-scan 1.55s ease-in-out infinite;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", overlayCss, StringComparison.Ordinal);
        Assert.Contains("animation: tl-finalizing-piece-breathe 2.8s ease-in-out infinite !important;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("animation: tl-finalizing-frame-breathe 2.8s ease-in-out infinite !important;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("animation: tl-finalizing-progress-scan 2.6s ease-in-out infinite !important;", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-piece-breathe", overlayCss, StringComparison.Ordinal);
        Assert.Contains("@keyframes tl-finalizing-frame-breathe", overlayCss, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseCancellation_StatusModelUsesCanonicalCancelledState()
    {
        Assert.Equal("CANCELLED", TodoX.Web.Models.Timelapse.TimelapseOperationStatuses.Cancelled);
        Assert.Equal("CANCELLED", TodoX.Web.Models.Timelapse.TimelapseParentStatuses.Cancelled);
        Assert.Equal("cancelled", TodoX.Web.Services.Render.RenderJobStatuses.Cancelled);
        Assert.Equal("Đã dừng", TodoX.Web.Models.Timelapse.TimelapseStatusText.Operation(TodoX.Web.Models.Timelapse.TimelapseOperationStatuses.Cancelled));
        Assert.Equal("Đã dừng", TodoX.Web.Models.Timelapse.TimelapseStatusText.Parent(TodoX.Web.Models.Timelapse.TimelapseParentStatuses.Cancelled));
    }

    [Fact]
    public void TimelapseCancellation_WorkflowCancelsJobImageVideoAndKeepsMedia()
    {
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");

        Assert.Contains("CancelJobAsync(Guid jobId", workflow, StringComparison.Ordinal);
        Assert.Contains("CancelImageAsync(Guid jobId, int progressPercent", workflow, StringComparison.Ordinal);
        Assert.Contains("CancelVideoAsync(Guid jobId, int clipIndex", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_JOB_CANCELLED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_IMAGE_CANCELLED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_VIDEO_CANCELLED", workflow, StringComparison.Ordinal);
        Assert.Contains("status='CANCELLED'", workflow, StringComparison.Ordinal);
        Assert.Contains("request_json=COALESCE(v.request_json, jsonb_build_object()) - 'worker_claim'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("result_media_id=NULL", workflow[workflow.IndexOf("private static async Task CancelImagesAsync", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("cancel_reason=@reason", workflow, StringComparison.Ordinal);
        Assert.Contains("status = RenderJobStatuses.Cancelled", workflow, StringComparison.Ordinal);
        Assert.Contains("return \"Đã dừng\";", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseCancellation_WorkerClaimsAndRaceGuardsRespectCancelledOperations()
    {
        var repository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("AND j.status <> 'cancelled'", repository, StringComparison.Ordinal);
        Assert.Contains("AND active_attempt=@attempt", repository, StringComparison.Ordinal);
        Assert.Contains("AND status='RENDERING'", repository, StringComparison.Ordinal);
        Assert.Contains("SELECT EXISTS (SELECT 1 FROM {versionTable} WHERE {versionFk}=@id AND attempt=@attempt AND status='FAILED')", repository, StringComparison.Ordinal);
        Assert.Contains("AND EXISTS (", repository, StringComparison.Ordinal);
        Assert.Contains("FROM timelapse.timelapse_final_outputs", repository, StringComparison.Ordinal);
        Assert.Contains("AND status='COMPLETED'", repository, StringComparison.Ordinal);
        Assert.Contains("AND status='FAILED'", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseCancellation_UiShowsStopAndRetryControlsWithoutCompletedStop()
    {
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");

        Assert.Contains("CanCancelJob", razor, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"CancelJobAsync\"", razor, StringComparison.Ordinal);
        Assert.Contains("CancelImageAsync(image)", razor, StringComparison.Ordinal);
        Assert.Contains("CancelVideoAsync(clip.ClipIndex)", razor, StringComparison.Ordinal);
        Assert.Contains("CanCancelOperation(string? status)", razor, StringComparison.Ordinal);
        Assert.Contains("status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Rendering", razor, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Cancelled ? \"TẠO LẠI\"", razor, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Cancelled ? \"RENDER LẠI\"", razor, StringComparison.Ordinal);
        Assert.Contains("image.Status != TimelapseOperationStatuses.Cancelled", razor, StringComparison.Ordinal);
        Assert.Contains("clip.Status != TimelapseOperationStatuses.Cancelled", razor, StringComparison.Ordinal);
        Assert.Contains("is-cancelled", razor, StringComparison.Ordinal);
        Assert.Contains(".timelapse-stage-card.is-cancelled", css, StringComparison.Ordinal);
        Assert.Contains(".tl-status-badge.is-cancelled", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseCancellation_ManualMigrationAddsCancelledToAllStatusChecks()
    {
        var migration = ReadSource("database", "manual", "timelapse-cancellation", "01_add_cancelled_status.sql");

        Assert.Contains("ck_timelapse_image_stage_status", migration, StringComparison.Ordinal);
        Assert.Contains("ck_timelapse_image_stage_version_status", migration, StringComparison.Ordinal);
        Assert.Contains("ck_timelapse_video_clip_status", migration, StringComparison.Ordinal);
        Assert.Contains("ck_timelapse_video_clip_version_status", migration, StringComparison.Ordinal);
        Assert.Contains("ck_timelapse_final_output_status", migration, StringComparison.Ordinal);
        Assert.Equal(5, migration.Split("'CANCELLED'").Length - 1);
        Assert.Contains("DROP CONSTRAINT IF EXISTS", migration, StringComparison.Ordinal);
        Assert.Contains("ADD CONSTRAINT", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageRetry_IsTargetAwareAndDownstreamWaitingDoesNotHideRetry()
    {
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");

        var updateStart = workflow.IndexOf("private async Task<TimelapseWorkflowState> UpdateImageStageAsync", StringComparison.Ordinal);
        var updateEnd = workflow.IndexOf("private Task AddPromptUpdatedEventAsync", updateStart, StringComparison.Ordinal);
        var update = workflow[updateStart..updateEnd];
        Assert.DoesNotContain("state.HasActiveOperations", update, StringComparison.Ordinal);
        Assert.Contains("await EnsureImageRetryAllowedAsync(conn, tx, jobId, stage, snapshot.SceneCount);", update, StringComparison.Ordinal);

        var guardStart = workflow.IndexOf("private async Task EnsureImageRetryAllowedAsync", StringComparison.Ordinal);
        var guardEnd = workflow.IndexOf("public async Task<TimelapseWorkflowState> RetryVideoAsync", guardStart, StringComparison.Ordinal);
        var guard = workflow[guardStart..guardEnd];
        Assert.Contains("TimelapseRerenderImpactPlanner.Plan(sceneCount, stage.ProgressPercent)", guard, StringComparison.Ordinal);
        Assert.Contains("status='RENDERING'", guard, StringComparison.Ordinal);
        Assert.Contains("progress_percent = ANY(@progress)", guard, StringComparison.Ordinal);
        Assert.Contains("clip_index = ANY(@clipIndexes)", guard, StringComparison.Ordinal);
        Assert.Contains("timelapse.timelapse_final_outputs", guard, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Failed", guard, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Completed", guard, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Invalidated", guard, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Cancelled", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("TimelapseOperationStatuses.Waiting", guard, StringComparison.Ordinal);

        var canRetryStart = razor.IndexOf("private bool CanRetryImage", StringComparison.Ordinal);
        var canRetryEnd = razor.IndexOf("private static bool CanOpenPrompt", canRetryStart, StringComparison.Ordinal);
        var canRetry = razor[canRetryStart..canRetryEnd];
        Assert.DoesNotContain("Workflow.HasActiveOperations", canRetry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Failed", canRetry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Cancelled", canRetry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Completed", canRetry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Invalidated", canRetry, StringComparison.Ordinal);
        Assert.DoesNotContain("TimelapseOperationStatuses.Rendering", canRetry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Cancelled ? \"TẠO LẠI\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoRetry_IsTargetAwareAndKeepsConcurrencyGuards()
    {
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var retryStart = workflow.IndexOf("public async Task<TimelapseWorkflowState> RetryVideoAsync", StringComparison.Ordinal);
        var retryEnd = workflow.IndexOf("public async Task<TimelapseWorkflowState> StartFinalizerAsync", retryStart, StringComparison.Ordinal);
        var retry = workflow[retryStart..retryEnd];

        Assert.Contains("await LockJobAsync(conn, tx, jobId);", retry, StringComparison.Ordinal);
        Assert.Contains("await EnsureVideoRetryAllowedAsync(conn, tx, jobId, clipIndex);", retry, StringComparison.Ordinal);
        Assert.DoesNotContain("state.HasActiveOperations", retry, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", retry, StringComparison.Ordinal);
        Assert.Contains("clip_index=@clipIndex", retry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.IsActive(clip.Status)", retry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Failed", retry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Completed", retry, StringComparison.Ordinal);
        Assert.Contains("TimelapseOperationStatuses.Invalidated", retry, StringComparison.Ordinal);
        Assert.Contains("timelapse.timelapse_video_clip_versions", retry, StringComparison.Ordinal);
        Assert.Contains("attempt=@attempt", retry, StringComparison.Ordinal);
        Assert.Contains("progress = new[] { clip.StartProgressPercent, clip.EndProgressPercent }", retry, StringComparison.Ordinal);
        Assert.Contains("EnsureCompletedDependency(dependencyStatuses, clip.StartProgressPercent)", retry, StringComparison.Ordinal);
        Assert.Contains("EnsureCompletedDependency(dependencyStatuses, clip.EndProgressPercent)", retry, StringComparison.Ordinal);
        Assert.Contains("timelapse.timelapse_final_outputs", retry, StringComparison.Ordinal);
        Assert.Contains("status='RENDERING'", retry, StringComparison.Ordinal);
        Assert.Contains("StartReadyVideosAsync(conn, tx, jobId)", retry, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseDraftEdit_UiExposesPrefilledFormBeforeRenderOnly()
    {
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var css = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor.css");

        Assert.Contains("CHỈNH SỬA YÊU CẦU", razor, StringComparison.Ordinal);
        Assert.Contains("StartIcon=\"@Icons.Material.Filled.EditNote\"", razor, StringComparison.Ordinal);
        Assert.Contains("CanEditFullRequest", razor, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_job.Status, RenderJobStatuses.Draft", razor, StringComparison.Ordinal);
        Assert.Contains("_job.Workflow.Images.Count == 0", razor, StringComparison.Ordinal);
        Assert.Contains("_job.Workflow.Videos.Count == 0", razor, StringComparison.Ordinal);
        Assert.Contains("_job.Workflow.FinalOutput is null", razor, StringComparison.Ordinal);
        Assert.Contains("_editingRequest", razor, StringComparison.Ordinal);
        Assert.Contains("StartEditRequestAsync", razor, StringComparison.Ordinal);
        Assert.Contains("Profiles.GetEnabledProfilesAsync", razor, StringComparison.Ordinal);
        Assert.Contains("Title = _job.Snapshot.Title", razor, StringComparison.Ordinal);
        Assert.Contains("ProfileCode = _job.Snapshot.ProfileCode", razor, StringComparison.Ordinal);
        Assert.Contains("SceneCount = _job.Snapshot.SceneCount", razor, StringComparison.Ordinal);
        Assert.Contains("VideoMode = _job.Snapshot.VideoMode", razor, StringComparison.Ordinal);
        Assert.Contains("Ratio = _job.Snapshot.Ratio", razor, StringComparison.Ordinal);
        Assert.Contains("_editPreviewUrl = _job.Snapshot.OriginalImage.PublicUrl", razor, StringComparison.Ordinal);
        Assert.Contains("OnEditImageSelected", razor, StringComparison.Ordinal);
        Assert.Contains("JPG, PNG hoặc WebP", razor, StringComparison.Ordinal);
        Assert.Contains("Bỏ các thay đổi chưa lưu?", razor, StringComparison.Ordinal);
        Assert.Contains("TIẾP TỤC CHỈNH", razor, StringComparison.Ordinal);
        Assert.Contains("BỎ THAY ĐỔI", razor, StringComparison.Ordinal);
        Assert.Contains(".timelapse-action-row", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 700px)", css, StringComparison.Ordinal);
        Assert.Contains("flex-direction: column;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseDraftEdit_ServiceUpdatesExistingSnapshotAndRecalculatesPrice()
    {
        var service = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");
        var methodStart = service.IndexOf("public async Task<TimelapseJobView> UpdateDraftAsync", StringComparison.Ordinal);
        var methodEnd = service.IndexOf("public async Task<TimelapseJobView> StartOrResumeAsync", methodStart, StringComparison.Ordinal);
        var updateDraft = service[methodStart..methodEnd];

        Assert.Contains("RequireOwnedAsync(jobId, currentUser, ct)", updateDraft, StringComparison.Ordinal);
        Assert.Contains("TimelapseRequestRules.Validate(request", updateDraft, StringComparison.Ordinal);
        Assert.Contains("pg_advisory_xact_lock", updateDraft, StringComparison.Ordinal);
        Assert.Contains("timelapse.timelapse_image_stages", updateDraft, StringComparison.Ordinal);
        Assert.Contains("timelapse.timelapse_video_clips", updateDraft, StringComparison.Ordinal);
        Assert.Contains("timelapse.timelapse_final_outputs", updateDraft, StringComparison.Ordinal);
        Assert.Contains("RenderJobStatuses.Draft", updateDraft, StringComparison.Ordinal);
        Assert.Contains("_sellPrices.ResolveVideoScenePriceAsync", updateDraft, StringComparison.Ordinal);
        Assert.Contains("TimelapseSellPricing.EstimateVideoSubtotal", updateDraft, StringComparison.Ordinal);
        Assert.Contains("ProgressMapping = TimelapseRequestRules.GetProgressMapping(request.SceneCount)", updateDraft, StringComparison.Ordinal);
        Assert.Contains("VideoRenderConfirmed = request.AutoFinish", updateDraft, StringComparison.Ordinal);
        Assert.Contains("_media.SaveAsync", updateDraft, StringComparison.Ordinal);
        Assert.Contains("UPDATE render.render_jobs", updateDraft, StringComparison.Ordinal);
        Assert.Contains("input_json=CAST(@inputJson AS jsonb)", updateDraft, StringComparison.Ordinal);
        Assert.Contains("reference_json=CAST(@referenceJson AS jsonb)", updateDraft, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_DRAFT_UPDATED", updateDraft, StringComparison.Ordinal);
        Assert.DoesNotContain("EnqueueAsync", updateDraft, StringComparison.Ordinal);
        Assert.DoesNotContain("StartOrResumeAsync(jobId", updateDraft, StringComparison.Ordinal);
    }

    [Fact]
    public void TimelapseDraftEdit_StartUsesLatestSavedSnapshotAndSceneMappingStaysCanonical()
    {
        var service = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");
        var razor = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var startStart = service.IndexOf("public async Task<TimelapseJobView> StartOrResumeAsync", StringComparison.Ordinal);
        var startEnd = service.IndexOf("public async Task<TimelapseJobView> RetryImageAsync", startStart, StringComparison.Ordinal);
        var start = service[startStart..startEnd];

        Assert.Contains("var view = await RequireOwnedAsync(jobId, currentUser, ct);", start, StringComparison.Ordinal);
        Assert.Contains("_workflow.StartOrResumeAsync(jobId, view.Snapshot, currentUser, ct)", start, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(!_job.Workflow.CanStartRender || _busy || _editingRequest)\"", razor, StringComparison.Ordinal);
        Assert.Equal(
            new[] { 0, 35, 70, 100 },
            TodoX.Web.Models.Timelapse.TimelapseRequestRules.GetProgressMapping(3));
        Assert.Equal(
            new[] { 0, 25, 50, 75, 100 },
            TodoX.Web.Models.Timelapse.TimelapseRequestRules.GetProgressMapping(4));
        Assert.Equal(
            new[] { 0, 20, 40, 60, 80, 100 },
            TodoX.Web.Models.Timelapse.TimelapseRequestRules.GetProgressMapping(5));
        Assert.Equal(
            new[] { 0, 25, 40, 55, 70, 75, 90, 100 },
            TodoX.Web.Models.Timelapse.TimelapseRequestRules.GetProgressMapping(6));
        Assert.Equal(7, TodoX.Web.Models.Timelapse.TimelapseStageGraphBuilder.Build(6).VideoClips.Count);
    }

    [Fact]
    public void TimelapseAutoFinishDefaultsOnAndKeepsManualModeAvailable()
    {
        var create = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobCreate.razor");
        var detail = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var model = ReadSource("TodoX.Web", "Models", "Timelapse", "TimelapseModels.cs");
        var service = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var workerRepository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.True(new TodoX.Web.Models.Timelapse.TimelapseCreateRequest().AutoFinish);
        Assert.Contains("MudSwitch T=\"bool\" Color=\"Color.Warning\" @bind-Value=\"_request.AutoFinish\"", create, StringComparison.Ordinal);
        Assert.Contains("timelapse-auto-finish-help", create, StringComparison.Ordinal);
        Assert.Contains("AutoFinish = request.AutoFinish", service, StringComparison.Ordinal);
        Assert.Contains("RequireVideoConfirmation = request.RequireVideoConfirmation && !request.AutoFinish", service, StringComparison.Ordinal);
        Assert.Contains("VideoRenderConfirmed = request.AutoFinish", service, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@(!_editingRequest || !CanEditFullRequest)\"", detail, StringComparison.Ordinal);
        Assert.Contains("_job.Snapshot.AutoFinish ? ", detail, StringComparison.Ordinal);
        Assert.Contains("\"Bật\" : \"Tắt\"", detail, StringComparison.Ordinal);
        Assert.Contains("StartReadyVideosAsync(conn, tx, jobId)", workflow, StringComparison.Ordinal);
        Assert.Contains("TryStartFinalizerIfReadyAsync", workerRepository, StringComparison.Ordinal);
        Assert.Contains("public bool AutoFinish { get; set; } = true;", model, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderStatusNormalizer_MapsKnownStatuses()
    {
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("SUCCESS"));
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("completed"));
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("MEDIA_GENERATION_STATUS_SUCCESSFUL"));
        Assert.Equal(Ai79TaskStatusNormalizer.Success, Ai79TaskStatusNormalizer.Normalize("MEDIA_GENERATION_COMPLETED"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("FAILURE"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("error"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("MEDIA_GENERATION_STATUS_FAILED"));
        Assert.Equal(Ai79TaskStatusNormalizer.Failed, Ai79TaskStatusNormalizer.Normalize("MEDIA_GENERATION_FAILED"));
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
