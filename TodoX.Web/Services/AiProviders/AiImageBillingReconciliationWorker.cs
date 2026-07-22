using System.Text.Json;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiImageBillingReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<AiImageBillingReconciliationWorker> _logger;
    private readonly string _workerKey = $"ai-image-reconcile-{Environment.MachineName}-{Guid.NewGuid():N}";

    public AiImageBillingReconciliationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<AiImageBillingReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.GetValue("AiImageBilling:ReconciliationEnabled", true))
        {
            _logger.LogInformation("AI_IMAGE_RECONCILIATION_DISABLED");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI_IMAGE_RECONCILIATION_LOOP_FAILED");
            }

            var pollSeconds = Math.Clamp(_config.GetValue("AiImageBilling:ReconciliationPollSeconds", 60), 5, 3600);
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<IAiImageBillingService>();
        var tasks = scope.ServiceProvider.GetRequiredService<IYEScaleTaskClient>();
        var credentials = scope.ServiceProvider.GetRequiredService<IAiProviderCredentialResolver>();

        var batchSize = Math.Clamp(_config.GetValue("AiImageBilling:ReconciliationBatchSize", 10), 1, 100);
        var lockMinutes = Math.Clamp(_config.GetValue("AiImageBilling:ReconciliationLockMinutes", 5), 1, 60);
        var maxAttempts = Math.Clamp(_config.GetValue("AiImageBilling:ReconciliationMaxAttempts", 6), 1, 100);

        var claimed = await billing.ClaimReconciliationBatchAsync(
            _workerKey,
            batchSize,
            TimeSpan.FromMinutes(lockMinutes),
            maxAttempts,
            ct);

        foreach (var item in claimed)
        {
            await ReconcileItemAsync(billing, tasks, credentials, item, maxAttempts, ct);
        }
    }

    private async Task ReconcileItemAsync(
        IAiImageBillingService billing,
        IYEScaleTaskClient tasks,
        IAiProviderCredentialResolver credentials,
        AiImageBillingReconciliationItem item,
        int maxAttempts,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.ProviderTaskId))
        {
            await billing.MarkManualReviewAsync(
                item.LogicalRequestId,
                "Image billing reconciliation cannot verify provider state because provider_task_id is missing.",
                ct);
            _logger.LogWarning("AI_IMAGE_RECONCILIATION_MANUAL_REVIEW logicalRequestId={LogicalRequestId} reason=missing_task_id", item.LogicalRequestId);
            return;
        }

        try
        {
            if (item.ProviderAccountId is not Guid providerAccountId)
            {
                await billing.MarkManualReviewAsync(item.LogicalRequestId, "YEScale reconciliation cannot resolve credentials because provider_account_id is missing.", ct);
                _logger.LogWarning("AI_IMAGE_RECONCILIATION_MANUAL_REVIEW logicalRequestId={LogicalRequestId} reason=missing_provider_account_id", item.LogicalRequestId);
                return;
            }

            var credential = await credentials.ResolveAsync(providerAccountId, ct: ct);
            var status = await tasks.GetStatusAsync(item.ProviderTaskId, credential.SecretValue, ct);
            var usageJson = JsonSerializer.Serialize(new
            {
                taskId = item.ProviderTaskId,
                reconciliation = true,
                status = status.Status
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (status.IsSuccess)
            {
                await billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = item.LogicalRequestId,
                    Success = true,
                    ActualModel = item.ActualModel ?? item.RequestedModel,
                    ProviderTaskId = item.ProviderTaskId,
                    ProviderUsageJson = usageJson,
                    TariffSnapshotJson = item.TariffSnapshotJson
                }, ct);
                _logger.LogInformation("AI_IMAGE_RECONCILIATION_COMPLETED logicalRequestId={LogicalRequestId} taskId={TaskId}", item.LogicalRequestId, item.ProviderTaskId);
                return;
            }

            if (status.IsFailure)
            {
                await billing.CompleteAsync(new AiImageBillingCompleteRequest
                {
                    LogicalRequestId = item.LogicalRequestId,
                    Success = false,
                    ActualModel = item.ActualModel ?? item.RequestedModel,
                    ProviderTaskId = item.ProviderTaskId,
                    ProviderUsageJson = usageJson,
                    TariffSnapshotJson = item.TariffSnapshotJson,
                    ErrorMessage = "YEScale task ended in FAILURE during reconciliation."
                }, ct);
                _logger.LogWarning("AI_IMAGE_RECONCILIATION_RELEASED logicalRequestId={LogicalRequestId} taskId={TaskId}", item.LogicalRequestId, item.ProviderTaskId);
                return;
            }

            await billing.RescheduleReconciliationAsync(
                item.LogicalRequestId,
                $"YEScale task still pending: {status.Status}",
                TimeSpan.FromMinutes(Math.Min(30, item.ReconciliationAttemptCount * 2)),
                ct);
        }
        catch (YEScaleTaskException ex) when (ex.StatusCode is 401 or 403)
        {
            await billing.MarkManualReviewAsync(item.LogicalRequestId, "YEScale reconciliation is unauthorized. Check credentials/permissions.", ct);
            _logger.LogError(ex, "AI_IMAGE_RECONCILIATION_AUTH_FAILED logicalRequestId={LogicalRequestId} taskId={TaskId}", item.LogicalRequestId, item.ProviderTaskId);
        }
        catch (YEScaleTaskException ex) when (ex.IsTransient || ex.StatusCode is 408 or 429 || ex.StatusCode >= 500)
        {
            if (item.ReconciliationAttemptCount >= maxAttempts)
            {
                await billing.MarkManualReviewAsync(item.LogicalRequestId, "YEScale reconciliation exceeded max attempts.", ct);
                return;
            }

            await billing.RescheduleReconciliationAsync(
                item.LogicalRequestId,
                $"Transient YEScale reconciliation error: {ex.ErrorCode ?? ex.StatusCode?.ToString() ?? ex.GetType().Name}",
                TimeSpan.FromMinutes(Math.Min(30, item.ReconciliationAttemptCount * 2)),
                ct);
        }
    }
}
