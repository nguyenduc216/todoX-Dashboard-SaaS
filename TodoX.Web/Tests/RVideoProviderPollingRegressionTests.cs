using System.Reflection;
using Xunit;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Tests;

public sealed class RVideoProviderPollingRegressionTests
{
    [Fact]
    public void PendingPollSurvivesMoreThanThreeWorkerClaims()
    {
        var method = typeof(Services.VideoRender.SceneVideoWorkerHandler)
            .GetMethod("ResolveNextAttemptIndex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var versions = Enumerable.Range(0, 12)
            .Select(_ => new Services.VideoRender.SceneVideoVersionDto
            {
                LogicalRequestId = "scene-base",
                Status = "pending_reconciliation",
                ProviderTaskId = "a9896cf26fd2ff29"
            })
            .ToArray();

        for (var poll = 0; poll < 12; poll++)
        {
            Assert.Equal(0, method!.Invoke(null, new object[] { "scene-base", versions }));
            Assert.All(versions, version => Assert.Equal("a9896cf26fd2ff29", version.ProviderTaskId));
        }
    }

    [Fact]
    public void ProviderPollSchedulerDoesNotUseRetryBudget()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("Task<bool> ScheduleProviderPollAsync", source);
        Assert.Contains("SET status='queued'", source);
        Assert.Contains("lock_owner=NULL", source);
        Assert.Contains("lock_until=NULL", source);
        Assert.DoesNotContain("attempt_count < max_attempts", ProviderPollMethod(source));
        Assert.Contains("JOB_PROVIDER_POLL_SCHEDULED", ProviderPollMethod(source));
    }

    [Fact]
    public void MaxAttemptsDoesNotBlockProviderPollScheduling()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var providerPoll = ProviderPollMethod(source);

        Assert.DoesNotContain("max_attempts", providerPoll, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt_count", providerPoll, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PendingPollReusesSameProviderTaskIdWithoutSubmitOrReserve()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.DoesNotContain("IYEScaleTaskClient", source);
        Assert.DoesNotContain("HandleYescaleAsync", source);
        Assert.DoesNotContain("YEScale", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IVideoGenerationProviderAdapterResolver", source);
        Assert.Contains("GetReservationAsync(attemptLogicalRequestId", source);
        Assert.Contains("DeferProviderPollAsync(job, taskId!", source);
        Assert.Contains("if (string.IsNullOrWhiteSpace(taskId))", source);
        Assert.Contains("SubmitAsync", source);
        Assert.Contains("ReserveAsync", source);

        var existingTaskBlock = source[
            source.IndexOf("var existingTaskId = await _versions.GetSceneVideoProviderTaskIdAsync", StringComparison.Ordinal)..];
        var submitIndex = existingTaskBlock.IndexOf("SubmitAsync", StringComparison.Ordinal);
        var reserveIndex = existingTaskBlock.IndexOf("ReserveAsync", StringComparison.Ordinal);
        var reuseIndex = existingTaskBlock.IndexOf("GetReservationAsync", StringComparison.Ordinal);

        Assert.True(reuseIndex >= 0);
        Assert.True(submitIndex > reuseIndex);
        Assert.True(reserveIndex < reuseIndex);
    }

    [Fact]
    public void SceneVideoBatchRequiresExplicitSceneIds()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("RVIDEO_VIDEO_SCENE_IDS_REQUIRED", source);
        Assert.DoesNotContain("project.Scenes.Select(x => x.Id).ToHashSet()", source);
    }

