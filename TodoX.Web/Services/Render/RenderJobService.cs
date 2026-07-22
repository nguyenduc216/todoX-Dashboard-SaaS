using System.Text.Json;
using Dapper;
using Npgsql;
using TodoX.Web.Data;

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

    Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<RenderJobDto?> GetByLogCodeAsync(string logCode, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobDto>> ListByLogCodeAsync(string logCode, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobEventDto>> GetEventsAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobEventDto>> GetEventsByLogCodeAsync(string logCode, CancellationToken ct = default);
    Task AddEventAsync(Guid jobId, string eventType, string message, object? data = null, string level = "info", CancellationToken ct = default);
    Task<bool> CancelAsync(Guid jobId, string reason, Guid? userId = null, CancellationToken ct = default);
    Task<RenderJobDto?> RetryAsync(Guid jobId, Guid? userId = null, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextAsync(string workerKey, TimeSpan lockFor, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextByJobTypeAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> jobTypes, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextExcludingJobTypesAsync(string workerKey, TimeSpan lockFor, IReadOnlyCollection<string> excludedJobTypes, CancellationToken ct = default);
    Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default);
    Task ScheduleRetryAsync(Guid jobId, TimeSpan delay, string errorCode, string errorMessage, CancellationToken ct = default);
}

public sealed class RenderJobService : IRenderJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<RenderJobService> _logger;

    public RenderJobService(TodoXConnectionFactory factory, TenantContext tenant, ILogger<RenderJobService> logger)
    {
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<RenderJobDto> EnqueueAsync(RenderJobCreateModel model, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(model.JobType))
        {
            throw new ArgumentException("Job type is required.", nameof(model));
        }

        await _tenant.EnsureLoadedAsync(ct);
        var inputJson = ToJson(model.Input ?? new { });
        var promptJson = ToJson(model.Prompt ?? new { });
        var referenceJson = ToJson(model.References ?? Array.Empty<object>());

        using var conn = await _factory.OpenAsync(ct);
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
                priority = model.Priority,
                input = inputJson,
                prompt = promptJson,
                refs = referenceJson,
                logCode = model.LogCode,
                pointCost = model.PointCostEstimate,
                pointStatus = model.PointStatus,
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

        await AddEventAsync(job.Id, "JOB_QUEUED", "Render job queued.", new
        {
            job.JobType,
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
                    priority = model.Priority,
                    input = inputJson,
                    prompt = promptJson,
                    refs = referenceJson,
                    logCode = model.LogCode,
                    pointCost = model.PointCostEstimate,
                    pointStatus = model.PointStatus,
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

        await AddEventAsync(job.Id, "JOB_QUEUED", "Render job queued.", new
        {
            job.JobType,
            job.Priority,
            job.PointCostEstimate,
            job.PointStatus
        }, ct: ct);

        return (job, false);
    }

    public async Task UpsertSnapshotAsync(Guid jobId, object projectSnapshot, object sceneSnapshots, CancellationToken ct = default)
    {
        await AddEventAsync(jobId, "snapshot_updated", "Render job snapshot updated.", new
        {
            projectSnapshot,
            sceneSnapshots
        }, ct: ct);
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
        if (current is null || current.Status != RenderJobStatuses.Failed)
        {
            return null;
        }

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
            PointCostEstimate = current.PointCostEstimate,
            PointStatus = current.PointCostEstimate > 0 ? RenderPointStatuses.Pending : RenderPointStatuses.NotRequired,
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

    private async Task<RenderJobDto?> ClaimNextInternal(
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
                   WHERE status='queued'
                     AND (retry_after IS NULL OR retry_after <= now())
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

        sql += """
                ORDER BY priority ASC, queued_at ASC
                FOR UPDATE SKIP LOCKED
                LIMIT 1;
               """;

        var job = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(sql, parameters, tx);

        if (job is null)
        {
            tx.Commit();
            return null;
        }

        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='preparing',
                   worker_key=@workerKey,
                   lock_owner=@workerKey,
                   lock_until=now() + (@lockSeconds || ' seconds')::interval,
                   attempt_count=attempt_count + 1,
                   started_at=COALESCE(started_at, now()),
                   updated_at=now()
             WHERE id=@id;
            """,
            new { id = job.Id, workerKey, lockSeconds = Math.Max(1, (int)lockFor.TotalSeconds) },
            tx);

        tx.Commit();
        await AddEventAsync(job.Id, "WORKER_CLAIMED", "Worker claimed render job.", new { workerKey }, ct: ct);
        return await GetAsync(job.Id, ct);
    }

    public async Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default)
    {
        var dbStatus = NormalizeStatus(status);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   output_json = CASE WHEN @output IS NULL THEN output_json ELSE CAST(@output AS jsonb) END,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   completed_at = CASE
                       WHEN @status IN ('completed', 'failed', 'cancelled') THEN now()
                       WHEN @status='pending_reconciliation' THEN NULL
                       ELSE completed_at
                   END,
                   cancelled_at = CASE WHEN @status='cancelled' THEN now() ELSE cancelled_at END,
                   updated_at=now()
             WHERE id=@jobId;
            """,
            new { jobId, status = dbStatus, output = output is null ? null : ToJson(output), errorCode, errorMessage });

        var level = dbStatus == RenderJobStatuses.Failed ? "error"
            : dbStatus == RenderJobStatuses.Cancelled || dbStatus == RenderJobStatuses.PendingReconciliation ? "warning"
            : "info";
        await AddEventAsync(jobId, "JOB_STATUS_CHANGED", $"Render job status changed to {dbStatus}.",
            new { status = dbStatus, errorCode, errorMessage }, level, ct);
    }

    private static string NormalizeStatus(string status)
        => status.Equals(RenderJobStatuses.Processing, StringComparison.OrdinalIgnoreCase) ? RenderJobStatuses.Rendering : status;

    public static string BuildProjectJobLockName(string jobType, long projectId)
        => $"render.render_jobs:{jobType.Trim().ToLowerInvariant()}:{projectId}";

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
        await conn.ExecuteAsync(
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

        await AddEventAsync(jobId, "JOB_RETRY_SCHEDULED", "Render job retry scheduled.",
            new { retryAfterSeconds = Math.Max(1, (int)delay.TotalSeconds), errorCode, errorMessage }, "warning", ct);
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
            (@tenant, @user, @customer, @type, 'queued', @priority,
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
                  queued_at AS QueuedAt, started_at AS StartedAt, completed_at AS CompletedAt,
                  cancelled_at AS CancelledAt, created_at AS CreatedAt, updated_at AS UpdatedAt;
        """;

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
