using System.Text.Json;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Timelapse;
using Xunit;

namespace TodoX.Web.Tests;

public class TimelapsePromptEditingTests
{
    [Theory]
    [InlineData(75, new[] { 75, 50, 25, 0 }, new[] { 1, 2, 3, 4 })]
    [InlineData(50, new[] { 50, 25, 0 }, new[] { 1, 2, 3 })]
    [InlineData(25, new[] { 25, 0 }, new[] { 1, 2 })]
    [InlineData(0, new[] { 0 }, new[] { 1 })]
    public void RerenderImpactPlanner_UsesFourSceneReverseDependencyGraph(
        int selectedProgress,
        int[] expectedImages,
        int[] expectedClips)
    {
        var impact = TimelapseRerenderImpactPlanner.Plan(4, selectedProgress);

        Assert.Equal(selectedProgress, impact.SelectedProgressPercent);
        Assert.Equal(expectedImages, impact.ImageProgressesToInvalidate);
        Assert.Equal(expectedClips, impact.VideoClipIndexesToInvalidate);
        Assert.True(impact.InvalidatesFinalOutput);
    }

    [Fact]
    public void RerenderImpactPlanner_RejectsOriginalImage()
    {
        Assert.Throws<InvalidOperationException>(() => TimelapseRerenderImpactPlanner.Plan(4, 100));
    }

    [Fact]
    public void PromptSnapshot_PreservesProfileAndStoresAuditableOverride()
    {
        const string original =
            """{"profileCode":"townhouse","profileJson":{"prompt":"Base construction prompt"},"capturedAtUtc":"2026-08-13T00:00:00Z"}""";

        var updated = TimelapsePromptSnapshot.WithCustomerOverride(original, "  Customer stage prompt  ");
        using var doc = JsonDocument.Parse(updated);

        Assert.Equal("townhouse", doc.RootElement.GetProperty("profileCode").GetString());
        Assert.Equal(
            "Base construction prompt",
            doc.RootElement.GetProperty("profileJson").GetProperty("prompt").GetString());
        Assert.Equal(
            "Customer stage prompt",
            doc.RootElement.GetProperty(TimelapsePromptSnapshot.CustomerOverrideProperty).GetString());
        Assert.True(doc.RootElement.TryGetProperty("customer_prompt_updated_at_utc", out _));
    }

    [Fact]
    public void PromptResolver_PrefersCustomerOverrideAndOtherwiseKeepsProfileBehavior()
    {
        var snapshot = new TimelapseJobSnapshot
        {
            ProfileCode = "townhouse",
            ProfileName = "Nhà phố"
        };
        var withOverride = TimelapsePromptSnapshot.WithCustomerOverride(
            """{"profileJson":{"prompt":"Base prompt"}}""",
            "Exact customer prompt");

        Assert.Equal(
            "Exact customer prompt",
            TimelapsePromptResolver.ResolveImagePrompt(snapshot, 75, withOverride));

        var fallback = TimelapsePromptResolver.ResolveImagePrompt(
            snapshot,
            75,
            """{"profileJson":{"prompt":"Base prompt"}}""");
        Assert.Contains("Base prompt", fallback);
        Assert.Contains("75%", fallback);
        Assert.Contains("Nhà phố", fallback);
    }

    [Fact]
    public void ImagePrompt_CompilesLandscapeProfilePhaseInsteadOfSendingRawProfileJson()
    {
        var snapshot = new TimelapseJobSnapshot
        {
            ProfileName = "Landscape construction",
            ProfileCode = "landscape"
        };
        var profileSnapshot = """
            {
              "ProfileJson": "{\"id\":27,\"enabled\":true,\"category\":\"landscape\",\"select_no\":71,\"phase_rules\":[{\"min_progress\":80,\"max_progress\":100,\"phase_goal\":\"finish the exterior landscape installation\",\"prompt_fragment\":\"Install the final paving and planting details.\",\"must_exist\":[\"finished path\"],\"must_not_exist\":[\"construction debris\"]}],\"continuity_rules\":{\"must_preserve\":[\"same site layout\"]}}"
            }
            """;

        var prompt = TimelapsePromptResolver.ResolveImagePrompt(snapshot, 80, profileSnapshot);

        Assert.Contains("finish the exterior landscape installation", prompt, StringComparison.Ordinal);
        Assert.Contains("Install the final paving and planting details", prompt, StringComparison.Ordinal);
        Assert.Contains("Must exist: finished path", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"id\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"select_no\"", prompt, StringComparison.Ordinal);
        TimelapsePromptResolver.ValidateProviderPrompt(prompt);
    }

    [Fact]
    public void ImagePromptValidation_RejectsRawProfileMetadataBeforeProviderSubmit()
    {
        var exception = Assert.Throws<TimelapseInvalidCompiledPromptException>(() =>
            TimelapsePromptResolver.ValidateProviderPrompt("{\"id\":27,\"select_no\":71,\"category\":\"landscape\"}"));

        Assert.Equal(TimelapsePromptResolver.InvalidCompiledPromptErrorCode, exception.ErrorCode);
    }

