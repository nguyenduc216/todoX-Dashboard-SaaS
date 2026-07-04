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
    private readonly string _workerKey = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public RenderJobWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<RenderJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("Render job worker is registered but disabled. Set RenderQueue:Enabled=true to start processing.");
            return;
        }

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
                var job = await jobs.ClaimNextAsync(_workerKey, lockFor, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                    continue;
                }

                await ProcessJobAsync(jobs, dispatcher, job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Render job worker loop failed.");
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }

    private async Task ProcessJobAsync(IRenderJobService jobs, IRenderJobDispatcher dispatcher, RenderJobDto job, CancellationToken ct)
    {
        try
        {
            await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Rendering, ct: ct);
            await dispatcher.DispatchAsync(job, ct);
            await jobs.MarkStatusAsync(job.Id, RenderJobStatuses.Completed, ct: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await jobs.CancelAsync(job.Id, "Worker cancellation requested.", ct: CancellationToken.None);
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
