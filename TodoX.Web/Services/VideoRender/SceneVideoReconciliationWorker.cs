using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed class SceneVideoReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SceneVideoReconciliationWorker> _logger;

    public SceneVideoReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SceneVideoReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("RenderQueue:Enabled", false))
        {
            _logger.LogInformation("Scene video reconciliation worker is disabled because RenderQueue:Enabled=false.");
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Clamp(
            _configuration.GetValue("VideoRender:ReconciliationIntervalSeconds", 30), 5, 300));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
                await tenant.EnsureLoadedAsync(stoppingToken);
                var repository = scope.ServiceProvider.GetRequiredService<VideoRenderRepository>();
                var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
                foreach (var jobId in await repository.ListPersistentSceneVideoReconciliationJobsAsync(stoppingToken))
                {
                    await jobs.ScheduleProviderPollAsync(
                        jobId,
                        delay,
                        "SCENE_VIDEO_RECONCILIATION_WORKER",
                        "Persistent reconciliation worker retained the existing provider task and scheduled another poll.",
                        stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scene video reconciliation scan failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }
}
