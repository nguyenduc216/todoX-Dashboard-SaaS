using TodoX.Web.Data;

namespace TodoX.Web.Services.Render;

/// <summary>
/// Dedicated lanes for scene image child jobs ("render_scene_image"), mirroring <see cref="SceneVideoJobWorker"/>.
/// The shared <see cref="RenderJobWorker"/> claims one job at a time, so a project with 8 scenes submitted its
/// images one after another and the scenes still waiting looked as if nothing had started. Every scene of a job
/// must reach the provider at the same time, so the image queue gets its own parallel lanes.
/// </summary>
public sealed class SceneImageJobWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SceneImageJobWorker> _logger;

    public SceneImageJobWorker(IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<SceneImageJobWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>Lane count: one scene image per lane, so a whole job's scenes start together.</summary>
    internal static int ResolveParallelism(IConfiguration config)
        => Math.Clamp(config.GetValue("RenderQueue:MaxConcurrentSceneImageJobs", 8), 1, 16);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("Scene image job worker is disabled because RenderQueue:Enabled=false.");
            return;
        }

        var lanes = ResolveParallelism(_config);
        _logger.LogInformation("SCENE_IMAGE_WORKER_STARTED lanes={Lanes}", lanes);
        await Task.WhenAll(Enumerable.Range(0, lanes).Select(index => RunLaneAsync(index + 1, stoppingToken)));
    }

    private async Task RunLaneAsync(int lane, CancellationToken stoppingToken)
    {
        var workerKey = $"{Environment.MachineName}-scene-image-{lane}-{Guid.NewGuid():N}";
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
                var job = await jobs.ClaimNextByJobTypeAsync(workerKey, lockFor,
                    new[] { SceneImageRenderWorkItemHandler.JobTypeName }, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(idleDelay, stoppingToken);
                    continue;
                }

                // Same lifecycle as the shared lane: billing, retries and terminal failures are unchanged.
                await RenderJobWorker.ProcessJobAsync(jobs, dispatcher, job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scene image job worker lane {Lane} failed.", lane);
                await Task.Delay(idleDelay, stoppingToken);
            }
        }
    }
}
