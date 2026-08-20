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
}
