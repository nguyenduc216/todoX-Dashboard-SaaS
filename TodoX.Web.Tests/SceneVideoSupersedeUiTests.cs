using Xunit;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Tests;

public sealed class SceneVideoSupersedeUiTests
{
    [Fact]
    public void UnknownSubmissionRemainsActiveWithoutTaskId()
    {
        var method = typeof(VideoRenderEligibilityService).GetMethod("IsSceneVideoVersionActivelyRunning",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var version = new SceneVideoVersionDto { Status = "pending_reconciliation" };
        Assert.True((bool)method.Invoke(null, new object[] { version, false })!);
    }

    [Fact]
    public void NewParentJobCreatesDistinctLogicalRequestWithoutReusingOldVersion()
    {
        Assert.NotEqual(SceneVideoRenderHandler.BuildLogicalRequestId(Guid.NewGuid(), 7),
            SceneVideoRenderHandler.BuildLogicalRequestId(Guid.NewGuid(), 7));
        var source = File.ReadAllText(Path.Combine(Root, "Services/VideoRender/SceneMediaVersioningService.cs"));
        var start = source.IndexOf("public async Task<SceneVideoVersionDto> CreateQueuedSceneVideoVersionAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task", start + 20, StringComparison.Ordinal);
        var create = source[start..end];
        Assert.Contains("INSERT INTO video_render.scene_video_versions", create);
        Assert.Contains("max(version_number), 0) + 1", create);
        Assert.DoesNotContain("DELETE FROM", create);
        Assert.DoesNotContain("is_selected=false", create);
    }

    [Theory]
    [InlineData(VideoRenderEligibilityStatus.Eligible, PointBillingIntent.InitialRender, true)]
    [InlineData(VideoRenderEligibilityStatus.AlreadyCompleted, PointBillingIntent.InitialRender, false)]
    [InlineData(VideoRenderEligibilityStatus.AlreadyCompleted, PointBillingIntent.UserRerender, true)]
    [InlineData(VideoRenderEligibilityStatus.AlreadyActive, PointBillingIntent.UserRerender, false)]
    [InlineData(VideoRenderEligibilityStatus.MissingImage, PointBillingIntent.UserRerender, false)]
    [InlineData(VideoRenderEligibilityStatus.InvalidPrompt, PointBillingIntent.UserRerender, false)]
    public void OnlyExplicitRerenderAcceptsCompletedOutput(VideoRenderEligibilityStatus status, PointBillingIntent intent, bool expected)
        => Assert.Equal(expected, RVideoRerenderRequestPolicy.CanProceed(status, intent));

    [Fact]
    public void SavedCompositeModelKeyPreservesModelModeAndDuration()
    {
        foreach (var policy in RVideoVideoModelPolicy.Models)
        {
            var metadata = new ScenePromptMetadata();
            metadata.Extra["video_model"] = RVideoSceneVideoModelOptionsService.BuildKey(policy.ProviderCode, policy.Model, policy.Mode);
            metadata.Extra["video_duration"] = "12";
            var input = new SceneVideoRenderInput();
            Assert.True(RVideoRerenderRequestPolicy.ApplySavedOverride(metadata, input));
            Assert.True(input.ManualOverride);
            Assert.Equal(policy.Model, input.RequestedModelCode);
            Assert.Equal(policy.Mode, input.RequestedMode);
            Assert.Equal(12, input.RequestedDurationSeconds);
            Assert.Null(input.ExistingSceneVideoVersionId);
            Assert.False(input.ReuseExistingSceneVideoVersion);
        }
    }

    [Theory]
    [InlineData("unknown|unknown|unknown", "12")]
    [InlineData(null, "0")]
    [InlineData(null, "invalid")]
    public void InvalidSavedOverrideFailsClosed(string? key, string duration)
    {
        var policy = RVideoVideoModelPolicy.GetInitial();
        var metadata = new ScenePromptMetadata();
        metadata.Extra["video_model"] = key ?? RVideoSceneVideoModelOptionsService.BuildKey(policy.ProviderCode, policy.Model, policy.Mode);
        metadata.Extra["video_duration"] = duration;
        Assert.False(RVideoRerenderRequestPolicy.ApplySavedOverride(metadata, new SceneVideoRenderInput()));
    }

    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TodoX.Web"));

    [Fact]
    public void V2ExplicitNewVersionCarriesSavedSettingsAndAttemptSnapshot()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Components/Pages/RVideo/RVideoV2Preview.razor"));
        Assert.Contains("hasVideoBaseline && hasAnyImageSource", source);
        Assert.Contains("WithRenderSettings(metadata)", source);
        Assert.Contains("RVideoRules.ResolveRenderSettings(JobView!.Project.OriginalPrompt)", source);
        Assert.Contains("_latestVideoVersions.GetValueOrDefault(scene.Id)?.Id", source);
        Assert.Contains("_latestVideoVersions.GetValueOrDefault(scene.Id)?.RenderJobId", source);
    }

    [Fact]
    public void LegacyRerenderHasSeparateCompletedVersionPermissionAndVisibleMenuAction()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Components/Pages/RenderVideoJobs.razor"));
        Assert.Contains("Tạo lượt video mới", source);
        Assert.Contains("allowCompletedVersion: true", source);
        Assert.Contains("!allowCompletedVersion && HasCompletedVideoVersion(scene)", source);
    }

    [Fact]
    public void ExplicitRerenderCanPassCompletedEligibilityAtBothSubmissionBoundaries()
    {
        foreach (var file in new[] { "RVideoSceneVideoRenderAction.cs", "SceneVideoRenderHandler.cs" })
        {
            var source = File.ReadAllText(Path.Combine(Root, "Services/VideoRender", file));
            Assert.Contains("RVideoRerenderRequestPolicy.CanProceed", source);
        }
    }
}
