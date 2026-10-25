using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using TodoX.Web.Services;
using TodoX.Web.Services.Platform;
using TodoX.Web.Components.Pages;
using TodoX.Web.Components.Pages.RVideo;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class Scene07RetryControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static SceneVideoUiState State(string status = "failed", bool jobTask = false, bool versionTask = false,
        SceneVideoSubmitBlockReason block = SceneVideoSubmitBlockReason.None, long? generation = null)
    {
        var id = Guid.NewGuid();
        return new(null, new RenderJobDto { Id = id, Status = status, ProviderTaskId = jobTask ? "task" : null },
            new SceneVideoVersionDto { Id = Guid.NewGuid(), SceneId = 42, RenderJobId = id, Status = status,
                ProviderVideoIdBase = versionTask ? "base" : null }, block, status == "pending_reconciliation", generation);
    }

    [Fact]
    public void FailedWithoutTaskStillNeedsNewOperationBecauseLegacyRetryWouldTransferImmutableOwner()
    {
        var state = State();
        Assert.Contains("job sở hữu", state.RetryDisabledReason);
        Assert.True(state.RequiresExplicitOperation);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FailedWithProviderIdentityAlwaysOffersSupersedeEvenWithoutHead(bool jobTask, bool versionTask)
    {
        var state = State(jobTask: jobTask, versionTask: versionTask);
        Assert.NotEmpty(state.RetryDisabledReason);
        Assert.True(state.RequiresExplicitOperation);
        foreach (var (component, field, method) in new (object, string, string)[] {
            (new RenderVideoJobs(), "_sceneVideoUiStates", "NeedsExplicitVideoOperation"),
            (new RVideoV2Preview(), "_videoStates", "NeedsExplicitOperation") })
        {
            var states = (Dictionary<long, SceneVideoUiState>)component.GetType().GetField(field, Private)!.GetValue(component)!;
            states[42] = state;
            Assert.Equal(true, component.GetType().GetMethod(method, Private)!.Invoke(component, [new VideoProjectSceneDto { Id = 42 }]));
        }
    }

    [Fact]
    public void PendingAndUnresolvedIntentsNeverAllowOldRetry()
    {
        Assert.True(State("pending_reconciliation").RequiresExplicitOperation);
        foreach (var block in Enum.GetValues<SceneVideoSubmitBlockReason>().Where(x => x != SceneVideoSubmitBlockReason.None))
        {
            var state = State(block: block);
            Assert.NotEmpty(state.RetryDisabledReason);
            Assert.True(state.RequiresExplicitOperation);
        }
    }

    [Fact]
    public void HeadedOperationCannotUseLegacyEnqueueOrRetry()
    {
        Assert.NotEmpty(State(generation: 0).RetryDisabledReason);
        Assert.True(State(generation: 1).RequiresExplicitOperation);
    }

    [Fact]
    public void SelectedHistoricalProviderTaskDoesNotContaminateCurrentFailedAttempt()
    {
        var state = State() with { SelectedVersion = new() { Status = "completed", ProviderTaskId = "historical-task", IsSelected = true } };
        Assert.False(state.HasProviderIdentity);
        Assert.Contains("job sở hữu", state.RetryDisabledReason);
        Assert.True(state.RequiresExplicitOperation);
        var component = new RenderVideoJobs();
        ((Dictionary<long, SceneVideoUiState>)typeof(RenderVideoJobs).GetField("_sceneVideoUiStates", Private)!.GetValue(component)!)[42] = state;
        Assert.Equal(VideoSceneStatuses.Failed, typeof(RenderVideoJobs).GetMethod("ResolveVideoSceneStatus", Private)!.Invoke(component,
            [new VideoProjectSceneDto { Id = 42, Status = VideoSceneStatuses.VideoRendering }]));
    }

    [Fact]
    public void MissingOrMismatchedVersionFailsClosed()
    {
        Assert.NotEmpty((State() with { LatestJobVersion = null }).RetryDisabledReason);
        var state = State();
        state.LatestJobVersion!.RenderJobId = Guid.NewGuid();
        Assert.NotEmpty(state.RetryDisabledReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VideoFailureDoesNotBlockImageRerender(bool completedImage)
    {
        var scene = new VideoProjectSceneDto { Id = 42, Status = VideoSceneStatuses.Failed, StaticImageUrl = completedImage ? "/old.png" : null };
        var legacy = new RenderVideoJobs();
        ((Dictionary<long, SceneVideoUiState>)typeof(RenderVideoJobs).GetField("_sceneVideoUiStates", Private)!.GetValue(legacy)!)[42] = State(jobTask: true);
        Assert.Equal("", typeof(RenderVideoJobs).GetMethod("SceneImageDisabledReason", Private)!.Invoke(legacy, [scene]));
        var v2 = new RVideoV2Preview();
        Assert.Equal(false, typeof(RVideoV2Preview).GetMethod("ImageBlocked", Private)!.Invoke(v2, [scene]));
    }

    [Fact]
    public void SharedReferenceAndRealImageActivityBlockOnlyImageAction()
    {
        var scene = new VideoProjectSceneDto { Id = 42 };
        var legacy = new RenderVideoJobs();
        typeof(RenderVideoJobs).GetField("_useReferenceImageForAllScenes", Private)!.SetValue(legacy, true);
        Assert.Contains("tham chiếu chung", (string)typeof(RenderVideoJobs).GetMethod("SceneImageDisabledReason", Private)!.Invoke(legacy, [scene])!);
        typeof(RenderVideoJobs).GetField("_useReferenceImageForAllScenes", Private)!.SetValue(legacy, false);
        ((Dictionary<long, IReadOnlyList<SceneImageVersionDto>>)typeof(RenderVideoJobs).GetField("_imageVersions", Private)!.GetValue(legacy)!)[42] =
            [new() { Status = "processing" }];
        Assert.NotEqual("", typeof(RenderVideoJobs).GetMethod("SceneImageDisabledReason", Private)!.Invoke(legacy, [scene]));
        var v2 = new RVideoV2Preview();
        ((HashSet<long>)typeof(RVideoV2Preview).GetField("_activeImageScenes", Private)!.GetValue(v2)!).Add(42);
        Assert.Equal(true, typeof(RVideoV2Preview).GetMethod("ImageBlocked", Private)!.Invoke(v2, [scene]));
    }

    [Theory]
    [InlineData("processing", true)]
    [InlineData("queued", true)]
    [InlineData("pending_reconciliation", false)]
    [InlineData("failed", false)]
    public void V2PollsRunningOperationsButNotTerminalOrManualReviewHistory(string status, bool expected)
    {
        using var v2 = new RVideoV2Preview();
        var states = (Dictionary<long, SceneVideoUiState>)typeof(RVideoV2Preview).GetField("_videoStates", Private)!.GetValue(v2)!;
        states[42] = State(status) with { HasActiveJob = status is "processing" or "queued" or "pending_reconciliation" };
        Assert.Equal(expected, typeof(RVideoV2Preview).GetMethod("HasRunningOperation", Private)!.Invoke(v2, []));
        ((HashSet<long>)typeof(RVideoV2Preview).GetField("_activeImageScenes", Private)!.GetValue(v2)!).Add(42);
        Assert.Equal(true, typeof(RVideoV2Preview).GetMethod("HasRunningOperation", Private)!.Invoke(v2, []));
    }

    [Fact]
    public void ActionDiagnosticsKeepApplicationCodeAndHideRawProviderData()
    {
        var textType = typeof(RVideoV2Preview).Assembly.GetType("TodoX.Web.Components.Pages.RVideo.RVideoUserText")!;
        var method = textType.GetMethod("ActionError")!;
        var message = (string)method.Invoke(null, ["RVIDEO_VIDEO_IMAGE_REQUIRED", "secret-token https://signed.example/?token=private"])!;
        Assert.StartsWith("RVIDEO_VIDEO_IMAGE_REQUIRED:", message);
        Assert.DoesNotContain("secret", message);
        Assert.DoesNotContain("signed.example", message);
        var invalid = (string)method.Invoke(null, ["bad\nsecret", "secret"])!;
        Assert.StartsWith("RVIDEO_ACTION_FAILED:", invalid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task V2ImageDoubleClickUsesOneCanonicalActionAndKeepsExistingImage(bool completedImage)
    {
        var scene = new VideoProjectSceneDto { Id = 42, ProjectId = 1, Status = "failed", ImagePrompt = "image prompt",
            StaticImageUrl = completedImage ? "/old.png" : null };
        var auth = new AuthStateService(null!, null!, NullLogger<AuthStateService>.Instance);
        typeof(AuthStateService).GetProperty(nameof(AuthStateService.CurrentUser))!.SetValue(auth, SceneVideoOperationDraftTests.Actor());
        using var component = new RVideoV2Preview();
        component.JobView = new RVideoJobView {
            CoreJob = new CoreJobView(Guid.NewGuid(), null, "rvideo", null, null, "failed", "web", null, null, null,
                0, 0, 0, "pending", null, default, default, null, null, DateTime.UtcNow, null, null),
            Project = new VideoProjectDto { Id = 1, Scenes = [scene], OriginalPrompt = "" } };
        var imageAction = new ImageAction();
        var dialog = DispatchProxy.Create<IDialogService, SceneVideoSupersedeUiTests.DialogProxy>();
        var dialogState = (SceneVideoSupersedeUiTests.DialogProxy)(object)dialog;
        dialogState.Answer = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Inject(string name, object value) => typeof(RVideoV2Preview).GetProperty(name, Private)!.SetValue(component, value);
        Inject("AuthState", auth); Inject("SceneImageRender", imageAction);
        Inject("SceneVersions", DispatchProxy.Create<ISceneMediaVersioningService, ImageVersionsProxy>());
        Inject("DialogService", dialog);
        Inject("Snackbar", DispatchProxy.Create<ISnackbar, SceneVideoSupersedeUiTests.SnackbarProxy>());
        Inject("Logger", NullLogger<RVideoV2Preview>.Instance);
        var handler = typeof(RVideoV2Preview).GetMethod("HandleRerenderImageAsync", Private)!;
        var first = (Task)handler.Invoke(component, [scene])!;
        await (Task)handler.Invoke(component, [scene])!;
        Assert.Equal(0, imageAction.Enqueues);
        dialogState.Answer.SetResult(true);
        await first;
        Assert.Equal(1, imageAction.Enqueues);
        Assert.Equal(completedImage ? "/old.png" : null, scene.StaticImageUrl);
    }

    public class ImageVersionsProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method!.Name == "HasActiveImageVersionAsync" ? Task.FromResult(false) : throw new InvalidOperationException(method.Name);
    }

    private sealed class ImageAction : IRVideoSceneImageRenderAction
    {
        public int Enqueues;
        public Task<RVideoSceneImageRenderEstimate> EstimateAsync(RVideoSceneImageRenderRequest request, CancellationToken ct = default)
            => Task.FromResult(new RVideoSceneImageRenderEstimate(1, 100, 99, 0, true));
        public Task<RVideoSceneImageRenderResult> EnqueueRerenderAsync(RVideoSceneImageRenderRequest request, CurrentUserSession user, CancellationToken ct = default)
        {
            Enqueues++;
            return Task.FromResult(RVideoSceneImageRenderResult.Ok(Guid.NewGuid(), Guid.NewGuid(), "new-image-request"));
        }
    }
}
