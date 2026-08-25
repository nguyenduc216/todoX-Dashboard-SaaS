using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.Timelapse;

public sealed class TimelapseImageWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseImageWorker> _logger;

    public TimelapseImageWorker(IServiceScopeFactory scopeFactory, IConfiguration config, IOptions<TimelapseProviderWorkerOptions> options, ILogger<TimelapseImageWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
            await tenant.EnsureLoadedAsync(stoppingToken);
            _logger.LogInformation(
                "TIMELAPSE_IMAGE_WORKER_TENANT worker=timelapse-image tenantId={TenantId} tenantCode={TenantCode} machine={MachineName} processId={ProcessId}",
                tenant.TenantId,
                tenant.TenantCode,
                Environment.MachineName,
                Environment.ProcessId);
        }

        await TimelapseWorkerLoop.RunAsync(
            "timelapse-image",
            _options.ImageParallelism,
            _config,
            _options,
            _scopeFactory,
            _logger,
            async (scope, workerKey, claimFor, ct) =>
            {
                var repo = scope.ServiceProvider.GetRequiredService<ITimelapseWorkerRepository>();
                var item = await repo.ClaimImageAsync(workerKey, claimFor, ct);
                if (item is null)
                {
                    await repo.DiagnoseImageClaimsAsync(workerKey, TimeSpan.FromSeconds(60), ct);
                    return new TimelapseWorkerIterationResult(false, false);
                }

                _logger.LogInformation("TIMELAPSE_WORKER_CLAIM_RETURNED worker={WorkerName} workerKey={WorkerKey} stageId={StageId} attempt={Attempt} providerTaskIdPresent={ProviderTaskIdPresent}",
                    "timelapse-image", workerKey, item.Id, item.Attempt, !string.IsNullOrWhiteSpace(item.ProviderTaskId));
                await scope.ServiceProvider.GetRequiredService<ITimelapseProviderRuntime>().ProcessImageAsync(item, ct);
                return new TimelapseWorkerIterationResult(true, true);
            },
            stoppingToken);
    }
}

public sealed class TimelapseVideoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseVideoWorker> _logger;

    public TimelapseVideoWorker(IServiceScopeFactory scopeFactory, IConfiguration config, IOptions<TimelapseProviderWorkerOptions> options, ILogger<TimelapseVideoWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => TimelapseWorkerLoop.RunAsync(
            "timelapse-video",
            _options.VideoParallelism,
            _config,
            _options,
            _scopeFactory,
            _logger,
            async (scope, workerKey, claimFor, ct) =>
            {
                var repo = scope.ServiceProvider.GetRequiredService<ITimelapseWorkerRepository>();
                var item = await repo.ClaimVideoAsync(workerKey, claimFor, ct);
                if (item is null)
                {
                    return new TimelapseWorkerIterationResult(false, false);
                }

                await scope.ServiceProvider.GetRequiredService<ITimelapseProviderRuntime>().ProcessVideoAsync(item, ct);
                return new TimelapseWorkerIterationResult(true, true);
            },
            stoppingToken);
}

public sealed class TimelapseFinalizerWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly TimelapseProviderWorkerOptions _options;
    private readonly ILogger<TimelapseFinalizerWorker> _logger;

    public TimelapseFinalizerWorker(IServiceScopeFactory scopeFactory, IConfiguration config, IOptions<TimelapseProviderWorkerOptions> options, ILogger<TimelapseFinalizerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => TimelapseWorkerLoop.RunAsync(
            "timelapse-finalizer",
            _options.FinalizerParallelism,
            _config,
            _options,
            _scopeFactory,
            _logger,
            async (scope, workerKey, claimFor, ct) =>
            {
                var repo = scope.ServiceProvider.GetRequiredService<ITimelapseWorkerRepository>();
                var item = await repo.ClaimFinalizerAsync(workerKey, claimFor, ct);
                if (item is null)
                {
                    var reconciled = await scope.ServiceProvider
                        .GetRequiredService<ITimelapseCoreLifecycleBridge>()
                        .ReconcileCompletionAsync(ct);
                    return new TimelapseWorkerIterationResult(false, reconciled);
                }

                await scope.ServiceProvider.GetRequiredService<ITimelapseFinalizerRuntime>().ProcessAsync(item, ct);
                return new TimelapseWorkerIterationResult(true, true);
            },
            stoppingToken);
}

