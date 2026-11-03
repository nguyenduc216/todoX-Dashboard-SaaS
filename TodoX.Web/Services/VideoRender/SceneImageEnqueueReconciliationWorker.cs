using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.VideoRender;

// RVID-UI-V2-SCENE-RERENDER-HISTORY / TODOX-20261015-RVIDEO-RECONCILIATION-IMPL-001.
// This worker never submits provider work or enqueues render jobs. Positive handoff is
// intentionally disabled until job-writer serialization is independently approved.
public sealed class SceneImageEnqueueReconciliationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SceneImageEnqueueReconciliationWorker> _logger;

    public SceneImageEnqueueReconciliationWorker(IServiceScopeFactory scopes, IConfiguration configuration,
        ILogger<SceneImageEnqueueReconciliationWorker> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("ImageEnqueueReconciliation:Enabled", false)) return;
        var intervalSeconds = _configuration.GetValue<int>("ImageEnqueueReconciliation:IntervalSeconds");
        var leaseSeconds = _configuration.GetValue<int>("ImageEnqueueReconciliation:LeaseSeconds");
        var backoffSeconds = _configuration.GetValue<int>("ImageEnqueueReconciliation:BackoffSeconds");
        var batchSize = _configuration.GetValue<int>("ImageEnqueueReconciliation:BatchSize");
        if (intervalSeconds <= 0 || leaseSeconds <= 0 || backoffSeconds <= 0 || batchSize is < 1 or > 100)
        {
            _logger.LogError("Image enqueue reconciliation disabled: invalid or missing scheduling configuration");
            return;
        }
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
                await tenant.EnsureLoadedAsync(stoppingToken);
                var repository = new SceneImageEnqueueReconciliationRepository(
                    scope.ServiceProvider.GetRequiredService<TodoXConnectionFactory>(), tenant);
                var lease = TimeSpan.FromSeconds(leaseSeconds);
                var backoff = TimeSpan.FromSeconds(backoffSeconds);
                for (var i = 0; i < batchSize; i++)
                {
                    var claim = await repository.ClaimAsync(lease, stoppingToken);
                    if (claim is null) break;
                    try
                    {
                        await repository.ReconcileAsync(claim, backoff, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        // The lease expires on failure; a later worker can recover the claim.
                        _logger.LogError(ex, "Image enqueue reconciliation claim failed versionId={VersionId}", claim.VersionId);
                    }
                }
                var pendingManual = await repository.CountManualBacklogAsync(stoppingToken);
                if (pendingManual > 0)
                    _logger.LogWarning("Image enqueue manual verification backlog count={Count}", pendingManual);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Npgsql.PostgresException ex) when (ex.SqlState is "42P01" or "42703")
            {
                _logger.LogError(ex, "Image enqueue reconciliation disabled: required database schema is missing");
                return;
            }
            catch (Exception ex) { _logger.LogError(ex, "Image enqueue reconciliation scan failed"); }
            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

internal sealed record ImageEnqueueClaim(Guid VersionId, Guid Token);

internal sealed record ImageEnqueueDecision(bool Manual, string? Reason);

internal static class ImageEnqueueReconciliationPolicy
{
    internal static ImageEnqueueDecision Decide(int jobCount, bool deadlineExpired, int invalidCount = 0)
    {
        if (invalidCount > 0) return new(true, "AMBIGUOUS_JOB_IDENTITY");
        if (jobCount > 1) return new(true, "DUPLICATE_JOB_IDENTITY");
        if (jobCount == 1) return new(true, "POSITIVE_HANDOFF_UNAPPROVED");
        if (deadlineExpired) return new(true, "DEADLINE_EXPIRED");
        return new(false, null);
    }
}

