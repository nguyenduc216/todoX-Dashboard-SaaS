using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseCoreLifecycleBridge
{
    Task ReportProgressAsync(
        TimelapseJobSnapshot snapshot,
        string step,
        int progressPercent,
        string message,
        CancellationToken ct = default);

    Task AdvanceAsync(
        Guid legacyJobId,
        Guid? userId,
        Guid? customerId,
        TimelapseJobSnapshot snapshot,
        CancellationToken ct = default);

    Task CompleteAsync(
        TimelapseFinalizerWorkItem item,
        Guid mediaId,
        string objectKey,
        string publicUrl,
        CancellationToken ct = default);

    Task<bool> ReconcileCompletionAsync(CancellationToken ct = default);

    Task FailAsync(
        Guid legacyJobId,
        TimelapseJobSnapshot snapshot,
        string? errorCode,
        string errorMessage,
        CoreFailureBillingPolicy billingPolicy,
        CancellationToken ct = default);
}

public sealed class TimelapseCoreLifecycleBridge : ITimelapseCoreLifecycleBridge
{
    private static readonly CoreExecutionAuthority Authority =
        CoreExecutionAuthority.Trusted(nameof(TimelapseCoreLifecycleBridge));

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ITimelapseWorkflowService _workflow;
    private readonly ICoreJobCompletionService _completion;
    private readonly ILogger<TimelapseCoreLifecycleBridge> _logger;

    public TimelapseCoreLifecycleBridge(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ITimelapseWorkflowService workflow,
        ICoreJobCompletionService completion,
        ILogger<TimelapseCoreLifecycleBridge> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _workflow = workflow;
        _completion = completion;
        _logger = logger;
    }

