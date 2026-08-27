using System.Text.Json;
using Npgsql;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.VideoRender;

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
                if (IsMissingBillingTable(ex))
                {
                    _logger.LogWarning(ex, "AI_IMAGE_RECONCILIATION_DISABLED_MISSING_SCHEMA");
                    return;
                }

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
        var videoService = scope.ServiceProvider.GetRequiredService<IRVideo79AiVideoService>();
        var versions = scope.ServiceProvider.GetRequiredService<ISceneMediaVersioningService>();
        var completion = scope.ServiceProvider.GetRequiredService<IRVideoSceneVideoCompletionService>();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();

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
            await ReconcileItemAsync(billing, tasks, versions, completion, videoService, tenant, item, maxAttempts, ct);
        }
    }

    private async Task ReconcileItemAsync(
        IAiImageBillingService billing,
        IYEScaleTaskClient tasks,
        ISceneMediaVersioningService versions,
        IRVideoSceneVideoCompletionService completion,
        IRVideo79AiVideoService videoService,
        TenantContext tenant,
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

        if (string.Equals(item.ProviderCode, "79ai", StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.CapabilityCode, "rvideo_scene_video_generation", StringComparison.OrdinalIgnoreCase))
        {
            await Reconcile79AiVideoAsync(billing, versions, completion, videoService, tenant, item, ct);
            return;
        }

        try
        {
            var status = await tasks.GetStatusAsync(item.ProviderTaskId, ct);
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

    private async Task Reconcile79AiVideoAsync(
        IAiImageBillingService billing,
        ISceneMediaVersioningService versions,
        IRVideoSceneVideoCompletionService completion,
        IRVideo79AiVideoService videoService,
        TenantContext tenant,
        AiImageBillingReconciliationItem item,
        CancellationToken ct)
    {
        var version = await versions.GetSceneVideoVersionByLogicalRequestIdAsync(item.LogicalRequestId, ct);
        if (version is null)
        {
            await billing.MarkManualReviewAsync(item.LogicalRequestId, "79AI video reconciliation could not locate a recoverable scene video version.", ct);
            return;
        }

        var runtime = await videoService.ResolveRuntimeAsync(item.ProviderId, item.ProviderCapabilityId, item.ProviderCode!, ct);
        var poll = await videoService.PollAsync(runtime, item.ProviderTaskId!, ct);

        if (string.Equals(poll.NormalizedStatus, Ai79TaskStatusNormalizer.Running, StringComparison.OrdinalIgnoreCase))
        {
            await billing.RescheduleReconciliationAsync(item.LogicalRequestId, $"79AI video task still pending: {poll.NormalizedStatus}", TimeSpan.FromMinutes(Math.Min(30, item.ReconciliationAttemptCount * 2)), ct);
            return;
        }

        if (string.Equals(poll.NormalizedStatus, Ai79TaskStatusNormalizer.Failed, StringComparison.OrdinalIgnoreCase))
        {
            await billing.CompleteAsync(new AiImageBillingCompleteRequest
            {
                LogicalRequestId = item.LogicalRequestId,
                Success = false,
                ActualModel = item.ActualModel ?? item.RequestedModel,
                ProviderTaskId = item.ProviderTaskId,
                ProviderUsageJson = poll.SanitizedResponseJson,
                TariffSnapshotJson = item.TariffSnapshotJson,
                ErrorMessage = poll.ErrorMessage ?? "79AI video task failed during reconciliation."
            }, ct);
            if (!string.IsNullOrWhiteSpace(version.Status))
            {
                await versions.FailSceneVideoVersionAsync(version.Id, poll.ErrorCode ?? "provider_failure", poll.ErrorMessage ?? "79AI video task failed during reconciliation.", ct);
            }
            return;
        }

        var outputUrl = poll.OutputUrl;
        if (string.IsNullOrWhiteSpace(outputUrl))
        {
            await billing.MarkManualReviewAsync(item.LogicalRequestId, "79AI video task succeeded but did not return an output URL.", ct);
            return;
        }

        await completion.CompleteProviderVideoAsync(new RVideoSceneVideoCompletionRequest(
            version.ProjectId,
            version.SceneId,
            SceneIndex: 0,
            version.Id,
            version.RenderJobId,
            version.StorageKey,
            item.LogicalRequestId,
            item.ProviderTaskId!,
            outputUrl,
            item.ProviderCode,
            item.ActualModel ?? item.RequestedModel,
            item.ProviderCapabilityId,
            poll.SanitizedResponseJson,
            item.TariffSnapshotJson,
            item.CustomerChargedPoints,
            version.EstimatedUsd,
            version.CostSource,
            version.AspectRatio,
            version.PosterUrl,
            version.DurationSeconds,
            UserId: null,
            CustomerId: null,
            IsRecovery: true), ct);
        _logger.LogInformation("AI_IMAGE_RECONCILIATION_COMPLETED logicalRequestId={LogicalRequestId} taskId={TaskId}", item.LogicalRequestId, item.ProviderTaskId);
    }

    private static bool IsMissingBillingTable(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                string.Equals(postgres.SqlState, PostgresErrorCodes.UndefinedTable, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