internal static class TimelapseWorkerLoop
{
    public static async Task RunAsync(
        string workerName,
        int parallelism,
        IConfiguration config,
        TimelapseProviderWorkerOptions options,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        Func<IServiceScope, string, TimeSpan, CancellationToken, Task<TimelapseWorkerIterationResult>> processOneAsync,
        CancellationToken stoppingToken)
    {
        var renderQueueEnabled = config.GetValue("RenderQueue:Enabled", false);
        var effectiveParallelism = Math.Max(1, parallelism);
        var idleDelayMs = Math.Max(250, options.IdleDelayMs);
        var claimMinutes = Math.Max(1, options.ClaimMinutes);

        logger.LogInformation(
            "TIMELAPSE_WORKER_START worker={WorkerName} renderQueueEnabled={RenderQueueEnabled} timelapseEnabled={TimelapseEnabled} configuredParallelism={ConfiguredParallelism} effectiveParallelism={EffectiveParallelism} idleDelayMs={IdleDelayMs} pollDelayMs={PollDelayMs} claimMinutes={ClaimMinutes} providerCode={ProviderCode} imageCapabilityCode={ImageCapabilityCode} imageModelName={ImageModelName} machineName={MachineName} processId={ProcessId}",
            workerName,
            renderQueueEnabled,
            options.Enabled,
            parallelism,
            effectiveParallelism,
            idleDelayMs,
            Math.Max(250, options.PollDelayMs),
            claimMinutes,
            options.ProviderCode,
            options.ImageCapabilityCode,
            options.ImageModelName,
            Environment.MachineName,
            Environment.ProcessId);

        if (renderQueueEnabled && !options.Enabled)
        {
            logger.LogWarning("TIMELAPSE_WORKER_DISABLED worker={WorkerName} reason=TimelapseProviderWorkers:Enabled=false renderQueueEnabled=true", workerName);
        }

        if (parallelism <= 0)
        {
            logger.LogWarning("TIMELAPSE_WORKER_PARALLELISM_NORMALIZED worker={WorkerName} configuredParallelism={ConfiguredParallelism} effectiveParallelism={EffectiveParallelism}",
                workerName, parallelism, effectiveParallelism);
        }

        if (!renderQueueEnabled || !options.Enabled)
        {
            logger.LogInformation("TIMELAPSE_WORKER_DISABLED worker={WorkerName} reason={Reason}",
                workerName,
                renderQueueEnabled ? "TimelapseProviderWorkers:Enabled=false" : "RenderQueue:Enabled=false");
            return;
        }

        var lanes = Enumerable.Range(1, effectiveParallelism)
            .Select(lane => RunLaneAsync(workerName, lane, options, scopeFactory, logger, processOneAsync, stoppingToken))
            .ToArray();
        await Task.WhenAll(lanes);
    }

