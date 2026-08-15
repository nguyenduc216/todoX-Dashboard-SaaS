using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

public interface ICoreJobApplicationService
{
    Task<CoreJobView> CreateAsync(CoreRequestContext context, CoreCreateJobRequest request, CancellationToken ct = default);
    Task<CoreJobView?> GetAsync(CoreRequestContext context, Guid jobId, CancellationToken ct = default);
    Task<CoreJobListResult> ListAsync(CoreRequestContext context, CoreJobListRequest request, CancellationToken ct = default);
    Task<CoreJobView> CancelAsync(CoreRequestContext context, Guid jobId, string? reason = null, CancellationToken ct = default);
    Task<CoreJobView> RetryAsync(CoreRequestContext context, Guid jobId, CoreRetryJobRequest request, CancellationToken ct = default);
}

public sealed class CoreInsufficientBalanceException : InvalidOperationException
{
    public CoreInsufficientBalanceException(Guid jobId, string message)
        : base(message)
    {
        JobId = jobId;
    }

    public Guid JobId { get; }
}

/// <summary>
/// Canonical transport-neutral job lifecycle shared by Dashboard and API transports.
/// Service validation, pricing, idempotency, caller scoping and billing happen here.
/// </summary>
public sealed class CoreJobApplicationService : ICoreJobApplicationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ICoreServiceCatalogService _catalog;
    private readonly ICoreBillingService _billing;

    public CoreJobApplicationService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ICoreServiceCatalogService catalog,
        ICoreBillingService billing)
    {
        _factory = factory;
        _tenant = tenant;
        _catalog = catalog;
        _billing = billing;
    }

    public Task<CoreJobView> CreateAsync(
        CoreRequestContext context,
        CoreCreateJobRequest request,
        CancellationToken ct = default)
        => CreateInternalAsync(context, request, retryOfJobId: null, ct);

    private async Task<CoreJobView> CreateInternalAsync(
        CoreRequestContext context,
        CoreCreateJobRequest request,
        Guid? retryOfJobId,
        CancellationToken ct)
    {
        CoreJobAccess.EnsureAuthenticated(context);

        if (string.IsNullOrWhiteSpace(request.ServiceCode))
        {
            throw new ArgumentException("Service code is required.", nameof(request));
        }

        var channel = context.NormalizedChannel;
        var service = await _catalog.GetByCodeAsync(request.ServiceCode, ct)
            ?? throw new KeyNotFoundException($"TodoX service '{request.ServiceCode}' was not found.");

        if (!service.Enabled)
        {
            throw new InvalidOperationException($"TodoX service '{service.ServiceCode}' is disabled.");
        }

        if (request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("Job input is required.", nameof(request));
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey ?? context.ExternalRequestId);
        if (RequiresIdempotencyKey(channel) && idempotencyKey is null)
        {
            throw new ArgumentException(
                $"IdempotencyKey is required for channel '{channel}' to prevent duplicate paid jobs.",
                nameof(request));
        }

        var logicalRequestId = idempotencyKey is null
            ? null
            : BuildLogicalRequestId(context, service.ServiceCode, idempotencyKey);
        var estimate = await _billing.EstimateAsync(context, service, request.Input, ct);

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        if (logicalRequestId is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
                new { lockName = $"core-service-job:{logicalRequestId}" },
                tx,
                cancellationToken: ct));

            var existing = await conn.QuerySingleOrDefaultAsync<CoreJobRow>(new CommandDefinition(
                SelectSql +
                """
                 WHERE r.tenant_id = @tenantId
                   AND r.job_type = @jobType
                   AND r.logical_request_id = @logicalRequestId
                 ORDER BY r.created_at DESC
                 LIMIT 1;
                """,
                new
                {
                    tenantId = _tenant.TenantId,
                    jobType = RenderJobTypes.CoreService,
                    logicalRequestId
                },
                tx,
                cancellationToken: ct));

            if (existing is not null)
            {
                tx.Commit();
                return Map(existing);
            }
        }

        var jobId = Guid.NewGuid();
        var envelope = new CoreServiceJobEnvelope
        {
            ServiceId = service.Id,
            ServiceCode = service.ServiceCode,
            Channel = channel,
            ClientId = NormalizeOptional(context.ClientId),
            ExternalRequestId = NormalizeOptional(context.ExternalRequestId),
            Payload = request.Input.Clone(),
            Prompt = request.Prompt?.Clone(),
            References = request.References?.Clone()
        };

        var inputJson = JsonSerializer.Serialize(envelope, JsonOptions);
        var promptJson = request.Prompt is null ? "{}" : request.Prompt.Value.GetRawText();
        var referenceJson = request.References is null ? "[]" : request.References.Value.GetRawText();
        var optionsJson = JsonSerializer.Serialize(new
        {
            platform = new
            {
                channel,
                client_id = NormalizeOptional(context.ClientId),
                external_request_id = NormalizeOptional(context.ExternalRequestId),
                service_code = service.ServiceCode,
                retry_of_job_id = retryOfJobId
            },
            billing = new
            {
                estimate.QualityTier,
                estimate.ImageCount,
                estimate.SceneCount,
                estimate.DurationSeconds
            }
        }, JsonOptions);

        var initialPointStatus = estimate.ChargeRequired
            ? RenderPointStatuses.Pending
            : RenderPointStatuses.NotRequired;
        var row = await conn.QuerySingleAsync<CoreJobRow>(new CommandDefinition(
            """
            INSERT INTO render.render_jobs
                (id, tenant_id, customer_id, user_id, service_id,
                 logical_request_id, job_type, operation_type, source_type,
                 status, current_step, progress_percent, priority,
                 input_json, prompt_json, reference_json, output_json, options,
                 point_cost_estimate, point_cost_charged, point_status,
                 retry_of_job_id, max_attempts, queued_at, created_at)
            VALUES
                (@jobId, @tenantId, @customerId, @userId, @serviceId,
                 @logicalRequestId, @jobType, @operationType, @sourceType,
                 'draft', 'billing', 0, @priority,
                 CAST(@inputJson AS jsonb), CAST(@promptJson AS jsonb), CAST(@referenceJson AS jsonb), '[]'::jsonb, CAST(@optionsJson AS jsonb),
                 @pointCostEstimate, 0, @pointStatus,
                 @retryOfJobId, 1, now(), now())
            RETURNING id AS Id,
                      service_id AS ServiceId,
                      customer_id AS CustomerId,
                      user_id AS UserId,
                      status AS Status,
                      source_type AS SourceType,
                      operation_type AS OperationType,
                      logical_request_id AS LogicalRequestId,
                      current_step AS CurrentStep,
                      progress_percent AS ProgressPercent,
                      point_cost_estimate AS PointCostEstimate,
                      point_cost_charged AS PointCostCharged,
                      point_status AS PointStatus,
                      retry_of_job_id AS RetryOfJobId,
                      input_json::text AS InputJson,
                      prompt_json::text AS PromptJson,
                      reference_json::text AS ReferenceJson,
                      output_json::text AS OutputJson,
                      error_code AS ErrorCode,
                      error_message AS ErrorMessage,
                      created_at AS CreatedAt,
                      updated_at AS UpdatedAt,
                      completed_at AS CompletedAt;
            """,
            new
            {
                jobId,
                tenantId = _tenant.TenantId,
                customerId = context.CustomerId,
                userId = context.UserId,
                serviceId = service.Id,
                logicalRequestId,
                jobType = RenderJobTypes.CoreService,
                operationType = service.ServiceType,
                sourceType = channel,
                priority = Math.Clamp(request.Priority, 1, 1000),
                inputJson,
                promptJson,
                referenceJson,
                optionsJson,
                pointCostEstimate = estimate.EstimatedPoints,
                pointStatus = initialPointStatus,
                retryOfJobId
            },
            tx,
            cancellationToken: ct));

        await AddEventAsync(conn, tx, row.Id, "CORE_JOB_CREATED", "Core service job created in billing stage.",
            new
            {
                service_code = service.ServiceCode,
                channel,
                logical_request_id = logicalRequestId,
                point_cost_estimate = estimate.EstimatedPoints,
                retry_of_job_id = retryOfJobId
            });
        tx.Commit();

        var reservation = await _billing.ReserveAsync(row.Id, context, estimate, ct);
        if (!reservation.Success)
        {
            throw new CoreInsufficientBalanceException(row.Id, reservation.ErrorMessage ?? "Core billing reservation failed.");
        }

        return await GetAsync(context, row.Id, ct)
            ?? throw new InvalidOperationException("Core job disappeared after billing reservation.");
    }

    public async Task<CoreJobView?> GetAsync(CoreRequestContext context, Guid jobId, CancellationToken ct = default)
    {
        CoreJobAccess.EnsureAuthenticated(context);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CoreJobRow>(new CommandDefinition(
            SelectSql +
            """
             WHERE r.id = @jobId
               AND r.tenant_id = @tenantId
               AND r.job_type = @jobType
               AND (
                    @trustedInternal = true
                    OR (r.customer_id = @customerId AND @customerId IS NOT NULL)
               )
             LIMIT 1;
            """,
            new
            {
                jobId,
                tenantId = _tenant.TenantId,
                jobType = RenderJobTypes.CoreService,
                trustedInternal = IsTrustedInternal(context),
                customerId = context.CustomerId
            },
            cancellationToken: ct));

        return row is null ? null : Map(row);
    }

    public async Task<CoreJobListResult> ListAsync(
        CoreRequestContext context,
        CoreJobListRequest request,
        CancellationToken ct = default)
    {
        CoreJobAccess.EnsureAuthenticated(context);
        await _tenant.EnsureLoadedAsync(ct);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;
        var where = new StringBuilder(
            """
             WHERE r.tenant_id=@tenantId
               AND r.job_type=@jobType
               AND (@trustedInternal=true OR (r.customer_id=@customerId AND @customerId IS NOT NULL))
            """);
        var parameters = new DynamicParameters(new
        {
            tenantId = _tenant.TenantId,
            jobType = RenderJobTypes.CoreService,
            trustedInternal = IsTrustedInternal(context),
            customerId = context.CustomerId,
            limit = pageSize,
            offset
        });

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = NormalizeStatusFilter(request.Status);
            where.AppendLine(" AND r.status=@status");
            parameters.Add("status", status);
        }

        if (!string.IsNullOrWhiteSpace(request.ServiceCode))
        {
            where.AppendLine(" AND upper(s.service_code)=upper(@serviceCode)");
            parameters.Add("serviceCode", request.ServiceCode.Trim());
        }

        using var conn = await _factory.OpenAsync(ct);
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM render.render_jobs r LEFT JOIN catalog.services s ON s.id=r.service_id" + where,
            parameters,
            cancellationToken: ct));
        var rows = await conn.QueryAsync<CoreJobRow>(new CommandDefinition(
            SelectSql + where + " ORDER BY r.created_at DESC, r.id DESC LIMIT @limit OFFSET @offset;",
            parameters,
            cancellationToken: ct));
        return new CoreJobListResult(rows.Select(Map).ToList(), page, pageSize, total);
    }

    public async Task<CoreJobView> CancelAsync(
        CoreRequestContext context,
        Guid jobId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var current = await GetAsync(context, jobId, ct)
            ?? throw new KeyNotFoundException("Core job was not found.");
        if (!CanCancelStatus(current.Status))
        {
            throw new InvalidOperationException($"Terminal job '{current.Status}' cannot be cancelled.");
        }

        var result = await _billing.RefundOrReleaseAsync(
            jobId,
            string.IsNullOrWhiteSpace(reason) ? "Core job cancelled by caller." : reason.Trim(),
            markCancelled: true,
            ct);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Core job cancellation failed.");
        }

        return await GetAsync(context, jobId, ct)
            ?? throw new InvalidOperationException("Core job disappeared after cancellation.");
    }

    public async Task<CoreJobView> RetryAsync(
        CoreRequestContext context,
        Guid jobId,
        CoreRetryJobRequest request,
        CancellationToken ct = default)
    {
        var source = await GetRowScopedAsync(context, jobId, ct)
            ?? throw new KeyNotFoundException("Core job was not found.");
        if (source.Status != RenderJobStatuses.Failed)
        {
            throw new InvalidOperationException("Only failed Core jobs can be retried.");
        }

        var release = await _billing.RefundOrReleaseAsync(
            source.Id,
            "Release or refund source job before retry.",
            markCancelled: false,
            ct);
        if (!release.Success)
        {
            throw new InvalidOperationException(release.ErrorMessage ?? "Source job billing could not be released.");
        }

        var envelope = DeserializeEnvelope(source.InputJson);
        var retryContext = context with { Channel = source.SourceType ?? context.NormalizedChannel };
        var retryKey = NormalizeIdempotencyKey(request.IdempotencyKey ?? context.ExternalRequestId);
        if (RequiresIdempotencyKey(retryContext.NormalizedChannel) && retryKey is null)
        {
            throw new ArgumentException("IdempotencyKey is required when retrying an external Core job.", nameof(request));
        }

        retryKey ??= $"retry-{source.Id:N}-{Guid.NewGuid():N}";
        return await CreateInternalAsync(
            retryContext,
            new CoreCreateJobRequest
            {
                ServiceCode = source.ServiceCode ?? envelope.ServiceCode,
                Input = envelope.Payload.Clone(),
                Prompt = envelope.Prompt?.Clone(),
                References = envelope.References?.Clone(),
                Priority = source.Priority,
                IdempotencyKey = $"retry:{source.Id:N}:{retryKey}"
            },
            source.Id,
            ct);
    }

    private async Task<CoreJobRow?> GetRowScopedAsync(
        CoreRequestContext context,
        Guid jobId,
        CancellationToken ct)
    {
        CoreJobAccess.EnsureAuthenticated(context);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<CoreJobRow>(new CommandDefinition(
            SelectSql +
            """
             WHERE r.id=@jobId
               AND r.tenant_id=@tenantId
               AND r.job_type=@jobType
               AND (@trustedInternal=true OR (r.customer_id=@customerId AND @customerId IS NOT NULL))
             LIMIT 1;
            """,
            new
            {
                jobId,
                tenantId = _tenant.TenantId,
                jobType = RenderJobTypes.CoreService,
                trustedInternal = IsTrustedInternal(context),
                customerId = context.CustomerId
            },
            cancellationToken: ct));
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
                (@jobId, @tenantId, @eventType, 'info', @message, CAST(@data AS jsonb), now());
            """,
            new
            {
                jobId,
                tenantId = _tenant.TenantId,
                eventType,
                message,
                data = JsonSerializer.Serialize(data, JsonOptions)
            },
            tx);

    private static CoreServiceJobEnvelope DeserializeEnvelope(string inputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<CoreServiceJobEnvelope>(inputJson, JsonOptions)
                ?? throw new InvalidOperationException("Core job input envelope is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Core job input envelope is invalid.", ex);
        }
    }

    private static CoreJobView Map(CoreJobRow row)
    {
        JsonElement output;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(row.OutputJson) ? "[]" : row.OutputJson);
            output = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            output = JsonSerializer.SerializeToElement(Array.Empty<object>(), JsonOptions);
        }

        return new CoreJobView(
            row.Id,
            row.ServiceId,
            row.ServiceCode ?? string.Empty,
            row.CustomerId,
            row.UserId,
            row.Status,
            row.SourceType ?? CoreChannelCodes.System,
            row.OperationType,
            row.LogicalRequestId,
            row.CurrentStep,
            row.ProgressPercent,
            row.PointCostEstimate,
            row.PointCostCharged,
            row.PointStatus,
            row.RetryOfJobId,
            output,
            row.ErrorCode,
            row.ErrorMessage,
            row.CreatedAt,
            row.UpdatedAt,
            row.CompletedAt);
    }

    internal static bool RequiresIdempotencyKey(string channel)
        => channel is CoreChannelCodes.Zalo
            or CoreChannelCodes.Telegram
            or CoreChannelCodes.Partner
            or CoreChannelCodes.Api;

    internal static bool CanCancelStatus(string status)
        => status is not (RenderJobStatuses.Completed or RenderJobStatuses.Failed or RenderJobStatuses.Cancelled);

    internal static string BuildIdempotencyLockName(
        CoreRequestContext context,
        string serviceCode,
        string idempotencyKey)
        => $"core-service-job:{BuildLogicalRequestId(context, serviceCode, idempotencyKey)}";

    internal static string BuildLogicalRequestId(
        CoreRequestContext context,
        string serviceCode,
        string idempotencyKey)
    {
        var scope = string.Join(':',
            context.NormalizedChannel,
            context.CustomerId?.ToString("N") ?? "no-customer",
            NormalizeOptional(context.ClientId) ?? "no-client",
            serviceCode.Trim().ToUpperInvariant(),
            idempotencyKey);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));
        return $"core:{context.NormalizedChannel}:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string NormalizeStatusFilter(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return normalized switch
        {
            RenderJobStatuses.Draft
                or RenderJobStatuses.Queued
                or RenderJobStatuses.Preparing
                or RenderJobStatuses.Rendering
                or RenderJobStatuses.PostProcessing
                or RenderJobStatuses.PendingReconciliation
                or RenderJobStatuses.Completed
                or RenderJobStatuses.Failed
                or RenderJobStatuses.Cancelled => normalized,
            _ => throw new ArgumentOutOfRangeException(nameof(status), $"Unsupported Core job status '{status}'.")
        };
    }

    private static bool IsTrustedInternal(CoreRequestContext context)
        => context.IsTrustedInternal && context.NormalizedChannel == CoreChannelCodes.System;

    private static string? NormalizeIdempotencyKey(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Idempotency key must be 200 characters or fewer.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string SelectSql =
        """
        SELECT r.id AS Id,
               r.service_id AS ServiceId,
               s.service_code AS ServiceCode,
               r.customer_id AS CustomerId,
               r.user_id AS UserId,
               r.status AS Status,
               r.priority AS Priority,
               r.source_type AS SourceType,
               r.operation_type AS OperationType,
               r.logical_request_id AS LogicalRequestId,
               r.current_step AS CurrentStep,
               r.progress_percent AS ProgressPercent,
               r.point_cost_estimate AS PointCostEstimate,
               r.point_cost_charged AS PointCostCharged,
               r.point_status AS PointStatus,
               r.retry_of_job_id AS RetryOfJobId,
               r.input_json::text AS InputJson,
               r.prompt_json::text AS PromptJson,
               r.reference_json::text AS ReferenceJson,
               r.output_json::text AS OutputJson,
               r.error_code AS ErrorCode,
               r.error_message AS ErrorMessage,
               r.created_at AS CreatedAt,
               r.updated_at AS UpdatedAt,
               r.completed_at AS CompletedAt
          FROM render.render_jobs r
          LEFT JOIN catalog.services s ON s.id = r.service_id
        """;

    private sealed class CoreJobRow
    {
        public Guid Id { get; init; }
        public Guid? ServiceId { get; init; }
        public string? ServiceCode { get; init; }
        public Guid? CustomerId { get; init; }
        public Guid? UserId { get; init; }
        public string Status { get; init; } = RenderJobStatuses.Queued;
        public int Priority { get; init; }
        public string? SourceType { get; init; }
        public string? OperationType { get; init; }
        public string? LogicalRequestId { get; init; }
        public string? CurrentStep { get; init; }
        public int ProgressPercent { get; init; }
        public decimal PointCostEstimate { get; init; }
        public decimal PointCostCharged { get; init; }
        public string PointStatus { get; init; } = RenderPointStatuses.NotRequired;
        public Guid? RetryOfJobId { get; init; }
        public string InputJson { get; init; } = "{}";
        public string PromptJson { get; init; } = "{}";
        public string ReferenceJson { get; init; } = "[]";
        public string OutputJson { get; init; } = "[]";
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
    }
}
