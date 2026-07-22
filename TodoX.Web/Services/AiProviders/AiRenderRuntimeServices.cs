using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.AiProviders;

public static class AiRenderEventTypes
{
    public const string JobCreated = "job_created";
    public const string JobQueued = "job_queued";
    public const string JobClaimed = "job_claimed";
    public const string InputValidated = "input_validated";
    public const string InputStaged = "input_staged";
    public const string ProviderSelected = "provider_selected";
    public const string ProviderAccountClaimed = "provider_account_claimed";
    public const string CredentialResolved = "credential_resolved";
    public const string BillingEstimated = "billing_estimated";
    public const string BillingReserved = "billing_reserved";
    public const string ProviderSubmitStarted = "provider_submit_started";
    public const string ProviderSubmitted = "provider_submitted";
    public const string ProviderPolled = "provider_polled";
    public const string ProviderCallbackReceived = "provider_callback_received";
    public const string ProviderGenerating = "provider_generating";
    public const string ProviderCompleted = "provider_completed";
    public const string ProviderFailed = "provider_failed";
    public const string OutputStageStarted = "output_stage_started";
    public const string OutputStaged = "output_staged";
    public const string ArtifactCreated = "artifact_created";
    public const string UsageFinalized = "usage_finalized";
    public const string BillingCompleted = "billing_completed";
    public const string BillingReleased = "billing_released";
    public const string BillingRefundPending = "billing_refund_pending";
    public const string BillingRefunded = "billing_refunded";
    public const string BillingReconciliationPending = "billing_reconciliation_pending";
    public const string LeaseHeartbeat = "lease_heartbeat";
    public const string LeaseReleased = "lease_released";
    public const string RetryScheduled = "retry_scheduled";
    public const string JobCompleted = "job_completed";
    public const string JobFailed = "job_failed";
    public const string JobCancelled = "job_cancelled";
    public const string ManualReviewRequired = "manual_review_required";
}

public static class AiRenderStepKeys
{
    public const string ValidateInput = "validate_input";
    public const string StageInput = "stage_input";
    public const string ResolveProvider = "resolve_provider";
    public const string ClaimAccount = "claim_account";
    public const string ReserveBilling = "reserve_billing";
    public const string SubmitProvider = "submit_provider";
    public const string WaitProvider = "wait_provider";
    public const string StageOutput = "stage_output";
    public const string FinalizeUsage = "finalize_usage";
    public const string FinalizeBilling = "finalize_billing";
    public const string ReleaseLease = "release_lease";
    public const string CompleteBusinessJob = "complete_business_job";
}

public sealed class AiRenderCompletionRequest
{
    public Guid RenderJobId { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public Guid? LeaseId { get; set; }
    public string? WorkerKey { get; set; }
    public string? ProviderCode { get; set; }
    public string? CapabilityCode { get; set; }
    public string? FeatureCode { get; set; }
    public string? ModelName { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? ProviderStatus { get; set; }
    public bool Success { get; set; }
    public IReadOnlyList<string> OutputUrls { get; set; } = Array.Empty<string>();
    public decimal? ProviderUsageQuantity { get; set; }
    public string? ProviderUsageUnit { get; set; }
    public decimal? ProviderEstimatedCost { get; set; }
    public decimal? ProviderActualCost { get; set; }
    public string? ProviderCurrency { get; set; }
    public string? RawUsageJson { get; set; }
    public string? RawResponseJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string CompletionSource { get; set; } = "poll";
    public string? LogicalRequestId { get; set; }
    public string? ArtifactType { get; set; }
    public string? MimeType { get; set; }
}

public sealed record AiRenderCompletionResult(
    bool Found,
    bool CompletedNow,
    string Status,
    Guid RenderJobId,
    string? ExistingOutputJson,
    string? ErrorMessage);

public interface IAiRenderCompletionService
{
    Task<AiRenderCompletionResult> CompleteAsync(AiRenderCompletionRequest request, CancellationToken ct = default);
}

public sealed class AiRenderCompletionService : IAiRenderCompletionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IAiProviderUsageService _usage;
    private readonly IAiBillingService _billing;
    private readonly IAiProviderAccountRepository _accounts;

    public AiRenderCompletionService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IAiProviderUsageService usage,
        IAiBillingService billing,
        IAiProviderAccountRepository accounts)
    {
        _factory = factory;
        _tenant = tenant;
        _usage = usage;
        _billing = billing;
        _accounts = accounts;
    }

