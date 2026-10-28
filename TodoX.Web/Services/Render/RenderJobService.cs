using System.Text.Json;
using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services;

namespace TodoX.Web.Services.Render;

public interface IRenderJobService
{
    Task<RenderJobDto> EnqueueAsync(RenderJobCreateModel model, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a job only when no active job of the same type already exists for the given project.
    /// Serialised by a PostgreSQL transaction-level advisory lock keyed on (jobType, projectId) so two
    /// concurrent requests cannot both create a batch. Returns the existing active job (AlreadyActive=true)
    /// instead of creating a duplicate.
    /// </summary>
    Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForProjectIfNoneActiveAsync(RenderJobCreateModel model, long projectId, CancellationToken ct = default);

    Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForLogCodeIfNoneActiveAsync(RenderJobCreateModel model, string logCode, CancellationToken ct = default);
    Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForSceneIfNoneActiveAsync(RenderJobCreateModel model, long sceneId, string? logicalRequestId = null, CancellationToken ct = default);
    Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueGuardedVideoRerenderAsync(RenderJobCreateModel model, long projectId, long sceneId, Guid? acknowledgedVersionId, Guid? acknowledgedRenderJobId, bool explicitRiskAcknowledgement, CancellationToken ct = default)
        => throw new NotSupportedException("Guarded video rerender requires the canonical database implementation.");

    Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<RenderJobDto?> GetByLogCodeAsync(string logCode, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobDto>> ListByLogCodeAsync(string logCode, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobEventDto>> GetEventsAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobEventDto>> GetEventsByLogCodeAsync(string logCode, CancellationToken ct = default);
    Task AddEventAsync(Guid jobId, string eventType, string message, object? data = null, string level = "info", CancellationToken ct = default);
    Task<int> GetProviderReconciliationAttemptCountAsync(Guid jobId, CancellationToken ct = default);
    Task<bool> CancelAsync(Guid jobId, string reason, Guid? userId = null, CancellationToken ct = default);
    Task<RenderJobDto?> RetryAsync(Guid jobId, Guid? userId = null, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextAsync(string workerKey, TimeSpan lockFor, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextByJobTypeAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> jobTypes, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextExcludingJobTypesAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> excludedJobTypes, CancellationToken ct = default);
    Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default);
    Task MarkSceneVideoClaimRenderingAsync(RenderJobDto claim, string workerKey, CancellationToken ct = default);
    Task VerifySceneVideoSubmitClaimAsync(RenderJobDto claim, Guid versionId, string logicalRequestId, CancellationToken ct = default);
    Task<bool> QuarantineRejectedSceneVideoClaimAsync(RenderJobDto claim, string workerKey, string reason, CancellationToken ct = default);
    Task ScheduleRetryAsync(Guid jobId, TimeSpan delay, string errorCode, string errorMessage, CancellationToken ct = default);
    Task<bool> ScheduleProviderPollAsync(
        Guid jobId,
        TimeSpan delay,
        string reasonCode,
        string reasonMessage,
        CancellationToken ct = default,
        bool enforceReconciliationLimit = true,
        bool enforceProviderPollTimeout = false);

    /// <summary>
    /// Recovery-safe persistent reconciliation path. Atomic guards: retry_after must be due (NULL or &lt;= now())
    /// and lock must not be live (lock_until IS NULL OR &lt;= now()). Does not emit event on no-op. Idempotent across instances.
    /// </summary>
    Task<bool> SchedulePersistentProviderPollAsync(
        Guid jobId,
        TimeSpan delay,
        string reasonCode,
        string reasonMessage,
        CancellationToken ct = default);
    Task SetProviderIdentifiersAsync(Guid jobId, string? providerTaskId, string? providerVideoIdBase, CancellationToken ct = default);
    Task<bool> MarkRecoveredCompletedAsync(Guid jobId, long projectId, long sceneId, Guid sceneVideoVersionId, string logicalRequestId, CancellationToken ct = default);
    Task UpsertSnapshotAsync(Guid jobId, object projectSnapshot, object sceneSnapshots, CancellationToken ct = default);
}

public sealed class RenderJobService : IRenderJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string LegacyStorageCollisionErrorCode = "RenderJobTerminalFailureException";

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<RenderJobService> _logger;
    private readonly IConfiguration _configuration;

    public RenderJobService(TodoXConnectionFactory factory, TenantContext tenant, IConfiguration configuration, ILogger<RenderJobService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<RenderJobDto> EnqueueAsync(RenderJobCreateModel model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(model));
        }

        await _tenant.EnsureLoadedAsync(ct);
        var initialStatus = NormalizeInitialStatus(model.InitialStatus);
        var inputJson = ToJson(model.Input ?? new { });
        var promptJson = ToJson(model.Prompt ?? new { });
        var referenceJson = ToJson(model.References ?? Array.Empty<object>());
        var pointCostEstimate = LegacyPointBillingFeatureFlags.NormalizePointCostEstimate(_configuration, model.PointCostEstimate);
        var pointStatus = LegacyPointBillingFeatureFlags.NormalizePointStatus(_configuration, model.PointStatus, pointCostEstimate);

        using var conn = await _factory.OpenAsync(ct);
        if (model.JobType.Trim() == RenderJobTypes.RenderSceneVideo)
        {
            using var snapshot = JsonDocument.Parse(inputJson);
            if (snapshot.RootElement.ValueKind != JsonValueKind.Object
                || !snapshot.RootElement.TryGetProperty("sceneId", out var sceneValue)
                || !sceneValue.TryGetInt64(out var sceneId)
                || !snapshot.RootElement.TryGetProperty("projectId", out var projectValue)
                || !projectValue.TryGetInt64(out var projectId)
                || sceneId <= 0 || projectId <= 0)
                throw new InvalidOperationException("Scene video job requires a valid project and scene snapshot.");

            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
                new { lockName = BuildSceneJobLockName(RenderJobTypes.RenderSceneVideo, sceneId) }, tx);
            await EnsureSceneVideoEnqueueAllowedAsync(conn, tx, sceneId, projectId);
            var created = await conn.QuerySingleAsync<RenderJobDto>(InsertJobSql, new
            {
                tenant = _tenant.TenantId, user = model.UserId, customer = model.CustomerId,
                type = RenderJobTypes.RenderSceneVideo, status = initialStatus, priority = model.Priority,
                input = inputJson, prompt = promptJson, refs = referenceJson, logCode = model.LogCode,
                pointCost = pointCostEstimate, pointStatus, provider = model.ProviderCode,
                model = model.ModelCode, maxAttempts = Math.Max(1, model.MaxAttempts)
            }, tx);
            tx.Commit();
            await AddEventAsync(created.Id, initialStatus == RenderJobStatuses.Draft ? "JOB_CREATED" : "JOB_QUEUED",
                initialStatus == RenderJobStatuses.Draft ? "Render job draft saved." : "Render job queued.",
                new { created.JobType, created.Status, created.Priority, created.PointCostEstimate, created.PointStatus }, ct: ct);
            return created;
        }
        // customer_id must stay nullable for system/admin jobs (CurrentUser.CustomerId == null). We never
        // substitute a tenant/fake/Guid.Empty customer. If the deployed schema still has customer_id NOT NULL,
        // translate the raw 23502 into an actionable message telling the admin to run the DB sync script.
        var customerScope = model.CustomerId is null ? "system" : "customer";
        RenderJobDto job;
        try
        {
            job = await conn.QuerySingleAsync<RenderJobDto>(
            InsertJobSql,
            new
            {
                tenant = _tenant.TenantId,
                user = model.UserId,
                customer = model.CustomerId,
                type = model.JobType.Trim(),
                status = initialStatus,
                priority = model.Priority,
                input = inputJson,
                prompt = promptJson,
                refs = referenceJson,
                logCode = model.LogCode,
                pointCost = pointCostEstimate,
                pointStatus,
                provider = model.ProviderCode,
                model = model.ModelCode,
                maxAttempts = Math.Max(1, model.MaxAttempts)
            });
        }
        catch (PostgresException ex) when (IsRenderJobsCustomerIdNotNullViolation(ex))
        {
            // Safe log only — no connection string, credentials, or customer identifiers.
            _logger.LogError(ex,
                "RENDER_JOB_ENQUEUE_SCHEMA_MISMATCH jobType={JobType} userId={UserId} customerId={CustomerId} tenantId={TenantId} customerScope={CustomerScope} logCode={LogCode} projectId={ProjectId} sqlState={SqlState} schema={Schema} table={Table} column={Column}",
                model.JobType, model.UserId, model.CustomerId, _tenant.TenantId, customerScope, model.LogCode,
                ExtractProjectId(inputJson), ex.SqlState, ex.SchemaName, ex.TableName, ex.ColumnName);
            throw new InvalidOperationException(
                "Database render_jobs chưa đồng bộ: customer_id đang NOT NULL trong khi system/admin job không có customer. "
                + "Vui lòng chạy file SQL đồng bộ database do quản trị viên cung cấp.", ex);
        }

        var eventType = initialStatus == RenderJobStatuses.Draft ? "JOB_CREATED" : "JOB_QUEUED";
        var eventMessage = initialStatus == RenderJobStatuses.Draft ? "Render job draft saved." : "Render job queued.";
        await AddEventAsync(job.Id, eventType, eventMessage, new
        {
            job.JobType,
            job.Status,
            job.Priority,
            job.PointCostEstimate,
            job.PointStatus
        }, ct: ct);

        return job;
    }

