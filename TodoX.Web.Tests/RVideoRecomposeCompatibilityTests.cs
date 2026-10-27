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
}