    public async Task ReportProgressAsync(
        TimelapseJobSnapshot snapshot,
        string step,
        int progressPercent,
        string message,
        CancellationToken ct = default)
    {
        if (snapshot.CoreJobId is not Guid coreJobId)
        {
            return;
        }

        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            var current = await conn.QuerySingleOrDefaultAsync<CoreProgressRow>(
                """
                SELECT status AS Status,
                       progress_percent AS ProgressPercent
                  FROM render.render_jobs
                 WHERE id=@coreJobId
                   AND tenant_id=@tenant
                   AND job_type=@jobType
                 LIMIT 1;
                """,
                new
                {
                    coreJobId,
                    tenant = _tenant.TenantId,
                    jobType = RenderJobTypes.CoreService
                });
            if (current is null
                || current.Status is RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled
                || current.ProgressPercent >= progressPercent)
            {
                return;
            }

            await _completion.MarkProgressAsync(
                Authority,
                new CoreJobProgressRequest(
                    coreJobId,
                    step,
                    progressPercent,
                    message,
                    JsonSerializer.SerializeToElement(new
                    {
                        service_code = ConstructionTimelapseAdapter.ConstructionServiceCode
                    })),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TIMELAPSE_CORE_PROGRESS_FAILED coreJobId={CoreJobId} step={Step} progress={Progress}",
                coreJobId,
                step,
                progressPercent);
        }
    }

    public async Task AdvanceAsync(
        Guid legacyJobId,
        Guid? userId,
        Guid? customerId,
        TimelapseJobSnapshot snapshot,
        CancellationToken ct = default)
    {
        if (snapshot.CoreJobId is null)
        {
            return;
        }

        try
        {
            var state = await _workflow.GetStateAsync(legacyJobId, ct);
            if (string.Equals(
                    state.ParentStatus,
                    TimelapseParentStatuses.Failed,
                    StringComparison.OrdinalIgnoreCase))
            {
                await FailAsync(
                    legacyJobId,
                    snapshot,
                    null,
                    string.Empty,
                    CoreFailureBillingPolicy.ReleaseReservation,
                    ct);
                return;
            }

            var generatedImages = state.Images.Where(x => !x.IsOriginal).ToArray();
            if (generatedImages.Length > 0
                && generatedImages.All(x => TimelapseOperationStatuses.IsCurrentCompleted(x.Status)))
            {
                await ReportProgressAsync(
                    snapshot,
                    "images_ready",
                    45,
                    "Construction Timelapse images are ready.",
                    ct);
                await ReportProgressAsync(
                    snapshot,
                    "video_generation",
                    60,
                    "Construction Timelapse video generation started.",
                    ct);
            }

            if (!state.CanFinalize)
            {
                return;
            }

            await ReportProgressAsync(
                snapshot,
                "post_processing",
                85,
                "Construction Timelapse clips are ready for final processing.",
                ct);
            try
            {
                await _workflow.StartFinalizerAsync(
                    legacyJobId,
                    snapshot,
                    BuildLegacyCustomerSession(userId, customerId),
                    ct);
            }
            catch (InvalidOperationException)
            {
                var latest = await _workflow.GetStateAsync(legacyJobId, ct);
                if (latest.FinalOutput?.Status is not (
                        TimelapseOperationStatuses.Waiting
                        or TimelapseOperationStatuses.Rendering
                        or TimelapseOperationStatuses.Completed))
                {
                    throw;
                }
            }

            await ReportProgressAsync(
                snapshot,
                "finalizing",
                95,
                "Construction Timelapse final video is being assembled.",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TIMELAPSE_CORE_ADVANCE_FAILED coreJobId={CoreJobId} legacyJobId={LegacyJobId}",
                snapshot.CoreJobId,
                legacyJobId);
        }
    }

    public async Task CompleteAsync(
        TimelapseFinalizerWorkItem item,
        Guid mediaId,
        string objectKey,
        string publicUrl,
        CancellationToken ct = default)
    {
        if (item.Snapshot.CoreJobId is not Guid coreJobId)
        {
            return;
        }

        _ = await TryCompleteAsync(
            coreJobId,
            item.JobId,
            mediaId,
            objectKey,
            publicUrl,
            ct);
    }

    public async Task<bool> ReconcileCompletionAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CoreCompletionReconciliationRow>(
            """
            SELECT core.id AS CoreJobId,
                   legacy.id AS LegacyJobId,
                   final.result_media_id AS MediaId,
                   final.object_key AS ObjectKey,
                   final.public_url AS PublicUrl
              FROM render.render_jobs legacy
              JOIN timelapse.timelapse_final_outputs final
                ON final.job_id=legacy.id
               AND final.status='COMPLETED'
              JOIN render.render_jobs core
                ON core.id::text=legacy.input_json->>'coreJobId'
               AND core.tenant_id=legacy.tenant_id
               AND core.job_type=@coreJobType
             WHERE legacy.tenant_id=@tenant
               AND legacy.job_type=@legacyJobType
               AND legacy.input_json ? 'coreJobId'
               AND core.status NOT IN ('completed','failed','cancelled')
               AND final.result_media_id IS NOT NULL
               AND NULLIF(final.object_key, '') IS NOT NULL
               AND NULLIF(final.public_url, '') IS NOT NULL
             ORDER BY final.completed_at, final.id
             LIMIT 1;
            """,
            new
            {
                tenant = _tenant.TenantId,
                coreJobType = RenderJobTypes.CoreService,
                legacyJobType = RenderJobTypes.Timelapse
            });
        if (row is null)
        {
            var failure = await conn.QuerySingleOrDefaultAsync<CoreFailureReconciliationRow>(
                """
                SELECT core.id AS CoreJobId,
                       legacy.id AS LegacyJobId,
                       legacy.error_code AS ErrorCode,
                       legacy.error_message AS ErrorMessage,
                       EXISTS (
                           SELECT 1
                             FROM timelapse.timelapse_final_outputs final
                            WHERE final.job_id=legacy.id
                              AND final.status='FAILED'
                       ) AS FinalizerFailed,
                       EXISTS (
                           SELECT 1
                             FROM timelapse.timelapse_image_stages image_stage
                            WHERE image_stage.job_id=legacy.id
                              AND image_stage.status='FAILED'
                              AND NULLIF(image_stage.provider_task_id, '') IS NOT NULL
                           UNION ALL
                           SELECT 1
                             FROM timelapse.timelapse_video_clips video_clip
                            WHERE video_clip.job_id=legacy.id
                              AND video_clip.status='FAILED'
                              AND NULLIF(video_clip.provider_task_id, '') IS NOT NULL
                       ) AS ProviderTaskStarted
                  FROM render.render_jobs legacy
                  JOIN render.render_jobs core
                    ON core.id::text=legacy.input_json->>'coreJobId'
                   AND core.tenant_id=legacy.tenant_id
                   AND core.job_type=@coreJobType
                 WHERE legacy.tenant_id=@tenant
                   AND legacy.job_type=@legacyJobType
                   AND legacy.status='FAILED'
                   AND legacy.input_json ? 'coreJobId'
                   AND core.status NOT IN ('completed','failed','cancelled')
                 ORDER BY legacy.updated_at, legacy.id
                 LIMIT 1;
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    coreJobType = RenderJobTypes.CoreService,
                    legacyJobType = RenderJobTypes.Timelapse
                });
            if (failure is null)
            {
                return false;
            }

            return await TryFailAsync(
                failure.CoreJobId,
                failure.LegacyJobId,
                failure.ErrorCode,
                failure.ErrorMessage,
                failure.FinalizerFailed || failure.ProviderTaskStarted
                    ? CoreFailureBillingPolicy.KeepCharge
                    : CoreFailureBillingPolicy.ReleaseReservation,
                ct);
        }

        return await TryCompleteAsync(
            row.CoreJobId,
            row.LegacyJobId,
            row.MediaId,
            row.ObjectKey,
            row.PublicUrl,
            ct);
    }

    public async Task FailAsync(
        Guid legacyJobId,
        TimelapseJobSnapshot snapshot,
        string? errorCode,
        string errorMessage,
        CoreFailureBillingPolicy billingPolicy,
        CancellationToken ct = default)
    {
        if (snapshot.CoreJobId is not Guid coreJobId)
        {
            return;
        }

        try
        {
            await _tenant.EnsureLoadedAsync(ct);
            using var conn = await _factory.OpenAsync(ct);
            var failure = await conn.QuerySingleOrDefaultAsync<LegacyFailureRow>(new CommandDefinition(
                """
                SELECT j.status AS Status,
                       j.error_code AS ErrorCode,
                       j.error_message AS ErrorMessage,
                       EXISTS (
                           SELECT 1
                             FROM timelapse.timelapse_image_stages image_stage
                            WHERE image_stage.job_id=j.id
                              AND image_stage.status='FAILED'
                              AND NULLIF(image_stage.provider_task_id, '') IS NOT NULL
                           UNION ALL
                           SELECT 1
                             FROM timelapse.timelapse_video_clips video_clip
                            WHERE video_clip.job_id=j.id
                              AND video_clip.status='FAILED'
                              AND NULLIF(video_clip.provider_task_id, '') IS NOT NULL
                       ) AS ProviderTaskStarted
                  FROM render.render_jobs j
                 WHERE j.id=@legacyJobId
                   AND j.tenant_id=@tenant
                   AND j.job_type=@jobType
                 LIMIT 1;
                """,
                new
                {
                    legacyJobId,
                    tenant = _tenant.TenantId,
                    jobType = RenderJobTypes.Timelapse
                },
                cancellationToken: ct));
            if (failure is null
                || !string.Equals(
                    failure.Status,
                    TimelapseParentStatuses.Failed,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var effectiveErrorCode = string.IsNullOrWhiteSpace(errorCode)
                ? failure.ErrorCode
                : errorCode;
            var effectiveErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? failure.ErrorMessage
                : errorMessage;
            var effectiveBillingPolicy = ResolveFailurePolicy(
                billingPolicy,
                failure.ProviderTaskStarted);
            _ = await TryFailAsync(
                coreJobId,
                legacyJobId,
                effectiveErrorCode,
                effectiveErrorMessage,
                effectiveBillingPolicy,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TIMELAPSE_CORE_FAILURE_BRIDGE_FAILED coreJobId={CoreJobId} billingPolicy={BillingPolicy}",
                coreJobId,
                billingPolicy);
        }
    }

    internal static CoreFailureBillingPolicy ResolveFailurePolicy(
        CoreFailureBillingPolicy requestedPolicy,
        bool providerTaskStarted)
        => requestedPolicy == CoreFailureBillingPolicy.ReleaseReservation && providerTaskStarted
            ? CoreFailureBillingPolicy.KeepCharge
            : requestedPolicy;

    private static CurrentUserSession BuildLegacyCustomerSession(Guid? userId, Guid? customerId)
        => new()
        {
            UserId = userId ?? Guid.Empty,
            CustomerId = customerId,
            Role = TodoXUserRole.CustomerUser,
            IsAuthenticated = true,
            DisplayName = "Core Construction Timelapse"
        };

    private async Task<bool> TryCompleteAsync(
        Guid coreJobId,
        Guid legacyJobId,
        Guid mediaId,
        string objectKey,
        string publicUrl,
        CancellationToken ct)
    {
        try
        {
            var output = BuildOutput(legacyJobId, mediaId, objectKey, publicUrl);
            await _completion.CompleteAsync(
                Authority,
                new CoreJobCompleteRequest(
                    coreJobId,
                    output,
                    "Construction Timelapse final video completed."),
                ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TIMELAPSE_CORE_COMPLETION_FAILED coreJobId={CoreJobId} legacyJobId={LegacyJobId}",
                coreJobId,
                legacyJobId);
            return false;
        }
    }

    private async Task<bool> TryFailAsync(
        Guid coreJobId,
        Guid legacyJobId,
        string? errorCode,
        string? errorMessage,
        CoreFailureBillingPolicy billingPolicy,
        CancellationToken ct)
    {
        try
        {
            await _completion.FailAsync(
                Authority,
                new CoreJobFailRequest(
                    coreJobId,
                    string.IsNullOrWhiteSpace(errorCode)
                        ? "timelapse_failed"
                        : errorCode.Trim(),
                    string.IsNullOrWhiteSpace(errorMessage)
                        ? "Construction Timelapse execution failed."
                        : errorMessage.Trim(),
                    billingPolicy),
                ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "TIMELAPSE_CORE_FAILURE_BRIDGE_FAILED coreJobId={CoreJobId} legacyJobId={LegacyJobId} billingPolicy={BillingPolicy}",
                coreJobId,
                legacyJobId,
                billingPolicy);
            return false;
        }
    }

    internal static JsonElement BuildOutput(
        Guid legacyJobId,
        Guid mediaId,
        string objectKey,
        string publicUrl)
        => JsonSerializer.SerializeToElement(new
        {
            outputs = new[]
            {
                new
                {
                    type = "video",
                    url = publicUrl,
                    mime_type = "video/mp4",
                    thumbnail_url = (string?)null,
                    metadata = new
                    {
                        media_id = mediaId,
                        object_key = objectKey,
                        legacy_job_id = legacyJobId,
                        legacy_job_uuid = legacyJobId,
                        service_code = ConstructionTimelapseAdapter.ConstructionServiceCode
                    }
                }
            }
        });

    private sealed class CoreProgressRow
    {
        public string Status { get; init; } = string.Empty;
        public int ProgressPercent { get; init; }
    }

    private sealed class CoreCompletionReconciliationRow
    {
        public Guid CoreJobId { get; init; }
        public Guid LegacyJobId { get; init; }
        public Guid MediaId { get; init; }
        public string ObjectKey { get; init; } = string.Empty;
        public string PublicUrl { get; init; } = string.Empty;
    }

    private sealed class LegacyFailureRow
    {
        public string Status { get; init; } = string.Empty;
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public bool ProviderTaskStarted { get; init; }
    }

    private sealed class CoreFailureReconciliationRow
    {
        public Guid CoreJobId { get; init; }
        public Guid LegacyJobId { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public bool FinalizerFailed { get; init; }
        public bool ProviderTaskStarted { get; init; }
    }
}
