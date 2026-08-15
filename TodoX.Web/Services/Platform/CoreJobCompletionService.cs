using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

public sealed record CoreJobProgressRequest(
    Guid JobId,
    string CurrentStep,
    int ProgressPercent,
    string? Message = null,
    JsonElement? Data = null);

public sealed record CoreJobCompleteRequest(
    Guid JobId,
    JsonElement Output,
    string? Message = null);

public sealed record CoreJobFailRequest(
    Guid JobId,
    string ErrorCode,
    string ErrorMessage,
    CoreFailureBillingPolicy BillingPolicy = CoreFailureBillingPolicy.ReleaseReservation);

public sealed record CoreExecutionCorrelation(
    string ExecutionSystem,
    string ExternalExecutionId,
    string? Adapter = null,
    JsonElement? Metadata = null);

public sealed class CoreExecutionAuthority
{
    private CoreExecutionAuthority(string source)
    {
        Source = source;
    }

    public string Source { get; }

    internal static CoreExecutionAuthority Trusted(string source)
        => new(string.IsNullOrWhiteSpace(source)
            ? throw new ArgumentException("Trusted execution source is required.", nameof(source))
            : source.Trim());
}

public interface ICoreJobCompletionService
{
    Task MarkDeferredAsync(
        CoreExecutionAuthority authority,
        Guid jobId,
        CoreExecutionCorrelation correlation,
        string? message = null,
        CancellationToken ct = default);

    Task MarkProgressAsync(
        CoreExecutionAuthority authority,
        CoreJobProgressRequest request,
        CancellationToken ct = default);

    Task<CoreBillingCompletion> CompleteAsync(
        CoreExecutionAuthority authority,
        CoreJobCompleteRequest request,
        CancellationToken ct = default);

    Task<CoreBillingCompletion> FailAsync(
        CoreExecutionAuthority authority,
        CoreJobFailRequest request,
        CancellationToken ct = default);
}

/// <summary>
/// Internal callback/finalizer boundary for long-running Core jobs. Public API v1 does not expose
/// completion/failure endpoints; trusted runtimes call this service after external work is truly done.
/// </summary>
public sealed class CoreJobCompletionService : ICoreJobCompletionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ICoreBillingService _billing;

    public CoreJobCompletionService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ICoreBillingService billing)
    {
        _factory = factory;
        _tenant = tenant;
        _billing = billing;
    }

    public async Task MarkDeferredAsync(
        CoreExecutionAuthority authority,
        Guid jobId,
        CoreExecutionCorrelation correlation,
        string? message = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (string.IsNullOrWhiteSpace(correlation.ExecutionSystem))
        {
            throw new ArgumentException("Execution system is required.", nameof(correlation));
        }

        if (string.IsNullOrWhiteSpace(correlation.ExternalExecutionId))
        {
            throw new ArgumentException("External execution id is required.", nameof(correlation));
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status='rendering',
                   current_step=CASE
                       WHEN progress_percent > 1 THEN current_step
                       ELSE 'external_execution'
                   END,
                   progress_percent=GREATEST(progress_percent, 1),
                   options=jsonb_set(
                       COALESCE(options, '{}'::jsonb),
                       '{execution}',
                       CAST(@execution AS jsonb),
                       true),
                   lock_owner=NULL,
                   lock_until=NULL,
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant
               AND job_type=@jobType
               AND status NOT IN ('completed','failed','cancelled')
               AND progress_percent < @progress;
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                jobType = RenderJobTypes.CoreService,
                execution = JsonSerializer.Serialize(new
                {
                    system = correlation.ExecutionSystem.Trim(),
                    external_execution_id = correlation.ExternalExecutionId.Trim(),
                    adapter = Normalize(correlation.Adapter),
                    authority = authority.Source,
                    metadata = correlation.Metadata
                }, JsonOptions)
            },
            tx);

        await AddEventAsync(conn, tx, jobId, "CORE_JOB_DEFERRED", message ?? "Core job accepted for external execution.",
            new
            {
                system = correlation.ExecutionSystem,
                externalExecutionId = correlation.ExternalExecutionId,
                adapter = correlation.Adapter
            });
        tx.Commit();
    }

    public async Task MarkProgressAsync(
        CoreExecutionAuthority authority,
        CoreJobProgressRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ValidateProgress(request);

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var changed = await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=CASE
                       WHEN lower(@step)='post_processing' THEN 'post_processing'
                       ELSE status
                   END,
                   current_step=@step,
                   progress_percent=@progress,
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant
               AND job_type=@jobType
               AND status NOT IN ('completed','failed','cancelled');
            """,
            new
            {
                jobId = request.JobId,
                tenant = _tenant.TenantId,
                jobType = RenderJobTypes.CoreService,
                step = request.CurrentStep.Trim(),
                progress = request.ProgressPercent
            },
            tx);

        if (changed > 0)
        {
            await AddEventAsync(conn, tx, request.JobId, "CORE_JOB_PROGRESS",
                request.Message ?? $"Core job progress updated to {request.ProgressPercent}%.",
                new
                {
                    step = request.CurrentStep,
                    progress = request.ProgressPercent,
                    authority = authority.Source,
                    data = request.Data
                });
        }

        tx.Commit();
    }

    public async Task<CoreBillingCompletion> CompleteAsync(
        CoreExecutionAuthority authority,
        CoreJobCompleteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var result = await _billing.CompleteAsync(
            new CoreBillingCompletionRequest(
                request.JobId,
                request.Output,
                request.Message ?? $"Core job completed by {authority.Source}."),
            ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Core job completion failed.");
        }

        return result;
    }

    public async Task<CoreBillingCompletion> FailAsync(
        CoreExecutionAuthority authority,
        CoreJobFailRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (string.IsNullOrWhiteSpace(request.ErrorCode))
        {
            throw new ArgumentException("Error code is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ErrorMessage))
        {
            throw new ArgumentException("Error message is required.", nameof(request));
        }

        var result = await _billing.FailAsync(
            new CoreBillingFailureRequest(
                request.JobId,
                request.ErrorCode.Trim(),
                request.ErrorMessage.Trim(),
                request.BillingPolicy),
            ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Core job failure handling failed.");
        }

        return result;
    }

    private async Task AddEventAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid jobId,
        string eventType,
        string message,
        object data)
        => await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_events
                (job_id, tenant_id, event_type, level, message, data_json, created_at)
            VALUES
                (@jobId, @tenant, @eventType, 'info', @message, CAST(@data AS jsonb), now());
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                eventType,
                message,
                data = JsonSerializer.Serialize(data, JsonOptions)
            },
            tx);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static void ValidateProgress(CoreJobProgressRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentStep))
        {
            throw new ArgumentException("Current step is required.", nameof(request));
        }

        if (request.ProgressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Progress percent must be between 0 and 100.");
        }
    }
}
