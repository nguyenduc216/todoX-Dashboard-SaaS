using System.Text;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoRuntimeSqlTests
{
    [Fact]
    public void RuntimeMigrationAddsSceneVideoProviderCapabilityId()
    {
        var sql = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "rvideo", "02_reconcile_scene_video_versions_runtime.sql"),
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
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "rvideo", "verify_rvideo_runtime.sql"),
            Encoding.UTF8);

        Assert.Contains("('provider_capability_id')", sql);
        Assert.Contains("table_name='scene_video_versions'", sql);
    }

    [Fact]
    public void RecoverySqlResolvesByCoreJobProjectAndSceneVideoVersion()
    {
        var sql = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "manual", "rvideo-scene-video-42804-recovery.sql"),
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
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "database", "scene-media-versioning", "03_add_scene_audio_versioning.sql"),
            Encoding.UTF8);

        Assert.Contains("CREATE TABLE IF NOT EXISTS video_render.scene_audio_versions", sql);
        Assert.Contains("selected_audio_version_id", sql);
        Assert.Contains("voice_audio_version_id", sql);
    }
}
