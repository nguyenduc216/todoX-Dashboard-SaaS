using Xunit;

namespace TodoX.Web.Tests;

public sealed class MediaHistorySelectionRegressionTests
{
    [Fact]
    public void RDanceReferenceHistoryUsesDedicatedSelectionMethodAndRealButtons()
    {
        var service = ReadRepoFile("Services", "DanceSell", "DanceSellPhase2Services.cs");
        var page = ReadRepoFile("Components", "Pages", "RDanceJobDetail.razor");

        Assert.Contains("SelectReferenceVersionAsync", service);
        Assert.Contains("version.Status is not (DanceSellReferenceStatuses.Ready or DanceSellReferenceStatuses.Approved)", service);
        Assert.Contains("version.MediaId is null", service);
        Assert.Contains("References.SelectReferenceVersionAsync", page);
        Assert.Contains("MudButton Variant=\"Variant.Outlined\" Color=\"Color.Warning\" StartIcon=\"@Icons.Material.Filled.CloudUpload\"", page);
        Assert.Contains("rdance-file-button-wrapper", page);
        Assert.DoesNotContain("rdance-upload-button", page);
        Assert.Contains("<InputFile class=\"rdance-hidden-file-input\" OnChange=\"OnCharacterSelected\"", page);
    }

    [Fact]
    public void RVideoHistorySelectsCompletedSceneMediaWithoutRenderingAgain()
    {
        var page = ReadRepoFile("Components", "Pages", "RenderVideoJobs.razor");
        var service = ReadRepoFile("Services", "VideoRender", "SceneMediaVersioningService.cs");

        Assert.Contains("OpenFinalHistoryAsync", page);
        Assert.Contains("SelectFinalVersionAsync(version)", page);
        Assert.Contains("SelectFinalVideoVersionAsync", service);
        Assert.Contains("status='completed'", service);
    }

    [Fact]
    public void TimelapseHistorySelectsExistingImageVideoAndFinalOutputVersions()
    {
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");
        var service = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");

        Assert.Contains("ListHistoryAsync", page);
        Assert.Contains("SelectHistoryAsync(JobId, selected", page);
        Assert.Contains("FROM timelapse.timelapse_image_stage_versions", service);
        Assert.Contains("FROM timelapse.timelapse_video_clip_versions", service);
        Assert.Contains("FROM timelapse.timelapse_final_outputs", service);
        Assert.Contains("UPDATE render.render_jobs", service);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