    [Fact]
    public void TimelapseImageClaim_RequiresUsableCompletedDependency()
    {
        var repository = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("d.status='COMPLETED'", repository, StringComparison.Ordinal);
        Assert.Contains("d.result_media_id IS NOT NULL", repository, StringComparison.Ordinal);
        Assert.Contains("NULLIF(d.public_url,'') IS NOT NULL OR NULLIF(d.object_key,'') IS NOT NULL", repository, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptEditingPolicy_BlocksRenderingButAllowsStoppedStages()
    {
        Assert.False(TimelapsePromptSnapshot.CanEdit(TimelapseOperationStatuses.Rendering));
        Assert.True(TimelapsePromptSnapshot.CanEdit(TimelapseOperationStatuses.Waiting));
        Assert.True(TimelapsePromptSnapshot.CanEdit(TimelapseOperationStatuses.Failed));
        Assert.True(TimelapsePromptSnapshot.CanEdit(TimelapseOperationStatuses.Completed));
        Assert.True(TimelapsePromptSnapshot.CanEdit(TimelapseOperationStatuses.Invalidated));
    }

    [Fact]
    public void PromptEditing_SourceContracts_CoverUiOwnershipVersioningAndCurrentAttemptTime()
    {
        var page = ReadSource("TodoX.Web", "Components", "Pages", "TimelapseJobDetail.razor");
        var promptDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "TimelapseImagePromptDialog.razor");
        var confirmDialog = ReadSource("TodoX.Web", "Components", "Dialogs", "TimelapseRerenderConfirmDialog.razor");
        var jobService = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseJobService.cs");
        var workflow = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkflowService.cs");
        var worker = ReadSource("TodoX.Web", "Services", "Timelapse", "TimelapseWorkerRepository.cs");

        Assert.Contains("Icons.Material.Filled.EditNote", page, StringComparison.Ordinal);
        Assert.Contains("Xem / chỉnh sửa prompt", page, StringComparison.Ordinal);
        Assert.Contains("if (!image.IsOriginal)", page, StringComparison.Ordinal);
        Assert.Contains("OpenImagePromptAsync", page, StringComparison.Ordinal);
        Assert.Contains("ConfirmImageRerenderAsync", page, StringComparison.Ordinal);
        Assert.Contains("TimelapseRerenderImpactPlanner.Plan", page, StringComparison.Ordinal);
        Assert.Contains("Bắt đầu:", page, StringComparison.Ordinal);
        Assert.Contains("Chưa bắt đầu", page, StringComparison.Ordinal);
        Assert.Contains("Hoàn thành:", page, StringComparison.Ordinal);
        Assert.Contains("image.StartedAt", page, StringComparison.Ordinal);
        Assert.Contains("image.CompletedAt", page, StringComparison.Ordinal);
        Assert.Contains("CanRetryImage", page, StringComparison.Ordinal);
        Assert.Contains("_job?.Workflow.HasActiveOperations != true", page, StringComparison.Ordinal);
        Assert.Contains("timelapse-action-progress", page, StringComparison.Ordinal);

        Assert.Contains("Prompt ảnh @Image.ProgressPercent%", promptDialog, StringComparison.Ordinal);
        Assert.Contains("Lưu prompt", promptDialog, StringComparison.Ordinal);
        Assert.Contains("Lưu &amp; Render lại", promptDialog, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@IsRendering\"", promptDialog, StringComparison.Ordinal);
        Assert.Contains("Prompt mới chỉ có hiệu lực khi bạn Render lại ảnh này.", promptDialog, StringComparison.Ordinal);

        Assert.Contains("Ảnh tạo lại", confirmDialog, StringComparison.Ordinal);
        Assert.Contains("Video bị ảnh hưởng", confirmDialog, StringComparison.Ordinal);
        Assert.Contains("Video hoàn chỉnh", confirmDialog, StringComparison.Ordinal);
        Assert.Contains("autofocus", confirmDialog, StringComparison.Ordinal);

        Assert.Contains("RequireOwnedAsync(jobId, currentUser", jobService, StringComparison.Ordinal);
        Assert.Contains("UpdateImagePromptAsync(", jobService, StringComparison.Ordinal);
        Assert.Contains("HydrateImagePrompts", jobService, StringComparison.Ordinal);

        Assert.Contains("tenant_id=@tenant", workflow, StringComparison.Ordinal);
        Assert.Contains("job_id=@jobId", workflow, StringComparison.Ordinal);
        Assert.Contains("id=@imageStageId", workflow, StringComparison.Ordinal);
        Assert.Contains("stage.IsOriginal || stage.ProgressPercent >= 100", workflow, StringComparison.Ordinal);
        Assert.Contains("!TimelapsePromptSnapshot.CanEdit(stage.Status)", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_IMAGE_PROMPT_UPDATED", workflow, StringComparison.Ordinal);
        Assert.Contains("TIMELAPSE_IMAGE_RERENDER_REQUESTED", workflow, StringComparison.Ordinal);
        Assert.Contains("prompt_snapshot_json=CAST(@promptSnapshotJson AS jsonb)", workflow, StringComparison.Ordinal);
        Assert.Contains("if (!rerender)", workflow, StringComparison.Ordinal);
        Assert.Contains("prompt_snapshot_json::text AS PromptSnapshotJson", workflow, StringComparison.Ordinal);
        Assert.Contains("started_at AS StartedAt", workflow, StringComparison.Ordinal);
        Assert.Contains("completed_at AS CompletedAt", workflow, StringComparison.Ordinal);
        Assert.Contains("started_at=NULL", workflow, StringComparison.Ordinal);
        Assert.Contains("started_at=now()", workflow, StringComparison.Ordinal);
        Assert.Contains("active_attempt=active_attempt+1", workflow, StringComparison.Ordinal);
        Assert.Contains("'RENDERING', prompt_snapshot_json", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE timelapse.timelapse_image_stage_versions SET prompt_snapshot_json", workflow, StringComparison.Ordinal);

        Assert.Contains("AdvanceAfterImageCompletedAsync", worker, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO timelapse.timelapse_image_stage_versions", worker, StringComparison.Ordinal);
        Assert.Contains("'RENDERING', prompt_snapshot_json", worker, StringComparison.Ordinal);
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TodoX.Dashboard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
