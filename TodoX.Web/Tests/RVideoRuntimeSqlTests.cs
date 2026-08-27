using System.Text;
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

        Assert.Contains("voice_audio_version_id AS VoiceAudioVersionId", select);
        Assert.Contains("voice_audio_version_id=COALESCE(@voiceAudioVersionId, voice_audio_version_id)", complete);
        var sqlIdentifiersOnly = (select + complete)
            .Replace("VoiceAudioVersionId", string.Empty, StringComparison.Ordinal)
            .Replace("@voiceAudioVersionId", string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("voiceaudioversionid", sqlIdentifiersOnly, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\n               VoiceAudioVersionId", select);
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

}
