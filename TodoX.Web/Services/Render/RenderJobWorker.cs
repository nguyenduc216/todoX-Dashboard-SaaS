using System.Runtime.CompilerServices;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Render;

public interface IRenderJobHandler
{
    string JobType { get; }
    Task HandleAsync(RenderJobDto job, CancellationToken ct);
}

public interface IRenderJobDispatcher
{
    Task DispatchAsync(RenderJobDto job, CancellationToken ct);
}

public sealed class RenderJobDispatcher : IRenderJobDispatcher
{
    private readonly IEnumerable<IRenderJobHandler> _handlers;

    public RenderJobDispatcher(IEnumerable<IRenderJobHandler> handlers)
    {
        _handlers = handlers;
    }

    public async Task DispatchAsync(RenderJobDto job, CancellationToken ct)
    {
        var handler = _handlers.FirstOrDefault(x => string.Equals(x.JobType, job.JobType, StringComparison.OrdinalIgnoreCase));
        if (handler is null)
        {
            throw new InvalidOperationException($"No render job handler registered for job type '{job.JobType}'.");
        }

        await handler.HandleAsync(job, ct);
    }
}

public sealed class RenderJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RenderJobWorker> _logger;

    public RenderJobWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RenderJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>Số luồng song song của hàng chờ chung (RenderQueue:GeneralLanes, mặc định 4, tối đa 16).</summary>
    internal static int LaneCount(IConfiguration config) => Math.Clamp(config.GetValue("RenderQueue:GeneralLanes", 4), 1, 16);

    /// <summary>Thời gian tối đa một lệnh được giữ luồng (RenderQueue:JobTimeoutMinutes, mặc định 45 phút).</summary>
    internal static TimeSpan JobTimeout(IConfiguration config) => TimeSpan.FromMinutes(Math.Clamp(config.GetValue("RenderQueue:JobTimeoutMinutes", 45), 1, 24 * 60));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("Render job worker is registered but disabled. Set RenderQueue:Enabled=true to start processing.");
            return;
        }

        // Each lane claims and runs its own job, so one slow or hung job no longer holds back every other queued job.
        // Claims use FOR UPDATE SKIP LOCKED, so lanes never take the same job.
        var lanes = LaneCount(_config);
        _logger.LogInformation("Render job worker started with {Lanes} parallel lanes.", lanes);
        await Task.WhenAll(Enumerable.Range(1, lanes).Select(lane => RunLaneAsync(lane, stoppingToken)));
    }

    private async Task RunLaneAsync(int lane, CancellationToken stoppingToken)
    {
        var workerKey = $"{Environment.MachineName}-general-{lane}-{Guid.NewGuid():N}";
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(250, _config.GetValue("RenderQueue:IdleDelayMs", 1500)));
        var lockFor = TimeSpan.FromMinutes(Math.Max(1, _config.GetValue("RenderQueue:LockMinutes", 15)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
                await tenant.EnsureLoadedAsync(stoppingToken);

                var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IRenderJobDispatcher>();
                // Scene images and scene videos each have their own parallel worker.
                var job = await jobs.ClaimNextExcludingJobTypesAsync(workerKey, lockFor,
                    new[] { RenderJobTypes.RenderSceneVideo, SceneImageRenderWorkItemHandler.JobTypeName }, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                    continue;
                }

                var abandoned = await ProcessWithTimeoutAsync(job, JobTimeout(_config), stoppingToken);
                if (abandoned)
                {
                    _logger.LogError("RENDER_JOB_TIMEOUT lane={Lane} jobId={JobId} jobType={JobType}: job exceeded its time limit and was marked failed; the lane moves on.",
                        lane, job.Id, job.JobType);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Render job worker lane {Lane} failed.", lane);
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }

    /// <summary>
    /// Runs one job in its own DI scope. If it runs past <paramref name="timeout"/>, the job is cancelled and marked failed,
    /// and the lane is released even when the handler ignores cancellation (its task is left to finish in the background).
    /// Returns true when the job was abandoned for timing out.
    /// </summary>
    private async Task<bool> ProcessWithTimeoutAsync(RenderJobDto job, TimeSpan timeout, CancellationToken stoppingToken)
    {
        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var abandoned = new StrongBox<bool>(false);
        var scope = _scopeFactory.CreateScope();
        var work = RunJobInScopeAsync(scope, job, () => Volatile.Read(ref abandoned.Value), jobCts.Token);
        var finished = await Task.WhenAny(work, Task.Delay(timeout, stoppingToken));
        if (finished == work)
        {
            scope.Dispose();
            await work;
            return false;
        }

        stoppingToken.ThrowIfCancellationRequested();
        Volatile.Write(ref abandoned.Value, true);
        jobCts.Cancel();
        _ = work.ContinueWith(t => { scope.Dispose(); _ = t.Exception; }, TaskScheduler.Default);
        await MarkTimedOutAsync(job, timeout);
        return true;
    }

    private static async Task RunJobInScopeAsync(IServiceScope scope, RenderJobDto job, Func<bool> isAbandoned, CancellationToken ct)
    {
        await Task.Yield();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        await tenant.EnsureLoadedAsync(ct);
        var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IRenderJobDispatcher>();
        await ProcessJobAsync(jobs, dispatcher, job, ct, isAbandoned);
    }

    private async Task MarkTimedOutAsync(RenderJobDto job, TimeSpan timeout)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            await tenant.EnsureLoadedAsync(CancellationToken.None);
            var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
            var message = $"Lệnh chạy quá {timeout.TotalMinutes:0} phút nên bị dừng để không chặn các lệnh khác.";
            await jobs.AddEventAsync(job.Id, "JOB_TIMEOUT", message, new { job.JobType, job.AttemptCount, timeoutMinutes = timeout.TotalMinutes }, "error", CancellationToken.None);
            await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed, errorCode: "RENDER_JOB_TIMEOUT", errorMessage: message, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not mark timed-out render job {JobId} as failed.", job.Id);
        }
    }

    internal static async Task ProcessJobAsync(IRenderJobService jobs, IRenderJobDispatcher dispatcher, RenderJobDto job, CancellationToken ct, Func<bool>? isAbandoned = null)
    {
        // A job abandoned for timing out was already marked failed; it must not write any status afterwards.
        bool Abandoned() => isAbandoned?.Invoke() == true;
        try
        {
            await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Rendering, ct: ct);
            await dispatcher.DispatchAsync(job, ct);
            if (Abandoned()) return;
            await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Completed, ct: ct);
        }
        catch (Exception) when (Abandoned())
        {
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await jobs.CancelAsync(job.Id, "Worker cancellation requested.", ct: CancellationToken.None);
        }
        catch (RenderJobDeferredException ex)
        {
            await jobs.AddEventAsync(job.Id, "JOB_DEFERRED", ex.Message,
                new { job.AttemptCount, job.MaxAttempts }, "info", ct);
        }
        catch (RenderJobTerminalFailureException ex)
        {
            await jobs.AddEventAsync(job.Id, "JOB_FAILED", ex.Message,
                new { ex.GetType().Name, job.AttemptCount, job.MaxAttempts }, "error", ct);
            await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed, errorCode: ex.GetType().Name, errorMessage: ex.Message, ct: ct);
        }
        catch (Exception ex)
        {
            var shouldRetry = job.AttemptCount < job.MaxAttempts;
            if (shouldRetry)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(0, job.AttemptCount)) * 5));
                await jobs.ScheduleRetryAsync(job.Id, delay, ex.GetType().Name, ex.Message, ct);
            }
            else
            {
                await jobs.AddEventAsync(job.Id, "JOB_FAILED", ex.Message,
                    new { ex.GetType().Name, job.AttemptCount, job.MaxAttempts }, "error", ct);
                await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed, errorCode: ex.GetType().Name, errorMessage: ex.Message, ct: ct);
            }
        }
    }
}
