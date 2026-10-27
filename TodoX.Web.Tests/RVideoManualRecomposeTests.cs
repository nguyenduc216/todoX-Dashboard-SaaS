using System.Reflection;
using System.Text.Json;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public class FinalMergeProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Call = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args!);
    public static T Create<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = Create<T, FinalMergeProxy>();
        ((FinalMergeProxy)(object)proxy).Call = call;
        return proxy;
    }
}

public sealed class RVideoManualRecomposeTests
{
    private sealed class Harness
    {
        public readonly CurrentUserSession User = new() { UserId = Guid.NewGuid(), IsAuthenticated = true };
        public readonly VideoProjectDto Project;
        public readonly List<RenderJobCreateModel> Created = [];
        public readonly SemaphoreSlim Gate = new(1);
        public bool Ready = true;
        public bool Enabled = true;
        public bool Active;
        public readonly RVideoProjectFinalizationService Service;
        private readonly Guid _activeJob = Guid.NewGuid();
        public Harness()
        {
            Project = new() { Id = 42, UserId = User.UserId, Status = VideoProjectStatuses.Completed,
                FinalVideoUrl = "/existing-final.mp4", FinalVideoPath = "existing-final.mp4",
                Scenes = [new() { Id = 7, SceneIndex = 1, DurationSeconds = 2 }] };
            var versions = FinalMergeProxy.Create<ISceneMediaVersioningService>((method, args) => method.Name switch
            {
                "IsEnabledAsync" => Task.FromResult(Enabled),
                "GetFinalVideoVersioningStateAsync" => Task.FromResult(FinalVideoVersioningState.FromFlag(Enabled ? "true" : "false", 1)),
                "GetSelectedVideoVersionAsync" => Task.FromResult<SceneVideoVersionDto?>(Ready ? new() { Id = Guid.NewGuid(), Status = "completed" } : null),
                "GetSelectedAudioVersionAsync" => Task.FromResult<SceneAudioVersionDto?>(null),
                _ => throw new InvalidOperationException("Unexpected version write or lookup: " + method.Name)
            });
            var jobs = FinalMergeProxy.Create<IRenderJobService>((method, args) => method.Name == "EnqueueForLogCodeIfNoneActiveAsync"
                ? Enqueue((RenderJobCreateModel)args[0]!, (string)args[1]!)
                : throw new InvalidOperationException("Unexpected provider/render call: " + method.Name));
            Service = new((_, _) => Task.FromResult<VideoProjectDto?>(Project),
                (_, _) => Task.FromResult<RVideoJobSettingsDto?>(new() { VoiceMode = RVideoVoiceModes.None }),
                (_, _, _, _, _, _) => Task.CompletedTask, versions, jobs, (_, _, _, _) => Task.CompletedTask, () => User);
        }
        private async Task<(RenderJobDto, bool)> Enqueue(RenderJobCreateModel model, string key)
        {
            await Gate.WaitAsync();
            try
            {
                Assert.Equal(RVideoProjectFinalizationService.BuildMergeLogicalRequestKey(Project.Id), key);
                if (Active) return (new() { Id = _activeJob }, true);
                Active = true;
                Created.Add(model);
                return (new() { Id = _activeJob }, false);
            }
            finally { Gate.Release(); }
        }
        public Task<RVideoProjectFinalizationResult> Recompose() => Service.TryEnqueueFinalMergeAsync(Project.Id, RVideoProjectFinalizationContracts.TriggerManual);
    }

    [Fact]
    public async Task CompletedFinalCanRecomposeWithoutChangingExistingFinalOrCallingProviders()
    {
        var h = new Harness();
        Assert.True((await h.Recompose()).Enqueued);
        var model = Assert.Single(h.Created);
        Assert.Equal(RenderJobTypes.MergeProjectVideo, model.JobType);
        Assert.Equal("ffmpeg", model.ProviderCode);
        Assert.Equal(0, model.PointCostEstimate);
        Assert.Equal(RenderPointStatuses.NotRequired, model.PointStatus);
        Assert.Equal("/existing-final.mp4", h.Project.FinalVideoUrl);
    }

    [Fact]
    public async Task TwoConcurrentRequestsShareOneActiveMerge()
    {
        var h = new Harness();
        var results = await Task.WhenAll(Task.Run(h.Recompose), Task.Run(h.Recompose));
        Assert.Single(results, r => r.Enqueued);
        Assert.Single(results, r => r.AlreadyActive);
        Assert.Single(h.Created);
    }

    [Fact]
    public async Task FailedMergeCanRetryWithFreshRequestIdentityAndSameConcurrencyKey()
    {
        var h = new Harness();
        Assert.True((await h.Recompose()).Enqueued);
        h.Active = false; // terminal previous job, as returned by the persistent active-job guard
        h.Project.Status = VideoProjectStatuses.Failed;
        Assert.True((await h.Recompose()).Enqueued);
        var ids = h.Created.Select(m => JsonSerializer.SerializeToElement(m.Input).GetProperty("logicalRequestId").GetString()).ToArray();
        Assert.NotEqual(ids[0], ids[1]);
        Assert.Equal("/existing-final.mp4", h.Project.FinalVideoUrl);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("readiness")]
    [InlineData("versioning")]
    [InlineData("authentication")]
    public async Task InvalidRequestsNeverEnqueue(string reason)
    {
        var h = new Harness();
        if (reason == "owner") h.Project.UserId = Guid.NewGuid();
        if (reason == "readiness") h.Ready = false;
        if (reason == "versioning") h.Enabled = false;
        if (reason == "authentication") h.User.IsAuthenticated = false;
        Assert.False((await h.Recompose()).Enqueued);
        Assert.Empty(h.Created);
    }
}
