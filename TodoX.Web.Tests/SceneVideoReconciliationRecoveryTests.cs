using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;
using Xunit;

namespace TodoX.Web.Tests;

/// <summary>
/// Job 9890fa94 (project 231): 79AI failed the scene videos (#vid_server_busy) but they stayed "đang xử lý" forever.
/// The persistent re-poll always threw (missing SQL parameters since 2026-10-28), one throwing job stopped the scan,
/// and a restart cancelled jobs that already held a 79AI task.
/// </summary>
public sealed class SceneVideoReconciliationRecoveryTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Root, .. parts]));

    [Fact]
    public void Persistent_poll_binds_every_parameter_its_sql_uses()
    {
        var source = Read("TodoX.Web", "Services", "Render", "RenderJobService.cs");
        var start = source.IndexOf("public async Task<bool> SchedulePersistentProviderPollAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("sceneTx?.Commit();", start, StringComparison.Ordinal);
        var body = source[start..end];
        var sqlEnd = body.LastIndexOf("\"\"\"", StringComparison.Ordinal);
        var sql = body[..sqlEnd];
        var args = body[sqlEnd..];
        foreach (var name in System.Text.RegularExpressions.Regex.Matches(sql, @"@([A-Za-z]+)").Select(m => m.Groups[1].Value).Distinct())
            Assert.Matches(@"[\s{,]" + name + @"\b", args);
    }

    [Fact]
    public async Task One_failing_job_does_not_stop_the_others()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, ScheduleProxy>();
        var proxy = (ScheduleProxy)(object)jobs;
        var bad = Guid.NewGuid();
        proxy.Throws = bad;
        var ids = new[] { bad, Guid.NewGuid(), Guid.NewGuid() };

        await SceneVideoReconciliationWorker.SchedulePersistentJobsAsync(ids, jobs, TimeSpan.FromSeconds(30), CancellationToken.None, NullLogger.Instance);

        Assert.Equal(ids, proxy.Scheduled);
    }

    [Fact]
    public async Task Shutdown_keeps_a_job_that_already_has_a_provider_task()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, ScheduleProxy>();
        var proxy = (ScheduleProxy)(object)jobs;
        proxy.Job = new RenderJobDto { Id = Guid.NewGuid(), ProviderTaskId = "16352363f4bb4685", ProviderVideoIdBase = "2206b3eb65567281" };

        await SceneVideoJobWorker.StopForShutdownAsync(jobs, proxy.Job.Id, NullLogger.Instance);

        Assert.Equal(new[] { "MarkStatusAsync:pending_reconciliation" }, proxy.Writes);
    }

    [Fact]
    public async Task Shutdown_cancels_a_job_not_yet_sent_to_the_provider()
    {
        var jobs = DispatchProxy.Create<IRenderJobService, ScheduleProxy>();
        var proxy = (ScheduleProxy)(object)jobs;
        proxy.Job = new RenderJobDto { Id = Guid.NewGuid() };

        await SceneVideoJobWorker.StopForShutdownAsync(jobs, proxy.Job.Id, NullLogger.Instance);

        Assert.Equal(new[] { "CancelAsync" }, proxy.Writes);
    }

    public class ScheduleProxy : DispatchProxy
    {
        public Guid Throws { get; set; }
        public RenderJobDto? Job { get; set; }
        public List<Guid> Scheduled { get; } = [];
        public List<string> Writes { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case nameof(IRenderJobService.SchedulePersistentProviderPollAsync):
                    var id = (Guid)args![0]!;
                    Scheduled.Add(id);
                    return id == Throws ? Task.FromException<bool>(new InvalidOperationException("RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED")) : Task.FromResult(true);
                case nameof(IRenderJobService.GetAsync):
                    return Task.FromResult(Job);
                case nameof(IRenderJobService.MarkStatusAsync):
                    Writes.Add($"MarkStatusAsync:{args![1]}");
                    return Task.CompletedTask;
                case nameof(IRenderJobService.CancelAsync):
                    Writes.Add("CancelAsync");
                    return Task.FromResult(true);
                default:
                    throw new NotSupportedException(method.Name);
            }
        }
    }
}
