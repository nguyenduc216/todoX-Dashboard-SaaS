using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoRuntimeSqlTests
{
    [Fact]
    public void RuntimeMigrationAddsSceneVideoProviderCapabilityId()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "TodoX.Web", "database", "rvideo", "02_reconcile_scene_video_versions_runtime.sql"),
            Encoding.UTF8);

        Assert.Contains("ADD COLUMN IF NOT EXISTS provider_capability_id bigint", sql);
        Assert.Contains("CREATE INDEX IF NOT EXISTS ix_scene_video_versions_provider_capability_id", sql);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyScriptRequiresSceneVideoProviderCapabilityId()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "TodoX.Web", "database", "rvideo", "verify_rvideo_runtime.sql"),
            Encoding.UTF8);

        Assert.Contains("('provider_capability_id')", sql);
        Assert.Contains("table_name='scene_video_versions'", sql);
    }

    [Fact]
    public void RecoverySqlResolvesByCoreJobProjectAndSceneVideoVersion()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "database", "manual", "rvideo-scene-video-42804-recovery.sql"),
            Encoding.UTF8);

        Assert.Contains("JOIN target_project p ON (j.input_json->>'projectId') = p.project_id::text", sql);
        Assert.Contains("j.job_type = 'render_scene_video'", sql);
        Assert.Contains("v.provider_task_id IS NOT NULL", sql);
        Assert.Contains("v.status <> 'completed'", sql);
    }

    [Fact]
    public void AudioVersionMigrationAddsStandaloneSceneAudioTable()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "database", "scene-media-versioning", "03_add_scene_audio_versioning.sql"),
            Encoding.UTF8);

        Assert.Contains("CREATE TABLE IF NOT EXISTS video_render.scene_audio_versions", sql);
        Assert.Contains("selected_audio_version_id", sql);
        Assert.Contains("voice_audio_version_id", sql);
    }

    [Fact]
    public void SceneVideoVersionSqlUsesPhysicalVoiceAudioColumnOnly()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var select = ExtractConstBlock(source, "SelectSceneVideoVersionSql");
        var complete = ExtractMethodBlock(source, "public async Task CompleteSceneVideoVersionAsync");
        var updateSql = ExtractSceneVideoCompletionUpdateSql(complete);

        Assert.Contains("voice_audio_version_id AS VoiceAudioVersionId", select);
        Assert.Contains("voice_audio_version_id=COALESCE(@voiceAudioVersionId, voice_audio_version_id)", complete);
        var sqlIdentifiersOnly = (select + updateSql)
            .Replace("VoiceAudioVersionId", string.Empty, StringComparison.Ordinal)
            .Replace("@voiceAudioVersionId", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("voiceaudioversionid", sqlIdentifiersOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\n               VoiceAudioVersionId", select);
    }

    [Fact]
    public void SceneVideoCompletionBindsEveryVoiceAudioSqlParameter()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var method = ExtractMethodBlock(source, "public async Task CompleteSceneVideoVersionAsync");
        var parameterObject = ExtractSceneVideoCompletionUpdateParameterObject(method);
        var sqlParameterIndex = method.IndexOf("@voiceAudioVersionId", StringComparison.Ordinal);
        var executeParameterIndex = parameterObject.IndexOf("voiceAudioVersionId = request.VoiceAudioVersionId", StringComparison.Ordinal);

        Assert.True(sqlParameterIndex >= 0);
        Assert.True(executeParameterIndex >= 0);
        Assert.Contains("voiceAudioVersionId = request.VoiceAudioVersionId,", parameterObject);
    }

    [Theory]
    [InlineData("@voiceAudioVersionId", "voiceAudioVersionId = request.VoiceAudioVersionId")]
    [InlineData("@providerCode", "request.ProviderCode")]
    [InlineData("@providerCapabilityId", "request.ProviderCapabilityId")]
    [InlineData("@modelName", "modelName = request.ModelName")]
    [InlineData("@providerTaskId", "request.ProviderTaskId")]
    [InlineData("@resultMediaId", "request.ResultMediaId")]
    [InlineData("@videoUrl", "request.VideoUrl")]
    [InlineData("@videoPath", "request.VideoPath")]
    [InlineData("@posterUrl", "request.PosterUrl")]
    [InlineData("@durationSeconds", "request.DurationSeconds")]
    [InlineData("@aspectRatio", "request.AspectRatio")]
    [InlineData("@mimeType", "request.MimeType")]
    [InlineData("@billingLogicalRequestId", "request.BillingLogicalRequestId")]
    [InlineData("@estimatedUsd", "request.EstimatedUsd")]
    [InlineData("@actualUsd", "request.ActualUsd")]
    [InlineData("@chargedPoints", "request.ChargedPoints")]
    [InlineData("@refundedPoints", "request.RefundedPoints")]
    [InlineData("@costSource", "request.CostSource")]
    [InlineData("@versionId", "versionId")]
    [InlineData("@tenant", "tenant = _tenant.TenantId")]
    public void SceneVideoCompletionSqlParametersHaveDapperBindings(string sqlParameter, string binding)
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var method = ExtractMethodBlock(source, "public async Task CompleteSceneVideoVersionAsync");
        var parameterObject = ExtractSceneVideoCompletionUpdateParameterObject(method);

        Assert.Contains(sqlParameter, method);
        Assert.Contains(binding, parameterObject);
    }

    [Fact]
    public void SceneVideoVersionSqlKeepsImportantDtoMappingsExplicit()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var select = ExtractConstBlock(source, "SelectSceneVideoVersionSql");

        Assert.Contains("source_image_version_id AS SourceImageVersionId", select);
        Assert.Contains("provider_capability_id AS ProviderCapabilityId", select);
        Assert.Contains("provider_task_id AS ProviderTaskId", select);
        Assert.Contains("billing_logical_request_id AS BillingLogicalRequestId", select);
        Assert.Contains("result_media_id AS ResultMediaId", select);
        Assert.Contains("voice_audio_version_id AS VoiceAudioVersionId", select);
        Assert.Contains("poster_url AS PosterUrl", select);
        Assert.Contains("source_file_path AS SourceFilePath", select);
        Assert.Contains("public_url AS PublicUrl", select);
    }

    [Fact]
    public void SceneVideoVersionSchemaKeepsCanonicalVoiceAudioColumn()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "database", "scene-media-versioning", "01_add_scene_media_versioning.sql"),
            Encoding.UTF8);

        Assert.Contains("voice_audio_version_id uuid NULL", sql);
        Assert.DoesNotContain("voiceaudioversionid", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public void SceneVideoVersionDtoHydratesNullableVoiceAudioVersionId(string? value)
    {
        var version = new TodoX.Web.Services.VideoRender.SceneVideoVersionDto
        {
            VoiceAudioVersionId = value is null ? null : Guid.Parse(value)
        };

        Assert.Equal(value is null ? null : Guid.Parse(value), version.VoiceAudioVersionId);
    }

    [Fact]
    public void SceneVideoRecoveryReusesExistingProviderTaskWithoutSubmissionOrReservation()
    {
        var worker = ReadRepoFile("Services", "VideoRender", "SceneVideoWorkerHandler.cs");
        var completion = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");
        var reconciliation = ReadRepoFile("Services", "AiProviders", "AiImageBillingReconciliationWorker.cs");

        Assert.Contains("GetRecoverableSceneVideoVersionAsync", worker);
        Assert.Contains("GetSceneVideoProviderTaskIdAsync", worker);
        Assert.Contains("RVIDEO_VIDEO_PROVIDER_REUSED", worker);
        Assert.Contains("GetReservationAsync(attemptLogicalRequestId", worker);
        Assert.Contains("SubmitAsync", worker);
        Assert.Contains("ReserveAsync", worker);

        var existingTaskBlock = worker[worker.IndexOf("var existingTaskId = await _versions.GetSceneVideoProviderTaskIdAsync", StringComparison.Ordinal)..];
        var resubmitGuard = existingTaskBlock[..existingTaskBlock.IndexOf("if (string.IsNullOrWhiteSpace(taskId))", StringComparison.Ordinal)];
        Assert.DoesNotContain("SubmitAsync", resubmitGuard);
        Assert.DoesNotContain("ReserveAsync", resubmitGuard);

        Assert.Contains("CompleteProviderVideoAsync", worker);
        Assert.Contains("CompleteProviderVideoAsync", reconciliation);
        Assert.Contains("IsRecovery: true", reconciliation);
        Assert.Contains("RVIDEO_VIDEO_RECOVERY_BEGIN", completion);
        Assert.Contains("RVIDEO_VIDEO_PROVIDER_RESULT_SUCCESS", completion);
        Assert.Contains("RVIDEO_VIDEO_PERSIST_BEGIN", completion);
        Assert.Contains("RVIDEO_VIDEO_PERSIST_SUCCESS", completion);
        Assert.Contains("SCENE_VIDEO_READY", completion);
        Assert.Contains("RVIDEO_VIDEO_BILLING_COMPLETED", completion);
        Assert.Contains("RVIDEO_VIDEO_RECOVERY_COMPLETED", completion);
    }

    [Fact]
    public void SceneVideoCompletionPersistsResultAndSynchronizesSceneAfterBillingSafeOrder()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var completionService = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");
        var method = ExtractMethodBlock(source, "public async Task CompleteSceneVideoVersionAsync");

        Assert.Contains("result_media_id=COALESCE(@resultMediaId, result_media_id)", method);
        Assert.Contains("public_url=@videoUrl", method);
        Assert.Contains("source_file_path=@videoPath", method);
        Assert.Contains("SET status='completed'", method);
        Assert.Contains("selected_video_version_id=@versionId", method);
        Assert.Contains("scene_video_url=COALESCE(@videoUrl, scene_video_url)", method);
        Assert.Contains("scene_video_path=COALESCE(@videoPath, scene_video_path)", method);

        var persistIndex = completionService.IndexOf("CompleteSceneVideoVersionAsync", StringComparison.Ordinal);
        var billingIndex = completionService.IndexOf("_billing.CompleteAsync", StringComparison.Ordinal);
        Assert.True(persistIndex >= 0);
        Assert.True(billingIndex > persistIndex);
    }

    [Fact]
    public void ReplaceScenesInsertKeepsVoiceAndStatusColumnContractAligned()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderRepository.cs");
        var method = ExtractMethodBlock(source, "public async Task<List<VideoProjectSceneDto>> ReplaceScenesAsync");
        var insertSql = ExtractInsertSql(method);
        var columns = ExtractInsertTargetColumns(insertSql);
        var values = ExtractValuesExpressions(insertSql);

        Assert.Equal(columns.Count, values.Count);
        Assert.Equal(new[]
        {
            "project_id",
            "tenant_id",
            "scene_index",
            "title",
            "duration_seconds",
            "scene_prompt",
            "image_prompt",
            "video_prompt",
            "static_image_path",
            "static_image_url",
            "scene_video_path",
            "scene_video_url",
            "voice_enabled",
            "speaker_key",
            "voice_text",
            "voice_instruction",
            "status",
            "error_message",
            "created_at",
            "updated_at"
        }, columns);
        Assert.Equal(new[]
        {
            "@projectId",
            "@tenant",
            "@sceneIndex",
            "@title",
            "@duration",
            "@scenePrompt",
            "@imagePrompt",
            "@videoPrompt",
            "@staticImagePath",
            "@staticImageUrl",
            "@sceneVideoPath",
            "@sceneVideoUrl",
            "@voiceEnabled",
            "@speakerKey",
            "@voiceText",
            "@voiceInstruction",
            "@status",
            "@errorMessage",
            "now()",
            "now()"
        }, values);

        Assert.Contains("voice_enabled", columns);
        Assert.Contains("speaker_key", columns);
        Assert.Contains("voice_text", columns);
        Assert.Contains("voice_instruction", columns);
        Assert.Contains("voice_enabled, speaker_key, voice_text, voice_instruction", NormalizeSql(insertSql));
        Assert.Contains("voice_enabled => @voiceEnabled", NormalizeMappings(columns, values));
        Assert.Contains("speaker_key => @speakerKey", NormalizeMappings(columns, values));
        Assert.Contains("voice_text => @voiceText", NormalizeMappings(columns, values));
        Assert.Contains("voice_instruction => @voiceInstruction", NormalizeMappings(columns, values));
        Assert.Contains("status => @status", NormalizeMappings(columns, values));
        Assert.Contains("error_message => @errorMessage", NormalizeMappings(columns, values));
    }

    [Fact]
    public void SceneVideoMuxCompletionKeepsStorageKeyAsRawProvenanceContract()
    {
        var versioning = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var method = ExtractMethodBlock(versioning, "public async Task CompleteSceneVideoVersionAsync");

        Assert.DoesNotContain("storage_key=COALESCE(@storageKey, storage_key)", method);
        Assert.DoesNotContain("@storageKey", method);
        Assert.DoesNotContain("StorageKey", method);
    }

    [Fact]
    public void LegacyVoiceHydrationIsIdempotentAndPreservesExistingValues()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderRepository.cs");
        var method = ExtractMethodBlock(source, "public async Task<bool> HydrateSceneVoiceMetadataAsync");

        Assert.Contains("RVIDEO_VOICE_METADATA_READY", method);
        Assert.Contains("ScenePromptMetadata.FromScene(scene)", method);
        Assert.Contains("voice_text=COALESCE(NULLIF(voice_text, ''), NULLIF(@voiceText, ''))", method);
        Assert.Contains("voice_instruction=COALESCE(NULLIF(voice_instruction, ''), NULLIF(@voiceInstruction, ''))", method);
        Assert.Contains("speaker_key=COALESCE(NULLIF(speaker_key, ''), NULLIF(@speakerKey, ''))", method);
        Assert.Contains("voiceMode == RVideoVoiceModes.None", method);
        Assert.Contains("AND (voice_enabled IS DISTINCT FROM @voiceEnabled", method);
    }

    [Fact]
    public void FinalMergeHasFastCopyAndNormalizedTranscodeFallbackCommands()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderMergeHandler.cs");

        Assert.Contains("PROJECT_MERGE_COPY_FAILED", source);
        Assert.Contains("PROJECT_MERGE_TRANSCODE_FALLBACK_STARTED", source);
        Assert.Contains("PROJECT_MERGE_TRANSCODE_FALLBACK_COMPLETED", source);
        Assert.Contains("ffmpeg-copy.log", source);
        Assert.Contains("ffmpeg-fallback.log", source);
        Assert.Contains("\"-c\", \"copy\"", source);
        Assert.Contains("\"-c:v\", \"libx264\"", source);
        Assert.Contains("\"-pix_fmt\", \"yuv420p\"", source);
        Assert.Contains("\"-c:a\", \"aac\"", source);
        Assert.Contains("\"-ar\", \"48000\"", source);
        Assert.Contains("\"-ac\", \"2\"", source);
        Assert.Contains("\"-movflags\", \"+faststart\"", source);
    }

    [Fact]
    public void ManualNativeReadyProjectEnqueuesSingleFfmpegCopyConcatFinalMerge()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoProjectFinalizationService.cs");
        var method = ExtractMethodBlock(source, "public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync");

        Assert.Contains("BuildMergeLogicalRequestKey(projectId)", method);
        Assert.Contains("=> $\"rvideo-final-merge:{projectId}\"", source);
        Assert.Contains("foreach (var scene in project.Scenes)", method);
        Assert.Contains("GetSelectedVideoVersionAsync(scene.Id, ct)", method);
        Assert.Contains("GetSelectedAudioVersionAsync(scene.Id, ct)", method);
        Assert.Contains("RVideoRules.GetSceneReadiness(scene, settings, selectedVideo, selectedAudio)", method);
        Assert.Contains("missing.Count > 0", method);
        Assert.Contains("return NotEnqueued(\"not_ready\", logicalRequestId)", method);
        Assert.Contains("EnqueueForLogCodeIfNoneActiveAsync", method);
        Assert.Contains("JobType = RenderJobTypes.MergeProjectVideo", method);
        Assert.Contains("LogCode = logicalRequestId", method);
        Assert.Contains("ProviderCode = \"ffmpeg\"", method);
        Assert.Contains("ModelCode = \"copy_concat\"", method);
        Assert.DoesNotContain("RenderJobTypes.RenderSceneVideo", method);
        Assert.DoesNotContain("RenderJobTypes.RenderSceneAudio", method);
        Assert.DoesNotContain("CreateQueuedSceneAudioVersionAsync", method);
    }

    [Fact]
    public void FinalMergeEnqueueIsIdempotentByLogicalRequestId()
    {
        var finalization = ReadRepoFile("Services", "VideoRender", "RVideoProjectFinalizationService.cs");
        var renderJobs = ReadRepoFile("Services", "Render", "RenderJobService.cs");
        var method = ExtractMethodBlock(finalization, "public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync");
        var enqueue = ExtractMethodBlock(renderJobs, "public async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForLogCodeIfNoneActiveAsync");

        Assert.Contains("var logicalRequestId = BuildMergeLogicalRequestKey(projectId)", method);
        Assert.Contains("EnqueueForLogCodeIfNoneActiveAsync", method);
        Assert.Contains("BuildLogCodeJobLockName(jobType, uniqueLogCode)", enqueue);
        Assert.Contains("log_code = @logCode", enqueue);
        Assert.Contains("status IN ('queued', 'preparing', 'rendering', 'post_processing', 'pending_reconciliation')", enqueue);
        Assert.Contains("return (active, true)", enqueue);
        Assert.Contains("already_active", method);
    }

    [Fact]
    public void IncompleteProjectDoesNotEnqueueFinalMerge()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoProjectFinalizationService.cs");
        var method = ExtractMethodBlock(source, "public async Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync");

        Assert.Contains("foreach (var scene in project.Scenes)", method);
        Assert.Contains("GetSelectedVideoVersionAsync(scene.Id, ct)", method);
        Assert.Contains("RVideoRules.GetSceneReadiness(scene, settings, selectedVideo, selectedAudio)", method);
        Assert.True(
            method.IndexOf("return NotEnqueued(\"not_ready\", logicalRequestId)", StringComparison.Ordinal)
            < method.IndexOf("EnqueueForLogCodeIfNoneActiveAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void LibraryVoiceProjectRequiresSelectedCompletedAudioMuxBeforeFinalMerge()
    {
        var settings = new TodoX.Web.Models.RVideoJobSettingsDto
        {
            VoiceMode = TodoX.Web.Models.RVideoVoiceModes.Library
        };
        var audioId = Guid.NewGuid();
        var scene = new TodoX.Web.Models.VideoProjectSceneDto
        {
            Id = 55,
            VoiceText = "Xin chao",
            SelectedAudioVersionId = audioId
        };
        var completedVideoWithoutMuxAudio = new TodoX.Web.Services.VideoRender.SceneVideoVersionDto
        {
            Status = "completed",
            VoiceAudioVersionId = null
        };
        var completedAudio = new TodoX.Web.Services.VideoRender.SceneAudioVersionDto
        {
            Id = audioId,
            Status = "completed"
        };

        Assert.False(TodoX.Web.Models.RVideoRules.IsSceneFinalReady(scene, settings, completedVideoWithoutMuxAudio, completedAudio));
        var completedVideoWithMuxAudio = new TodoX.Web.Services.VideoRender.SceneVideoVersionDto
        {
            Status = "completed",
            VoiceAudioVersionId = audioId
        };
        Assert.True(TodoX.Web.Models.RVideoRules.IsSceneFinalReady(
            scene,
            settings,
            completedVideoWithMuxAudio,
            completedAudio));
    }

    [Fact]
    public void FinalMergeSuccessSynchronizesCoreRVideoJobAfterProjectCompletion()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderMergeHandler.cs");

        var projectCompletedIndex = source.IndexOf("UpdateProjectAsync(project.Id, VideoProjectStatuses.Completed", StringComparison.Ordinal);
        var syncCompletedIndex = source.IndexOf("_rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Completed", StringComparison.Ordinal);
        var mergedEventIndex = source.IndexOf("PROJECT_MERGED", StringComparison.Ordinal);
        var state = TodoX.Web.Services.VideoRender.RVideoJobService.ResolveCoreLifecycleState(
            TodoX.Web.Models.RVideoStages.Result,
            TodoX.Web.Models.VideoProjectStatuses.Completed);

        Assert.True(projectCompletedIndex >= 0);
        Assert.True(syncCompletedIndex > projectCompletedIndex);
        Assert.True(mergedEventIndex > syncCompletedIndex);
        Assert.Equal(TodoX.Web.Services.Render.RenderJobStatuses.Completed, state.Status);
        Assert.Equal(100, state.ProgressPercent);
    }

    [Fact]
    public void FinalMergeFailureSynchronizesCoreRVideoJobAndRethrows()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderMergeHandler.cs");
        var catchBlock = source[source.IndexOf("catch (Exception ex) when", StringComparison.Ordinal)..];

        var retryGateIndex = catchBlock.IndexOf("if (job.AttemptCount < job.MaxAttempts)", StringComparison.Ordinal);
        var retryProjectIndex = catchBlock.IndexOf("UpdateProjectAsync(project.Id, VideoProjectStatuses.Merging", StringComparison.Ordinal);
        var retrySyncIndex = catchBlock.IndexOf("_rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Merging", StringComparison.Ordinal);
        var retryEventIndex = catchBlock.IndexOf("PROJECT_MERGE_RETRYABLE_FAILED", StringComparison.Ordinal);
        var projectFailedIndex = catchBlock.IndexOf("UpdateProjectAsync(project.Id, VideoProjectStatuses.Failed", StringComparison.Ordinal);
        var syncFailedIndex = catchBlock.IndexOf("_rvideoJobs.SyncLifecycleAsync(project.Id, RVideoStages.Result, VideoProjectStatuses.Failed", StringComparison.Ordinal);
        var failedEventIndex = catchBlock.IndexOf("PROJECT_MERGE_FAILED", StringComparison.Ordinal);
        var rethrowIndex = catchBlock.LastIndexOf("throw;", StringComparison.Ordinal);
        var state = TodoX.Web.Services.VideoRender.RVideoJobService.ResolveCoreLifecycleState(
            TodoX.Web.Models.RVideoStages.Result,
            TodoX.Web.Models.VideoProjectStatuses.Failed);

        Assert.True(retryGateIndex >= 0);
        Assert.True(retryProjectIndex > retryGateIndex);
        Assert.True(retrySyncIndex > retryProjectIndex);
        Assert.True(retryEventIndex > retrySyncIndex);
        Assert.True(projectFailedIndex >= 0);
        Assert.True(syncFailedIndex > projectFailedIndex);
        Assert.True(failedEventIndex > syncFailedIndex);
        Assert.True(rethrowIndex > failedEventIndex);
        Assert.Equal(TodoX.Web.Services.Render.RenderJobStatuses.Failed, state.Status);
        Assert.Equal(100, state.ProgressPercent);
    }

    [Fact]
    public void SceneAudioAutoChainRequiresSelectedCompletedVideoAndLogicalRequestReuse()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoSceneAudioAutoChainService.cs");

        Assert.Contains("GetSelectedVideoVersionAsync(sceneId, ct)", source);
        Assert.Contains("selected_video_not_completed", source);
        Assert.Contains("GetSceneAudioVersionByLogicalRequestIdAsync", source);
        Assert.Contains("BuildLogicalRequestKey(projectId, sceneId)", source);
        Assert.Contains("same request is already active", source);
    }

    [Fact]
    public void SystemVersionEndpointSurfacesBuildStampAndWorkerState()
    {
        var program = ReadRepoFile("Program.cs");
        var project = File.ReadAllText(Path.Combine(RepoRoot, "TodoX.Web", "TodoX.Web.csproj"), Encoding.UTF8);

        Assert.Contains("app.MapGet(\"/system/version\"", program);
        Assert.Contains("AssemblyInformationalVersionAttribute", program);
        Assert.Contains("AssemblyMetadataAttribute", program);
        Assert.Contains("BuildCommit", program);
        Assert.Contains("BuildBranch", project);
        Assert.Contains("BuildTimeUtc", project);
        Assert.Contains("<BuildCommit Condition=\"'$(BuildCommit)' == ''\">unknown</BuildCommit>", project);
        Assert.Contains("<_Parameter1>BuildCommit</_Parameter1>", project);
        Assert.DoesNotContain("informationalVersion[(plus + 1)..]", program);
    }

    [Fact]
    public void SystemVersionMetadataFallsBackToUnknownAndDoesNotExposeConfiguration()
    {
        var program = ReadRepoFile("Program.cs");
        var endpointStart = program.IndexOf("app.MapGet(\"/system/version\"", StringComparison.Ordinal);
        var endpointEnd = program.IndexOf("app.MapPost(\"/api/ai/cost/estimate\"", endpointStart, StringComparison.Ordinal);
        var endpoint = program[endpointStart..endpointEnd];

        Assert.Contains("metadata.TryGetValue", endpoint);
        Assert.Contains("? value", endpoint);
        Assert.Contains(": \"unknown\"", endpoint);
        Assert.DoesNotContain("configuration.AsEnumerable()", endpoint);
        Assert.DoesNotContain("ConnectionStrings", endpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", endpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Token", endpoint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("renderQueueEnabled = configuration.GetValue", endpoint);
        Assert.Contains("rvideoLifecycleRegistered", endpoint);
    }

    [Fact]
    public void LegacyAudioCostBackfillIsConditionalAndPreservesNewValues()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "database", "scene-media-versioning", "05_align_legacy_scene_audio_versions_with_runtime.sql"),
            Encoding.UTF8);

        Assert.Contains("column_name='estimated_provider_cost'", sql);
        Assert.Contains("SET estimated_usd=COALESCE(estimated_usd, estimated_provider_cost)", sql);
        Assert.Contains("column_name='actual_provider_cost'", sql);
        Assert.Contains("SET actual_usd=COALESCE(actual_usd, actual_provider_cost)", sql);
        Assert.Contains("WHERE estimated_usd IS NULL", sql);
        Assert.Contains("WHERE actual_usd IS NULL", sql);
        Assert.Contains("EXECUTE", sql);
    }

    [Fact]
    public void LifecycleSyncIsSafeForProjectsWithoutCoreJob()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoJobService.cs");
        var method = ExtractMethodBlock(source, "public async Task SyncLifecycleAsync");

        Assert.Contains("SELECT core_job_id FROM video_render.video_projects", method);
        Assert.Contains("if (jobId is null) return;", method);
        Assert.Contains("status NOT IN ('completed','failed','cancelled')", method);
    }

    [Fact]
    public void SharedFinalizationServiceUsesStableLogicalMergeKeyAndSingleEnqueuePath()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoProjectFinalizationService.cs");
        var worker = ReadRepoFile("Services", "VideoRender", "RVideoLifecycleWorker.cs");
        var completion = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");
        var mux = ReadRepoFile("Services", "VideoRender", "SceneAudioMuxHandler.cs");
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("IRVideoProjectFinalizationService", source);
        Assert.Contains("rvideo-final-merge:", source);
        Assert.Contains("TryEnqueueFinalMergeAsync", source);
        Assert.Contains("RVideoProjectFinalizationContracts.TriggerAuto", worker);
        Assert.DoesNotContain("TryEnqueueProjectMergeAsync", worker);
        Assert.Contains("_finalization.TryEnqueueFinalMergeAsync", completion);
        Assert.Contains("_finalization.TryEnqueueFinalMergeAsync", mux);
        Assert.Contains("RVideoFinalization.TryEnqueueFinalMergeAsync", page);
        Assert.DoesNotContain("ProviderCode = \"internal_merge\"", page);
        Assert.DoesNotContain("ModelCode = \"ffmpeg_concat\"", page);
    }

    [Fact]
    public void SceneVideoCompletionTriggersSharedFinalMergeAfterLifecycleSync()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");

        var syncIndex = source.IndexOf("_rvideoJobs.SyncLifecycleAsync", StringComparison.Ordinal);
        var finalMergeIndex = source.IndexOf("_finalization.TryEnqueueFinalMergeAsync", StringComparison.Ordinal);

        Assert.True(syncIndex >= 0);
        Assert.True(finalMergeIndex > syncIndex);
        Assert.Contains("RVIDEO_VIDEO_FINAL_MERGE_TRIGGER_FAILED", source);
        Assert.Contains("RVideoProjectFinalizationContracts.TriggerSceneVideoReady", source);
    }

    [Fact]
    public void SceneAudioMuxCompletionAlsoTriggersSharedFinalMerge()
    {
        var source = ReadRepoFile("Services", "VideoRender", "SceneAudioMuxHandler.cs");

        Assert.Contains("_finalization.TryEnqueueFinalMergeAsync", source);
        Assert.Contains("RVideoProjectFinalizationContracts.TriggerSceneAudioReady", source);
    }

    [Fact]
    public void ReplaceScenesSynchronizesSceneCountAndTotalSecondsInSameTransaction()
    {
        var source = ReadRepoFile("Services", "VideoRender", "VideoRenderRepository.cs");
        var method = ExtractMethodBlock(source, "public async Task<List<VideoProjectSceneDto>> ReplaceScenesAsync");

        Assert.Contains("DELETE FROM video_render.video_project_scenes", method);
        Assert.Contains("UPDATE video_render.video_projects", method);
        Assert.Contains("scene_count=(SELECT count(*)::int FROM video_render.video_project_scenes", method);
        Assert.Contains("total_seconds=(SELECT COALESCE(sum(duration_seconds), 0)::int FROM video_render.video_project_scenes", method);
    }

    [Fact]
    public void SceneVideoCompletionBillsBeforeNonCriticalPostPersistenceEffects()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");

        var versionIndex = source.IndexOf("CompleteSceneVideoVersionAsync", StringComparison.Ordinal);
        var readyIndex = source.IndexOf("\"SCENE_VIDEO_READY\"", StringComparison.Ordinal);
        var billingIndex = source.IndexOf("_billing.CompleteAsync", StringComparison.Ordinal);
        var finalizerIndex = source.IndexOf("_finalizer.TryFinalizeSceneMediaAsync", StringComparison.Ordinal);
        var lifecycleIndex = source.IndexOf("_rvideoJobs.SyncLifecycleAsync", StringComparison.Ordinal);

        Assert.True(versionIndex >= 0);
        Assert.True(readyIndex > versionIndex);
        Assert.True(billingIndex > readyIndex);
        Assert.True(finalizerIndex > billingIndex);
        Assert.True(lifecycleIndex > finalizerIndex);
    }

    [Fact]
    public void SceneVideoPostCompletionFailuresAreDiagnosedAndIsolated()
    {
        var source = ReadRepoFile("Services", "VideoRender", "RVideoSceneVideoCompletionService.cs");

        Assert.Contains("RVIDEO_VIDEO_FINALIZER_FAILED", source);
        Assert.Contains("RVIDEO_VIDEO_LIFECYCLE_SYNC_FAILED", source);
        Assert.Contains("RVIDEO_VIDEO_RENDER_JOB_RECOVERY_MARK_FAILED", source);
        Assert.Contains("RecordPostCompletionFailureAsync", source);
        Assert.Contains("safeErrorMessage", source);
    }

    [Fact]
    public void ReconciliationIsolatesItemsAndReschedulesUnexpectedFailures()
    {
        var source = ReadRepoFile("Services", "AiProviders", "AiImageBillingReconciliationWorker.cs");

        Assert.Contains("foreach (var item in claimed)", source);
        Assert.Contains("AI_IMAGE_RECONCILIATION_ITEM_FAILED", source);
        Assert.Contains("await billing.RescheduleReconciliationAsync", source);
        Assert.Contains("catch (OperationCanceledException) when (ct.IsCancellationRequested)", source);
        Assert.Contains("exceptionType", source);
        Assert.Contains("safeErrorMessage", source);
    }

    [Fact]
    public void RecoveryResolvesActualSceneIndexAndRearmUsesCanonicalIdentity()
    {
        var worker = ReadRepoFile("Services", "AiProviders", "AiImageBillingReconciliationWorker.cs");
        var rearm = File.ReadAllText(
            Path.Combine(RepoRoot, "database", "manual", "rvideo-project-11-video-reconciliation-rearm.sql"),
            Encoding.UTF8);

        Assert.Contains("var scene = await projects.GetSceneAsync(version.SceneId, ct);", worker);
        Assert.Contains("scene.SceneIndex", worker);
        Assert.DoesNotContain("SceneIndex: 0", worker);
        Assert.Contains("ON v.logical_request_id = b.logical_request_id", rearm);
        Assert.Contains("AND v.provider_task_id = b.provider_task_id", rearm);
        Assert.DoesNotContain("v.billing_logical_request_id = b.logical_request_id", rearm);
    }

    [Fact]
    public void SuccessfulSceneVideoCompletionClearsStaleFailureFields()
    {
        var versioning = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");
        var billing = ReadRepoFile("Services", "AiProviders", "AiImageBillingService.cs");

        var versionCompletion = ExtractMethodBlock(versioning, "public async Task CompleteSceneVideoVersionAsync");
        var billingCompletion = ExtractMethodBlock(billing, "public async Task<AiImageBillingCompletion> CompleteAsync");

        Assert.Contains("error_code=NULL", versionCompletion);
        Assert.Contains("error_message=NULL", versionCompletion);
        Assert.Contains("error_message = NULL", billingCompletion);
        Assert.Contains("pending_reconciliation_at = NULL", billingCompletion);
    }

    [Fact]
    public void ManualRVideoReferenceImageSqlAddsDurableBooleanSettingColumn()
    {
        var sql = File.ReadAllText(
            Path.Combine(RepoRoot, "database", "manual", "rvideo", "20260829_add_rvideo_reference_image_setting.sql"),
            Encoding.UTF8);

        Assert.Contains("ADD COLUMN IF NOT EXISTS use_reference_image_for_all_scenes boolean NOT NULL DEFAULT false", sql);
        Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot, "TodoX.Web" }.Concat(parts).ToArray()), Encoding.UTF8);

    private static string ExtractConstBlock(string source, string constName)
    {
        var start = source.IndexOf($"private const string {constName}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {constName}.");
        var end = source.IndexOf("\"\"\";", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Could not find end of {constName}.");
        return source[start..end];
    }

    private static string ExtractMethodBlock(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {signature}.");
        var openBrace = source.IndexOf('{', start);
        Assert.True(openBrace > start, $"Could not find opening brace for {signature}.");

        var depth = 0;
        for (var i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[start..(i + 1)];
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"Could not find end of {signature}.");
    }

    private static string ExtractSceneVideoCompletionUpdateParameterObject(string method)
    {
        var sqlStart = method.IndexOf("voice_audio_version_id=COALESCE(@voiceAudioVersionId, voice_audio_version_id)", StringComparison.Ordinal);
        Assert.True(sqlStart >= 0, "Could not find scene-video completion update SQL.");
        var objectStart = method.IndexOf("new\r\n            {", sqlStart, StringComparison.Ordinal);
        if (objectStart < 0)
        {
            objectStart = method.IndexOf("new\n            {", sqlStart, StringComparison.Ordinal);
        }

        Assert.True(objectStart > sqlStart, "Could not find scene-video completion Dapper parameter object.");
        var objectEnd = method.IndexOf("            }, tx);", objectStart, StringComparison.Ordinal);
        Assert.True(objectEnd > objectStart, "Could not find end of scene-video completion Dapper parameter object.");
        return method[objectStart..objectEnd];
    }

    private static string ExtractSceneVideoCompletionUpdateSql(string method)
    {
        var sqlStart = method.IndexOf("UPDATE video_render.scene_video_versions", StringComparison.Ordinal);
        Assert.True(sqlStart >= 0, "Could not find scene-video completion update SQL.");
        var objectStart = method.IndexOf("new\r\n            {", sqlStart, StringComparison.Ordinal);
        if (objectStart < 0)
        {
            objectStart = method.IndexOf("new\n            {", sqlStart, StringComparison.Ordinal);
        }

        Assert.True(objectStart > sqlStart, "Could not find scene-video completion Dapper parameter object.");
        return method[sqlStart..objectStart];
    }

    private static string ExtractInsertSql(string method)
    {
        var match = Regex.Match(
            method,
            @"INSERT INTO video_render\.video_project_scenes\s*\((?<columns>.*?)\)\s*VALUES\s*\((?<values>.*?)\)\s*;",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Could not find scene replacement INSERT SQL.");
        return match.Value;
    }

    private static IReadOnlyList<string> ExtractInsertTargetColumns(string insertSql)
    {
        var match = Regex.Match(
            insertSql,
            @"INSERT INTO video_render\.video_project_scenes\s*\((?<columns>.*?)\)\s*VALUES",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Could not isolate INSERT target columns.");
        return SplitSqlItems(match.Groups["columns"].Value);
    }

    private static IReadOnlyList<string> ExtractValuesExpressions(string insertSql)
    {
        var match = Regex.Match(
            insertSql,
            @"VALUES\s*\((?<values>.*?)\)\s*;",
            RegexOptions.Singleline);

        Assert.True(match.Success, "Could not isolate INSERT VALUES expressions.");
        return SplitSqlItems(match.Groups["values"].Value);
    }

    private static IReadOnlyList<string> SplitSqlItems(string block)
        => block
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeSql(string sql)
        => sql.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal);

    private static string NormalizeMappings(IReadOnlyList<string> columns, IReadOnlyList<string> values)
        => string.Join(", ", columns.Zip(values, (column, value) => $"{column} => {value}"));

}
