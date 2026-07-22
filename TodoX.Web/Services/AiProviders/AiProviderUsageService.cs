using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.AiProviders;

public interface IAiProviderUsageService
{
    Task<Guid> RecordAsync(AiProviderUsageLog log, CancellationToken ct = default);
}

public interface IAiProviderUsageRepository
{
    Task<Guid> UpsertAsync(AiProviderUsageLog log, CancellationToken ct = default);
}

public sealed class AiProviderUsageService : IAiProviderUsageService
{
    private static readonly HashSet<string> AllowedUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "credits", "tokens", "token_1000", "request", "requests", "image", "images",
        "second", "seconds", "video_second", "video_seconds", "minute", "minutes", "fixed", "usd"
    };

    private readonly IAiProviderUsageRepository _repository;

    public AiProviderUsageService(IAiProviderUsageRepository repository)
    {
        _repository = repository;
    }

    public Task<Guid> RecordAsync(AiProviderUsageLog log, CancellationToken ct = default)
    {
        log.UnitType = NormalizeUnit(log.UnitType);
        log.Status = NormalizeStatus(log.Status);
        log.AttemptNo = Math.Max(1, log.AttemptNo);
        log.RenderJobId ??= TryParseGuid(log.JobId);
        log.IdempotencyKey = BuildIdempotencyKey(log);
        log.ProviderUsageJson = NullIfBlank(log.ProviderUsageJson);
        log.MetadataJson = NullIfBlank(log.MetadataJson);
        log.RequestJson = NullIfBlank(log.RequestJson);
        log.ResponseJson = NullIfBlank(log.ResponseJson);
        return _repository.UpsertAsync(log, ct);
    }

    private static string NormalizeUnit(string? unit)
    {
        var value = string.IsNullOrWhiteSpace(unit) ? "request" : unit.Trim();
        return AllowedUnits.Contains(value) ? value : "request";
    }

    private static string NormalizeStatus(string? status)
    {
        var value = string.IsNullOrWhiteSpace(status) ? "pending" : status.Trim();
        return value.Equals("completed", StringComparison.OrdinalIgnoreCase) ? "success" : value.ToLowerInvariant();
    }

    private static string BuildIdempotencyKey(AiProviderUsageLog log)
    {
        if (!string.IsNullOrWhiteSpace(log.IdempotencyKey))
        {
            return log.IdempotencyKey.Trim();
        }

        var logical = FirstNonBlank(log.RequestId, log.RenderJobId?.ToString("N"), log.JobId, Guid.NewGuid().ToString("N"));
        var task = FirstNonBlank(log.ProviderTaskId, "no-provider-task");
        var provider = FirstNonBlank(log.ProviderCode, log.ProviderId?.ToString(), "provider");
        return $"{logical}:{provider}:{task}:{log.AttemptNo}:{log.Status}".ToLowerInvariant();
    }

    private static Guid? TryParseGuid(string? value)
        => Guid.TryParse(value, out var id) ? id : null;

    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class AiProviderUsageRepository : IAiProviderUsageRepository
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AiProviderUsageRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<Guid> UpsertAsync(AiProviderUsageLog log, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO public.todox_ai_provider_usage_log
                (tenant_id, customer_id, user_id, render_job_id,
                 provider_id, provider_capability_id, provider_account_id,
                 provider_code, capability_code, feature_code, operation_type, model_name,
                 provider_task_id, attempt_no, logical_request_id,
                 quantity, unit_type, unit_cost_points, total_points,
                 provider_raw_cost, provider_cost_currency, usage_source, provider_usage_json,
                 status, error_code, error_message, metadata_json, request_json, response_json,
                 idempotency_key, finalized_at, created_by, created_at)
            VALUES
                (@tenant, @CustomerGuid, @UserId, @RenderJobId,
                 @ProviderId, @ProviderCapabilityId, @ProviderAccountId,
                 @ProviderCode, @CapabilityCode, @FeatureCode, @OperationType, @ModelName,
                 @ProviderTaskId, @AttemptNo, @RequestId,
                 @Quantity, @UnitType, COALESCE(@UnitCostPoints, 0), COALESCE(@TotalPoints, 0),
                 @ProviderRawCost, @ProviderCostCurrency, @UsageSource, COALESCE(CAST(@ProviderUsageJson AS jsonb), '{}'::jsonb),
                 @Status, @ErrorCode, @ErrorMessage, COALESCE(CAST(@MetadataJson AS jsonb), '{}'::jsonb),
                 CAST(@RequestJson AS jsonb), CAST(@ResponseJson AS jsonb),
                 @IdempotencyKey, CASE WHEN @Status IN ('success','failed','cancelled','refunded') THEN now() ELSE NULL END,
                 @CreatedBy, now())
            ON CONFLICT (idempotency_key) DO UPDATE
                SET quantity = EXCLUDED.quantity,
                    unit_type = EXCLUDED.unit_type,
                    unit_cost_points = EXCLUDED.unit_cost_points,
                    total_points = EXCLUDED.total_points,
                    provider_raw_cost = COALESCE(EXCLUDED.provider_raw_cost, public.todox_ai_provider_usage_log.provider_raw_cost),
                    provider_cost_currency = COALESCE(EXCLUDED.provider_cost_currency, public.todox_ai_provider_usage_log.provider_cost_currency),
                    provider_usage_json = CASE
                        WHEN EXCLUDED.provider_usage_json = '{}'::jsonb THEN public.todox_ai_provider_usage_log.provider_usage_json
                        ELSE EXCLUDED.provider_usage_json
                    END,
                    status = EXCLUDED.status,
                    error_code = COALESCE(EXCLUDED.error_code, public.todox_ai_provider_usage_log.error_code),
                    error_message = COALESCE(EXCLUDED.error_message, public.todox_ai_provider_usage_log.error_message),
                    response_json = COALESCE(EXCLUDED.response_json, public.todox_ai_provider_usage_log.response_json),
                    finalized_at = COALESCE(EXCLUDED.finalized_at, public.todox_ai_provider_usage_log.finalized_at)
            RETURNING id;
            """,
            new
            {
                tenant = _tenant.TenantId,
                log.CustomerGuid,
                log.UserId,
                log.RenderJobId,
                log.ProviderId,
                log.ProviderCapabilityId,
                log.ProviderAccountId,
                log.ProviderCode,
                log.CapabilityCode,
                log.FeatureCode,
                log.OperationType,
                log.ModelName,
                log.ProviderTaskId,
                log.AttemptNo,
                log.RequestId,
                log.Quantity,
                log.UnitType,
                log.UnitCostPoints,
                log.TotalPoints,
                log.ProviderRawCost,
                log.ProviderCostCurrency,
                log.UsageSource,
                log.ProviderUsageJson,
                log.Status,
                log.ErrorCode,
                log.ErrorMessage,
                log.MetadataJson,
                log.RequestJson,
                log.ResponseJson,
                log.IdempotencyKey,
                log.CreatedBy
            });
    }
}