    public async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForProjectIfNoneActiveAsync(RenderJobCreateModel model, long projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(model));
        }

        await _tenant.EnsureLoadedAsync(ct);
        var jobType = model.JobType.Trim();
        var initialStatus = NormalizeInitialStatus(model.InitialStatus);
        var pointCostEstimate = LegacyPointBillingFeatureFlags.NormalizePointCostEstimate(_configuration, model.PointCostEstimate);
        var pointStatus = LegacyPointBillingFeatureFlags.NormalizePointStatus(_configuration, model.PointStatus, pointCostEstimate);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        // Serialise concurrent enqueue attempts for the same (jobType, projectId). The advisory lock is
        // transaction-scoped, so it is released automatically on commit/rollback — no leak on error.
        await conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = BuildProjectJobLockName(jobType, projectId) },
            tx);

        var active = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(
            SelectJobSql +
            """
             WHERE job_type = @jobType
               AND status IN ('queued', 'preparing', 'rendering', 'post_processing', 'pending_reconciliation')
               AND (input_json->>'projectId') = @projectId
             ORDER BY queued_at DESC, created_at DESC
             LIMIT 1;
            """,
            new { jobType, projectId = projectId.ToString() }, tx);

        if (active is not null)
        {
            tx.Commit();
            return (active, true);
        }

        var inputJson = ToJson(model.Input ?? new { });
        var promptJson = ToJson(model.Prompt ?? new { });
        var referenceJson = ToJson(model.References ?? Array.Empty<object>());
        var customerScope = model.CustomerId is null ? "system" : "customer";

        RenderJobDto job;
        try
        {
            job = await conn.QuerySingleAsync<RenderJobDto>(
                InsertJobSql,
                new
                {
                    tenant = _tenant.TenantId,
                    user = model.UserId,
                    customer = model.CustomerId,
                    type = jobType,
                    status = initialStatus,
                    priority = model.Priority,
                    input = inputJson,
                    prompt = promptJson,
                    refs = referenceJson,
                    logCode = model.LogCode,
                    pointCost = pointCostEstimate,
                    pointStatus,
                    provider = model.ProviderCode,
                    model = model.ModelCode,
                    maxAttempts = Math.Max(1, model.MaxAttempts)
                }, tx);
        }
        catch (PostgresException ex) when (IsRenderJobsCustomerIdNotNullViolation(ex))
        {
            tx.Rollback();
            _logger.LogError(ex,
                "RENDER_JOB_ENQUEUE_SCHEMA_MISMATCH jobType={JobType} userId={UserId} customerId={CustomerId} tenantId={TenantId} customerScope={CustomerScope} logCode={LogCode} projectId={ProjectId} sqlState={SqlState} schema={Schema} table={Table} column={Column}",
                jobType, model.UserId, model.CustomerId, _tenant.TenantId, customerScope, model.LogCode,
                ExtractProjectId(inputJson), ex.SqlState, ex.SchemaName, ex.TableName, ex.ColumnName);
            throw new InvalidOperationException(
                "Database render_jobs chưa đồng bộ: customer_id đang NOT NULL trong khi system/admin job không có customer. "
                + "Vui lòng chạy file SQL đồng bộ database do quản trị viên cung cấp.", ex);
        }

        tx.Commit();

        var eventType = initialStatus == RenderJobStatuses.Draft ? "JOB_CREATED" : "JOB_QUEUED";
        var eventMessage = initialStatus == RenderJobStatuses.Draft ? "Render job draft saved." : "Render job queued.";
        await AddEventAsync(job.Id, eventType, eventMessage, new
        {
            job.JobType,
            job.Status,
            job.Priority,
            job.PointCostEstimate,
            job.PointStatus
        }, ct: ct);

        return (job, false);
    }

    public async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForLogCodeIfNoneActiveAsync(RenderJobCreateModel model, string logCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(logCode))
        {
            throw new ArgumentException("Log code is required.", nameof(logCode));
        }

        await _tenant.EnsureLoadedAsync(ct);
        var jobType = model.JobType.Trim();
        var uniqueLogCode = logCode.Trim();
        var initialStatus = NormalizeInitialStatus(model.InitialStatus);
        var pointCostEstimate = LegacyPointBillingFeatureFlags.NormalizePointCostEstimate(_configuration, model.PointCostEstimate);
        var pointStatus = LegacyPointBillingFeatureFlags.NormalizePointStatus(_configuration, model.PointStatus, pointCostEstimate);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = BuildLogCodeJobLockName(jobType, uniqueLogCode) },
            tx);

        var active = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(
            SelectJobSql +
            """
             WHERE job_type = @jobType
               AND log_code = @logCode
               AND status IN ('queued', 'preparing', 'rendering', 'post_processing', 'pending_reconciliation')
             ORDER BY queued_at DESC, created_at DESC
             LIMIT 1;
            """,
            new { jobType, logCode = uniqueLogCode }, tx);

        if (active is not null)
        {
            tx.Commit();
            return (active, true);
        }

        var inputJson = ToJson(model.Input ?? new { });
        // FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261025-RVIDEO-LIVE-REMERGE-003.
        // Under the existing admission lock, close the completion/preflight race for the same merge snapshot.
        if (jobType == RenderJobTypes.MergeProjectVideo)
        {
            using var input = JsonDocument.Parse(inputJson);
            if (input.RootElement.TryGetProperty("mergeSelectionKey", out var snapshot) && snapshot.ValueKind == JsonValueKind.String)
            {
                var completed = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(SelectJobSql + "\n" + """
                    WHERE tenant_id=@tenant AND job_type=@jobType AND log_code=@logCode AND status='completed'
                      AND input_json->>'mergeSelectionKey'=@snapshot
                      AND id IN (SELECT render_job_id FROM video_render.final_video_versions
                                 WHERE tenant_id=@tenant AND status='completed' AND is_selected=true)
                    ORDER BY created_at DESC LIMIT 1;
                    """, new { tenant=_tenant.TenantId, jobType, logCode=uniqueLogCode, snapshot=snapshot.GetString() }, tx);
                if (completed is not null) { tx.Commit(); return (completed, true); }
            }
        }
        var promptJson = ToJson(model.Prompt ?? new { });
        var referenceJson = ToJson(model.References ?? Array.Empty<object>());
        var customerScope = model.CustomerId is null ? "system" : "customer";

        RenderJobDto job;
        try
        {
            job = await conn.QuerySingleAsync<RenderJobDto>(
                InsertJobSql,
                new
                {
                    tenant = _tenant.TenantId,
                    user = model.UserId,
                    customer = model.CustomerId,
                    type = jobType,
                    status = initialStatus,
                    priority = model.Priority,
                    input = inputJson,
                    prompt = promptJson,
                    refs = referenceJson,
                    logCode = uniqueLogCode,
                    pointCost = pointCostEstimate,
                    pointStatus,
                    provider = model.ProviderCode,
                    model = model.ModelCode,
                    maxAttempts = Math.Max(1, model.MaxAttempts)
                }, tx);
        }
        catch (PostgresException ex) when (IsRenderJobsCustomerIdNotNullViolation(ex))
        {
            tx.Rollback();
            _logger.LogError(ex,
                "RENDER_JOB_ENQUEUE_SCHEMA_MISMATCH jobType={JobType} userId={UserId} customerId={CustomerId} tenantId={TenantId} customerScope={CustomerScope} logCode={LogCode} sqlState={SqlState} schema={Schema} table={Table} column={Column}",
                jobType, model.UserId, model.CustomerId, _tenant.TenantId, customerScope, uniqueLogCode,
                ex.SqlState, ex.SchemaName, ex.TableName, ex.ColumnName);
            throw new InvalidOperationException(
                "Database render_jobs chưa đồng bộ: customer_id đang NOT NULL trong khi system/admin job không có customer. "
                + "Vui lòng chạy file SQL đồng bộ database do quản trị viên cung cấp.", ex);
        }

        tx.Commit();

        var eventType = initialStatus == RenderJobStatuses.Draft ? "JOB_CREATED" : "JOB_QUEUED";
        var eventMessage = initialStatus == RenderJobStatuses.Draft ? "Render job draft saved." : "Render job queued.";
        await AddEventAsync(job.Id, eventType, eventMessage, new
        {
            job.JobType,
            job.Status,
            job.Priority,
            job.PointCostEstimate,
            job.PointStatus,
            logCode = uniqueLogCode
        }, ct: ct);

        return (job, false);
    }

    public async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueForSceneIfNoneActiveAsync(RenderJobCreateModel model, long sceneId, string? logicalRequestId = null, CancellationToken ct = default)
        => await EnqueueSceneCoreAsync(model, sceneId, logicalRequestId, null, ct);

    public Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueGuardedVideoRerenderAsync(RenderJobCreateModel model, long projectId, long sceneId, Guid? acknowledgedVersionId, Guid? acknowledgedRenderJobId, bool explicitRiskAcknowledgement, CancellationToken ct = default)
        => EnqueueSceneCoreAsync(model, sceneId, null, new VideoRerenderGuard(projectId, acknowledgedVersionId, acknowledgedRenderJobId, explicitRiskAcknowledgement), ct);

    private sealed record VideoRerenderGuard(long ProjectId, Guid? VersionId, Guid? JobId, bool RiskAcknowledged);

    private async Task<(RenderJobDto Job, bool AlreadyActive)> EnqueueSceneCoreAsync(RenderJobCreateModel model, long sceneId, string? logicalRequestId, VideoRerenderGuard? guard, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(model));
        }

        await _tenant.EnsureLoadedAsync(ct);
        var jobType = model.JobType.Trim();
        var requestId = logicalRequestId?.Trim();
        var initialStatus = NormalizeInitialStatus(model.InitialStatus);
        var pointCostEstimate = LegacyPointBillingFeatureFlags.NormalizePointCostEstimate(_configuration, model.PointCostEstimate);
        var pointStatus = LegacyPointBillingFeatureFlags.NormalizePointStatus(_configuration, model.PointStatus, pointCostEstimate);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = BuildSceneJobLockName(jobType, sceneId) },
            tx);

        if (guard is not null)
        {
            if (jobType != RenderJobTypes.RenderSceneVideo)
                throw new InvalidOperationException("Video rerender guard requires a scene-video job.");
            await EnsureSceneVideoEnqueueAllowedAsync(conn, tx, sceneId, guard.ProjectId);
            var latest = await conn.QuerySingleOrDefaultAsync<(Guid Id, string Status, Guid? RenderJobId)>(
                """
                SELECT id, status, render_job_id AS RenderJobId
                  FROM video_render.scene_video_versions
                 WHERE tenant_id=@tenant AND project_id=@projectId AND scene_id=@sceneId
                 ORDER BY version_number DESC LIMIT 1;
                """, new { tenant = _tenant.TenantId, projectId = guard.ProjectId, sceneId }, tx);
            if ((latest.Id == Guid.Empty && (guard.VersionId is not null || guard.JobId is not null))
                || (latest.Id != Guid.Empty && (guard.VersionId != latest.Id || guard.JobId != latest.RenderJobId)))
                throw new InvalidOperationException("RVIDEO_ACK_STALE: Video attempt changed; refresh and confirm again.");
            if (latest.Id != Guid.Empty && string.Equals(latest.Status, "pending_reconciliation", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("RVIDEO_SUBMIT_RECONCILIATION_PENDING: Prior submission requires reconciliation.");
        }

        var active = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(
            SelectJobSql +
            """
             WHERE job_type = @jobType
               AND status IN ('queued', 'preparing', 'rendering', 'processing', 'post_processing', 'pending_reconciliation')
               AND COALESCE(input_json->>'sceneId', input_json->>'scene_id') = @sceneId
               AND (@logicalRequestId IS NULL
                    OR COALESCE(input_json->>'logicalRequestId', input_json->>'logical_request_id') = @logicalRequestId)
               AND (@jobType <> 'render_scene_video' OR NOT (
                    error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                    OR COALESCE(input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
                    OR (status='pending_reconciliation' AND error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
                        AND NULLIF(provider_task_id, '') IS NULL
                        AND NULLIF(provider_video_id_base, '') IS NULL
                        AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions v WHERE v.render_job_id=render.render_jobs.id)
                        AND NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents i WHERE i.render_job_id=render.render_jobs.id))))
             ORDER BY queued_at DESC, created_at DESC
             LIMIT 1;
            """,
            new { jobType, sceneId = sceneId.ToString(), logicalRequestId = requestId }, tx);

        if (active is not null)
        {
            tx.Commit();
            return (active, true);
        }

        if (jobType == RenderJobTypes.RenderSceneVideo && await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM render.render_jobs j
             WHERE j.tenant_id=@tenant AND j.job_type='render_scene_video'
               AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=@sceneId
               AND j.status='pending_reconciliation'
               AND (j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                    OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'));
            """, new { tenant = _tenant.TenantId, sceneId = sceneId.ToString() }, tx))
            throw new InvalidOperationException(TodoX.Web.Services.VideoRender.SceneVideoManualReview.Message);

        if (jobType == RenderJobTypes.RenderSceneVideo)
        {
            using var snapshot = JsonDocument.Parse(ToJson(model.Input ?? new { }));
            if (!snapshot.RootElement.TryGetProperty("projectId", out var projectValue)
                || !projectValue.TryGetInt64(out var projectId))
                throw new InvalidOperationException("Scene video job requires a valid project snapshot.");
            await EnsureSceneVideoEnqueueAllowedAsync(conn, tx, sceneId, projectId);
        }

        var inputJson = ToJson(model.Input ?? new { });
        var promptJson = ToJson(model.Prompt ?? new { });
        var referenceJson = ToJson(model.References ?? Array.Empty<object>());
        var customerScope = model.CustomerId is null ? "system" : "customer";

        RenderJobDto job;
        try
        {
            job = await conn.QuerySingleAsync<RenderJobDto>(
                InsertJobSql,
                new
                {
                    tenant = _tenant.TenantId,
                    user = model.UserId,
                    customer = model.CustomerId,
                    type = jobType,
                    status = initialStatus,
                    priority = model.Priority,
                    input = inputJson,
                    prompt = promptJson,
                    refs = referenceJson,
                    logCode = model.LogCode,
                    pointCost = pointCostEstimate,
                    pointStatus,
                    provider = model.ProviderCode,
                    model = model.ModelCode,
                    maxAttempts = Math.Max(1, model.MaxAttempts)
                }, tx);
        }
        catch (PostgresException ex) when (IsRenderJobsCustomerIdNotNullViolation(ex))
        {
            tx.Rollback();
            _logger.LogError(ex,
                "RENDER_JOB_ENQUEUE_SCHEMA_MISMATCH jobType={JobType} userId={UserId} customerId={CustomerId} tenantId={TenantId} customerScope={CustomerScope} logCode={LogCode} sceneId={SceneId} logicalRequestId={LogicalRequestId} sqlState={SqlState} schema={Schema} table={Table} column={Column}",
                jobType, model.UserId, model.CustomerId, _tenant.TenantId, customerScope, model.LogCode, sceneId, requestId,
                ex.SqlState, ex.SchemaName, ex.TableName, ex.ColumnName);
            throw new InvalidOperationException(
                "Database render_jobs chưa đồng bộ: customer_id đang NOT NULL trong khi system/admin job không có customer. "
                + "Vui lòng chạy file SQL đồng bộ database do quản trị viên cung cấp.", ex);
        }

        tx.Commit();

        var eventType = initialStatus == RenderJobStatuses.Draft ? "JOB_CREATED" : "JOB_QUEUED";
        var eventMessage = initialStatus == RenderJobStatuses.Draft ? "Render job draft saved." : "Render job queued.";
        await AddEventAsync(job.Id, eventType, eventMessage, new
        {
            job.JobType,
            job.Status,
            job.Priority,
            job.PointCostEstimate,
            job.PointStatus,
            sceneId,
            logicalRequestId = requestId
        }, ct: ct);

        return (job, false);
    }

    public async Task UpsertSnapshotAsync(Guid jobId, object projectSnapshot, object sceneSnapshots, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_snapshots
                (job_id, tenant_id, project_snapshot, scene_snapshots, created_at, updated_at)
            VALUES
                (@jobId, @tenant, CAST(@projectSnapshot AS jsonb), CAST(@sceneSnapshots AS jsonb), now(), now())
            ON CONFLICT (job_id)
            DO UPDATE SET project_snapshot = EXCLUDED.project_snapshot,
                          scene_snapshots = EXCLUDED.scene_snapshots,
                          updated_at = now();
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                projectSnapshot = ToJson(projectSnapshot),
                sceneSnapshots = ToJson(sceneSnapshots)
            });
    }

    public async Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<RenderJobDto>(SelectJobSql + " WHERE id=@id;", new { id = jobId });
    }

    public async Task<RenderJobDto?> GetByLogCodeAsync(string logCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<RenderJobDto>(SelectJobSql + " WHERE log_code=@logCode ORDER BY queued_at DESC, created_at DESC LIMIT 1;", new { logCode });
    }

    public async Task<IReadOnlyList<RenderJobDto>> ListByLogCodeAsync(string logCode, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<RenderJobDto>(SelectJobSql + " WHERE log_code=@logCode ORDER BY queued_at DESC, created_at DESC LIMIT 20;", new { logCode });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RenderJobEventDto>> GetEventsAsync(Guid jobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<RenderJobEventDto>(
            """
            SELECT id AS Id, job_id AS JobId, tenant_id AS TenantId, event_type AS EventType,
                   level AS Level, message AS Message, data_json::text AS DataJson,
                   provider_code AS ProviderCode, model_code AS ModelCode, created_at AS CreatedAt
              FROM render.render_job_events
             WHERE job_id=@jobId
             ORDER BY created_at, id;
            """, new { jobId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<RenderJobEventDto>> GetEventsByLogCodeAsync(string logCode, CancellationToken ct = default)
    {
        var job = await GetByLogCodeAsync(logCode, ct);
        if (job is null)
        {
            return Array.Empty<RenderJobEventDto>();
        }

        return await GetEventsAsync(job.Id, ct);
    }

    public async Task AddEventAsync(Guid jobId, string eventType, string message, object? data = null, string level = "info", CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_events
                (job_id, tenant_id, event_type, level, message, data_json, created_at)
            VALUES
                (@jobId, @tenant, @eventType, @level, @message, CAST(@data AS jsonb), now());
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                eventType,
                level,
                message,
                data = ToJson(data ?? new { })
            });
    }

    public async Task<bool> CancelAsync(Guid jobId, string reason, Guid? userId = null, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='cancelled',
                   point_status = CASE WHEN point_status='charged' THEN point_status ELSE 'cancelled' END,
                   cancel_reason=@reason,
                   cancelled_at=now(),
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE id=@jobId
               AND status IN ('queued', 'preparing', 'rendering', 'post_processing', 'failed');
            """, new { jobId, reason });

        if (changed > 0)
        {
            await AddEventAsync(jobId, "JOB_CANCELLED", reason, new { userId }, "warning", ct);
        }

        return changed > 0;
    }

    public async Task<RenderJobDto?> RetryAsync(Guid jobId, Guid? userId = null, CancellationToken ct = default)
    {
        var current = await GetAsync(jobId, ct);
        if (current is null || current.Status is not (RenderJobStatuses.Failed or RenderJobStatuses.Cancelled))
        {
            return null;
        }
        if (TodoX.Web.Services.VideoRender.SceneVideoManualReview.RequiresReview(current))
            throw new InvalidOperationException(TodoX.Web.Services.VideoRender.SceneVideoManualReview.Message);

        var clone = new RenderJobCreateModel
        {
            UserId = current.UserId,
            CustomerId = current.CustomerId,
            JobType = current.JobType,
            Priority = current.Priority,
            Input = JsonSerializer.Deserialize<object>(current.InputJson),
            Prompt = JsonSerializer.Deserialize<object>(current.PromptJson),
            References = JsonSerializer.Deserialize<object>(current.ReferenceJson),
            LogCode = current.LogCode,
            PointCostEstimate = LegacyPointBillingFeatureFlags.NormalizePointCostEstimate(_configuration, current.PointCostEstimate),
            PointStatus = LegacyPointBillingFeatureFlags.NormalizePointStatus(_configuration, current.PointStatus, current.PointCostEstimate),
            ProviderCode = current.ProviderCode,
            ModelCode = current.ModelCode,
            MaxAttempts = current.MaxAttempts
        };

        var retried = await EnqueueAsync(clone, ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE render.render_jobs SET retry_of_job_id=@source, updated_at=now() WHERE id=@id;",
            new { id = retried.Id, source = jobId });
        await AddEventAsync(jobId, "JOB_RETRY_CREATED", "Retry job created.", new { retryJobId = retried.Id, userId }, ct: ct);
        await AddEventAsync(retried.Id, "JOB_RETRY_OF", "Job created as retry of failed job.", new { sourceJobId = jobId, userId }, ct: ct);
        return retried;
    }

    public async Task<RenderJobDto?> ClaimNextAsync(string workerKey, TimeSpan lockFor, CancellationToken ct = default)
        => await ClaimNextInternal(workerKey, lockFor, jobTypes: null, ct);

    public async Task<RenderJobDto?> ClaimNextByJobTypeAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> jobTypes, CancellationToken ct = default)
        => await ClaimNextInternal(workerKey, lockFor, jobTypes, ct);

    public async Task<RenderJobDto?> ClaimNextExcludingJobTypesAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> excludedJobTypes, CancellationToken ct = default)
        => await ClaimNextInternal(workerKey, lockFor, includeJobTypes: null, excludeJobTypes: excludedJobTypes, ct);

    private async Task<RenderJobDto?> ClaimNextInternal(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string>? jobTypes, CancellationToken ct)
        => await ClaimNextInternal(workerKey, lockFor, jobTypes, excludeJobTypes: null, ct);

    // FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER
    // CHANGE_ID: TODOX-20261028-SCENE-VIDEO-QUEUE-BLOCKED-001
    // A fenced UPDATE rolls back its claim, not the queue. Audit quarantine preserves immutable history.
    private async Task<RenderJobDto?> ClaimNextInternal(
        string workerKey, TimeSpan lockFor, IReadOnlyCollection<string>? includeJobTypes,
        IReadOnlyCollection<string>? excludeJobTypes, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var result = await ClaimNextCandidateAsync(workerKey, lockFor, includeJobTypes, excludeJobTypes, ct);
            if (!result.Quarantined) return result.Job;
        }
    }

    private async Task<(RenderJobDto? Job, bool Quarantined)> ClaimNextCandidateAsync(
        string workerKey,
        TimeSpan lockFor,
        IReadOnlyCollection<string>? includeJobTypes,
        IReadOnlyCollection<string>? excludeJobTypes,
        CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var sql = SelectJobSql +
                  """
                   WHERE (
                         (status='queued'
                          AND (retry_after IS NULL OR retry_after <= now())
                          AND (COALESCE(input_json->>'providerPoll', 'false') = 'true'
                               OR (attempt_count <= max_attempts AND attempt_count < max_attempts)))
                         OR (
                              job_type='core_service'
                              AND status='rendering'
                              AND attempt_count=0
                              AND worker_key IS NULL
                              AND lock_owner IS NULL
                              AND lock_until IS NULL
                              AND started_at IS NULL
                         )
                   )
                  """;
        object parameters;
        if (includeJobTypes is not null && includeJobTypes.Count > 0)
        {
            sql += " AND job_type = ANY(@jobTypes)";
            parameters = new { jobTypes = includeJobTypes.ToArray(), excludedJobTypes = excludeJobTypes?.ToArray() ?? Array.Empty<string>() };
        }
        else if (excludeJobTypes is not null && excludeJobTypes.Count > 0)
        {
            sql += " AND NOT (job_type = ANY(@excludedJobTypes))";
            parameters = new { excludedJobTypes = excludeJobTypes.ToArray() };
        }
        else
        {
            parameters = new { };
        }

        sql += " AND (job_type <> 'render_video_batch' OR video_render.scene_video_job_is_current(tenant_id,id))";
        sql += " AND (job_type <> 'render_scene_video' OR NOT EXISTS (SELECT 1 FROM render.render_job_events q WHERE q.tenant_id=render_jobs.tenant_id AND q.job_id=render_jobs.id AND q.event_type='SCENE_VIDEO_QUEUE_QUARANTINED'))";
        sql += ResolveClaimOrderSql(includeJobTypes);

        var job = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(sql, parameters, tx);

        if (job is null)
        {
            _logger.LogDebug(
                "RENDER_JOB_CLAIM_RESULT workerKey={WorkerKey} claimResult=none includeJobTypes={IncludeJobTypes} excludeJobTypes={ExcludeJobTypes}",
                workerKey,
                includeJobTypes is null ? null : string.Join(",", includeJobTypes),
                excludeJobTypes is null ? null : string.Join(",", excludeJobTypes));
            tx.Commit();
            return (null, false);
        }

        if (job.JobType == RenderJobTypes.RenderSceneVideo)
        {
            // A concurrent SELECT may have taken its snapshot before the previous lane committed
            // the audit event. Recheck after obtaining the job lock so quarantine is recorded once.
            if (await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM render.render_job_events WHERE tenant_id=@tenant AND job_id=@id AND event_type='SCENE_VIDEO_QUEUE_QUARANTINED');",
                new { tenant = job.TenantId, id = job.Id }, tx))
            {
                tx.Commit();
                return (null, true);
            }
            var reason = await GetSceneVideoQueueRejectionAsync(conn, tx, job);
            if (reason is not null)
            {
                await QuarantineSceneVideoQueueCandidateAsync(conn, tx, job, workerKey, reason);
                tx.Commit();
                return (null, true);
            }
        }

        var isCoreServiceRecoveryClaim =
            string.Equals(job.JobType, RenderJobTypes.CoreService, StringComparison.OrdinalIgnoreCase)
            && string.Equals(job.Status, RenderJobStatuses.Rendering, StringComparison.OrdinalIgnoreCase)
            && job.AttemptCount == 0
            && string.IsNullOrWhiteSpace(job.WorkerKey)
            && job.StartedAt is null;
        _logger.LogInformation(
            "RENDER_JOB_CLAIM_SELECTED jobId={JobId} jobType={JobType} currentStatus={CurrentStatus} attemptCount={AttemptCount} workerKey={CurrentWorkerKey} claimResult=selected coreServiceRecovery={CoreServiceRecovery}",
            job.Id,
            job.JobType,
            job.Status,
            job.AttemptCount,
            job.WorkerKey,
            isCoreServiceRecoveryClaim);

        await conn.ExecuteAsync("SAVEPOINT scene_video_claim;", transaction: tx);
        int claimedRows;
        try
        {
            claimedRows = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='preparing',
                   worker_key=@workerKey,
                   lock_owner=@workerKey,
                   lock_until=now() + (@lockSeconds || ' seconds')::interval,
                   input_json=input_json - 'providerPoll',
                   attempt_count=attempt_count + CASE
                       WHEN COALESCE(input_json->>'providerPoll', 'false') = 'true' THEN 0
                       ELSE 1
                   END,
                   started_at=COALESCE(started_at, now()),
                   updated_at=now()
             WHERE id=@id
               AND attempt_count <= max_attempts
               AND (COALESCE(input_json->>'providerPoll', 'false') = 'true'
                    OR attempt_count < max_attempts);
            """,
            new { id = job.Id, workerKey, lockSeconds = Math.Max(1, (int)lockFor.TotalSeconds) },
            tx);
        }
        catch (PostgresException ex) when (job.JobType == RenderJobTypes.RenderSceneVideo && IsSceneVideoQueueOwnershipFence(ex))
        {
            // Keep the candidate row lock (acquired before savepoint), undo attempt/lease mutations.
            await conn.ExecuteAsync("ROLLBACK TO SAVEPOINT scene_video_claim;", transaction: tx);
            await QuarantineSceneVideoQueueCandidateAsync(conn, tx, job, workerKey, "VIDEO_OPERATION_SUPERSEDED");
            tx.Commit();
            return (null, true);
        }

        if (claimedRows == 0)
        {
            _logger.LogWarning(
                "RENDER_JOB_CLAIM_RESULT jobId={JobId} jobType={JobType} currentStatus={CurrentStatus} attemptCount={AttemptCount} workerKey={WorkerKey} claimResult=attempt_budget_rejected",
                job.Id,
                job.JobType,
                job.Status,
                job.AttemptCount,
                workerKey);
            tx.Commit();
            return (null, false);
        }

        tx.Commit();
        await AddEventAsync(job.Id, "WORKER_CLAIMED", "Worker claimed render job.", new { workerKey }, ct: ct);
        var claimed = await GetAsync(job.Id, ct);
        _logger.LogInformation(
            "RENDER_JOB_CLAIM_RESULT jobId={JobId} jobType={JobType} currentStatus={PreviousStatus} attemptCountBefore={AttemptCountBefore} workerKey={WorkerKey} claimResult={ClaimResult} claimedStatus={ClaimedStatus} attemptCountAfter={AttemptCountAfter} startedAtSet={StartedAtSet}",
            job.Id,
            job.JobType,
            job.Status,
            job.AttemptCount,
            workerKey,
            claimed is null ? "missing_after_update" : "claimed",
            claimed?.Status,
            claimed?.AttemptCount,
            claimed?.StartedAt is not null);
        return (claimed, false);
    }

    internal static bool IsSceneVideoQueueOwnershipFence(PostgresException exception)
        => exception.SqlState == "55000"
           && exception.MessageText.StartsWith("VIDEO_OPERATION_SUPERSEDED:", StringComparison.Ordinal);

    private static Task<string?> GetSceneVideoQueueRejectionAsync(System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx, RenderJobDto job)
        => conn.ExecuteScalarAsync<string?>(
            """
            SELECT CASE
              WHEN NOT video_render.scene_video_job_is_current(j.tenant_id,j.id)
                THEN 'VIDEO_OPERATION_NOT_CURRENT'
              WHEN NOT EXISTS (
                SELECT 1 FROM video_render.scene_video_operation_heads h
                 WHERE h.tenant_id=j.tenant_id
                   AND h.scene_id::text=COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id'))
              THEN CASE
                WHEN COALESCE(j.input_json->>'operationGeneration','0') NOT IN ('0','')
                  THEN 'NO_OPERATION_HEAD_NONLEGACY_GENERATION'
                WHEN NOT EXISTS (
                  SELECT 1 FROM video_render.video_project_scenes s
                   WHERE s.tenant_id=j.tenant_id AND s.project_id::text=j.input_json->>'projectId'
                     AND s.id::text=j.input_json->>'sceneId')
                  OR NULLIF(btrim(j.input_json->>'logicalRequestId'),'') IS NULL
                  THEN 'LEGACY_OPERATION_IDENTITY_INVALID'
                WHEN EXISTS (
                  SELECT 1 FROM video_render.scene_video_versions v
                   WHERE v.tenant_id=j.tenant_id AND v.logical_request_id=j.input_json->>'logicalRequestId'
                     AND (v.render_job_id IS DISTINCT FROM j.id
                       OR v.project_id::text IS DISTINCT FROM j.input_json->>'projectId'
                       OR v.scene_id::text IS DISTINCT FROM j.input_json->>'sceneId'))
                  THEN 'LEGACY_VERSION_OWNERSHIP_INVALID'
                WHEN COALESCE(j.input_json->>'providerPoll','false') <> 'true'
                  AND (NULLIF(btrim(j.provider_task_id),'') IS NOT NULL
                    OR NULLIF(btrim(j.provider_video_id_base),'') IS NOT NULL
                    OR EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
                         WHERE i.tenant_id=j.tenant_id AND i.render_job_id=j.id)
                    OR EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                         WHERE v.tenant_id=j.tenant_id AND v.logical_request_id=j.input_json->>'logicalRequestId'
                           AND (NULLIF(btrim(v.provider_task_id),'') IS NOT NULL
                             OR NULLIF(btrim(v.provider_video_id_base),'') IS NOT NULL
                             OR v.status IN ('submitted','pending_reconciliation','completed'))))
                  THEN 'LEGACY_SUBMIT_EVIDENCE_REQUIRES_REVIEW'
                WHEN COALESCE(j.input_json->>'providerPoll','false') = 'true'
                  AND NULLIF(btrim(j.provider_task_id),'') IS NULL
                  AND NULLIF(btrim(j.provider_video_id_base),'') IS NULL
                  THEN 'LEGACY_POLL_IDENTITY_REQUIRED'
                ELSE NULL END
              ELSE NULL END
            FROM render.render_jobs j WHERE j.id=@id AND j.tenant_id=@tenant;
            """, new { id = job.Id, tenant = job.TenantId }, tx);

    private async Task QuarantineSceneVideoQueueCandidateAsync(System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx, RenderJobDto job, string workerKey, string reason)
    {
        // The generation trigger forbids even status changes on a historical owner. Store a durable
        // queue exclusion in the existing audit table, never rewrite its job/version/provider outcome.
        await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_events(job_id,tenant_id,event_type,level,message,data_json,created_at)
            VALUES (@id,@tenant,'SCENE_VIDEO_QUEUE_QUARANTINED','warning',
              'Scene video queue candidate quarantined before dispatch; manual ownership reconciliation required.',
              jsonb_build_object('reason',@reason::text,'workerKey',@workerKey::text,
                'attemptCount',@attempt,'status',@status::text,
                'projectId',@input::jsonb->>'projectId','sceneId',@input::jsonb->>'sceneId',
                'logicalRequestId',@input::jsonb->>'logicalRequestId','operationGeneration',@input::jsonb->>'operationGeneration',
                'providerTaskId',@task::text,'providerVideoIdBase',@video::text),now());
            """, new
            {
                id = job.Id,
                tenant = job.TenantId,
                reason,
                workerKey,
                attempt = job.AttemptCount,
                status = job.Status,
                input = job.InputJson,
                task = job.ProviderTaskId,
                video = job.ProviderVideoIdBase
            }, tx);
        _logger.LogWarning("SCENE_VIDEO_QUEUE_QUARANTINED jobId={JobId} tenantId={TenantId} reason={Reason}; no dispatch or retry.",
            job.Id, job.TenantId, reason);
    }

    internal static string ResolveClaimOrderSql(IReadOnlyCollection<string>? includeJobTypes)
        => IsSceneVideoOnlyClaim(includeJobTypes)
            ? """
                   ORDER BY
                       priority ASC,
                       CASE
                           WHEN retry_after IS NULL THEN queued_at
                           ELSE GREATEST(queued_at, retry_after)
                       END ASC,
                       queued_at ASC
                   FOR UPDATE SKIP LOCKED
                   LIMIT 1;
                  """
            : """
                   ORDER BY priority ASC, queued_at ASC
                   FOR UPDATE SKIP LOCKED
                   LIMIT 1;
                  """;

    internal static DateTime ResolveEffectiveClaimReadyTime(RenderJobDto job)
        => job.RetryAfter is null || job.RetryAfter <= job.QueuedAt
            ? job.QueuedAt
            : job.RetryAfter.Value;

    internal static IReadOnlyList<RenderJobDto> OrderForSceneVideoClaimFairness(IEnumerable<RenderJobDto> jobs)
        => jobs
            .OrderBy(job => job.Priority)
            .ThenBy(ResolveEffectiveClaimReadyTime)
            .ThenBy(job => job.QueuedAt)
            .ToArray();

    private static bool IsSceneVideoOnlyClaim(IReadOnlyCollection<string>? includeJobTypes)
        => includeJobTypes is not null
           && includeJobTypes.Count == 1
           && includeJobTypes.Any(jobType => string.Equals(jobType, RenderJobTypes.RenderSceneVideo, StringComparison.OrdinalIgnoreCase));

    public async Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
    {
        var dbStatus = NormalizeStatus(status);
        using var conn = await _factory.OpenAsync(ct);
        using var sceneTx = await BeginSceneVideoWriterAsync(conn, jobId, ct, allowPreDispatch: dbStatus == RenderJobStatuses.Rendering);
        var updated = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   output_json = CASE WHEN @output IS NULL THEN output_json ELSE CAST(@output AS jsonb) END,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   point_status = CASE
                       WHEN job_type='dance_sell'
                            AND @status='completed'
                            AND point_status='pending' THEN 'charged'
                       ELSE point_status
                   END,
                   completed_at = CASE
                       WHEN @status IN ('completed', 'failed', 'cancelled') THEN now()
                       WHEN @status='pending_reconciliation' THEN NULL
                       ELSE completed_at
                   END,
                   cancelled_at = CASE WHEN @status='cancelled' THEN now() ELSE cancelled_at END,
                   lock_owner = CASE WHEN @sceneVideo AND @status IN ('pending_reconciliation','completed','failed','cancelled') THEN NULL ELSE lock_owner END,
                   lock_until = CASE WHEN @sceneVideo AND @status IN ('pending_reconciliation','completed','failed','cancelled') THEN NULL ELSE lock_until END,
                   updated_at=now()
             WHERE id=@jobId
               AND (@sceneVideo=false OR (tenant_id=@tenant AND job_type='render_scene_video'
                    AND status IN ('queued','preparing','rendering','post_processing','pending_reconciliation')
                    AND (@status <> 'rendering' OR status IN ('preparing','rendering'))));
            """,
            new
            {
                jobId,
                status = dbStatus,
                output = output is null ? null : ToJson(output),
                errorCode,
                errorMessage,
                sceneVideo = sceneTx is not null,
                tenant = _tenant.TenantId
            }, sceneTx);
        if (sceneTx is not null && updated != 1)
            throw new InvalidOperationException("RVIDEO_JOB_STATUS_REJECTED");
        sceneTx?.Commit();

        var level = dbStatus == RenderJobStatuses.Failed ? "error"
            : dbStatus == RenderJobStatuses.Cancelled || dbStatus == RenderJobStatuses.PendingReconciliation ? "warning"
            : "info";
        await AddEventAsync(jobId, "JOB_STATUS_CHANGED", $"Render job status changed to {dbStatus}.",
            new { status = dbStatus, errorCode, errorMessage }, level, ct);
    }

    public async Task MarkSceneVideoClaimRenderingAsync(RenderJobDto claim, string workerKey, CancellationToken ct = default)
    {
        if (claim.JobType != RenderJobTypes.RenderSceneVideo || string.IsNullOrWhiteSpace(workerKey)
            || claim.WorkerKey != workerKey || claim.AttemptCount <= 0)
            throw new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST");
        using var conn = await _factory.OpenAsync(ct);
        using var tx = await BeginSceneVideoWriterAsync(conn, claim.Id, ct, allowPreDispatch: true);
        if (tx is null) throw new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST");
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs SET status='rendering', updated_at=now()
             WHERE id=@id AND tenant_id=@tenant AND job_type='render_scene_video'
               AND status='preparing' AND worker_key=@workerKey AND lock_owner=@workerKey
               AND attempt_count=@attempt AND lock_until > now();
            """, new { id = claim.Id, tenant = _tenant.TenantId, workerKey, attempt = claim.AttemptCount }, tx);
        if (changed != 1) throw new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST");
        tx.Commit();
        await AddEventAsync(claim.Id, "JOB_STATUS_CHANGED", "Render job status changed to rendering.", new { status = RenderJobStatuses.Rendering }, ct: ct);
    }

    public async Task VerifySceneVideoSubmitClaimAsync(RenderJobDto claim, Guid versionId, string logicalRequestId, CancellationToken ct = default)
    {
        if (claim.JobType != RenderJobTypes.RenderSceneVideo || string.IsNullOrWhiteSpace(claim.WorkerKey)
            || claim.AttemptCount <= 0 || versionId == Guid.Empty || string.IsNullOrWhiteSpace(logicalRequestId))
            throw new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST");
        using var conn = await _factory.OpenAsync(ct);
        using var tx = await BeginSceneVideoWriterAsync(conn, claim.Id, ct);
        if (tx is null) throw new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST");
        var valid = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM render.render_jobs j
              JOIN video_render.scene_video_versions v ON v.render_job_id=j.id
             WHERE j.id=@id AND j.tenant_id=@tenant AND j.job_type='render_scene_video'
               AND j.status='rendering' AND j.worker_key=@workerKey AND j.lock_owner=@workerKey
               AND j.attempt_count=@attempt AND j.lock_until > now()
               AND j.provider_task_id IS NULL AND j.provider_video_id_base IS NULL
               AND v.id=@versionId AND v.tenant_id=j.tenant_id
               AND v.project_id=(j.input_json->>'projectId')::bigint
               AND v.scene_id=(j.input_json->>'sceneId')::bigint
               AND v.logical_request_id=@logicalRequestId
               AND v.logical_request_id <> ''
               AND v.id=(SELECT id FROM video_render.scene_video_versions latest
                         WHERE latest.tenant_id=j.tenant_id AND latest.scene_id=v.scene_id
                           AND latest.project_id=v.project_id ORDER BY version_number DESC LIMIT 1));
            """, new
            {
                id = claim.Id,
                tenant = _tenant.TenantId,
                workerKey = claim.WorkerKey,
                attempt = claim.AttemptCount,
                versionId,
                logicalRequestId
            }, tx);
        if (!valid) throw new InvalidOperationException("RVIDEO_JOB_CALLER_OWNERSHIP_LOST");
        tx.Commit();
    }

    public async Task<bool> QuarantineRejectedSceneVideoClaimAsync(RenderJobDto claim, string workerKey, string reason, CancellationToken ct = default)
    {
        if (claim.JobType != RenderJobTypes.RenderSceneVideo || string.IsNullOrWhiteSpace(workerKey)
            || claim.WorkerKey != workerKey || claim.AttemptCount <= 0) return false;
        using var conn = await _factory.OpenAsync(ct);
        // Conditional UPDATE does not touch a newer attempt or another worker's lease.
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs SET status='pending_reconciliation',
                   error_code=@reason, error_message='Scene video claim rejected; manual reconciliation required.',
                   retry_after=NULL, lock_owner=NULL, lock_until=NULL, updated_at=now()
             WHERE id=@id AND tenant_id=@tenant AND job_type='render_scene_video'
               AND status='preparing' AND worker_key=@workerKey AND lock_owner=@workerKey
               AND attempt_count=@attempt AND provider_task_id IS NULL AND provider_video_id_base IS NULL;
            """, new { id = claim.Id, tenant = _tenant.TenantId, workerKey, attempt = claim.AttemptCount, reason });
        if (changed == 1)
            await AddEventAsync(claim.Id, "JOB_PENDING_RECONCILIATION", "Scene video claim rejected; manual reconciliation required.",
                new { claim.AttemptCount, reason }, "warning", ct);
        return changed == 1;
    }

    private static string NormalizeStatus(string status)
        => status.Equals(RenderJobStatuses.Processing, StringComparison.OrdinalIgnoreCase) ? RenderJobStatuses.Rendering : status;

    private static string NormalizeInitialStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)
            || status.Equals(RenderJobStatuses.Queued, StringComparison.OrdinalIgnoreCase))
        {
            return RenderJobStatuses.Queued;
        }

        if (status.Equals(RenderJobStatuses.Draft, StringComparison.OrdinalIgnoreCase))
        {
            return RenderJobStatuses.Draft;
        }

        throw new ArgumentOutOfRangeException(nameof(status), "Only queued or draft may be used when creating a render job.");
    }

    public static string BuildProjectJobLockName(string jobType, long projectId)
        => $"render.render_jobs:{jobType.Trim().ToLowerInvariant()}:{projectId}";

    public static string BuildLogCodeJobLockName(string jobType, string logCode)
        => $"render.render_jobs:{jobType.Trim().ToLowerInvariant()}:log:{logCode.Trim().ToLowerInvariant()}";

    public static string BuildSceneJobLockName(string jobType, long sceneId, string? logicalRequestId = null)
        => logicalRequestId is null
            ? $"render.render_jobs:{jobType.Trim().ToLowerInvariant()}:scene:{sceneId}"
            : $"render.render_jobs:{jobType.Trim().ToLowerInvariant()}:scene:{sceneId}:logical:{logicalRequestId.Trim().ToLowerInvariant()}";

    private async Task EnsureSceneVideoEnqueueAllowedAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, long sceneId, long projectId)
    {
        if (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads WHERE tenant_id=@tenant AND scene_id=@sceneId);",
            new { tenant = _tenant.TenantId, sceneId }, tx))
            throw new InvalidOperationException("RVIDEO_OPERATION_ACK_REQUIRED: use the explicit generation-aware user action.");
        var state = await conn.QuerySingleOrDefaultAsync<(long SceneId, bool RequiresReview)>(
            """
            SELECT s.id AS SceneId, EXISTS (
                SELECT 1 FROM render.render_jobs j
                WHERE j.tenant_id=s.tenant_id AND j.job_type='render_scene_video'
                  AND j.status='pending_reconciliation'
                  AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=s.id::text
                  AND (j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                       OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt')
            ) AS RequiresReview
            FROM video_render.video_project_scenes s
            JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
            WHERE s.id=@sceneId AND s.project_id=@projectId AND s.tenant_id=@tenant
            FOR UPDATE OF s;
            """, new { sceneId, projectId, tenant = _tenant.TenantId }, tx);
        if (state.SceneId != sceneId)
            throw new InvalidOperationException("Scene video project or tenant mismatch.");
        if (state.RequiresReview)
            throw new InvalidOperationException(TodoX.Web.Services.VideoRender.SceneVideoManualReview.Message);
    }

    public static bool IsRenderJobsCustomerIdNotNullViolation(PostgresException ex)
        => ex.SqlState == PostgresErrorCodes.NotNullViolation
           && string.Equals(ex.SchemaName, "render", StringComparison.OrdinalIgnoreCase)
           && string.Equals(ex.TableName, "render_jobs", StringComparison.OrdinalIgnoreCase)
           && string.Equals(ex.ColumnName, "customer_id", StringComparison.OrdinalIgnoreCase);

    private static string? ExtractProjectId(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("projectId", out var projectId))
            {
                return null;
            }

            return projectId.ValueKind switch
            {
                JsonValueKind.Number when projectId.TryGetInt64(out var value) => value.ToString(),
                JsonValueKind.String => projectId.GetString(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task ScheduleRetryAsync(Guid jobId, TimeSpan delay, string errorCode, string errorMessage, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='queued',
                   retry_after=now() + (@delaySeconds || ' seconds')::interval,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   lock_owner=NULL,
                   lock_until=NULL,
                   updated_at=now()
             WHERE id=@jobId
               AND attempt_count < max_attempts
               AND status IN ('preparing', 'rendering', 'post_processing', 'failed');
            """,
            new { jobId, delaySeconds = Math.Max(1, (int)delay.TotalSeconds), errorCode, errorMessage });

        if (changed > 0)
        {
            await AddEventAsync(jobId, "JOB_RETRY_SCHEDULED", "Render job retry scheduled.",
                new { retryAfterSeconds = Math.Max(1, (int)delay.TotalSeconds), errorCode, errorMessage }, "warning", ct);
            return;
        }

        await AddEventAsync(jobId, "JOB_RETRY_NOT_SCHEDULED",
            "Render job retry was not scheduled because the retry budget or current status blocked the update.",
            new { retryAfterSeconds = Math.Max(1, (int)delay.TotalSeconds), errorCode, errorMessage }, "warning", ct);
    }

    public async Task<bool> ScheduleProviderPollAsync(
        Guid jobId,
        TimeSpan delay,
        string reasonCode,
        string reasonMessage,
        CancellationToken ct = default,
        bool enforceReconciliationLimit = true,
        bool enforceProviderPollTimeout = false)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var sceneTx = await BeginSceneVideoWriterAsync(conn, jobId, ct);
        var delaySeconds = Math.Max(1, (int)delay.TotalSeconds);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='queued',
                   input_json=jsonb_set(
                       jsonb_set(
                           COALESCE(input_json, '{}'::jsonb) || '{"providerPoll": true}'::jsonb,
                           '{providerPollCount}',
                           to_jsonb(COALESCE(NULLIF(input_json->>'providerPollCount', '')::int, 0) + 1),
                           true),
                       '{providerPollStartedAt}',
                       COALESCE(input_json->'providerPollStartedAt', to_jsonb(now())),
                       true) || CASE WHEN input_json->>'providerRecoveryStartedAt' IS NOT NULL
                         THEN jsonb_build_object('providerRecoveryPollCount',COALESCE((input_json->>'providerRecoveryPollCount')::int,0)+1)
                         ELSE '{}'::jsonb END,
                   retry_after=now() + (@delaySeconds || ' seconds')::interval,
                   error_code=@reasonCode,
                   error_message=@reasonMessage,
                   lock_owner=NULL,
                   lock_until=NULL,
                   updated_at=now()
             WHERE id=@jobId
               AND status IN ('queued', 'preparing', 'rendering', 'post_processing', 'pending_reconciliation', 'failed')
               AND (job_type <> 'render_scene_video' OR (
                    video_render.scene_video_job_is_current(tenant_id,id) AND
                    error_code IS DISTINCT FROM 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                    AND NOT (COALESCE(input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt')
                    AND NULLIF(btrim(provider_task_id), '') IS NOT NULL
                    AND NULLIF(btrim(provider_video_id_base), '') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM video_render.scene_video_versions v
                         WHERE v.render_job_id=render.render_jobs.id AND v.tenant_id=render.render_jobs.tenant_id
                           AND (NULLIF(btrim(v.provider_task_id), '') IS NULL
                                OR NULLIF(btrim(v.provider_video_id_base), '') IS NULL))))
               AND (@enforceReconciliationLimit = false
                    OR COALESCE(NULLIF(input_json->>'providerPollCount', '')::int, 0) < @maxReconciliationRetries)
               AND (@enforceProviderPollTimeout = false
                    OR input_json->>'providerPollStartedAt' IS NULL
                    OR (input_json->>'providerPollStartedAt')::timestamptz
                       + (@providerPollTimeoutMinutes || ' minutes')::interval > now())
               AND (job_type<>'render_scene_video' OR input_json->>'providerRecoveryStartedAt' IS NULL
                    OR (COALESCE((input_json->>'providerRecoveryPollCount')::int,0)<@maxRecoveryPolls
                        AND (input_json->>'providerRecoveryStartedAt')::timestamptz
                            + (@recoveryWindowMinutes * interval '1 minute')>now()));
            """,
            new
            {
                jobId,
                delaySeconds,
                reasonCode,
                reasonMessage,
                maxReconciliationRetries = GetMaxProviderPolls(),
                providerPollTimeoutMinutes = GetProviderPollTimeoutMinutes(),
                enforceReconciliationLimit,
                enforceProviderPollTimeout,
                maxRecoveryPolls = GetMaxKnownTaskRecoveryPolls(),
                recoveryWindowMinutes = GetKnownTaskRecoveryWindowMinutes(),
                sceneVideo = sceneTx is not null,
                tenant = _tenant.TenantId
            }, sceneTx);
        sceneTx?.Commit();

        if (changed <= 0)
        {
            await AddEventAsync(jobId, "JOB_PROVIDER_POLL_NOT_SCHEDULED",
                "Provider poll was not scheduled because the render job is no longer active.",
                new { retryAfterSeconds = delaySeconds, reasonCode, reasonMessage }, "warning", ct);
            return false;
        }

        await AddEventAsync(jobId, "JOB_PROVIDER_POLL_SCHEDULED",
            "Provider poll scheduled without consuming the application retry budget.",
            new { retryAfterSeconds = delaySeconds, reasonCode, reasonMessage }, "info", ct);
        return true;
    }

    public async Task<bool> SchedulePersistentProviderPollAsync(
        Guid jobId,
        TimeSpan delay,
        string reasonCode,
        string reasonMessage,
        CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        using var sceneTx = await BeginSceneVideoWriterAsync(conn, jobId, ct);
        // FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261025-KNOWN-TASK-RECOVERY-001.
        // Recovery keeps the original poll clock and has a separate finite budget/window.
        var delaySeconds = Math.Max(1, (int)delay.TotalSeconds);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='queued',
                   input_json=jsonb_set(
                       jsonb_set(
                           COALESCE(input_json, '{}'::jsonb) || '{"providerPoll": true}'::jsonb,
                           '{providerPollCount}',
                           to_jsonb(COALESCE(NULLIF(input_json->>'providerPollCount', '')::int, 0) + 1),
                           true),
                       '{providerPollStartedAt}',
                       COALESCE(input_json->'providerPollStartedAt', to_jsonb(now())),
                       true) || jsonb_build_object(
                           'providerRecoveryPollCount',COALESCE((input_json->>'providerRecoveryPollCount')::int,0)+1,
                           'providerRecoveryStartedAt',COALESCE(input_json->'providerRecoveryStartedAt',to_jsonb(now()))),
                   retry_after=now() + (@delaySeconds || ' seconds')::interval,
                   error_code=@reasonCode,
                   error_message=@reasonMessage,
                   lock_owner=NULL,
                   lock_until=NULL,
                   updated_at=now()
             WHERE id=@jobId
               AND status IN ('queued', 'preparing', 'rendering', 'post_processing', 'pending_reconciliation', 'failed')
               AND (job_type <> 'render_scene_video' OR (
                    video_render.scene_video_job_is_current(tenant_id,id) AND
                    error_code IS DISTINCT FROM 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                    AND NOT (COALESCE(input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt')
                    AND NULLIF(btrim(provider_task_id), '') IS NOT NULL
                    AND NULLIF(btrim(provider_video_id_base), '') IS NOT NULL
                    AND NOT EXISTS (
                        SELECT 1 FROM video_render.scene_video_versions v
                         WHERE v.render_job_id=render.render_jobs.id AND v.tenant_id=render.render_jobs.tenant_id
                           AND (NULLIF(btrim(v.provider_task_id), '') IS NULL
                                OR NULLIF(btrim(v.provider_video_id_base), '') IS NULL))))
               AND status<>'queued'
               AND (job_type<>'render_scene_video' OR (
                    (SELECT count(*) FROM video_render.scene_video_versions v
                        WHERE v.render_job_id=render.render_jobs.id AND v.tenant_id=render.render_jobs.tenant_id)=1
                    AND EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                        JOIN render.scene_video_submit_intents i ON i.tenant_id=v.tenant_id AND i.render_job_id=v.render_job_id
                          AND i.scene_id=v.scene_id AND i.logical_request_id=v.logical_request_id
                        WHERE v.render_job_id=render.render_jobs.id AND v.tenant_id=render.render_jobs.tenant_id
                          AND v.status IN ('submitted','processing','pending_reconciliation','rendering')
                          AND v.provider_task_id=render.render_jobs.provider_task_id
                          AND v.provider_video_id_base=render.render_jobs.provider_video_id_base
                          AND i.submit_state='CONFIRMED_PROVIDER_TASK'
                          AND i.provider_task_id=v.provider_task_id AND i.provider_video_id_base=v.provider_video_id_base)
                    AND (SELECT count(*) FROM render.scene_video_submit_intents i
                        WHERE i.render_job_id=render.render_jobs.id AND i.tenant_id=render.render_jobs.tenant_id)=1))
               AND COALESCE((input_json->>'providerRecoveryPollCount')::int,0)<@maxRecoveryPolls
               AND (input_json->>'providerRecoveryStartedAt' IS NULL
                    OR (input_json->>'providerRecoveryStartedAt')::timestamptz
                        + (@recoveryWindowMinutes * interval '1 minute')>now())
               AND (retry_after IS NULL OR retry_after <= now())
               AND (lock_until IS NULL OR lock_until <= now());
            """,
            new { jobId, delaySeconds, reasonCode, reasonMessage,
                maxRecoveryPolls = GetMaxKnownTaskRecoveryPolls(), recoveryWindowMinutes = GetKnownTaskRecoveryWindowMinutes() }, sceneTx);
        sceneTx?.Commit();

        if (changed <= 0)
        {
            _logger.LogDebug("Persistent reconciliation no-op for job {JobId}: future retry_after or live lock or terminal status.", jobId);
            return false;
        }

        await AddEventAsync(jobId, "JOB_PROVIDER_POLL_SCHEDULED",
            "Provider poll scheduled without consuming the application retry budget.",
            new { retryAfterSeconds = delaySeconds, reasonCode, reasonMessage }, "info", ct);
        return true;
    }


    private async Task<System.Data.IDbTransaction?> BeginSceneVideoWriterAsync(System.Data.IDbConnection conn, Guid jobId, CancellationToken ct, bool allowPreDispatch = false)
    {
        await _tenant.EnsureLoadedAsync(ct);
        // Identity lookup deliberately does not hold a job row lock while waiting for the scene lock.
        var identity = await conn.QuerySingleOrDefaultAsync<(string JobType, bool TenantOwned, long? ProjectId, long? SceneId)>(
            """
            SELECT job_type AS JobType,
                   tenant_id=@tenant AS TenantOwned,
                   CASE WHEN input_json->>'projectId' ~ '^[0-9]+$' THEN (input_json->>'projectId')::bigint END AS ProjectId,
                   CASE WHEN input_json->>'sceneId' ~ '^[0-9]+$' THEN (input_json->>'sceneId')::bigint END AS SceneId
              FROM render.render_jobs WHERE id=@jobId;
            """, new { jobId, tenant = _tenant.TenantId });
        if (identity.JobType != RenderJobTypes.RenderSceneVideo)
            return null;
        if (!identity.TenantOwned)
            throw new InvalidOperationException("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED");
        if (identity.ProjectId is null || identity.SceneId is null)
            throw new InvalidOperationException("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED");

        var tx = conn.BeginTransaction();
        try
        {
            await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
                new { lockName = BuildSceneJobLockName(identity.JobType, identity.SceneId.Value) }, tx);
            var scene = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM video_render.video_project_scenes WHERE id=@sceneId AND project_id=@projectId AND tenant_id=@tenant FOR UPDATE;",
                new { sceneId = identity.SceneId, projectId = identity.ProjectId, tenant = _tenant.TenantId }, tx);
            if (scene is null)
                throw new InvalidOperationException("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED");

            var owned = await conn.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS (
                    SELECT 1 FROM render.render_jobs j
                    JOIN video_render.scene_video_versions v ON v.render_job_id=j.id
                    WHERE j.id=@jobId AND j.tenant_id=@tenant AND j.job_type='render_scene_video'
                      AND j.input_json->>'projectId'=@projectId AND j.input_json->>'sceneId'=@sceneId
                      AND v.tenant_id=j.tenant_id AND v.project_id=@projectId::bigint
                      AND v.scene_id=@sceneId::bigint
                      AND v.logical_request_id=j.input_json->>'logicalRequestId'
                      AND v.id=(SELECT id FROM video_render.scene_video_versions
                                WHERE tenant_id=@tenant AND project_id=@projectId::bigint
                                  AND scene_id=@sceneId::bigint ORDER BY version_number DESC LIMIT 1)
                );
                """, new
                {
                    jobId,
                    tenant = _tenant.TenantId,
                    projectId = identity.ProjectId.Value.ToString(),
                    sceneId = identity.SceneId.Value.ToString()
                }, tx);
            if (!owned && allowPreDispatch)
            {
                owned = await conn.ExecuteScalarAsync<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1 FROM render.render_jobs j
                        WHERE j.id=@jobId AND j.tenant_id=@tenant AND j.job_type='render_scene_video'
                          AND j.status='preparing' AND j.lock_owner IS NOT NULL
                          AND j.worker_key=j.lock_owner AND j.lock_until > now()
                          AND j.provider_task_id IS NULL AND j.provider_video_id_base IS NULL
                          AND j.attempt_count > 0 AND j.attempt_count <= j.max_attempts
                          AND j.input_json->>'projectId'=@projectId
                          AND j.input_json->>'sceneId'=@sceneId
                          AND NULLIF(j.input_json->>'logicalRequestId', '') IS NOT NULL
                          -- Ownership is decided for THIS operation only. Whether the scene has ever
                          -- produced a version is irrelevant: a rerender of a scene that already holds
                          -- V1..Vn is a fresh operation and must be claimable before its own V(n+1) row
                          -- exists. The operation must not already own a version under its own logical
                          -- request id, which is what stops the same operation from claiming twice.
                          AND NOT EXISTS (
                              SELECT 1 FROM video_render.scene_video_versions v
                              WHERE v.tenant_id=@tenant AND v.project_id=@projectId::bigint
                                AND v.scene_id=@sceneId::bigint
                                AND v.logical_request_id=j.input_json->>'logicalRequestId')
                          AND NOT EXISTS (
                              SELECT 1 FROM render.render_jobs other
                              WHERE other.tenant_id=@tenant AND other.job_type='render_scene_video'
                                AND other.id <> j.id
                                AND other.input_json->>'projectId'=@projectId
                                AND other.input_json->>'sceneId'=@sceneId
                                AND (
                                    (other.status IN ('queued','preparing','rendering','post_processing')
                                        AND (NULLIF(btrim(other.lock_owner), '') IS NOT NULL
                                             OR NULLIF(btrim(other.worker_key), '') IS NOT NULL
                                             OR (other.lock_until IS NOT NULL AND other.lock_until > now())))
                                    OR (other.status NOT IN ('completed','failed','cancelled')
                                        AND (NULLIF(btrim(other.provider_task_id), '') IS NOT NULL
                                             OR NULLIF(btrim(other.provider_video_id_base), '') IS NOT NULL))
                                    OR EXISTS (
                                        SELECT 1 FROM render.scene_video_submit_intents i
                                        WHERE i.render_job_id=other.id AND i.tenant_id=other.tenant_id
                                          AND (i.submit_state IN ('SUBMIT_IN_FLIGHT','AMBIGUOUS_SUBMIT')
                                               OR (i.submit_state='SUBMIT_INTENT_PERSISTED'
                                                   AND other.status NOT IN ('completed','failed'))))))
                    );
                    """, new
                    {
                        jobId,
                        tenant = _tenant.TenantId,
                        projectId = identity.ProjectId.Value.ToString(),
                        sceneId = identity.SceneId.Value.ToString()
                    }, tx);
            }
            if (!owned)
                throw new InvalidOperationException("RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED");
            return tx;
        }
        catch
        {
            tx.Dispose();
            throw;
        }
    }

    public async Task SetProviderIdentifiersAsync(Guid jobId, string? providerTaskId, string? providerVideoIdBase, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var identity = await conn.QuerySingleOrDefaultAsync<(string JobType, long? ProjectId, long? SceneId)>(
            """
            SELECT job_type AS JobType,
                   CASE WHEN input_json->>'projectId' ~ '^[0-9]+$' THEN (input_json->>'projectId')::bigint END AS ProjectId,
                   CASE WHEN input_json->>'sceneId' ~ '^[0-9]+$' THEN (input_json->>'sceneId')::bigint END AS SceneId
              FROM render.render_jobs WHERE id=@jobId AND tenant_id=@tenant;
            """, new { jobId, tenant = _tenant.TenantId }, tx);
        if (identity.JobType == "render_video_batch")
        {
            if (identity.ProjectId is null || identity.SceneId is null)
                throw new InvalidOperationException("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED");

            await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
                new { lockName = BuildSceneJobLockName(identity.JobType, identity.SceneId.Value) }, tx);
            var authorized = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM video_render.video_project_scenes WHERE id=@sceneId AND project_id=@projectId AND tenant_id=@tenant FOR UPDATE;",
                new { sceneId = identity.SceneId, projectId = identity.ProjectId, tenant = _tenant.TenantId }, tx);
            if (authorized is null)
                throw new InvalidOperationException("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED");
        }

        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET provider_task_id=@providerTaskId,
                   provider_video_id_base=@providerVideoIdBase,
                   updated_at=now()
             WHERE id=@jobId AND tenant_id=@tenant
               AND (@sceneId IS NULL OR
                    (job_type='render_video_batch' AND input_json->>'sceneId'=@sceneId AND input_json->>'projectId'=@projectId));
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                providerTaskId,
                providerVideoIdBase,
                sceneId = identity.JobType == "render_video_batch" ? identity.SceneId?.ToString() : null,
                projectId = identity.JobType == "render_video_batch" ? identity.ProjectId?.ToString() : null
            }, tx);
        tx.Commit();
    }

    public async Task<bool> MarkRecoveredCompletedAsync(
        Guid jobId,
        long projectId,
        long sceneId,
        Guid sceneVideoVersionId,
        string logicalRequestId,
        CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = BuildSceneJobLockName("render_scene_video", sceneId) }, tx);
        var authorizedScene = await conn.ExecuteScalarAsync<long?>(
            "SELECT id FROM video_render.video_project_scenes WHERE id=@sceneId AND project_id=@projectId AND tenant_id=@tenant FOR UPDATE;",
            new { sceneId, projectId, tenant = _tenant.TenantId }, tx);
        if (authorizedScene is null)
            throw new InvalidOperationException("RVIDEO_JOB_SCENE_IDENTITY_REQUIRED");

        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs j
               SET status='completed',
                   completed_at=now(),
                   lock_owner=NULL,
                   lock_until=NULL,
                   updated_at=now()
             WHERE j.id=@jobId
               AND j.tenant_id=@tenant
               AND j.job_type='render_scene_video'
               AND j.status IN ('failed','pending_reconciliation')
               AND (
                    j.status='pending_reconciliation'
                    OR j.error_code IN (
                        'RVIDEO_VIDEO_PERSIST_FAILED',
                        'PROVIDER_SUCCESS_RECONCILIATION_FAILED',
                        'MEDIA_STORAGE_FAILED'
                    )
                    OR (
                        j.error_code = 'RenderJobTerminalFailureException'
                        AND j.error_message ILIKE '%Storage key%'
                        AND (
                            j.error_message ILIKE U&'%kh\00f4ng ghi \0111\00e8%'
                            OR j.error_message ILIKE U&'%kh\00c3\00b4ng ghi \00c4\2018\00c3\00a8%'
                            OR j.error_message ILIKE U&'%kh\00c3\0192\00c2\00b4ng ghi \00c3\201e\00e2\20ac\02dc\00c3\0192\00c2\00a8%'
                        )
                    )
               )
               AND (j.input_json->>'projectId')=@projectId
               AND (j.input_json->>'sceneId')=@sceneId
               AND (j.input_json->>'logicalRequestId')=@logicalRequestId
               AND EXISTS (
                   SELECT 1
                     FROM video_render.scene_video_versions v
                    WHERE v.id=@sceneVideoVersionId
                      AND v.tenant_id=@tenant
                      AND v.render_job_id=j.id
                      AND v.project_id=@projectId::bigint
                      AND v.scene_id=@sceneId::bigint
                      AND v.logical_request_id=@logicalRequestId
                      AND v.status='completed'
               )
               AND EXISTS (
                   SELECT 1
                     FROM billing.ai_image_billing_records b
                    WHERE b.tenant_id=@tenant
                      AND b.logical_request_id=@logicalRequestId
                      AND replace(b.render_job_id, '-', '') = replace(j.id::text, '-', '')
                      AND b.feature_code='render_job_scene_video'
                      AND b.capability_code='rvideo_scene_video_generation'
                      AND b.provider_task_id IS NOT NULL
                      AND btrim(b.provider_task_id) <> ''
                      AND b.status IN ('pending_reconciliation', 'completed')
               );
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                projectId = projectId.ToString(),
                sceneId = sceneId.ToString(),
                sceneVideoVersionId,
                logicalRequestId
            }, tx);
        tx.Commit();

        if (changed > 0)
        {
            await AddEventAsync(
                jobId,
                "RVIDEO_VIDEO_RECOVERY_COMPLETED",
                "Recovered a previously failed scene-video job after local persistence succeeded.",
                new { projectId, sceneId, sceneVideoVersionId, logicalRequestId },
                ct: ct);
        }

        return changed > 0;
    }

    private static bool IsLegacyStorageCollision(string? errorCode, string? errorMessage)
    {
        if (!string.Equals(errorCode, LegacyStorageCollisionErrorCode, StringComparison.Ordinal))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(errorMessage)
            && errorMessage.Contains("Storage key", StringComparison.OrdinalIgnoreCase)
            && (errorMessage.Contains("kh\u00f4ng ghi \u0111\u00e8", StringComparison.OrdinalIgnoreCase)
                || errorMessage.Contains("kh\u00c3\u00b4ng ghi \u00c4\u2018\u00c3\u00a8", StringComparison.OrdinalIgnoreCase)
                || errorMessage.Contains("\u00c3\u0192\u00c2\u00b4ng ghi \u00c3\u201e\u00e2\u20ac\u02dc\u00c3\u0192\u00c2\u00a8", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<int> GetProviderReconciliationAttemptCountAsync(Guid jobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)::int
              FROM render.render_job_events
             WHERE job_id=@jobId
               AND event_type='JOB_PROVIDER_POLL_SCHEDULED'
               AND COALESCE(data_json->>'reasonCode', '')='SCENE_VIDEO_RECONCILIATION_RETRY';
            """,
            new { jobId });
    }

    private const string SelectJobSql =
        """
        SELECT id AS Id, tenant_id AS TenantId, user_id AS UserId, customer_id AS CustomerId,
               job_type AS JobType, status AS Status, priority AS Priority, worker_key AS WorkerKey,
               input_json::text AS InputJson, prompt_json::text AS PromptJson,
               reference_json::text AS ReferenceJson, output_json::text AS OutputJson,
               log_code AS LogCode, error_code AS ErrorCode, error_message AS ErrorMessage,
               cancel_reason AS CancelReason, retry_of_job_id AS RetryOfJobId,
               attempt_count AS AttemptCount, max_attempts AS MaxAttempts, retry_after AS RetryAfter,
               point_cost_estimate AS PointCostEstimate, point_cost_charged AS PointCostCharged,
                point_status AS PointStatus, provider_code AS ProviderCode, model_code AS ModelCode,
               provider_task_id AS ProviderTaskId, provider_video_id_base AS ProviderVideoIdBase,
               queued_at AS QueuedAt, started_at AS StartedAt, completed_at AS CompletedAt,
               cancelled_at AS CancelledAt, created_at AS CreatedAt, updated_at AS UpdatedAt
          FROM render.render_jobs
        """;

    private const string InsertJobSql =
        """
        INSERT INTO render.render_jobs
            (tenant_id, user_id, customer_id, job_type, status, priority,
             input_json, prompt_json, reference_json, log_code,
             point_cost_estimate, point_status, provider_code, model_code, max_attempts,
             queued_at, created_at)
        VALUES
            (@tenant, @user, @customer, @type, @status, @priority,
             CAST(@input AS jsonb), CAST(@prompt AS jsonb), CAST(@refs AS jsonb), @logCode,
             @pointCost, @pointStatus, @provider, @model, @maxAttempts,
             now(), now())
        RETURNING id AS Id, tenant_id AS TenantId, user_id AS UserId, customer_id AS CustomerId,
                  job_type AS JobType, status AS Status, priority AS Priority, worker_key AS WorkerKey,
                  input_json::text AS InputJson, prompt_json::text AS PromptJson,
                  reference_json::text AS ReferenceJson, output_json::text AS OutputJson,
                  log_code AS LogCode, error_code AS ErrorCode, error_message AS ErrorMessage,
                  cancel_reason AS CancelReason, retry_of_job_id AS RetryOfJobId,
                  attempt_count AS AttemptCount, max_attempts AS MaxAttempts, retry_after AS RetryAfter,
                  point_cost_estimate AS PointCostEstimate, point_cost_charged AS PointCostCharged,
                  point_status AS PointStatus, provider_code AS ProviderCode, model_code AS ModelCode,
                  provider_task_id AS ProviderTaskId, provider_video_id_base AS ProviderVideoIdBase,
                  queued_at AS QueuedAt, started_at AS StartedAt, completed_at AS CompletedAt,
                  cancelled_at AS CancelledAt, created_at AS CreatedAt, updated_at AS UpdatedAt;
        """;

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private int GetMaxProviderPolls()
        => Math.Max(1, _configuration.GetValue("VideoRender:MaxReconciliationRetries", 3));

    private int GetProviderPollTimeoutMinutes()
        => Math.Max(1, _configuration.GetValue("VideoRender:MaxPollDurationMinutes", 30));

    private int GetMaxKnownTaskRecoveryPolls()
        => Math.Clamp(_configuration.GetValue("VideoRender:KnownTaskRecoveryMaxPolls", 20), 1, 200);

    private int GetKnownTaskRecoveryWindowMinutes()
        => Math.Clamp(_configuration.GetValue("VideoRender:KnownTaskRecoveryWindowMinutes", 1440), 1, 10080);
}