    public async Task<AiRenderCompletionResult> CompleteAsync(AiRenderCompletionRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        var job = await conn.QuerySingleOrDefaultAsync<RenderJobLockRow>(
            """
            SELECT id AS Id, tenant_id AS TenantId, user_id AS UserId, customer_id AS CustomerId,
                   status AS Status, output_json::text AS OutputJson, point_status AS PointStatus
              FROM render.render_jobs
             WHERE id=@RenderJobId
             FOR UPDATE;
            """,
            new { request.RenderJobId },
            tx);
        if (job is null)
        {
            tx.Commit();
            return new AiRenderCompletionResult(false, false, "missing", request.RenderJobId, null, "Render job not found.");
        }

        if (job.Status is RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled)
        {
            tx.Commit();
            return new AiRenderCompletionResult(true, false, job.Status, request.RenderJobId, job.OutputJson, null);
        }

        var status = request.Success ? RenderJobStatuses.Completed : RenderJobStatuses.Failed;
        var terminalEvent = request.Success ? AiRenderEventTypes.JobCompleted : AiRenderEventTypes.JobFailed;
        var providerEvent = request.Success ? AiRenderEventTypes.ProviderCompleted : AiRenderEventTypes.ProviderFailed;
        var redactedResponse = AiSecretRedactor.Redact(request.RawResponseJson);
        var outputJson = JsonSerializer.Serialize(new
        {
            outputUrls = request.OutputUrls,
            request.ProviderTaskId,
            request.ProviderStatus,
            request.CompletionSource
        }, JsonOptions);

        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   provider_task_id=COALESCE(@ProviderTaskId, provider_task_id),
                   provider_account_id=COALESCE(@ProviderAccountId, provider_account_id),
                   output_json=CAST(@outputJson AS jsonb),
                   error_code=@ErrorCode,
                   error_message=@ErrorMessage,
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE id=@RenderJobId;
            """,
            new
            {
                request.RenderJobId,
                status,
                request.ProviderTaskId,
                request.ProviderAccountId,
                outputJson,
                request.ErrorCode,
                request.ErrorMessage
            },
            tx);

        await UpsertStepAsync(conn, tx, request.RenderJobId, AiRenderStepKeys.FinalizeUsage, "Finalize usage", request.Success ? "completed" : "failed", 9, new
        {
            request.ProviderUsageQuantity,
            request.ProviderUsageUnit,
            request.ProviderActualCost,
            request.ProviderCurrency
        });
        await UpsertStepAsync(conn, tx, request.RenderJobId, AiRenderStepKeys.FinalizeBilling, "Finalize billing", request.Success ? "completed" : "failed", 10, new
        {
            request.LogicalRequestId,
            request.Success
        });
        await UpsertStepAsync(conn, tx, request.RenderJobId, AiRenderStepKeys.ReleaseLease, "Release lease", "completed", 11, new
        {
            request.LeaseId,
            request.ProviderAccountId
        });

        await AddEventAsync(conn, tx, request, providerEvent, request.Success ? "Provider completed." : "Provider failed.", request.Success ? "info" : "error", new
        {
            request.ProviderTaskId,
            request.ProviderStatus,
            request.ProviderUsageQuantity,
            request.ProviderUsageUnit,
            response = redactedResponse
        });
        await AddEventAsync(conn, tx, request, terminalEvent, request.Success ? "Render job completed." : request.ErrorMessage ?? "Render job failed.", request.Success ? "info" : "error", new
        {
            request.CompletionSource,
            request.OutputUrls,
            request.ErrorCode,
            request.ErrorMessage
        });

        var artifactType = string.IsNullOrWhiteSpace(request.ArtifactType) ? "result" : request.ArtifactType.Trim();
        foreach (var url in request.OutputUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO render.render_artifacts
                    (id, render_job_id, artifact_type, public_url, provider_url, mime_type, metadata_json, created_at)
                VALUES
                    (gen_random_uuid(), @RenderJobId, @artifactType, @url, @url, @mimeType,
                     jsonb_build_object('providerTaskId', @ProviderTaskId, 'completionSource', @CompletionSource),
                     now())
                ON CONFLICT DO NOTHING;
                """,
                new
                {
                    request.RenderJobId,
                    artifactType,
                    url,
                    mimeType = request.MimeType,
                    request.ProviderTaskId,
                    request.CompletionSource
                },
                tx);
        }

        tx.Commit();

        await _usage.RecordAsync(new AiProviderUsageLog
        {
            CustomerGuid = job.CustomerId,
            UserId = job.UserId,
            RenderJobId = request.RenderJobId,
            ProviderAccountId = request.ProviderAccountId,
            ProviderCode = request.ProviderCode,
            CapabilityCode = request.CapabilityCode,
            FeatureCode = request.FeatureCode,
            ModelName = request.ModelName,
            ProviderTaskId = request.ProviderTaskId,
            RequestId = request.LogicalRequestId,
            Quantity = request.ProviderUsageQuantity ?? 1,
            UnitType = request.ProviderUsageUnit ?? "request",
            ProviderRawCost = request.ProviderActualCost,
            ProviderCostCurrency = request.ProviderCurrency,
            ProviderUsageJson = request.RawUsageJson,
            ResponseJson = redactedResponse,
            Status = request.Success ? "success" : "failed",
            ErrorCode = request.ErrorCode,
            ErrorMessage = request.ErrorMessage,
            IdempotencyKey = $"{request.RenderJobId:N}:{request.ProviderTaskId ?? "no-task"}:{request.CompletionSource}:terminal"
        }, ct);

        if (!string.IsNullOrWhiteSpace(request.LogicalRequestId))
        {
            if (request.Success)
            {
                await _billing.CompleteAsync(new AiBillingCompletionRequest
                {
                    LogicalRequestId = request.LogicalRequestId,
                    Success = true,
                    ActualModel = request.ModelName,
                    ProviderTaskId = request.ProviderTaskId,
                    ProviderActualCost = request.ProviderActualCost,
                    ProviderCurrency = request.ProviderCurrency,
                    ProviderUsageJson = request.RawUsageJson,
                    ErrorMessage = null
                }, ct);
            }
            else
            {
                await _billing.CompleteAsync(new AiBillingCompletionRequest
                {
                    LogicalRequestId = request.LogicalRequestId,
                    Success = false,
                    ActualModel = request.ModelName,
                    ProviderTaskId = request.ProviderTaskId,
                    ProviderActualCost = request.ProviderActualCost,
                    ProviderCurrency = request.ProviderCurrency,
                    ProviderUsageJson = request.RawUsageJson,
                    ErrorMessage = request.ErrorMessage
                }, ct);
            }
        }

        if (request.LeaseId is Guid leaseId && !string.IsNullOrWhiteSpace(request.WorkerKey))
        {
            await _accounts.ReleaseLeaseAsync(leaseId, request.WorkerKey!, request.Success ? "completed" : "failed", ct);
            if (request.ProviderAccountId is Guid accountId)
            {
                if (request.Success)
                {
                    await _accounts.MarkAccountSuccessAsync(accountId, ct);
                }
                else
                {
                    await _accounts.MarkAccountFailureAsync(accountId, TimeSpan.FromMinutes(1), ct);
                }
            }
        }

        return new AiRenderCompletionResult(true, true, status, request.RenderJobId, outputJson, request.ErrorMessage);
    }

    private static async Task AddEventAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        AiRenderCompletionRequest request,
        string eventType,
        string message,
        string level,
        object data)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_events
                (job_id, render_job_id, tenant_id, event_type, level, message, data_json,
                 provider_code, model_code, provider_account_id, provider_task_id, created_at)
            VALUES
                (@RenderJobId, @RenderJobId, NULL, @eventType, @level, @message, CAST(@data AS jsonb),
                 @ProviderCode, @ModelName, @ProviderAccountId, @ProviderTaskId, now());
            """,
            new
            {
                request.RenderJobId,
                eventType,
                level,
                message,
                data = JsonSerializer.Serialize(data, JsonOptions),
                request.ProviderCode,
                request.ModelName,
                request.ProviderAccountId,
                request.ProviderTaskId
            },
            tx);
    }

    private static async Task UpsertStepAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid renderJobId,
        string stepKey,
        string stepName,
        string status,
        int order,
        object output)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO render.render_job_steps
                (id, render_job_id, step_key, step_name, step_order, status, attempt, output_json, started_at, finished_at, created_at)
            VALUES
                (gen_random_uuid(), @renderJobId, @stepKey, @stepName, @order, @status, 1, CAST(@output AS jsonb), now(), now(), now())
            ON CONFLICT (render_job_id, step_key, attempt) DO UPDATE
                SET status=EXCLUDED.status,
                    output_json=EXCLUDED.output_json,
                    finished_at=EXCLUDED.finished_at;
            """,
            new
            {
                renderJobId,
                stepKey,
                stepName,
                order,
                status,
                output = JsonSerializer.Serialize(output, JsonOptions)
            },
            tx);
    }

    private sealed class RenderJobLockRow
    {
        public Guid Id { get; init; }
        public Guid? TenantId { get; init; }
        public Guid? UserId { get; init; }
        public Guid? CustomerId { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? OutputJson { get; init; }
        public string? PointStatus { get; init; }
    }
}

