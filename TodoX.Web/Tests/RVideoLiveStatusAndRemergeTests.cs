using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoX.Web.Components.Pages;
using TodoX.Web.Components.Shared;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class RVideoLiveStatusAndRemergeTests
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly VideoProjectSceneDto Scene=new() { Id=42, ProjectId=7, Status=VideoSceneStatuses.VideoReady };
    private static SceneVideoUiState State(string status, Guid? job=null, long generation=1)
        => new(new SceneVideoVersionDto { Id=Guid.NewGuid(), IsSelected=true, Status="completed", PublicUrl="/old.mp4" },
            new RenderJobDto { Id=job??Guid.NewGuid(), Status=status },null,SceneVideoSubmitBlockReason.None,
            status is "queued" or "processing" or "pending_reconciliation",generation);
    private static object? Call(RenderVideoJobs page,string method,params object[] args)
        => typeof(RenderVideoJobs).GetMethod(method,Private)!.Invoke(page,args);

    [Theory]
    [InlineData("queued",MediaRenderState.Queued,"Đang tạo video")]
    [InlineData("rendering",MediaRenderState.Rendering,"Đang tạo video")]
    [InlineData("processing",MediaRenderState.Rendering,"Đang tạo video")]
    [InlineData("pending_reconciliation",MediaRenderState.Rendering,"Đang chờ đối soát")]
    [InlineData("failed",MediaRenderState.Failed,"Tạo video thất bại")]
    [InlineData("completed",MediaRenderState.Ready,"Hoàn tất")]
    public async Task NewOperationStatusWinsOverSelectedCompletedPlayback(string status,MediaRenderState media,string label)
    {
        await using var page=new RenderVideoJobs();
        var state=State(status);
        Assert.Equal(true,Call(page,"ApplySceneVideoUiState",42L,state));
        Assert.Equal(media,Call(page,"ResolveVideoMediaState",Scene));
        Assert.Equal(label,Call(page,"ResolveVideoSceneLabel",Scene));
        Assert.Same(state.SelectedVersion,Call(page,"ResolveVideoVersion",Scene));
        Assert.Equal("/old.mp4",state.SelectedVersion!.PublicUrl);
    }

    [Fact]
    public async Task ResultJobIdUpdatesImmediatelyAndStaleReloadCannotUndoIt()
    {
        await using var page=new RenderVideoJobs();
        var previous=State("completed");
        Call(page,"ApplySceneVideoUiState",42L,previous);
        var job=Guid.NewGuid();
        Call(page,"ApplySupersedeQueued",42L,job);
        Assert.Equal(MediaRenderState.Queued,Call(page,"ResolveVideoMediaState",Scene));
        Assert.Equal(false,Call(page,"ApplySceneVideoUiState",42L,previous));
        Assert.Equal("Đang tạo video",Call(page,"ResolveVideoSceneLabel",Scene));
        var completed=State("completed",job,2);
        Assert.Equal(true,Call(page,"ApplySceneVideoUiState",42L,completed));
        Assert.Equal(MediaRenderState.Ready,Call(page,"ResolveVideoMediaState",Scene));
        Assert.Same(completed.SelectedVersion,Call(page,"ResolveVideoVersion",Scene));
    }

    [Fact]
    public async Task StartingPollingTwiceUsesOneLoopAndAsyncDisposeStopsIt()
    {
        var page=new RenderVideoJobs();
        typeof(RenderVideoJobs).GetField("_projectId",Private)!.SetValue(page,7L);
        typeof(RenderVideoJobs).GetProperty("VideoOptions",Private)!.SetValue(page,new Monitor());
        await (Task)Call(page,"StartPolling")!;
        var task=typeof(RenderVideoJobs).GetField("_pollTask",Private)!.GetValue(page);
        await (Task)Call(page,"StartPolling")!;
        Assert.Same(task,typeof(RenderVideoJobs).GetField("_pollTask",Private)!.GetValue(page));
        await page.DisposeAsync();
        Assert.True(((Task)task!).IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalMergeCheckRunsAfterCompletedAndCannotRetryProviderOnFailure(bool throwMerge)
    {
        var jobs=DispatchProxy.Create<IRenderJobService,JobProbe>();
        var probe=(JobProbe)(object)jobs;
        var final=new FinalProbe(probe,throwMerge);
        var job=new RenderJobDto { Id=Guid.NewGuid(),InputJson="{\"projectId\":7,\"sceneId\":42}" };
        await SceneVideoJobWorker.MarkCompletedAndFinalizeAsync(jobs,final,job,NullLogger.Instance,CancellationToken.None);
        Assert.True(probe.Completed);
        Assert.Equal(1,final.Calls);
    }

    [Fact]
    public void QueuedMergeSnapshotRejectsNewGenerationOrSelectedVersion()
    {
        const string expected="{\"generations\":{\"42\":1},\"selected\":{\"42\":\"one\"}}";
        VideoRenderMergeHandler.RequireCurrentSnapshot(expected,"{\"selected\":{\"42\":\"one\"},\"generations\":{\"42\":1}}");
        Assert.Throws<RenderJobTerminalFailureException>(()=>VideoRenderMergeHandler.RequireCurrentSnapshot(expected,
            "{\"generations\":{\"42\":2},\"selected\":{\"42\":\"one\"}}"));
        Assert.Throws<RenderJobTerminalFailureException>(()=>VideoRenderMergeHandler.RequireCurrentSnapshot(expected,
            "{\"generations\":{\"42\":1},\"selected\":{\"42\":\"two\"}}"));
    }

    [Fact]
    public void MergeDeduplicationKeyIncludesSceneOrderAndGenerationSelectionSnapshot()
    {
        var key=RVideoProjectFinalizationService.BuildMergeSelectionKey("snapshot-one",[42,43]);
        Assert.Equal(key,RVideoProjectFinalizationService.BuildMergeSelectionKey("snapshot-one",[42,43]));
        Assert.NotEqual(key,RVideoProjectFinalizationService.BuildMergeSelectionKey("snapshot-two",[42,43]));
        Assert.NotEqual(key,RVideoProjectFinalizationService.BuildMergeSelectionKey("snapshot-one",[43,42]));
    }

    public class JobProbe:DispatchProxy
    {
        public bool Completed;
        protected override object? Invoke(MethodInfo? method,object?[]? args)
        {
            Assert.Equal("MarkStatusAsync",method!.Name);
            Assert.Equal(RenderJobStatuses.Completed,args![1]);
            Completed=true;
            return Task.CompletedTask;
        }
    }
    private sealed class FinalProbe(JobProbe jobs,bool shouldThrow):IRVideoProjectFinalizationService
    {
        public int Calls;
        public Task<RVideoProjectFinalizationResult> TryEnqueueFinalMergeAsync(long projectId,string source,CancellationToken ct=default)
        {
            Assert.True(jobs.Completed); Assert.Equal(7,projectId);
            Assert.Equal(RVideoProjectFinalizationContracts.TriggerSceneVideoReady,source); Calls++;
            if(shouldThrow) throw new InvalidOperationException("merge unavailable");
            return Task.FromResult(new RVideoProjectFinalizationResult(true,false,"enqueued",Guid.NewGuid(),"merge"));
        }
    }
    private sealed class Monitor:IOptionsMonitor<VideoRenderOptions>
    {
        public VideoRenderOptions CurrentValue {get;}=new() {PollIntervalSeconds=60};
        public VideoRenderOptions Get(string? name)=>CurrentValue;
        public IDisposable? OnChange(Action<VideoRenderOptions,string?> listener)=>null;
    }
}
