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
        var nextMethod = source.IndexOf("\n    public async Task", start + signature.Length, StringComparison.Ordinal);
        Assert.True(nextMethod > start, $"Could not find end of {signature}.");
        return source[start..nextMethod];
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
