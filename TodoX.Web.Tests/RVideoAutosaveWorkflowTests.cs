using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoAutosaveWorkflowTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string WebRoot = Path.Combine(RepoRoot, "TodoX.Web");

    [Fact]
    public void CreateSplit_UsesReadinessAndImplicitDraftSave()
    {
        var razor = Read("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("Disabled=\"@(_busy || !CanCreateOrSplitScenes)\"", razor, StringComparison.Ordinal);
        Assert.Contains("private bool CanCreateOrSplitScenes", razor, StringComparison.Ordinal);
        Assert.Contains("await EnsureDraftSavedAsync()", razor, StringComparison.Ordinal);
        Assert.Contains("await VideoRepo.ReplaceScenesAsync(_projectId!.Value, scenes)", razor, StringComparison.Ordinal);
        Assert.DoesNotContain("Disabled=\"@(_busy || _projectId is null)\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftPersistence_AssignsIdsAndSerializesConcurrentSaves()
    {
        var razor = Read("Components", "Pages", "RenderVideoJobs.razor");

        Assert.Contains("private readonly SemaphoreSlim _draftSaveGate", razor, StringComparison.Ordinal);
        Assert.Contains("await _draftSaveGate.WaitAsync()", razor, StringComparison.Ordinal);
        Assert.Contains("_jobId = created.JobId", razor, StringComparison.Ordinal);
        Assert.Contains("_projectId = created.ProjectId", razor, StringComparison.Ordinal);
        Assert.Contains("if (_jobId is null && _projectId is null)", razor, StringComparison.Ordinal);
        Assert.Contains("await RVideoJobs.UpdateAsync(jobId", razor, StringComparison.Ordinal);
        Assert.Contains("await AddDebugAsync(\"rvideo_draft_save_failed\"", razor, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneGrid_IsTwoColumnsOnDesktopAndOneColumnNarrow()
    {
        var razor = Read("Components", "Pages", "RenderVideoJobs.razor");
        var css = Read("Components", "Pages", "RenderVideoJobs.razor.css");

        Assert.Contains("class=\"scene-card-grid\"", razor, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", css, StringComparison.Ordinal);
        Assert.Contains(".scene-card-grid", css, StringComparison.Ordinal);
        Assert.Contains(".scene-card-grid {\n        grid-template-columns: minmax(0, 1fr);", css, StringComparison.Ordinal);
    }

    [Fact]
    public void VideoPreview_AutoplaysOnlyInDialogAndNotInCardFrame()
    {
        var razor = Read("Components", "Pages", "RenderVideoJobs.razor");
        var dialog = Read("Components", "Dialogs", "LandingIndustryVideoPreviewDialog.razor");
        var mediaFrame = Read("Components", "Shared", "RenderMediaFrame.razor");
        var js = Read("wwwroot", "js", "todox-render-log.js");

        Assert.Contains("OpenSceneVideoPreview(scene)", razor, StringComparison.Ordinal);
        Assert.Contains("AutoPlay)] = true", razor, StringComparison.Ordinal);
        Assert.Contains("autoplay=\"@AutoPlay\"", dialog, StringComparison.Ordinal);
        Assert.Contains("todoXVideoPreview.play", dialog, StringComparison.Ordinal);
        Assert.Contains("currentTime = 0", js, StringComparison.Ordinal);
        Assert.DoesNotContain("autoplay", mediaFrame, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { WebRoot }.Concat(parts).ToArray()));
}
