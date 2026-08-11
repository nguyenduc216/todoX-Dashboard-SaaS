using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiProviderCatalogSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AiProviderCatalogSyncOptions> _options;
    private readonly ILogger<AiProviderCatalogSyncWorker> _logger;
    private DateOnly? _lastRunLocalDate;

    public AiProviderCatalogSyncWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<AiProviderCatalogSyncOptions> options,
        ILogger<AiProviderCatalogSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var options = _options.CurrentValue;
                if (options.Enabled && IsDue(options))
                {
                    await RunDailySyncAsync(options, stoppingToken);
                    _lastRunLocalDate = DateOnly.FromDateTime(DateTime.Now);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI_PROVIDER_CATALOG_DAILY_SYNC_FAILED");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private bool IsDue(AiProviderCatalogSyncOptions options)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var hour = Math.Clamp(options.DailyHourLocal, 0, 23);
        return now.Hour >= hour && _lastRunLocalDate != today;
    }

    private async Task RunDailySyncAsync(AiProviderCatalogSyncOptions options, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var providers = scope.ServiceProvider.GetRequiredService<IAiProviderService>();
        var sync = scope.ServiceProvider.GetRequiredService<IAiProviderSyncService>();
        var configuredCodes = (options.ProviderCodes.Length == 0 ? new[] { "79ai" } : options.ProviderCodes)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var providerCode in configuredCodes)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, options.TimeoutSeconds)));

            var provider = await providers.GetProviderByCodeAsync(providerCode, timeout.Token);
            if (provider is null || !provider.Enabled)
            {
                _logger.LogInformation("AI_PROVIDER_CATALOG_DAILY_SYNC_SKIPPED providerCode={ProviderCode}", providerCode);
                continue;
            }

            var result = await sync.SyncProviderAsync(provider.Id, user: null, timeout.Token);
            if (!result.Success && options.RetryDelaySeconds > 0 && !stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(options.RetryDelaySeconds), stoppingToken);
                result = await sync.SyncProviderAsync(provider.Id, user: null, timeout.Token);
            }

            _logger.LogInformation(
                "AI_PROVIDER_CATALOG_DAILY_SYNC_RESULT providerCode={ProviderCode} success={Success} syncId={SyncId} message={Message}",
                providerCode,
                result.Success,
                result.SyncId,
                result.Message);
        }
    }
}
