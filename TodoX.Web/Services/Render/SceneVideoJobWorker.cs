namespace TodoX.Web.Services.Render;

public sealed class SceneVideoJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneVideoJobWorker> _logger;

    public SceneVideoJobWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<SceneVideoJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("Scene video job worker is disabled because RenderQueue:Enabled=false.");
            return;
        }

        var parallelism = Math.Max(1, _config.GetValue("VideoRender:MaxConcurrentSceneJobs", 3));
        var tasks = Enumerable.Range(0, parallelism)
            .Select(index => RunLaneAsync(index + 1, stoppingToken))
            .ToArray();

        await Task.WhenAll(tasks);
    }

    private async Task RunLaneAsync(int lane, CancellationToken stoppingToken)
    {
        var workerKey = $"{Environment.MachineName}-scene-video-{lane}-{Guid.NewGuid():N}";
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
                var job = await jobs.ClaimNextByJobTypeAsync(workerKey, lockFor, new[] { RenderJobTypes.RenderSceneVideo }, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                    continue;
                }

                try
                {
                    await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Rendering, ct: stoppingToken);
                    await dispatcher.DispatchAsync(job, stoppingToken);
                    await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Completed, ct: stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    await jobs.CancelAsync(job.Id, "Scene video worker cancellation requested.", ct: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    var shouldRetry = job.AttemptCount < job.MaxAttempts;
                    if (shouldRetry)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(0, job.AttemptCount)) * 5));
                        await jobs.ScheduleRetryAsync(job.Id, delay, ex.GetType().Name, ex.Message, stoppingToken);
                    }
                    else
                    {
                        await jobs.AddEventAsync(job.Id, "JOB_FAILED", ex.Message,
                            new { ex.GetType().Name, job.AttemptCount, job.MaxAttempts }, "error", stoppingToken);
                        await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Failed, errorCode: ex.GetType().Name, errorMessage: ex.Message, ct: stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scene video job worker lane {Lane} failed.", lane);
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}
