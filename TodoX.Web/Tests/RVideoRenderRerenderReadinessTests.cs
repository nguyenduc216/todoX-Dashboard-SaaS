using System.Reflection;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;
using TodoX.Web.Components.Pages;
using TodoX.Web.Components.Pages.RVideo;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// Runs production decision/UI methods, without a database or provider connection.
// The database transaction/worker integration is deliberately not simulated by these tests.
public sealed class RVideoRenderRerenderReadinessTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Project188PreHttpHistoryBlocksUntilJobsAndVersionsAreSettled(bool reconciled, bool blocked)
    {
        var reason = SceneSubmitGuard.Evaluate([], [new(reconciled ? "failed" : "pending_reconciliation",
            "SCENE_VIDEO_MANUAL_REVIEW_REQUIRED", true, false)],
            [new(reconciled ? "failed" : "queued", null, false, false)]);
        Assert.Equal(!blocked, SceneSubmitGuard.PermitsSubmit(reason));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Scene05FailedConfirmedTaskRequiresAuditedTerminalOutcome(bool auditedTerminalFailure, bool blocked)
    {
        var reason = SceneSubmitGuard.Evaluate(
            [new("CONFIRMED_PROVIDER_TASK", true, false, true, false, true, auditedTerminalFailure)],
            [new("failed", "provider_failure", true, true)], [new("failed", null, false, true)]);
        Assert.Equal(!blocked, SceneSubmitGuard.PermitsSubmit(reason));
    }

    [Theory]
    [InlineData("SUBMIT_IN_FLIGHT")]
    [InlineData("AMBIGUOUS_SUBMIT")]
    [InlineData("MANUAL_REVIEW_REQUIRED")]
    public void UnresolvedIntentStillBlocksWithCompletedSelectedVideo(string state)
        => Assert.Equal(SceneVideoSubmitBlockReason.ExistingIntentUnresolved,
            SceneSubmitGuard.Evaluate([new(state, true, false, true, false)], [], [new("completed", null, false, true)]));

    [Fact]
    public void SettledConfirmedTaskCannotResubmitItsOwnOperation()
        => Assert.True(SceneVideoSubmitIntentFact.BlocksSceneSubmit(
            new("CONFIRMED_PROVIDER_TASK", true, true, true, false, true, true)));

    [Theory]
    [InlineData("queued")]
    [InlineData("pending_reconciliation")]
    public void QueuedVersionRetainsCandidateZeroAcrossWorkerClaims(string status)
    {
        var methods = typeof(SceneVideoWorkerHandler).GetMethods(BindingFlags.Static | BindingFlags.NonPublic);
        var method = methods.Single(m => m.Name == "ResolveNextAttemptIndex" && m.GetParameters().Length == 3);
        var version = new SceneVideoVersionDto { LogicalRequestId = "new-operation", Status = status };
        var candidateType = typeof(SceneVideoWorkerHandler).GetNestedType("ResolvedFallbackCandidate", BindingFlags.NonPublic)!;
        Assert.Equal(0, method.Invoke(null, ["new-operation", new[] { version }, Array.CreateInstance(candidateType, 0)]));
    }

    [Theory]
    [InlineData(false, false, SceneVideoSubmitBlockReason.None, false)]
    [InlineData(true, false, SceneVideoSubmitBlockReason.None, false)]
    [InlineData(true, true, SceneVideoSubmitBlockReason.None, true)]
    [InlineData(true, false, SceneVideoSubmitBlockReason.ExistingIntentUnresolved, true)]
    [InlineData(true, false, SceneVideoSubmitBlockReason.SceneJobInFlightOrManualReview, true)]
    [InlineData(true, false, SceneVideoSubmitBlockReason.SceneVersionInFlightOrReconciliation, true)]
    public void V2ControlsUseOperationBlockerIndependentlyOfSelectedCompletedVideo(
        bool completedSelected, bool active, SceneVideoSubmitBlockReason reason, bool expectedBlocked)
    {
        var component = new RVideoV2Preview();
        var field = typeof(RVideoV2Preview).GetField("_videoStates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var states = (Dictionary<long, SceneVideoUiState>)field.GetValue(component)!;
        var selected = completedSelected ? new SceneVideoVersionDto { Id = Guid.NewGuid(), Status = "completed", IsSelected = true } : null;
        states[42] = new(selected, new RenderJobDto { Status = active ? "pending_reconciliation" : "failed" }, null, reason, active);
        var method = typeof(RVideoV2Preview).GetMethod("VideoBlocked", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(expectedBlocked, method.Invoke(component, [new VideoProjectSceneDto { Id = 42 }]));
        Assert.Same(selected, states[42].SelectedVersion);
        if (selected is not null) Assert.True(selected.IsSelected);
    }

    [Fact]
    public void V2MissingSnapshotFailsClosed()
    {
        var method = typeof(RVideoV2Preview).GetMethod("VideoBlocked", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(true, method.Invoke(new RVideoV2Preview(), [new VideoProjectSceneDto { Id = 42 }]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyRerenderKeepsSelectionAndBlocksOnlyUnresolvedOperation(bool unresolved)
    {
        var type = typeof(RenderVideoJobs);
        var component = new RenderVideoJobs();
        type.GetProperty("VideoOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, new VideoOptionsMonitor());
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var scene = new VideoProjectSceneDto { Id = 42, StaticImageUrl = "/uploads/test.png" };
        var selected = new SceneVideoVersionDto { Id = Guid.NewGuid(), Status = "completed", IsSelected = true };
        var states = (Dictionary<long, SceneVideoUiState>)type.GetField("_sceneVideoUiStates", flags)!.GetValue(component)!;
        states[42] = new(selected, new RenderJobDto { Status = unresolved ? "pending_reconciliation" : "failed" }, null,
            unresolved ? SceneVideoSubmitBlockReason.ExistingIntentUnresolved : SceneVideoSubmitBlockReason.None, unresolved);
        var draftType = type.GetNestedType("SceneEditorState", BindingFlags.NonPublic)!;
        var draft = Activator.CreateInstance(draftType)!;
        draftType.GetProperty("MotionPrompt")!.SetValue(draft, "Animate the existing scene.");
        type.GetField("_aspectRatio", flags)!.SetValue(component, "9:16");
        type.GetField("_resolution", flags)!.SetValue(component, "720P");
        var providerField = type.GetField("_defaultImageToVideoProvider", flags)!;
        providerField.SetValue(component, Activator.CreateInstance(providerField.FieldType));
        var canRender = type.GetMethod("CanRerenderSceneVideo", flags)!;
        Assert.Equal(!unresolved, canRender.Invoke(component, [scene, draft]));
        Assert.Same(selected, states[42].SelectedVersion);
        Assert.True(selected.IsSelected);
    }

    [Fact]
    public async Task DirectVideoActionRejectsPendingVersionBeforeEstimateOrEnqueue()
    {
        var versions = DispatchProxy.Create<ISceneMediaVersioningService, PendingVersions>();
        var action = new RVideoSceneVideoRenderAction(null!, null!, null!, null!, null!, versions, null!, null!);
        var result = await action.EnqueueRerenderAsync(new(1, new VideoProjectSceneDto { Id = 42 }, new()), new CurrentUserSession());
        Assert.False(result.Success);
        Assert.Equal("RVIDEO_SUBMIT_RECONCILIATION_PENDING", result.ErrorCode);
    }

    [Fact]
    public async Task ImageRerenderRejectsEmptyPromptBeforeAnyProviderOrEnqueue()
    {
        var action = new RVideoSceneImageRenderAction(null!, null!, null!, null!, null!, null!, null!);
        var result = await action.EnqueueRerenderAsync(new(1, new VideoProjectSceneDto { Id = 42 }, new(), "9:16"), new CurrentUserSession());
        Assert.False(result.Success);
        Assert.Equal("SCENE_IMAGE_PROMPT_EMPTY", result.ErrorCode);
    }

    [Fact]
    public void ImageUserRerenderUsesFreshLogicalOperation()
    {
        var first = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", 42, null);
        var second = SceneImageRenderService.BuildLogicalRequestId("render_job_scene_image_rerender", 42, null);
        Assert.NotEqual(first, second);
    }

    public class PendingVersions : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(ISceneMediaVersioningService.ListSceneVideoVersionsAsync))
                return Task.FromResult<IReadOnlyList<SceneVideoVersionDto>>([new() { Status = "pending_reconciliation" }]);
            throw new InvalidOperationException("Unexpected dependency: " + method?.Name);
        }
    }

    private sealed class VideoOptionsMonitor : IOptionsMonitor<VideoRenderOptions>
    {
        public VideoRenderOptions CurrentValue { get; } = new();
        public VideoRenderOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<VideoRenderOptions, string?> listener) => null;
    }

    [Fact]
    public void NewVideoParentCreatesNewChildLogicalIdentity()
    {
        var first = SceneVideoRenderHandler.BuildLogicalRequestId(Guid.NewGuid(), 42);
        var next = SceneVideoRenderHandler.BuildLogicalRequestId(Guid.NewGuid(), 42);
        Assert.NotEqual(first, next);
        Assert.EndsWith("-scene-42", next);
    }
}
