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

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => TimelapseWorkerLoop.RunAsync(
            "timelapse-image",
            Math.Max(1, _options.ImageParallelism),
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
                    return false;
                }

                await scope.ServiceProvider.GetRequiredService<ITimelapseProviderRuntime>().ProcessImageAsync(item, ct);
                return true;
            },
            stoppingToken);
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
            Math.Max(1, _options.VideoParallelism),
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
                    return false;
                }

                await scope.ServiceProvider.GetRequiredService<ITimelapseProviderRuntime>().ProcessVideoAsync(item, ct);
                return true;
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
            Math.Max(1, _options.FinalizerParallelism),
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
                    return false;
                }

                await scope.ServiceProvider.GetRequiredService<ITimelapseFinalizerRuntime>().ProcessAsync(item, ct);
                return true;
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
        Func<IServiceScope, string, TimeSpan, CancellationToken, Task<bool>> processOneAsync,
        CancellationToken stoppingToken)
    {
        if (!config.GetValue("RenderQueue:Enabled", false) || !options.Enabled)
        {
            logger.LogInformation("{WorkerName} worker is disabled.", workerName);
            return;
        }

        var lanes = Enumerable.Range(1, parallelism)
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
        Func<IServiceScope, string, TimeSpan, CancellationToken, Task<bool>> processOneAsync,
        CancellationToken stoppingToken)
    {
        var workerKey = $"{Environment.MachineName}-{workerName}-{lane}-{Guid.NewGuid():N}";
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(250, options.IdleDelayMs));
        var pollDelay = TimeSpan.FromMilliseconds(Math.Max(250, options.PollDelayMs));
        var claimFor = TimeSpan.FromMinutes(Math.Max(1, options.ClaimMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var processed = await processOneAsync(scope, workerKey, claimFor, stoppingToken);
                await Task.Delay(processed ? pollDelay : idleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{WorkerName} lane {Lane} failed.", workerName, lane);
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}
