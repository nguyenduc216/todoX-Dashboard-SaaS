using System.Text.Json;
using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.Render;

public interface IRenderJobService
{
    Task<RenderJobDto> EnqueueAsync(RenderJobCreateModel model, CancellationToken ct = default);
    Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<RenderJobEventDto>> GetEventsAsync(Guid jobId, CancellationToken ct = default);
    Task AddEventAsync(Guid jobId, string eventType, string message, object? data = null, string level = "info", CancellationToken ct = default);
    Task<bool> CancelAsync(Guid jobId, string reason, Guid? userId = null, CancellationToken ct = default);
    Task<RenderJobDto?> RetryAsync(Guid jobId, Guid? userId = null, CancellationToken ct = default);
    Task<RenderJobDto?> ClaimNextAsync(string workerKey, TimeSpan lockFor, CancellationToken ct = default);
    Task MarkStatusAsync(Guid jobId, string status, object? output = null, string? errorCode = null, string? errorMessage = null, CancellationToken ct = default);
    Task ScheduleRetryAsync(Guid jobId, TimeSpan delay, string errorCode, string errorMessage, CancellationToken ct = default);
}

public sealed class RenderJobService : IRenderJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public RenderJobService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
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
        var job = await conn.QuerySingleAsync<RenderJobDto>(
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
            """,
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

        await AddEventAsync(job.Id, "JOB_QUEUED", "Render job queued.", new
        {
            job.JobType,
            job.Priority,
            job.PointCostEstimate,
            job.PointStatus
        }, ct: ct);

        return job;
    }

    public async Task<RenderJobDto?> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<RenderJobDto>(SelectJobSql + " WHERE id=@id;", new { id = jobId });
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
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var job = await conn.QuerySingleOrDefaultAsync<RenderJobDto>(
            SelectJobSql +
            """
             WHERE status='queued'
               AND (retry_after IS NULL OR retry_after <= now())
             ORDER BY priority ASC, queued_at ASC
             FOR UPDATE SKIP LOCKED
             LIMIT 1;
            """,
            transaction: tx);

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
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   output_json = CASE WHEN @output IS NULL THEN output_json ELSE CAST(@output AS jsonb) END,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   completed_at = CASE WHEN @status IN ('completed', 'failed', 'cancelled') THEN now() ELSE completed_at END,
                   cancelled_at = CASE WHEN @status='cancelled' THEN now() ELSE cancelled_at END,
                   updated_at=now()
             WHERE id=@jobId;
            """,
            new { jobId, status, output = output is null ? null : ToJson(output), errorCode, errorMessage });

        var level = status == RenderJobStatuses.Failed ? "error" : status == RenderJobStatuses.Cancelled ? "warning" : "info";
        await AddEventAsync(jobId, "JOB_STATUS_CHANGED", $"Render job status changed to {status}.",
            new { status, errorCode, errorMessage }, level, ct);
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

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