    private static async Task RunLaneAsync(
        string workerName,
        int lane,
        TimelapseProviderWorkerOptions options,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        Func<IServiceScope, string, TimeSpan, CancellationToken, Task<TimelapseWorkerIterationResult>> processOneAsync,
        CancellationToken stoppingToken)
    {
        var workerKey = $"{Environment.MachineName}-{workerName}-{lane}-{Guid.NewGuid():N}";
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(250, options.IdleDelayMs));
        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(250, options.PollDelayMs));
        var claimFor = TimeSpan.FromMinutes(Math.Max(1, options.ClaimMinutes));
        var heartbeatEvery = TimeSpan.FromSeconds(Math.Max(15, options.HeartbeatSeconds));
        var heartbeat = new TimelapseWorkerHeartbeat(workerName, workerKey, heartbeatEvery);

        logger.LogInformation(
            "TIMELAPSE_WORKER_LANE_START worker={WorkerName} lane={Lane} workerKey={WorkerKey} idleDelayMs={IdleDelayMs} pollDelayMs={PollDelayMs} claimMinutes={ClaimMinutes}",
            workerName,
            lane,
            workerKey,
            (int)idleDelay.TotalMilliseconds,
            (int)pollDelay.TotalMilliseconds,
            (int)claimFor.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                heartbeat.MarkLoop();
                logger.LogDebug("TIMELAPSE_WORKER_CLAIM_BEGIN worker={WorkerName} lane={Lane} workerKey={WorkerKey}", workerName, lane, workerKey);
                var result = await processOneAsync(scope, workerKey, claimFor, stoppingToken);
                heartbeat.MarkClaimResult(result.Claimed, result.Succeeded);
                if (result.Claimed)
                {
                    logger.LogInformation("TIMELAPSE_WORKER_CLAIMED worker={WorkerName} lane={Lane} workerKey={WorkerKey}", workerName, lane, workerKey);
                }
                else if (heartbeat.ShouldLogNullClaim())
                {
                    logger.LogDebug("TIMELAPSE_WORKER_CLAIM_NULL worker={WorkerName} lane={Lane} workerKey={WorkerKey}", workerName, lane, workerKey);
                }

                heartbeat.LogIfDue(logger, lane);
                await Task.Delay(result.Claimed ? pollDelay : idleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                heartbeat.MarkError();
                logger.LogError(ex, "TIMELAPSE_WORKER_ERROR worker={WorkerName} lane={Lane} workerKey={WorkerKey}", workerName, lane, workerKey);
                heartbeat.LogIfDue(logger, lane, force: true);
                await Task.Delay(idleDelay, stoppingToken);
            }
        }

        logger.LogInformation("TIMELAPSE_WORKER_LANE_STOP worker={WorkerName} lane={Lane} workerKey={WorkerKey}", workerName, lane, workerKey);
    }
}

internal readonly record struct TimelapseWorkerIterationResult(bool Claimed, bool Succeeded);

internal sealed class TimelapseWorkerHeartbeat
{
    private readonly string _workerName;
    private readonly string _workerKey;
    private readonly TimeSpan _interval;
    private DateTimeOffset _lastHeartbeatAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastNullClaimAt = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastLoopAt;
    private DateTimeOffset? _lastClaimAt;
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset? _lastErrorAt;

    public TimelapseWorkerHeartbeat(string workerName, string workerKey, TimeSpan interval)
    {
        _workerName = workerName;
        _workerKey = workerKey;
        _interval = interval;
    }

    public void MarkLoop()
        => _lastLoopAt = DateTimeOffset.UtcNow;

    public void MarkClaimResult(bool claimed, bool succeeded)
    {
        var now = DateTimeOffset.UtcNow;
        if (claimed)
        {
            _lastClaimAt = now;
        }

        if (succeeded)
        {
            _lastSuccessAt = now;
        }
    }

    public void MarkError()
        => _lastErrorAt = DateTimeOffset.UtcNow;

    public bool ShouldLogNullClaim()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastNullClaimAt < _interval)
        {
            return false;
        }

        _lastNullClaimAt = now;
        return true;
    }

    public void LogIfDue(ILogger logger, int lane, bool force = false)
    {
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastHeartbeatAt < _interval)
        {
            return;
        }

        _lastHeartbeatAt = now;
        logger.LogInformation(
            "TIMELAPSE_WORKER_HEARTBEAT workerName={WorkerName} lane={Lane} workerKey={WorkerKey} machine={MachineName} processId={ProcessId} lastLoopAt={LastLoopAt} lastClaimAt={LastClaimAt} lastSuccessAt={LastSuccessAt} lastErrorAt={LastErrorAt}",
            _workerName,
            lane,
            _workerKey,
            Environment.MachineName,
            Environment.ProcessId,
            _lastLoopAt,
            _lastClaimAt,
            _lastSuccessAt,
            _lastErrorAt);
    }
}
