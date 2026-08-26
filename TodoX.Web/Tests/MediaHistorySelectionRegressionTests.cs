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

    [Fact]
    public void TimelapseSceneVideoHistoryUsesClipScopedHistoryAndSharedDialog()
    {
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");
        var service = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");
        var jobService = ReadRepoFile("Services", "Timelapse", "TimelapseJobService.cs");

        Assert.Contains("OpenSceneVideoHistoryAsync(TimelapseVideoClip clip)", page);
        Assert.Contains("ListSceneVideoHistoryAsync(JobId, clip.ClipIndex", page);
        Assert.Contains("StartIcon=\"@Icons.Material.Filled.History\"", page);
        Assert.Contains("VersionLabel = $\"Lần {item.Version}\"", page);
        Assert.Contains("ListSceneVideoHistoryAsync(Guid jobId, int clipIndex", service);
        Assert.Contains("AND (@clipIndex IS NULL OR c.clip_index=@clipIndex)", service);
        Assert.Contains("ListSceneVideoHistoryAsync(Guid jobId, int clipIndex, CurrentUserSession currentUser", jobService);
    }

    [Fact]
    public void TimelapseImageHistoryAndFinalHistoryUseSharedDialogAndCurrentPointers()
    {
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");
        var workflow = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");
        var jobService = ReadRepoFile("Services", "Timelapse", "TimelapseJobService.cs");

        Assert.Contains("OpenSceneImageHistoryAsync(TimelapseStageImage image)", page);
        Assert.Contains("ListSceneImageHistoryAsync(JobId, image.ProgressPercent", page);
        Assert.Contains("OpenFinalVideoHistoryAsync", page);
        Assert.Contains("ListFinalVideoHistoryAsync(JobId, currentUser)", page);
        Assert.Contains("LỊCH SỬ VIDEO", page);
        Assert.Contains("ListFinalVideoHistoryAsync(Guid jobId, CancellationToken", workflow);
        Assert.Contains("ListFinalVideoHistoryAsync(Guid jobId, CurrentUserSession currentUser", jobService);
        Assert.Contains("f.result_media_id::text = j.output_json->>'mediaId'", workflow);
        Assert.Contains("f.object_key = j.output_json->>'objectKey'", workflow);
        Assert.Contains("f.public_url = j.output_json->>'publicUrl'", workflow);
        Assert.Contains("COALESCE(f.completed_at, f.started_at, f.created_at)", workflow);
        Assert.DoesNotContain("f.version=(SELECT max(version)", workflow);
    }

    [Fact]
    public void TimelapseHistoryKeepsFailedAttemptsVisibleAndSelectionRequiresCompletedStatus()
    {
        var page = ReadRepoFile("Components", "Pages", "TimelapseJobDetail.razor");
        var workflow = ReadRepoFile("Services", "Timelapse", "TimelapseWorkflowService.cs");

        Assert.Contains("v.status='COMPLETED'", workflow);
        Assert.Contains("CanSelect = item.Status == TimelapseOperationStatuses.Completed && !item.IsSelected && !string.IsNullOrWhiteSpace(item.PublicUrl)", page);
        Assert.Contains("FROM timelapse.timelapse_final_outputs", workflow);
        Assert.Contains("f.error_message AS ErrorMessage", workflow);
    }

    private static string ReadRepoFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(parts).ToArray()));
}