internal sealed class SceneImageEnqueueReconciliationRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    internal SceneImageEnqueueReconciliationRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    internal async Task<ImageEnqueueClaim?> ClaimAsync(TimeSpan lease, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var token = Guid.NewGuid();
        // A single conditional UPDATE locks the selected row; SKIP LOCKED excludes rival workers.
        var id = await conn.ExecuteScalarAsync<Guid?>("""
            UPDATE video_render.scene_image_versions v
               SET enqueue_reconcile_claim_token=@token,
                   enqueue_reconcile_lease_until=now() + @lease
             WHERE v.id=(SELECT id FROM video_render.scene_image_versions
                          WHERE tenant_id=@tenant AND status='pending_reconciliation'
                            AND error_code='RENDER_JOB_ENQUEUE_UNKNOWN'
                            AND enqueue_reconcile_manual_at IS NULL
                            AND enqueue_reconcile_handed_off_at IS NULL
                            AND enqueue_reconcile_after <= now()
                            AND enqueue_reconcile_deadline IS NOT NULL
                            AND (enqueue_reconcile_lease_until IS NULL OR enqueue_reconcile_lease_until < now())
                          ORDER BY enqueue_reconcile_after, id
                          FOR UPDATE SKIP LOCKED LIMIT 1)
               AND v.tenant_id=@tenant
             RETURNING v.id;
            """, new { tenant = _tenant.TenantId, token, lease }, tx);
        tx.Commit();
        return id is null ? null : new ImageEnqueueClaim(id.Value, token);
    }

    internal async Task ReconcileAsync(ImageEnqueueClaim claim, TimeSpan backoff, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        // Read-only lookup before the fenced write; an empty result is never proof of non-enqueue.
        var evidence = await conn.QuerySingleAsync<(int total, int invalid)>("""
            SELECT count(*)::int AS total,
                   count(*) FILTER (WHERE (
                       j.input_json->>'projectId'=v.project_id::text
                       AND j.input_json->>'sceneId'=v.scene_id::text
                       AND j.input_json->>'logicalRequestId'=v.logical_request_id
                       AND j.input_json->>'imageVersionId'=v.id::text
                   ) IS NOT TRUE)::int AS invalid
              FROM video_render.scene_image_versions v
              JOIN render.render_jobs j ON j.tenant_id=v.tenant_id
                 AND j.job_type='render_scene_image'
                 AND j.input_json->>'imageVersionId'=@versionId
             WHERE v.id=@id AND v.tenant_id=@tenant;
            """, new { tenant = _tenant.TenantId, id = claim.VersionId, versionId = claim.VersionId.ToString() });
        // A positive result cannot hand off without a DB-enforced writer serialization gate.
        var positiveDecision = ImageEnqueueReconciliationPolicy.Decide(evidence.total, false, evidence.invalid);
        await conn.ExecuteAsync("""
            UPDATE video_render.scene_image_versions
               SET enqueue_reconcile_attempts=COALESCE(enqueue_reconcile_attempts,0)+1,
                   enqueue_reconcile_last_checked_at=now(),
                    enqueue_reconcile_manual_at=CASE WHEN now() >= enqueue_reconcile_deadline OR @manual
                                                    THEN now() ELSE enqueue_reconcile_manual_at END,
                    enqueue_reconcile_alert_type=CASE WHEN now() >= enqueue_reconcile_deadline OR @manual
                                                      THEN COALESCE(@reason, 'DEADLINE_EXPIRED') ELSE enqueue_reconcile_alert_type END,
                    enqueue_reconcile_alert_next_at=CASE WHEN now() >= enqueue_reconcile_deadline OR @manual
                                                        THEN now() ELSE enqueue_reconcile_alert_next_at END,
                   enqueue_reconcile_after=LEAST(now() + @backoff, enqueue_reconcile_deadline),
                   enqueue_reconcile_claim_token=NULL, enqueue_reconcile_lease_until=NULL
             WHERE id=@versionId AND tenant_id=@tenant AND status='pending_reconciliation'
               AND error_code='RENDER_JOB_ENQUEUE_UNKNOWN' AND enqueue_reconcile_manual_at IS NULL
               AND enqueue_reconcile_claim_token=@token AND enqueue_reconcile_lease_until > now();
            """, new { tenant = _tenant.TenantId, versionId = claim.VersionId, claim.Token, backoff, manual = positiveDecision.Manual, reason = positiveDecision.Reason });
    }

    internal async Task<int> CountManualBacklogAsync(CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM video_render.scene_image_versions
             WHERE tenant_id=@tenant AND enqueue_reconcile_manual_at IS NOT NULL
               AND enqueue_reconcile_alert_ack_at IS NULL;
            """, new { tenant = _tenant.TenantId });
    }
}