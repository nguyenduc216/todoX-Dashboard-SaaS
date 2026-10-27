using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoRecomposeCompatibilityTests
{
    private static string Read(string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "TodoX.Web", "Services"))) directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory!.FullName, "TodoX.Web", path));
    }

    [Theory]
    [InlineData(null, 0, false, "final_versioning_setting_missing_or_ambiguous")]
    [InlineData("true", 2, false, "final_versioning_setting_missing_or_ambiguous")]
    [InlineData("false", 1, false, "final_versioning_required")]
    [InlineData("true", 1, true, "ready")]
    [InlineData("YES", 1, true, "ready")]
    [InlineData("1", 1, true, "ready")]
    [InlineData(" true ", 1, false, "final_versioning_required")]
    public void VersioningFlagMustBeUnambiguousAndExplicit(string? value, int count, bool ready, string reason)
    {
        var result = FinalVideoVersioningState.FromFlag(value, count);
        Assert.Equal(ready, result.Ready);
        Assert.Equal(reason, result.Reason);
    }

    [Theory]
    [InlineData("final_versioning_required")]
    [InlineData("final_versioning_schema_not_ready")]
    [InlineData("final_versioning_setting_missing_or_ambiguous")]
    [InlineData("final_versioning_unavailable")]
    public void VersioningStatesHaveSpecificActionableMessages(string reason)
        => Assert.NotEqual(FinalMergeMessages.Blocked("unknown"), FinalMergeMessages.Blocked(reason));

    [Fact]
    public void LegacySeparatesInitialMergeAndRecomposeAndRecreatesChangedPlayer()
    {
        var source = Read("Components/Pages/RenderVideoJobs.razor");
        var position = source.IndexOf("<RVideoRecomposeAction", StringComparison.Ordinal);
        Assert.True(position > source.IndexOf("<MudButton Size=\"Size.Small\" Color=\"Color.Primary\" Variant=\"Variant.Filled\" StartIcon=\"@Icons.Material.Filled.Download\"", StringComparison.Ordinal));
        Assert.Contains("@key=\"_project.FinalVideoUrl\"", source);
        Assert.Contains("OnClick=\"EnqueueMergeAsync\"", source);
        Assert.DoesNotContain("CanClickFinalMerge", source[(position - 100)..(position + 150)]);
    }

    [Fact]
    public void BothUisUseSameReadOnlyMonitorAndManualEnqueue()
    {
        var v2 = Read("Components/Pages/RVideo/RVideoV2Result.razor");
        var shared = Read("Components/Shared/RVideoRecomposeAction.razor");
        Assert.Contains("<RVideoRecomposeAction", v2);
        Assert.Contains("async Task ReMergeAsync()", v2);
        Assert.Contains("@key=\"JobView.Project.FinalVideoUrl\"", v2);
        Assert.Contains("RVideoProjectFinalizationContracts.TriggerManual", shared);
        var watch = shared[shared.IndexOf("private async Task WatchAsync", StringComparison.Ordinal)..];
        Assert.Contains("ListByLogCodeAsync", watch);
        Assert.Contains("OnReloadRequested.InvokeAsync", watch);
        Assert.DoesNotContain("TryEnqueueFinalMergeAsync", watch);
        Assert.DoesNotContain("render_scene_video", shared);
    }

    [Fact]
    public void LegacyFinalIsNotBackfilledAndSnapshotPreservesPreviousLocation()
    {
        var handler = Read("Services/VideoRender/VideoRenderMergeHandler.cs");
        Assert.Contains("previousFinal = new { project.FinalVideoUrl, project.FinalVideoPath }", handler);
        Assert.Contains("GetFinalVideoVersioningStateAsync", handler);
        var versions = Read("Services/VideoRender/SceneMediaVersioningService.cs");
        var start = versions.IndexOf("public async Task<FinalVideoVersionDto> CreateQueuedFinalVideoVersionAsync", StringComparison.Ordinal);
        var end = versions.IndexOf("public async Task CompleteFinalVideoVersionAsync", start, StringComparison.Ordinal);
        var create = versions[start..end];
        Assert.Contains("v.scene_id=s.id", create);
        Assert.Contains("v.status='completed'", create);
        Assert.Contains("jsonb_build_object('durationSeconds'", create);
        Assert.DoesNotContain("'completed',true", create);
        Assert.DoesNotContain("UPDATE video_render.video_projects", create);
    }

    [Fact]
    public void ReadinessChecksExactForeignKeyRelationsWithoutRequiringRenderJobFk()
    {
        var sql = FinalVideoVersioningState.SchemaReadySql;
        Assert.Contains("required_fks(source_table,source_columns,target_table,target_columns)", sql);
        var required = sql[sql.IndexOf("required_fks(", StringComparison.Ordinal)..sql.IndexOf("missing_fks AS", StringComparison.Ordinal)];
        Assert.Contains("('video_render.video_projects','selected_final_video_version_id','video_render.final_video_versions','id')", required);
        Assert.Contains("('video_render.final_video_versions','project_id','video_render.video_projects','id')", required);
        Assert.Contains("('video_render.final_video_version_items','final_video_version_id','video_render.final_video_versions','id')", required);
        Assert.Contains("('video_render.final_video_version_items','scene_id','video_render.video_project_scenes','id')", required);
        Assert.Contains("('video_render.final_video_version_items','scene_video_version_id','video_render.scene_video_versions','id')", required);
        Assert.DoesNotContain("render.render_jobs", required);
        foreach (var clause in new[] { "fk.contype='f'", "fk.convalidated", "fk.conrelid=to_regclass(r.source_table)", "fk.confrelid=to_regclass(r.target_table)", "fk.conkey=ARRAY", "fk.confkey=ARRAY" })
            Assert.Contains(clause, sql);
        Assert.DoesNotContain("count(*) FROM pg_constraint", sql);
    }

    [Fact]
    public void RuntimeAndOperatorSchemaVerificationUseIdenticalPredicate()
    {
        static string Normalize(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", "");
        var runtime = Normalize(FinalVideoVersioningState.SchemaReadySql.Trim().TrimEnd(';'));
        var verification = Normalize(Read("database/manual/rvideo-recompose/verify.sql"));
        Assert.Contains(runtime, verification);
    }

    [Fact]
    public void FinalSnapshotKeepsExactPointerLineageAndRejectsDifferentResolutionVersion()
    {
        var source = Read("Services/VideoRender/SceneMediaVersioningService.cs");
        var start = source.IndexOf("public async Task<FinalVideoVersionDto> CreateQueuedFinalVideoVersionAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task CompleteFinalVideoVersionAsync", start, StringComparison.Ordinal);
        var snapshot = source[start..end];
        foreach (var condition in new[] { "v.id=s.selected_video_version_id", "v.scene_id=s.id", "v.project_id=s.project_id", "v.tenant_id=s.tenant_id", "v.status='completed'" })
            Assert.Contains(condition, snapshot);
        var handler = Read("Services/VideoRender/VideoRenderMergeHandler.cs");
        Assert.Contains("snapshot.Single(x => x.SceneId == scene.Id).SceneVideoVersionId != selectedVideo.Id", handler);
        Assert.Contains("ResolveLocalMergeInputAsync(selectedVideo, scene.Id, ct)", handler);
    }
}
