using System.Reflection;
using Microsoft.Extensions.Configuration;
using TodoX.Web.Services.Render;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// The shared render worker used to run one job at a time; a hung FFmpeg mux held every queued job (including
/// the batches that submit scene videos to 79AI) for hours. It now runs parallel lanes with a per-job time limit.
/// </summary>
public sealed class RenderJobWorkerLaneTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(x => x.Key, x => (string?)x.Value)).Build();

    [Fact]
    public void Lanes_default_to_four_and_are_bounded()
    {
        Assert.Equal(4, RenderJobWorker.LaneCount(Config()));
        Assert.Equal(6, RenderJobWorker.LaneCount(Config(("RenderQueue:GeneralLanes", "6"))));
        Assert.Equal(1, RenderJobWorker.LaneCount(Config(("RenderQueue:GeneralLanes", "0"))));
        Assert.Equal(16, RenderJobWorker.LaneCount(Config(("RenderQueue:GeneralLanes", "500"))));
        Assert.Equal(TimeSpan.FromMinutes(45), RenderJobWorker.JobTimeout(Config()));
        Assert.Equal(TimeSpan.FromMinutes(10), RenderJobWorker.JobTimeout(Config(("RenderQueue:JobTimeoutMinutes", "10"))));
    }

    [Fact]
    public async Task Abandoned_job_writes_no_status_after_it_finishes()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, JobProxy>();
        var proxy = (JobProxy)(object)jobs;
        var abandoned = false;
        await RenderJobWorker.ProcessJobAsync(jobs, new FakeDispatcher(() => abandoned = true), Job(), CancellationToken.None, () => abandoned);
        Assert.Equal(new[] { nameof(IRenderJobService.MarkStatusAsync) }, proxy.Calls); // only the initial "rendering"
    }

    [Fact]
    public async Task Abandoned_job_that_throws_is_not_retried_or_cancelled()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, JobProxy>();
        var proxy = (JobProxy)(object)jobs;
        var abandoned = false;
        using var cts = new CancellationTokenSource();
        await RenderJobWorker.ProcessJobAsync(jobs, new FakeDispatcher(() => { abandoned = true; cts.Cancel(); throw new OperationCanceledException(cts.Token); }),
            Job(), cts.Token, () => abandoned);
        Assert.Equal(new[] { nameof(IRenderJobService.MarkStatusAsync) }, proxy.Calls);
    }

    [Fact]
    public async Task Normal_job_is_marked_completed()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, JobProxy>();
        var proxy = (JobProxy)(object)jobs;
        await RenderJobWorker.ProcessJobAsync(jobs, new FakeDispatcher(() => { }), Job(), CancellationToken.None, () => false);
        Assert.Equal(new[] { nameof(IRenderJobService.MarkStatusAsync), nameof(IRenderJobService.MarkStatusAsync) }, proxy.Calls);
    }

    private static RenderJobDto Job() => new() { Id = Guid.NewGuid(), JobType = "render_scene_audio_mux", AttemptCount = 1, MaxAttempts = 3 };

    public class JobProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls.Add(targetMethod!.Name);
            return Task.CompletedTask;
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
