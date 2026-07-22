using Dapper;
using TodoX.Web.Data;

namespace TodoX.Web.Services.AiProviders;

public sealed class AiOperationLogFilter
{
    public string? Search { get; set; }
    public Guid? RenderJobId { get; set; }
    public Guid? BusinessEntityId { get; set; }
    public string? ProviderTaskId { get; set; }
    public string? LogicalRequestId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? UserId { get; set; }
    public string? ProviderCode { get; set; }
    public Guid? ProviderAccountId { get; set; }
    public string? CapabilityCode { get; set; }
    public string? ModelName { get; set; }
    public string? JobType { get; set; }
    public string? RenderStatus { get; set; }
    public string? BillingStatus { get; set; }
    public string? RefundStatus { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class AiOperationLogItem
{
    public Guid RenderJobId { get; init; }
    public string? LogCode { get; init; }
    public string JobType { get; init; } = string.Empty;
    public string RenderStatus { get; init; } = string.Empty;
    public string? ProviderCode { get; init; }
    public Guid? ProviderAccountId { get; init; }
    public string? ProviderTaskId { get; init; }
    public string? CapabilityCode { get; init; }
    public string? ModelName { get; init; }
    public decimal? UsageQuantity { get; init; }
    public string? UsageUnit { get; init; }
    public decimal? ProviderActualCost { get; init; }
    public string? ProviderCurrency { get; init; }
    public decimal? ChargedPoints { get; init; }
    public decimal? RefundedPoints { get; init; }
    public string? BillingStatus { get; init; }
    public string? RefundStatus { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed class AiOperationLogDetail
{
    public AiOperationLogItem Overview { get; init; } = new();
    public IReadOnlyList<dynamic> Steps { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> Timeline { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> Inputs { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> Artifacts { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> Attempts { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> Usage { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> Billing { get; init; } = Array.Empty<dynamic>();
    public IReadOnlyList<dynamic> WalletTransactions { get; init; } = Array.Empty<dynamic>();
}

public interface IAiOperationLogService
{
    Task<(IReadOnlyList<AiOperationLogItem> Items, int Total)> SearchAsync(AiOperationLogFilter filter, CancellationToken ct = default);
    Task<AiOperationLogDetail?> GetDetailAsync(Guid renderJobId, CancellationToken ct = default);
}

public sealed class AiOperationLogService : IAiOperationLogService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AiOperationLogService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<(IReadOnlyList<AiOperationLogItem> Items, int Total)> SearchAsync(AiOperationLogFilter filter, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var (where, args) = BuildWhere(filter);
        var total = await conn.ExecuteScalarAsync<int>($"SELECT count(*) FROM render.render_jobs j {where};", args);
        args.Add("limit", Math.Clamp(filter.PageSize, 1, 100));
        args.Add("offset", Math.Max(0, filter.Page - 1) * Math.Clamp(filter.PageSize, 1, 100));

        var rows = await conn.QueryAsync<AiOperationLogItem>(
            $"""
            SELECT j.id AS RenderJobId,
                   j.log_code AS LogCode,
                   j.job_type AS JobType,
                   j.status AS RenderStatus,
                   COALESCE(u.provider_code, j.provider_code) AS ProviderCode,
                   COALESCE(u.provider_account_id, j.provider_account_id) AS ProviderAccountId,
                   COALESCE(u.provider_task_id, j.provider_task_id) AS ProviderTaskId,
                   COALESCE(u.capability_code, b.capability_code) AS CapabilityCode,
                   COALESCE(u.model_name, j.model_code, b.actual_model, b.requested_model) AS ModelName,
                   u.quantity AS UsageQuantity,
                   u.unit_type AS UsageUnit,
                   COALESCE(b.provider_actual_cost, b.provider_actual_cost_usd, u.provider_raw_cost) AS ProviderActualCost,
                   COALESCE(b.provider_currency, u.provider_cost_currency) AS ProviderCurrency,
                   COALESCE(b.charged_points, b.customer_charged_points + b.system_charged_points) AS ChargedPoints,
                   b.refunded_points AS RefundedPoints,
                   COALESCE(b.billing_status, b.status) AS BillingStatus,
                   b.refund_status AS RefundStatus,
                   COALESCE(j.error_code, b.error_code, u.error_code) AS ErrorCode,
                   COALESCE(j.error_message, b.error_message, u.error_message) AS ErrorMessage,
                   j.created_at AS CreatedAt,
                   j.completed_at AS CompletedAt
              FROM render.render_jobs j
              LEFT JOIN LATERAL (
                    SELECT *
                      FROM public.todox_ai_provider_usage_log u
                     WHERE u.render_job_id = j.id
                     ORDER BY u.created_at DESC
                     LIMIT 1
              ) u ON true
              LEFT JOIN LATERAL (
                    SELECT *
                      FROM billing.ai_billing_records b
                     WHERE b.render_job_id = j.id
                     ORDER BY b.created_at DESC
                     LIMIT 1
              ) b ON true
              {where}
             ORDER BY j.created_at DESC
             LIMIT @limit OFFSET @offset;
            """,
            args);
        return (rows.ToList(), total);
    }

    public async Task<AiOperationLogDetail?> GetDetailAsync(Guid renderJobId, CancellationToken ct = default)
    {
        var (items, _) = await SearchAsync(new AiOperationLogFilter { RenderJobId = renderJobId, PageSize = 1 }, ct);
        var overview = items.FirstOrDefault();
        if (overview is null) return null;

        using var conn = await _factory.OpenAsync(ct);
        var steps = (await conn.QueryAsync("SELECT * FROM render.render_job_steps WHERE render_job_id=@renderJobId ORDER BY step_order, attempt;", new { renderJobId })).ToList();
        var timeline = (await conn.QueryAsync("SELECT * FROM render.render_job_events WHERE job_id=@renderJobId ORDER BY created_at, id;", new { renderJobId })).ToList();
        var inputs = (await conn.QueryAsync("SELECT * FROM render.render_job_inputs WHERE render_job_id=@renderJobId ORDER BY created_at;", new { renderJobId })).ToList();
        var artifacts = (await conn.QueryAsync("SELECT * FROM render.render_artifacts WHERE render_job_id=@renderJobId ORDER BY created_at;", new { renderJobId })).ToList();
        var attempts = (await conn.QueryAsync("SELECT * FROM billing.ai_provider_attempts WHERE render_job_id=@renderJobId ORDER BY attempt_no, attempt_number, created_at;", new { renderJobId })).ToList();
        var usage = (await conn.QueryAsync("SELECT * FROM public.todox_ai_provider_usage_log WHERE render_job_id=@renderJobId ORDER BY created_at;", new { renderJobId })).ToList();
        var billing = (await conn.QueryAsync("SELECT * FROM billing.ai_billing_records WHERE render_job_id=@renderJobId ORDER BY created_at;", new { renderJobId })).ToList();
        var walletTransactions = (await conn.QueryAsync(
            """
            SELECT t.*
              FROM billing.token_transactions t
              JOIN billing.ai_billing_records b
                ON b.reservation_transaction_id=t.id
                OR b.charge_transaction_id=t.id
                OR b.refund_transaction_id=t.id
                OR b.wallet_transaction_id=t.id
             WHERE b.render_job_id=@renderJobId
             ORDER BY t.created_at;
            """,
            new { renderJobId })).ToList();

        return new AiOperationLogDetail
        {
            Overview = overview,
            Steps = steps,
            Timeline = timeline,
            Inputs = inputs,
            Artifacts = artifacts,
            Attempts = attempts,
            Usage = usage,
            Billing = billing,
            WalletTransactions = walletTransactions
        };
    }

    private static (string Where, DynamicParameters Args) BuildWhere(AiOperationLogFilter filter)
    {
        var where = new List<string> { "WHERE 1=1" };
        var args = new DynamicParameters();
        void Add(string sql, string name, object? value)
        {
            where.Add(sql);
            args.Add(name, value);
        }

        if (filter.RenderJobId is Guid renderJobId) Add("AND j.id=@renderJobId", "renderJobId", renderJobId);
        if (filter.CustomerId is Guid customerId) Add("AND j.customer_id=@customerId", "customerId", customerId);
        if (filter.UserId is Guid userId) Add("AND j.user_id=@userId", "userId", userId);
        if (!string.IsNullOrWhiteSpace(filter.JobType)) Add("AND j.job_type=@jobType", "jobType", filter.JobType.Trim());
        if (!string.IsNullOrWhiteSpace(filter.RenderStatus)) Add("AND j.status=@renderStatus", "renderStatus", filter.RenderStatus.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Search)) Add("AND (j.id::text ILIKE @search OR COALESCE(j.log_code,'') ILIKE @search OR COALESCE(j.provider_task_id,'') ILIKE @search)", "search", $"%{filter.Search.Trim()}%");
        if (filter.FromUtc is DateTimeOffset from) Add("AND j.created_at >= @fromUtc", "fromUtc", from.UtcDateTime);
        if (filter.ToUtc is DateTimeOffset to) Add("AND j.created_at < @toUtc", "toUtc", to.UtcDateTime);

        return (string.Join('\n', where), args);
    }
}
