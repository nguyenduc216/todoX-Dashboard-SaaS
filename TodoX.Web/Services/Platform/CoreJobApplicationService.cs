using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Platform;

public interface ICoreJobApplicationService
{
    Task<CoreJobView> CreateAsync(CoreRequestContext context, CoreCreateJobRequest request, CancellationToken ct = default);
    Task<CoreJobView?> GetAsync(CoreRequestContext context, Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Canonical application boundary for creating TodoX jobs from Dashboard, Zalo, Telegram,
/// partner APIs and future clients. It owns transport-neutral validation and idempotency, while
/// execution remains delegated to CoreServiceJobHandler -> ICoreJobExecutionAdapter.
/// </summary>
public sealed class CoreJobApplicationService : ICoreJobApplicationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ICoreServiceCatalogService _catalog;

    public CoreJobApplicationService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ICoreServiceCatalogService catalog)
    {
        _factory = factory;
        _tenant = tenant;
        _catalog = catalog;
    }

    public async Task<CoreJobView> CreateAsync(
        CoreRequestContext context,
        CoreCreateJobRequest request,
        CancellationToken ct = default)
    {
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

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();

        if (idempotencyKey is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
                new { lockName = BuildIdempotencyLockName(context, service.ServiceCode, idempotencyKey) },
                tx,
                cancellationToken: ct));

            var existing = await conn.QuerySingleOrDefaultAsync<CoreJobRow>(new CommandDefinition(
                SelectSql +
                """
                 WHERE r.job_type = @jobType
                   AND r.service_id = @serviceId
                   AND r.source_type = @sourceType
                   AND r.logical_request_id = @logicalRequestId
                   AND r.customer_id IS NOT DISTINCT FROM @customerId
                 ORDER BY r.created_at DESC
                 LIMIT 1;
                """,
                new
                {
                    jobType = RenderJobTypes.CoreService,
                    serviceId = service.Id,
                    sourceType = channel,
                    logicalRequestId = idempotencyKey,
                    customerId = context.CustomerId
                },
                tx,
                cancellationToken: ct));

            if (existing is not null)
            {
                tx.Commit();
                return Map(existing);
            }
        }

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
                service_code = service.ServiceCode
            }
        }, JsonOptions);

        var row = await conn.QuerySingleAsync<CoreJobRow>(new CommandDefinition(
            """
            INSERT INTO render.render_jobs
                (tenant_id, customer_id, user_id, service_id,
                 logical_request_id, job_type, operation_type, source_type,
                 status, progress_percent, priority,
                 input_json, prompt_json, reference_json, output_json, options,
                 point_cost_estimate, point_cost_charged, point_status,
                 max_attempts, queued_at, created_at)
            VALUES
                (@tenantId, @customerId, @userId, @serviceId,
                 @logicalRequestId, @jobType, @operationType, @sourceType,
                 'queued', 0, @priority,
                 CAST(@inputJson AS jsonb), CAST(@promptJson AS jsonb), CAST(@referenceJson AS jsonb), '[]'::jsonb, CAST(@optionsJson AS jsonb),
                 0, 0, 'not_required',
                 1, now(), now())
            RETURNING id AS Id,
                      service_id AS ServiceId,
                      status AS Status,
                      source_type AS SourceType,
                      progress_percent AS ProgressPercent,
                      point_cost_estimate AS PointCostEstimate,
                      point_cost_charged AS PointCostCharged,
                      point_status AS PointStatus,
                      output_json::text AS OutputJson,
                      error_code AS ErrorCode,
                      error_message AS ErrorMessage,
                      created_at AS CreatedAt,
                      updated_at AS UpdatedAt,
                      completed_at AS CompletedAt;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                customerId = context.CustomerId,
                userId = context.UserId,
                serviceId = service.Id,
                logicalRequestId = idempotencyKey,
                jobType = RenderJobTypes.CoreService,
                operationType = service.ServiceType,
                sourceType = channel,
                priority = Math.Clamp(request.Priority, 1, 1000),
                inputJson,
                promptJson,
                referenceJson,
                optionsJson
            },
            tx,
            cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO render.render_job_events
                (job_id, tenant_id, event_type, level, message, data_json, created_at)
            VALUES
                (@jobId, @tenantId, 'JOB_QUEUED', 'info', 'Core service job queued.',
                 CAST(@eventData AS jsonb), now());
            """,
            new
            {
                jobId = row.Id,
                tenantId = _tenant.TenantId,
                eventData = JsonSerializer.Serialize(new
                {
                    service_code = service.ServiceCode,
                    channel,
                    idempotency_key = idempotencyKey
                }, JsonOptions)
            },
            tx,
            cancellationToken: ct));

        tx.Commit();
        row.ServiceCode = service.ServiceCode;
        return Map(row);
    }

    public async Task<CoreJobView?> GetAsync(CoreRequestContext context, Guid jobId, CancellationToken ct = default)
    {
        var channel = context.NormalizedChannel;
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<CoreJobRow>(new CommandDefinition(
            SelectSql +
            """
             WHERE r.id = @jobId
               AND r.job_type = @jobType
               AND (
                    @isSystem = true
                    OR (r.customer_id IS NOT DISTINCT FROM @customerId AND @customerId IS NOT NULL)
                    OR (r.user_id IS NOT DISTINCT FROM @userId AND @userId IS NOT NULL)
               )
             LIMIT 1;
            """,
            new
            {
                jobId,
                jobType = RenderJobTypes.CoreService,
                isSystem = channel == CoreChannelCodes.System,
                customerId = context.CustomerId,
                userId = context.UserId
            },
            cancellationToken: ct));

        return row is null ? null : Map(row);
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
            row.Status,
            row.SourceType ?? CoreChannelCodes.System,
            row.ProgressPercent,
            row.PointCostEstimate,
            row.PointCostCharged,
            row.PointStatus,
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

    internal static string BuildIdempotencyLockName(
        CoreRequestContext context,
        string serviceCode,
        string idempotencyKey)
        => string.Join(':',
            "core-service-job",
            context.NormalizedChannel,
            context.CustomerId?.ToString("N") ?? "no-customer",
            NormalizeOptional(context.ClientId) ?? "no-client",
            serviceCode.Trim().ToUpperInvariant(),
            idempotencyKey);

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
               r.status AS Status,
               r.source_type AS SourceType,
               r.progress_percent AS ProgressPercent,
               r.point_cost_estimate AS PointCostEstimate,
               r.point_cost_charged AS PointCostCharged,
               r.point_status AS PointStatus,
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
        public Guid Id { get; set; }
        public Guid? ServiceId { get; set; }
        public string? ServiceCode { get; set; }
        public string Status { get; set; } = RenderJobStatuses.Queued;
        public string? SourceType { get; set; }
        public int ProgressPercent { get; set; }
        public decimal PointCostEstimate { get; set; }
        public decimal PointCostCharged { get; set; }
        public string PointStatus { get; set; } = RenderPointStatuses.NotRequired;
        public string OutputJson { get; set; } = "[]";
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