    [Fact]
    public void SceneVideoChildEnqueueSkipsCompletedAndActiveScenes()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("SCENE_VIDEO_ALREADY_COMPLETED_SKIPPED", source);
        Assert.Contains("SCENE_VIDEO_ALREADY_ACTIVE_SKIPPED", source);
        Assert.Contains("VideoRenderEligibilityStatus.AlreadyCompleted", source);
        Assert.Contains("VideoRenderEligibilityStatus.AlreadyActive", source);
    }

    [Fact]
    public void SharedBaseSkipsSceneImageAutoEnqueue()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoLifecycleWorker.cs");

        Assert.Contains("var imageSceneIds = isAuto && !usesSharedReferenceImage", source);
        Assert.Contains(": Array.Empty<long>();", source);
    }

    [Fact]
    public void SharedBaseSkipsSceneImageBatch()
    {
        var source = ReadRepoFile("Services", "Render", "SceneImageBatchRenderHandler.cs");

        Assert.Contains("SCENE_IMAGE_BATCH_SHARED_BASE_SKIPPED", source);
        Assert.Contains("UseSharedReferenceImage || input.ImageInputMode == VideoSceneImageInputMode.SharedBaseImage", source);
    }

    [Fact]
    public void SharedBaseAllScenesUseSameSnapshot()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("_sharedReferenceImageMediaId", source);
        Assert.Contains("_sharedReferenceImageFileName", source);
        Assert.Contains("_sharedReferenceImageMimeType", source);
        Assert.Contains("input.ApplySharedReferenceImage(new TodoX.Web.Services.Render.RVideoSharedReferenceImageSnapshot(", source);
    }

    [Fact]
    public void RetryKeepsSharedSnapshot()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("SharedReferenceImageMediaId", source);
        Assert.Contains("SharedReferenceImageFileName", source);
        Assert.Contains("SharedReferenceImageMimeType", source);
        Assert.Contains("ResolveSourceImageVersionAsync(", source);
    }

    [Fact]
    public void SharedBaseStillSendsImageTo79Ai()
    {
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var provider = ReadRepoFile("Services", "VideoRender", "RVideo79AiVideoService.cs");

        Assert.Contains("options[\"images\"]", provider);
        Assert.Contains("referenceImages = imageInputMode == VideoSceneImageInputMode.SharedBaseImage", worker);
        Assert.Contains("new VideoProviderSourceImage(", worker);
    }

    [Fact]
    public void ConflictingBackgroundPromptIsNeutralized()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("change background", source);
        Assert.Contains("RVideoSharedBaseImagePromptGuard", source);
    }

    [Fact]
    public void ConflictingOutfitPromptIsNeutralized()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("wearing a different outfit", source);
        Assert.Contains("BlockedTerms", source);
    }

    [Fact]
    public void MotionPromptStillWorks()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("Animate only the subject's natural movements", source);
        Assert.Contains("product interaction", source);
    }

    [Fact]
    public void DialogueUnaffected()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("voiceText", source);
        Assert.Contains("voiceInstruction", source);
        Assert.Contains("RVideoRules.ComposeNativeVoicePrompt", source);
    }

    [Fact]
    public void NoFrozenOpeningGuard()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("Do not show the supplied image as a frozen still or separate opening shot", source);
        Assert.Contains("Begin immediately with natural motion inside this exact setup", source);
    }

    [Fact]
    public void NormalModeUnchanged()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("if (!useSharedReferenceImage)", source);
        Assert.Contains("return trimmed;", source);
    }

    [Fact]
    public void TransientPollFailureWithPersistedTaskUsesProviderPollScheduler()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var start = source.IndexOf("catch (VideoProviderTransientException ex)", StringComparison.Ordinal);
        var end = source.IndexOf("        }\n\n        await FailAsync", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);

        var pollCatch = source[start..end];
        Assert.Contains("if (!string.IsNullOrWhiteSpace(taskId))", pollCatch);
        Assert.Contains("DeferProviderPollAsync(job, taskId!", pollCatch);
        Assert.Contains("DeferPollAsync(job, attemptLogicalRequestId", pollCatch);
        Assert.DoesNotContain("DeferPollAsync(job, taskId!", pollCatch);
        Assert.Contains("same task ID will be retried", pollCatch);
        Assert.Contains("before provider submission; application retry will resubmit", pollCatch);
    }

    [Fact]
    public void ProviderSuccessPathDownloadsAndCompletesSceneVideoVersion()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("DownloadAndSaveBinaryAtObjectKeyAsync", source);
        Assert.Contains("CompleteSceneVideoVersionAsync", source);
        Assert.Contains("ProviderTaskId: taskId", source);
        Assert.Contains("ResultMediaId: saved.Id", source);
        Assert.Contains("saved.PublicUrl ?? saved.FileUrl", source);
    }

    [Fact]
    public void ProviderSuccessReconciliationIsBoundedAndEndsInFailedState()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var completion = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");

        Assert.Contains("MaxReconciliationRetries", source);
        Assert.Contains("PROVIDER_OUTPUT_URL_MISSING", source);
        Assert.Contains("MEDIA_STORAGE_FAILED", completion);
        Assert.Contains("RVIDEO_VIDEO_PERSIST_FAILED", source);
        Assert.Contains("PROVIDER_SUCCESS_RECONCILIATION_FAILED", source);
        Assert.Contains("await _versions.FailSceneVideoVersionAsync", source);
        Assert.Contains("VideoSceneStatuses.Failed", source);
        Assert.Contains("GetProviderReconciliationAttemptCountAsync", source);
        Assert.Contains("throw new RenderJobDeferredException", source);
        Assert.Contains("throw new RenderJobTerminalFailureException", source);
    }

    [Fact]
    public void VideoPollingRefreshDoesNotDependOnlyOnProjectStatus()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("_project.Scenes.Any(IsVideoSceneActive)", source);
        Assert.Contains("\"pending_reconciliation\"", source);
        Assert.Contains("HasActiveSceneRenders()", source);
    }

    [Fact]
    public void ProviderSuccessReconcilesExistingTaskWithoutResubmit()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var existingTaskBlock = source[
            source.IndexOf("var existingTaskId = await _versions.GetSceneVideoProviderTaskIdAsync", StringComparison.Ordinal)..];
        var submitBlock = existingTaskBlock[..existingTaskBlock.IndexOf("if (string.IsNullOrWhiteSpace(taskId))", StringComparison.Ordinal)];

        Assert.Contains("GetSceneVideoProviderTaskIdAsync", existingTaskBlock);
        Assert.Contains("GetReservationAsync(attemptLogicalRequestId", existingTaskBlock);
        Assert.DoesNotContain("SubmitAsync", submitBlock);
        Assert.Contains("adapter.PollAsync", existingTaskBlock);
        Assert.Contains("CompleteSceneVideoVersionAsync", existingTaskBlock);
    }

    [Fact]
    public void PersistentReconciliationWorkerReschedulesExistingProviderTasks()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoReconciliationWorker.cs");

        Assert.Contains("ListPersistentSceneVideoReconciliationJobsAsync", source);
        Assert.Contains("ScheduleProviderPollAsync", source);
        Assert.Contains("existing provider task", source);
        Assert.Contains("RenderQueue:Enabled", source);
    }

    [Fact]
    public void SceneVideoChildJobPersistsResolvedProviderAndModel()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("ProviderCode = route.ProviderCode", source);
        Assert.Contains("ModelCode = route.ModelName", source);
        Assert.DoesNotContain("yescale_task_video", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RVideoVideoModelPolicy.ProviderCode", source);
    }

    [Fact]
    public void ProviderAdapterResolutionIsCapabilityBased()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoGenerationProviderAdapter.cs");

        Assert.Contains("CanHandle(string providerCode, string capabilityCode)", source);
        Assert.Contains("adapter.CanHandle(providerCode, capabilityCode)", source);
        Assert.Contains("VIDEO_PROVIDER_ADAPTER_UNAVAILABLE", source);
    }

    [Fact]
    public void RVideoJobDetailWaitsForAuthBeforeLoadingOnHardReload()
    {
        var source = ReadRepoFile("Components", "Pages", "RVideoJobDetail.razor");

        Assert.Contains("RVideoDetailBootstrapState.WaitingForAuth", source);
        Assert.Contains("if (!AuthState.IsInitialized)", source);
        Assert.Contains("return;", source);
        Assert.DoesNotContain("!AuthState.IsInitialized || _loading", source);
        Assert.Contains("RVIDEO_DETAIL_WAITING_AUTH", source);
    }

    [Fact]
    public void RVideoJobDetailAuthChangeTriggersSingleGuardedLoad()
    {
        var source = ReadRepoFile("Components", "Pages", "RVideoJobDetail.razor");

        Assert.Contains("AuthState.OnChange += HandleAuthStateChanged", source);
        Assert.Contains("private void HandleAuthStateChanged()", source);
        Assert.Contains("await LoadJobAsync();", source);
        Assert.Contains("_loadInProgress || _loadedJobId == JobId", source);
        Assert.Contains("RVIDEO_DETAIL_AUTH_READY", source);
    }

    [Fact]
    public void RVideoJobDetailAuthenticatedCustomerLoadsAfterHydration()
    {
        var source = ReadRepoFile("Components", "Pages", "RVideoJobDetail.razor");

        Assert.Contains("AuthState.CurrentUser is not { IsCustomer: true } user", source);
        Assert.Contains("RVIDEO_DETAIL_LOAD_START", source);
        Assert.Contains("await RVideoJobs.GetByJobIdAsync(JobId, user, timeout.Token)", source);
        Assert.Contains("_bootstrapState = RVideoDetailBootstrapState.Ready", source);
        Assert.Contains("RVIDEO_DETAIL_LOAD_SUCCESS", source);
        Assert.Contains("<RenderVideoJobs Embedded=\"true\" JobId=\"@JobId\" ProjectId=\"@_view.Project.Id\" />", source);
    }

    [Fact]
    public void RVideoJobDetailTimeoutExitsSpinner()
    {
        var source = ReadRepoFile("Components", "Pages", "RVideoJobDetail.razor");

        Assert.Contains("CancellationTokenSource(LoadTimeout)", source);
        Assert.Contains("catch (OperationCanceledException ex) when (timeout?.Token.IsCancellationRequested == true)", source);
        Assert.Contains("RVIDEO_DETAIL_LOAD_TIMEOUT", source);
        Assert.Contains("_bootstrapState = RVideoDetailBootstrapState.Error", source);
        Assert.Contains("_loadInProgress = false", source);
    }

    [Fact]
    public void RVideoJobDetailFailedLoadRendersRetryState()
    {
        var source = ReadRepoFile("Components", "Pages", "RVideoJobDetail.razor");

        Assert.Contains("RVideoDetailBootstrapState.Error", source);
        Assert.Contains("OnClick=\"LoadJobAsync\"", source);
        Assert.Contains("Retry", source);
        Assert.Contains("RVIDEO_DETAIL_LOAD_FAILED", source);
        Assert.Contains("_loadedJobId = null", source);
    }

    [Fact]
    public void RVideoJobDetailUnsubscribesAuthChangeOnDispose()
    {
        var source = ReadRepoFile("Components", "Pages", "RVideoJobDetail.razor");

        Assert.Contains("@implements IDisposable", source);
        Assert.Contains("public void Dispose()", source);
        Assert.Contains("AuthState.OnChange -= HandleAuthStateChanged", source);
    }

    [Fact]
    public void RenderVideoJobsSkipsJobResolutionWhenProjectIdAlreadySupplied()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("if (_projectId is null && _jobId is not null && AuthState.CurrentUser is { IsCustomer: true } user)", source);
    }

    [Fact]
    public void RVideoLifecycleAutoEnqueuesSceneVideosAfterImagesAreReady()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoLifecycleWorker.cs");

        Assert.Contains("IRVideoSceneVideoAutoChainService", source);
        Assert.Contains("TryEnqueueSceneVideoAsync(project.Id, sceneId, \"RVIDEO_LIFECYCLE\"", source);
        Assert.Contains("Where(x => x.IsImageReady)", source);
        Assert.DoesNotContain("var videoSceneIds = decision.ShouldQueueVideo", source);
    }

    [Fact]
    public void RVideoLifecycleLoadsManualProjectsForTechnicalFinalizationWithoutAutoGeneration()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoLifecycleWorker.cs");
        var settingsQuery = source[
            source.IndexOf("private static async Task<IReadOnlyList<RVideoJobSettingsDto>> ListAutoSettingsAsync", StringComparison.Ordinal)..];
        var generationBlock = source[
            source.IndexOf("var isAuto =", StringComparison.Ordinal)..source.IndexOf("var readyScenes = isAuto", StringComparison.Ordinal)];

        Assert.Contains("execution_mode IN ('AUTO', 'MANUAL')", settingsQuery);
        Assert.Contains("var isAuto = string.Equals(setting.ExecutionMode, RVideoExecutionModes.Auto", source);
        Assert.Contains("if (isAuto", generationBlock);
        Assert.DoesNotContain("execution_mode='AUTO'", settingsQuery);
        Assert.Contains("TryEnqueueSceneAudioAsync", source);
        Assert.Contains("TryFinalizeSceneMediaAsync", source);
        Assert.Contains("TryEnqueueFinalMergeAsync", source);
        Assert.Contains("SyncLifecycleAsync", source);
    }

    [Fact]
    public void RVideoLifecycleManualPathCannotEnqueueSceneVideoOrImageJobs()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoLifecycleWorker.cs");
        var autoGenerationStart = source.IndexOf("var isAuto =", StringComparison.Ordinal);
        Assert.True(autoGenerationStart >= 0);
        Assert.Contains("if (isAuto", source);
        Assert.Contains("jobs.EnqueueForProjectIfNoneActiveAsync", source);
        Assert.Contains("autoChain.TryEnqueueSceneVideoAsync", source);
        Assert.Contains("var imageSceneIds = isAuto && !usesSharedReferenceImage", source);
        Assert.Contains("var readyScenes = isAuto", source);
        Assert.Contains("Array.Empty<long>()", source);
        Assert.DoesNotContain("RVideoExecutionModes.Manual", source);
        Assert.DoesNotContain("RenderSceneVideo", source);
        Assert.DoesNotContain("CreateQueuedSceneVideoVersionAsync", source);
    }

    [Fact]
    public void SceneImageReadyTriggersSceneScopedAutoChain()
    {
        var source = ReadRepoFile("Services", "Render", "SceneImageRenderWorkItemHandler.cs");

        Assert.Contains("SCENE_IMAGE_READY", source);
        Assert.Contains("_autoChain.TryEnqueueSceneVideoAsync(input.ProjectId, input.SceneId, \"SCENE_IMAGE_READY\"", source);
        Assert.DoesNotContain("SCENE_VIDEO_AUTO_ENQUEUED", source);
    }

    [Fact]
    public void AutoChainUsesSceneScopedLogicalRequestKeyAndSceneIds()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoAutoChainService.cs");

        Assert.Contains("BuildLogicalRequestKey(long projectId, long sceneId)", source);
        Assert.Contains("SceneIds = new[] { sceneId }", source);
        Assert.Contains("LogCode = logicalRequestKey", source);
        Assert.Contains("EnqueueForLogCodeIfNoneActiveAsync", source);
        Assert.Contains("SCENE_VIDEO_AUTO_ENQUEUE_SKIPPED", source);
        Assert.Contains("SCENE_VIDEO_AUTO_ENQUEUE_REQUESTED", source);
        Assert.Contains("SCENE_VIDEO_AUTO_ENQUEUED", source);
        Assert.Contains("BuildRVideoTrustedPayerContextAsync(projectId, sceneId", source);
        Assert.Contains("TrustedPayerContext = payerContext", source);
    }

    [Fact]
    public void ManualAndRetryVideoEnqueuesUseTheCanonicalPersistedPayerBuilder()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("@inject TodoX.Web.Services.VideoRender.IRVideoTrustedPayerContextService RVideoPayerContexts", source);
        Assert.Contains("BuildRVideoTrustedPayerContextAsync(_project.Id, sceneIds[0])", source);
        Assert.Contains("BuildRVideoTrustedPayerContextAsync(_project.Id, scene.Id)", source);
    }

    [Fact]
    public void SceneVideoBatchValidatesAndCopiesTrustedPayerContextToEachChild()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("RVIDEO_VIDEO_PAYER_CONTEXT_MISSING", source);
        Assert.Contains("ValidateAndBuildRVideoTrustedPayerContextAsync", source);
        Assert.Contains("TrustedPayerContext = input.TrustedPayerContext", source);
    }

    [Fact]
    public void SceneVideoWorkerValidatesPersistedPayerBeforeBillingAndProviderSubmit()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var validationIndex = source.IndexOf("ValidateAndBuildRVideoTrustedPayerContextAsync", StringComparison.Ordinal);
        var reserveIndex = source.IndexOf("ReserveAsync", StringComparison.Ordinal);
        var submitIndex = source.IndexOf("SubmitAsync", StringComparison.Ordinal);

        Assert.True(validationIndex >= 0);
        Assert.True(reserveIndex > validationIndex);
        Assert.True(submitIndex > reserveIndex);
        Assert.Contains("RVIDEO_VIDEO_PAYER_CONTEXT_INVALID", source);
        Assert.Contains("RVIDEO_VIDEO_BILLING_RESERVE_FAILED", source);
        Assert.Contains("RVIDEO_VIDEO_SUBMIT_BEGIN", source);
        Assert.Contains("RVIDEO_VIDEO_POLL_RESPONSE", source);
    }

    [Fact]
    public void VideoBillingUsesVideoSpecificInsufficientPointsMessage()
    {
        var source = ReadRepoFile("Services", "AiProviders", "AiImageBillingService.cs");

        Assert.Contains("\"render_job_scene_video\"", source);
        Assert.Contains("tạo video", source);
        Assert.Contains("FormatInsufficientPoints", source);
    }

    [Theory]
    [InlineData("dae8e8b8-842a-43cd-b68d-c0a634cc15bc", "dae8e8b8-842a-43cd-b68d-c0a634cc15bc")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("n8n", null)]
    [InlineData("legacy-user", null)]
    public void BillingCreatedByTextIsNormalizedWithoutThrowing(string? value, string? expected)
    {
        var normalized = AiImageBillingCreatedByParser.Normalize(value);

        Assert.Equal(expected is null ? null : Guid.Parse(expected), normalized);
    }

    [Fact]
    public void BillingRecordHydrationUsesStringCreatedByNormalizationForAllPaths()
    {
        var source = ReadRepoFile("Services", "AiProviders", "AiImageBillingService.cs");

        Assert.Equal(2, CountOccurrences(source, "QuerySingleOrDefaultAsync<BillingRecordRow>"));
        Assert.Equal(2, CountOccurrences(source, ".ToBillingRecord()"));
        Assert.Contains("public string? CreatedBy", source);
        Assert.Contains("AiImageBillingCreatedByParser.Normalize(CreatedBy)", source);
        Assert.DoesNotContain("QuerySingleOrDefaultAsync<BillingRecord>(", source);
        Assert.DoesNotContain("created_by::uuid", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExistingSceneImageTaskIsPolledWithoutASecondSubmission()
    {
        var source = ReadRepoFile("Services", "Render", "SceneImageRenderWorkItemHandler.cs");
        var render = ReadRepoFile("Services", "Render", "SceneImageRenderService.cs");

        Assert.Contains("GetSceneImageProviderTaskIdAsync", source);
        Assert.Contains("ProviderTaskId = taskId", source);
        Assert.Contains("ProviderTaskId = context.ProviderTaskId", render);
        Assert.Contains("ProviderTaskId = request.ProviderTaskId", ReadRepoFile("Services", "AiProviders", "AiImageRenderRouter.cs"));
    }

    [Fact]
    public void SceneVideoStorageKeyUsesVersionScopedImmutablePath()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var versionA = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var versionB = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var first = SceneMediaStorageKeys.SceneVideoOutput(tenantA, 42, 7, versionA);

        Assert.Equal(first, SceneMediaStorageKeys.SceneVideoOutput(tenantA, 42, 7, versionA));
        Assert.NotEqual(first, SceneMediaStorageKeys.SceneVideoOutput(tenantB, 42, 7, versionA));
        Assert.NotEqual(first, SceneMediaStorageKeys.SceneVideoOutput(tenantA, 42, 7, versionB));
    }

    [Fact]
    public void SceneVideoRecoveryCanLocateVersionByLogicalRequestId()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");

        Assert.Contains("GetSceneVideoVersionByLogicalRequestIdAsync", source);
        Assert.Contains("GetRecoverableSceneVideoVersionAsync", source);
        Assert.Contains("pending_reconciliation", source);
        Assert.Contains("RVIDEO_VIDEO_PERSIST_FAILED", source);
    }

    [Fact]
    public void _79AiReconciliationUses79AiVideoServiceAndNotYescalePoll()
    {
        var source = ReadRepoFile("Services", "AiProviders", "AiImageBillingReconciliationWorker.cs");
        var start = source.IndexOf("private async Task Reconcile79AiVideoAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool IsMissingBillingTable", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);

        var method = source[start..end];

        Assert.Contains("IRVideo79AiVideoService", source);
        Assert.Contains("string.Equals(item.ProviderCode, \"79ai\"", source);
        Assert.Contains("string.Equals(item.CapabilityCode, \"rvideo_scene_video_generation\"", source);
        Assert.Contains("Ai79TaskStatusNormalizer.Running", method);
        Assert.Contains("Ai79TaskStatusNormalizer.Failed", method);
        Assert.Contains("ResolveRuntimeAsync(item.ProviderId, item.ProviderCapabilityId, item.ProviderCode!", method);
        Assert.Contains("PollAsync(runtime, item.ProviderTaskId!", method);
        Assert.DoesNotContain("GetStatusAsync(", method);
    }

    [Fact]
    public void VideoPayerResolutionUsesOperationAwareMessageAndDiagnosticEvents()
    {
        var resolver = ReadRepoFile("Services", "AiProviders", "AiBillingPayerResolver.cs");
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("Cannot resolve billing payer for ", resolver);
        Assert.Contains("RVIDEO scene video", resolver);
        Assert.Contains("RVIDEO_VIDEO_BILLING_PAYER_RESOLVE_BEGIN", worker);
        Assert.Contains("RVIDEO_VIDEO_BILLING_PAYER_RESOLVED", worker);
        Assert.Contains("RVIDEO_VIDEO_BILLING_PAYER_FAILED", worker);
        Assert.Contains("RVIDEO_VIDEO_BILLING_RESERVE_BEGIN", worker);
        Assert.Contains("RVIDEO_VIDEO_BILLING_RESERVED", worker);
        Assert.Contains("RVIDEO_VIDEO_BILLING_RESERVE_FAILED", worker);
        Assert.Contains("availablePoints = reservation.AvailablePoints", worker);
    }

    [Fact]
    public void RVideoLifecycleSyncsWhenImageAndVideoRenderingBegin()
    {
        var image = ReadRepoFile("Services", "Render", "SceneImageRenderWorkItemHandler.cs");
        var video = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("SyncLifecycleAsync(input.ProjectId, RVideoStages.Image", image);
        Assert.Contains("SyncLifecycleAsync(project.Id, RVideoStages.Video", video);
    }

    [Fact]
    public void RenderJobServiceAddsLogCodeScopedIdempotentEnqueue()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");

        Assert.Contains("EnqueueForLogCodeIfNoneActiveAsync", source);
        Assert.Contains("BuildLogCodeJobLockName", source);
        Assert.Contains("log_code = @logCode", source);
    }

    [Theory]
    [InlineData("RVIDEO_VIDEO_PERSIST_FAILED", "Storage key collision", false)]
    [InlineData("RenderJobTerminalFailureException", "Storage key c\u00e1\u00bb\u00a7a phi\u00c3\u00aan b\u00e1\u00ba\u00a3n \u00c4\u2018\u00c3\u00a3 t\u00e1\u00bb\u201con t\u00e1\u00ba\u00a1i, kh\u00c3\u00b4ng ghi \u00c4\u2018\u00c3\u00a8.", true)]
    [InlineData("RenderJobTerminalFailureException", "Storage key c\u00c3\u0192\u00c2\u00a1\u00c2\u00bb\u00c2\u00a7a phi\u00c3\u0192\u00c2\u00aa\u00c2\u00aan b\u00c3\u0192\u00c2\u00a3n \u00c3\u201e\u00e2\u20ac\u02dc\u00c3\u0192\u00c2\u00a3 t\u00c3\u00a1\u00c2\u00bb\u00e2\u20ac\u0153n t\u00c3\u00a1\u00c2\u00ba\u00c2\u00a1i, kh\u00c3\u0192\u00c2\u00b4ng ghi \u00c3\u201e\u00e2\u20ac\u02dc\u00c3\u0192\u00c2\u00a8.", true)]
    [InlineData("RenderJobTerminalFailureException", "Storage key exists, overwrite was blocked.", false)]
    [InlineData("RenderJobTerminalFailureException", "Provider failed before storage key was created.", false)]
    public void LegacyStorageCollisionGuardOnlyAcceptsKnownMessage(
        string errorCode,
        string errorMessage,
        bool expected)
    {
        var method = typeof(Services.Render.RenderJobService)
            .GetMethod("IsLegacyStorageCollision", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(expected, method!.Invoke(null, new object?[] { errorCode, errorMessage }));
    }

    [Fact]
    public void RecoveredRenderJobUpdateKeepsStrictVersionAndBillingGuards()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var start = source.IndexOf("public async Task<bool> MarkRecoveredCompletedAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private static bool IsLegacyStorageCollision", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);

        var method = source[start..end];
        Assert.Contains("j.error_code = 'RenderJobTerminalFailureException'", method);
        Assert.Contains("j.error_message ILIKE '%Storage key%'", method);
        Assert.Contains("replace(b.render_job_id, '-', '') = replace(j.id::text, '-', '')", method);
        Assert.DoesNotContain("b.render_job_id=j.id", method);
        Assert.DoesNotContain("b.render_job_id::uuid", method);
        Assert.Contains("b.feature_code='render_job_scene_video'", method);
        Assert.Contains("b.capability_code='rvideo_scene_video_generation'", method);
        Assert.Contains("b.status IN ('pending_reconciliation', 'completed')", method);
        Assert.Contains("v.status='completed'", method);
    }

    [Theory]
    [InlineData("1e64c8d0935c4018b28c5f0d0f6b81be", true)]
    [InlineData("1e64c8d0-935c-4018-b28c-5f0d0f6b81be", true)]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", false)]
    [InlineData("not-a-uuid", false)]
    public void NormalizedBillingRenderJobIdComparisonSupportsHistoricalTextFormats(
        string billingRenderJobId,
        bool expectedMatch)
    {
        const string renderJobId = "1e64c8d0-935c-4018-b28c-5f0d0f6b81be";

        var matches = NormalizeRenderJobIdText(billingRenderJobId)
            == NormalizeRenderJobIdText(renderJobId);

        Assert.Equal(expectedMatch, matches);
    }

    [Fact]
    public void SharedMediaStateRendererIsUsedByRVideoTimelapseAndRDance()
    {
        var frame = ReadRepoFile("Components", "Shared", "RenderMediaFrame.razor");
        var rvideo = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var timelapse = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");
        var rdance = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("MediaRenderState", frame);
        Assert.Contains("State=\"@ResolveImageMediaState(sceneState)\"", rvideo);
        Assert.Contains("State=\"@ResolveTimelapseMediaState(image.Status)\"", timelapse);
        Assert.Contains("RenderMediaFrame", rdance);
    }

    [Fact]
    public void SharedMediaRendererKeepsFailedStateStatic()
    {
        var source = ReadRepoFile("Components", "Shared", "RenderMediaFrame.razor");
        Assert.Contains("private bool IsAnimated => EffectiveState is MediaRenderState.Queued", source);
        Assert.Contains("private bool IsFailed => EffectiveState == MediaRenderState.Failed", source);
    }

    [Fact]
    public void SharedMediaRendererUsesRealIndeterminateSpinnerForActiveStates()
    {
        var source = ReadRepoFile("Components", "Shared", "RenderMediaFrame.razor");
        Assert.Contains("MudProgressCircular", source);
        Assert.Contains("Indeterminate=\"true\"", source);
    }

    [Fact]
    public void RVideoVideoWorkItemContractsKeepUserIdNullableAndDropCreatedByText()
    {
        var renderHandler = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");
        var workerHandler = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");

        Assert.Contains("public Guid? UserId { get; set; }", renderHandler);
        Assert.Contains("public Guid? UserId { get; set; }", workerHandler);
        Assert.DoesNotContain("public string? CreatedBy", renderHandler);
        Assert.DoesNotContain("public string? CreatedBy", workerHandler);
    }

    [Fact]
    public void RVideoAutoChainUsesNullableProjectUserId()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoAutoChainService.cs");

        Assert.Contains("UserId = project.UserId", source);
        Assert.DoesNotContain("UserId = project.UserId ?? Guid.Empty", source);
        Assert.DoesNotContain("CreatedBy = triggerSource", source);
    }

    [Fact]
    public void RVideoAutoSharedReferenceSkipsImageWorkAndCarriesVideoInputReference()
    {
        var lifecycle = ReadRepoFile("Services", "VideoRender", "RVideoLifecycleWorker.cs");
        var autoChain = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoAutoChainService.cs");
        var eligibility = ReadRepoFile("Services", "VideoRender", "VideoRenderEligibilityService.cs");
        var handler = ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs");

        Assert.Contains("var imageSceneIds = isAuto && !usesSharedReferenceImage", lifecycle);
        Assert.Contains("Array.Empty<long>()", lifecycle);
        Assert.Contains("rvideoSettings?.UseReferenceImageForAllScenes == true", autoChain);
        Assert.Contains("RVideoSceneImageReferenceSelection.Resolve(rvideoSettings)", autoChain);
        Assert.Contains("enqueueInput.ApplySharedReferenceImage(sharedReference)", autoChain);
        Assert.Contains("SCENE_VIDEO_SHARED_REFERENCE_VALIDATION_FAILED", autoChain);
        Assert.Contains("var imageVersion = usesSharedReferenceImage", eligibility);
        Assert.Contains("? null", eligibility);
        Assert.Contains("!string.IsNullOrWhiteSpace(sharedReference.ObjectKey)", eligibility);
        Assert.DoesNotContain("ResolveSharedReferenceImageVersion", eligibility);
        Assert.Contains("input.SharedReferenceImageUrl", handler);
        Assert.Contains("input.SharedReferenceImageObjectKey", handler);
        Assert.Contains("SelectedSourceImageVersionId = sourceImageVersionId", handler);
        Assert.Contains("var sourceImageVersionId = input.UseSharedReferenceImage ? null", handler);
        Assert.DoesNotContain("CreateImageVersion", autoChain);
        Assert.DoesNotContain("scene_image_versions", autoChain);
    }

    [Fact]
    public void RVideoManualSceneVideoPathsCarrySharedReferenceInput()
    {
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var renderAll = page[
            page.IndexOf("private async Task EnqueueRenderAllScenesAsync()", StringComparison.Ordinal)..page.IndexOf("private async Task EnqueueMergeAsync()", StringComparison.Ordinal)];
        var renderOne = page[
            page.IndexOf("private async Task EnqueueSceneVideoAsync(VideoProjectSceneDto scene)", StringComparison.Ordinal)..page.IndexOf("private void OpenUrl", StringComparison.Ordinal)];
        var inputHelper = page[
            page.IndexOf("private bool TryApplySharedReferenceImage", StringComparison.Ordinal)..page.IndexOf("private string ProjectAspectRatio", StringComparison.Ordinal)];

        Assert.Contains("TryBuildSceneVideoRenderInput(sceneIds", renderAll);
        Assert.Contains("TryBuildSceneVideoRenderInput(new[] { scene.Id }", renderOne);
        Assert.Contains("_useReferenceImageForAllScenes", inputHelper);
        Assert.Contains("input.ApplySharedReferenceImage(new TodoX.Web.Services.Render.RVideoSharedReferenceImageSnapshot(", inputHelper);
        Assert.Contains("UseSharedReferenceImage = true;", ReadRepoFile("Services", "VideoRender", "SceneVideoRenderHandler.cs"));
        Assert.DoesNotContain("scene_image_versions", renderAll);
        Assert.DoesNotContain("scene_image_versions", renderOne);
    }

    [Fact]
    public void RVideoResultTabHidesFinalizeAfterFinalVideoAndTreatsReconciliationAsActive()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var guardIndex = source.IndexOf("@if (string.IsNullOrWhiteSpace(_project.FinalVideoUrl))", StringComparison.Ordinal);
        var buttonIndex = source.IndexOf("<MudButton Size=\"Size.Small\" Color=\"Color.Success\" Variant=\"Variant.Filled\" StartIcon=\"@Icons.Material.Filled.PlayArrow\"", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0);
        Assert.True(buttonIndex > guardIndex);
        Assert.Contains("RenderJobStatuses.PendingReconciliation", source);
    }

    [Fact]
    public void RVideoRetryUiDisablesTheRetryButtonAndFlipsSceneToQueuedImmediately()
    {
        var frame = ReadRepoFile("Components", "Shared", "RenderMediaFrame.razor");
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("Disabled=\"@RetryDisabled\"", frame);
        Assert.Contains("RetryDisabled=\"@_sceneVideoRetrying.Contains(scene.Id)\"", page);
        Assert.Contains("scene.Status = VideoSceneStatuses.VideoQueued;", page);
        Assert.Contains("_sceneVideoRetrying.Add(scene.Id)", page);
    }

    [Fact]
    public void RVideoProjectProjectionFallsBackToSelectedCompletedSceneVideoVersion()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderRepository.cs");

        Assert.Contains("video_render.scene_video_versions", source);
        Assert.Contains("is_selected=true", source);
        Assert.Contains("status='completed'", source);
        Assert.Contains("scene.SceneVideoUrl = selected.PublicUrl ?? selected.SourceFilePath", source);
        Assert.Contains("scene.Status = VideoSceneStatuses.VideoReady", source);
        Assert.Contains("public long SceneId { get; init; }", source);
    }

    [Fact]
    public void RVideoCustomerUiDoesNotExposeProviderBrandingOrInlineHistoryPanels()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.DoesNotContain("79AI đang tạo", source);
        Assert.DoesNotContain("Đã gửi 79AI", source);
        Assert.DoesNotContain("provider video 79AI", source);
        Assert.DoesNotContain("provider image-to-video", source);
        Assert.DoesNotContain("@RenderVideoHistoryPanel(scene)", source);
        Assert.DoesNotContain("@RenderImageHistoryPanel(scene)", source);
        Assert.Contains("Chưa cấu hình tạo video", source);
    }

    [Fact]
    public void RVideoCustomerUiUsesTwoMediaColumnsAndVideoVersionStatus()
    {
        var source = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.DoesNotContain("MudItem xs=\"12\" md=\"7\"", source);
        Assert.Contains("ResolveVideoSceneStatus(scene)", source);
        Assert.Contains("ResolveVideoMediaState(scene)", source);
        Assert.Contains("HasCompletedVideoVersion(scene)", source);
    }

    [Fact]
    public void RetryUpdateZeroRowsDoesNotEmitFalseSuccessEvent()
    {
        var source = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var retry = source[
            source.IndexOf("public async Task ScheduleRetryAsync", StringComparison.Ordinal)..source.IndexOf("private const string SelectJobSql", StringComparison.Ordinal)];

        Assert.Contains("if (changed > 0)", retry);
        Assert.Contains("JOB_RETRY_NOT_SCHEDULED", retry);
        Assert.DoesNotContain("await AddEventAsync(jobId, \"JOB_RETRY_SCHEDULED\"", retry[..retry.IndexOf("if (changed > 0)", StringComparison.Ordinal)]);
    }

    private static string ProviderPollMethod(string source)
    {
        var start = source.IndexOf("public async Task<bool> ScheduleProviderPollAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private const string SelectJobSql", start, StringComparison.Ordinal);
        Assert.True(start >= 0);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));

    private static string NormalizeRenderJobIdText(string value)
        => value.Replace("-", string.Empty, StringComparison.Ordinal);

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
