using System.Reflection;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

public sealed class SceneVideoClaimWorkerTests
{
    [Fact]
    public async Task OwnershipLostAtProviderBoundary_DoesNotSubmit()
    {
        var submits = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SceneVideoWorkerHandler.SubmitWithVerifiedClaimAsync(
                () => Task.FromException(new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST")),
                () => { submits++; return Task.FromResult("provider-task"); }));
        Assert.Equal(0, submits);
    }

    [Fact]
    public async Task ValidClaimAtProviderBoundary_SubmitsOnce()
    {
        var submits = 0;
        var result = await SceneVideoWorkerHandler.SubmitWithVerifiedClaimAsync(
            () => Task.CompletedTask,
            () => { submits++; return Task.FromResult("provider-task"); });
        Assert.Equal("provider-task", result);
        Assert.Equal(1, submits);
    }

    [Theory]
    [InlineData("RVIDEO_JOB_CALLER_OWNERSHIP_LOST")]
    [InlineData("RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED")]
    public async Task RejectedClaim_DoesNotDispatchOrRetry(string reason)
    {
        var jobs = DispatchProxy.Create<IRenderJobService, JobProxy>();
        var proxy = (JobProxy)(object)jobs;
        proxy.InvokeCall = (method, _) => method.Name switch
        {
            nameof(IRenderJobService.MarkSceneVideoClaimRenderingAsync) => Task.FromException(new InvalidOperationException(reason)),
            nameof(IRenderJobService.QuarantineRejectedSceneVideoClaimAsync) => Task.FromResult(true),
            _ => throw new Xunit.Sdk.XunitException($"Unexpected service call {method.Name}")
        };
        var dispatched = 0;
        var claim = Claim();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SceneVideoJobWorker.DispatchClaimAsync(jobs, new FakeDispatcher(() => dispatched++), claim, claim.WorkerKey!, CancellationToken.None));
        Assert.Equal(reason, error.Message);
        Assert.True(await SceneVideoJobWorker.QuarantineRejectedClaimAsync(jobs, claim, claim.WorkerKey!, reason, CancellationToken.None));
        Assert.Equal(0, dispatched);
        Assert.Equal(new[] { nameof(IRenderJobService.MarkSceneVideoClaimRenderingAsync),
            nameof(IRenderJobService.QuarantineRejectedSceneVideoClaimAsync) }, proxy.Calls);
    }

    [Fact]
    public async Task AcceptedClaim_DispatchesExactlyOnce()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, JobProxy>();
        var proxy = (JobProxy)(object)jobs;
        proxy.InvokeCall = (_, _) => Task.CompletedTask;
        var dispatched = 0;
        var claim = Claim();
        await SceneVideoJobWorker.DispatchClaimAsync(jobs, new FakeDispatcher(() => dispatched++),
            claim, claim.WorkerKey!, CancellationToken.None);
        Assert.Equal(1, dispatched);
        Assert.Equal(new[] { nameof(IRenderJobService.MarkSceneVideoClaimRenderingAsync) }, proxy.Calls);
    }

    private static RenderJobDto Claim() => new()
    {
        Id = Guid.NewGuid(), JobType = RenderJobTypes.RenderSceneVideo,
        WorkerKey = "test-worker", AttemptCount = 1, MaxAttempts = 2
    };

    public class JobProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> InvokeCall { get; set; } = (_, _) => throw new InvalidOperationException();
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return InvokeCall(targetMethod, args ?? []);
        }
    }

    private sealed class FakeDispatcher(Action dispatch) : IRenderJobDispatcher
    {
        public Task DispatchAsync(RenderJobDto job, CancellationToken ct)
        {
            dispatch();
            return Task.CompletedTask;
        }
    }
}