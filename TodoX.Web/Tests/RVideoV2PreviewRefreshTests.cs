using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Components.Pages.RVideo;
using TodoX.Web.Models;
using TodoX.Web.Services;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

// RVideo V2 UX: refreshes update scene state in place and a poll refresh only re-queries scenes with work in flight.
public sealed class RVideoV2PreviewRefreshTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public class CountingVersions : DispatchProxy
    {
        public readonly List<long> StateQueries = new();
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetSceneVideoUiStateAsync" => Query((long)args![0]!),
            "HasActiveImageVersionAsync" => Task.FromResult(false),
            _ => throw new InvalidOperationException(method.Name)
        };
        private Task<SceneVideoUiState> Query(long sceneId)
        {
            StateQueries.Add(sceneId);
            return Task.FromResult(new SceneVideoUiState(null, new RenderJobDto { Id = Guid.NewGuid(), Status = "rendering" }, null,
                SceneVideoSubmitBlockReason.None, HasActiveJob: true));
        }
    }

    [Fact]
    public async Task PollRefreshRequeriesOnlyBusyScenesAndKeepsOtherStates()
    {
        var auth = new AuthStateService(null!, null!, NullLogger<AuthStateService>.Instance);
        typeof(AuthStateService).GetProperty(nameof(AuthStateService.CurrentUser))!.SetValue(auth, SceneVideoOperationDraftTests.Actor());
        var versions = DispatchProxy.Create<ISceneMediaVersioningService, CountingVersions>();
        using var component = new RVideoV2Preview();
        component.JobView = new RVideoJobView
        {
            CoreJob = new CoreJobView(Guid.NewGuid(), null, "rvideo", null, null, "rendering", "web", null, null, null,
                0, 0, 0, "pending", null, default, default, null, null, DateTime.UtcNow, null, null),
            Project = new VideoProjectDto { Id = 1, OriginalPrompt = "", Scenes =
            [
                new VideoProjectSceneDto { Id = 1, SceneIndex = 1, Status = VideoSceneStatuses.VideoReady },
                new VideoProjectSceneDto { Id = 2, SceneIndex = 2, Status = VideoSceneStatuses.VideoRendering },
            ] }
        };
        typeof(RVideoV2Preview).GetProperty("AuthState", Private)!.SetValue(component, auth);
        typeof(RVideoV2Preview).GetProperty("SceneVersions", Private)!.SetValue(component, versions);
        typeof(RVideoV2Preview).GetProperty("Logger", Private)!.SetValue(component, NullLogger<RVideoV2Preview>.Instance);

        var states = (Dictionary<long, SceneVideoUiState>)typeof(RVideoV2Preview).GetField("_videoStates", Private)!.GetValue(component)!;
        var readyState = new SceneVideoUiState(null, new RenderJobDto { Id = Guid.NewGuid(), Status = "completed" }, null,
            SceneVideoSubmitBlockReason.None, HasActiveJob: false);
        states[1] = readyState;
        states[2] = readyState with { HasActiveJob = true };
        states[99] = readyState; // scene deleted since the last load

        typeof(RVideoV2Preview).GetField("_pollRefresh", Private)!.SetValue(component, true);
        await (Task)typeof(RVideoV2Preview).GetMethod("OnParametersSetAsync", Private)!.Invoke(component, [])!;

        Assert.Equal([2L], ((CountingVersions)(object)versions).StateQueries);
        Assert.Same(readyState, states[1]);          // untouched, never cleared
        Assert.True(states[2].HasActiveJob);         // refreshed in place
        Assert.False(states.ContainsKey(99));        // removed scenes are dropped
        Assert.False((bool)typeof(RVideoV2Preview).GetField("_pollRefresh", Private)!.GetValue(component)!);
    }

    [Fact]
    public void UnloadedSceneShowsLoadingInsteadOfRetryBlocked()
    {
        using var component = new RVideoV2Preview();
        var status = (string)typeof(RVideoV2Preview).GetMethod("VideoStatus", Private)!.Invoke(component, [new VideoProjectSceneDto { Id = 5 }])!;
        Assert.Equal("Đang tải…", status);
        // Controls stay disabled until the scene state is known.
        Assert.Equal(true, typeof(RVideoV2Preview).GetMethod("VideoBlocked", Private)!.Invoke(component, [new VideoProjectSceneDto { Id = 5 }]));
    }
    [Fact]
    public void BusyScenesShowSpinnerLabelAndIdleOrManualReviewScenesDoNot()
    {
        using var component = new RVideoV2Preview();
        var states = (Dictionary<long, SceneVideoUiState>)typeof(RVideoV2Preview).GetField("_videoStates", Private)!.GetValue(component)!;
        var images = (HashSet<long>)typeof(RVideoV2Preview).GetField("_activeImageScenes", Private)!.GetValue(component)!;
        var busyLabel = typeof(RVideoV2Preview).GetMethod("BusyLabel", Private)!;
        string? Label(long id) => (string?)busyLabel.Invoke(component, [new VideoProjectSceneDto { Id = id, Status = VideoSceneStatuses.ImageReady }]);
        var idle = new SceneVideoUiState(null, null, null, SceneVideoSubmitBlockReason.None, HasActiveJob: false);

        states[1] = idle with { LatestJob = new RenderJobDto { Id = Guid.NewGuid(), Status = "rendering" }, HasActiveJob = true };
        states[2] = idle;
        images.Add(2);
        states[3] = idle;
        states[4] = idle with { LatestJob = new RenderJobDto { Id = Guid.NewGuid(), Status = "pending_reconciliation" }, HasActiveJob = true };

        Assert.Equal("Đang tạo video…", Label(1));
        Assert.Equal("Đang tạo ảnh…", Label(2));
        Assert.Null(Label(3));
        Assert.Null(Label(4));    // waiting for manual review: no endless spinner
        Assert.Null(Label(5));    // state not loaded yet
    }
}
