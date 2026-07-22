using System.Text.Json;
using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services.AiProviders.Kie;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellProviderCatalog
{
    Task<IReadOnlyList<DanceSellProviderRouteDto>> GetRoutesAsync(string operationType, bool userSelectableOnly = false, CancellationToken ct = default);
    Task<DanceSellProviderRouteDto> GetDefaultRouteAsync(string operationType, CancellationToken ct = default);
    Task<DanceSellProviderRouteDto> ResolveAsync(string operationType, string? providerCode, string? modelName, CancellationToken ct = default);
}

public sealed class DanceSellProviderCatalog : IDanceSellProviderCatalog
{
    private readonly TodoXConnectionFactory _factory;

    public DanceSellProviderCatalog(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<DanceSellProviderRouteDto>> GetRoutesAsync(string operationType, bool userSelectableOnly = false, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var rows = await conn.QueryAsync<DanceSellProviderRouteDto>(
                """
                SELECT id AS Id, feature_code AS FeatureCode, operation_type AS OperationType,
                       provider_code AS ProviderCode, provider_capability_id AS ProviderCapabilityId,
                       provider_account_id AS ProviderAccountId, model_name AS ModelName, priority AS Priority,
                       is_default AS IsDefault, enabled AS Enabled, allow_user_select AS AllowUserSelect,
                       config_json::text AS ConfigJson
                  FROM public.todox_ai_feature_provider_route
                 WHERE feature_code = @featureCode
                   AND operation_type = @operationType
                   AND enabled = true
                   AND (@userSelectableOnly = false OR allow_user_select = true)
                 ORDER BY is_default DESC, priority, provider_code, model_name;
                """,
                new { featureCode = DanceSellConstants.FeatureCode, operationType, userSelectableOnly });
            var list = rows.ToList();
            return list.Count == 0 ? new[] { Fallback(operationType) } : list;
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            return new[] { Fallback(operationType) };
        }
    }

    public async Task<DanceSellProviderRouteDto> GetDefaultRouteAsync(string operationType, CancellationToken ct = default)
    {
        var routes = await GetRoutesAsync(operationType, userSelectableOnly: false, ct);
        return routes.FirstOrDefault(x => x.IsDefault) ?? routes.OrderBy(x => x.Priority).First();
    }

    public async Task<DanceSellProviderRouteDto> ResolveAsync(string operationType, string? providerCode, string? modelName, CancellationToken ct = default)
    {
        var routes = await GetRoutesAsync(operationType, userSelectableOnly: false, ct);
        if (string.IsNullOrWhiteSpace(providerCode) && string.IsNullOrWhiteSpace(modelName))
        {
            return routes.FirstOrDefault(x => x.IsDefault) ?? routes.OrderBy(x => x.Priority).First();
        }

        var route = routes.FirstOrDefault(x =>
            (string.IsNullOrWhiteSpace(providerCode) || x.ProviderCode.Equals(providerCode.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(modelName) || x.ModelName.Equals(modelName.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (route is null)
        {
            throw new InvalidOperationException("DANCE_SELL_PROVIDER_ROUTE_INVALID");
        }

        return route;
    }

    private static DanceSellProviderRouteDto Fallback(string operationType)
        => new()
        {
            Id = Guid.Empty,
            FeatureCode = DanceSellConstants.FeatureCode,
            OperationType = operationType,
            ProviderCode = DanceSellConstants.ProviderCode,
            ModelName = operationType == DanceSellOperationTypes.ReferenceImage
                ? DanceSellConstants.ReferenceModel
                : DanceSellConstants.Model,
            Priority = 100,
            Enabled = true,
            IsDefault = true,
            AllowUserSelect = true,
            ConfigJson = JsonSerializer.Serialize(new
            {
                source = "code_fallback_until_manual_sql_seeded",
                requiresManualSql = "database/manual/ai-operation-logs"
            }, KieJson.Options)
        };

    private static bool IsSchemaMissing(PostgresException ex)
        => ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn;
}

public interface IDanceSellOperationRepository
{
    Task<DanceSellProviderOperationDto?> UpsertOperationAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default);
    Task MarkSubmittedAsync(Guid operationId, string providerTaskId, string responseJson, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid operationId, string providerStatus, string responseJson, decimal? creditsConsumed, string? resultUrl, CancellationToken ct = default);
    Task MarkFailedAsync(Guid operationId, string providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default);
    Task UpsertAssetAsync(AiOperationAssetDto asset, CancellationToken ct = default);
    Task<PagedResult<DanceSellOperationLogItemDto>> SearchLogsAsync(DanceSellOperationLogFilter filter, CancellationToken ct = default);
    Task<DanceSellOperationLogDetailDto?> GetLogDetailAsync(Guid id, CancellationToken ct = default);
}

public sealed class DanceSellOperationRepository : IDanceSellOperationRepository
{
    private readonly TodoXConnectionFactory _factory;

    public DanceSellOperationRepository(TodoXConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<DanceSellProviderOperationDto?> UpsertOperationAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
    {
        operation.Id = operation.Id == Guid.Empty ? Guid.NewGuid() : operation.Id;
        operation.RequestJson = KieJsonRedactor.Redact(operation.RequestJson) ?? "{}";
        operation.ResponseJson = KieJsonRedactor.Redact(operation.ResponseJson);
        operation.CallbackJson = KieJsonRedactor.Redact(operation.CallbackJson);
        operation.ErrorJson = KieJsonRedactor.Redact(operation.ErrorJson);

        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.QuerySingleAsync<DanceSellProviderOperationDto>(
                """
                INSERT INTO dance_sell.dance_sell_provider_operations
                    (id, dance_sell_job_id, render_job_id, parent_operation_id, operation_type, attempt_no,
                     reference_mode, provider_code, provider_capability_id, provider_account_id, provider_model,
                     provider_task_id, status, provider_status, billing_status, refund_status, request_json,
                     response_json, callback_json, error_json, provider_usage_json, pricing_snapshot_json,
                     usage_quantity, usage_unit, credits_estimated, credits_consumed, provider_cost,
                     provider_currency, provider_cost_vnd, exchange_rate, todox_points_estimated,
                     todox_points_reserved, todox_points_charged, todox_points_refunded, balance_before,
                     balance_after, cost_source, error_code, error_message, created_at, started_at, submitted_at,
                     completed_at, failed_at, refunded_at, updated_at)
                VALUES
                    (@Id, @DanceSellJobId, @RenderJobId, @ParentOperationId, @OperationType, @AttemptNo,
                     @ReferenceMode, @ProviderCode, @ProviderCapabilityId, @ProviderAccountId, @ProviderModel,
                     @ProviderTaskId, @Status, @ProviderStatus, @BillingStatus, @RefundStatus, CAST(@RequestJson AS jsonb),
                     CAST(@ResponseJson AS jsonb), CAST(@CallbackJson AS jsonb), CAST(@ErrorJson AS jsonb),
                     CAST(@ProviderUsageJson AS jsonb), CAST(@PricingSnapshotJson AS jsonb), @UsageQuantity,
                     @UsageUnit, @CreditsEstimated, @CreditsConsumed, @ProviderCost, @ProviderCurrency,
                     @ProviderCostVnd, @ExchangeRate, @TodoxPointsEstimated, @TodoxPointsReserved,
                     @TodoxPointsCharged, @TodoxPointsRefunded, @BalanceBefore, @BalanceAfter,
                     @CostSource, @ErrorCode, @ErrorMessage, COALESCE(@CreatedAt, now()), @StartedAt,
                     @SubmittedAt, @CompletedAt, @FailedAt, @RefundedAt, now())
                ON CONFLICT (dance_sell_job_id, operation_type, attempt_no)
                DO UPDATE SET updated_at = now()
                RETURNING id AS Id, dance_sell_job_id AS DanceSellJobId, render_job_id AS RenderJobId,
                          parent_operation_id AS ParentOperationId, operation_type AS OperationType, attempt_no AS AttemptNo,
                          reference_mode AS ReferenceMode, provider_code AS ProviderCode,
                          provider_capability_id AS ProviderCapabilityId, provider_account_id AS ProviderAccountId,
                          provider_model AS ProviderModel, provider_task_id AS ProviderTaskId, status AS Status,
                          provider_status AS ProviderStatus, billing_status AS BillingStatus, refund_status AS RefundStatus,
                          request_json::text AS RequestJson, response_json::text AS ResponseJson,
                          callback_json::text AS CallbackJson, error_json::text AS ErrorJson,
                          provider_usage_json::text AS ProviderUsageJson, pricing_snapshot_json::text AS PricingSnapshotJson,
                          usage_quantity AS UsageQuantity, usage_unit AS UsageUnit, credits_estimated AS CreditsEstimated,
                          credits_consumed AS CreditsConsumed, provider_cost AS ProviderCost,
                          provider_currency AS ProviderCurrency, provider_cost_vnd AS ProviderCostVnd,
                          exchange_rate AS ExchangeRate, todox_points_estimated AS TodoxPointsEstimated,
                          todox_points_reserved AS TodoxPointsReserved, todox_points_charged AS TodoxPointsCharged,
                          todox_points_refunded AS TodoxPointsRefunded, balance_before AS BalanceBefore,
                          balance_after AS BalanceAfter, cost_source AS CostSource, error_code AS ErrorCode,
                          error_message AS ErrorMessage, created_at AS CreatedAt, started_at AS StartedAt,
                          submitted_at AS SubmittedAt, completed_at AS CompletedAt, failed_at AS FailedAt,
                          refunded_at AS RefundedAt, updated_at AS UpdatedAt;
                """,
                operation);
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            return null;
        }
    }

    public async Task MarkSubmittedAsync(Guid operationId, string providerTaskId, string responseJson, CancellationToken ct = default)
    {
        await ExecuteOptionalAsync(
            """
            UPDATE dance_sell.dance_sell_provider_operations
               SET status='submitted', provider_task_id=COALESCE(provider_task_id, @providerTaskId),
                   provider_status='submitted', response_json=CAST(@responseJson AS jsonb),
                   submitted_at=COALESCE(submitted_at, now()), updated_at=now()
             WHERE id=@operationId AND status NOT IN ('completed','failed','timeout','cancelled');
            """,
            new { operationId, providerTaskId, responseJson = KieJsonRedactor.Redact(responseJson) ?? "{}" },
            ct);
    }

    public async Task MarkCompletedAsync(Guid operationId, string providerStatus, string responseJson, decimal? creditsConsumed, string? resultUrl, CancellationToken ct = default)
    {
        await ExecuteOptionalAsync(
            """
            UPDATE dance_sell.dance_sell_provider_operations
               SET status='completed', provider_status=@providerStatus,
                   response_json=COALESCE(CAST(@responseJson AS jsonb), response_json),
                   usage_quantity=COALESCE(@creditsConsumed, usage_quantity),
                   usage_unit=CASE WHEN @creditsConsumed IS NULL THEN usage_unit ELSE 'credits' END,
                   credits_consumed=COALESCE(@creditsConsumed, credits_consumed),
                   cost_source=CASE WHEN @creditsConsumed IS NULL THEN COALESCE(cost_source, 'estimated') ELSE 'provider_response' END,
                   provider_usage_json=COALESCE(provider_usage_json, jsonb_build_object('creditsConsumed', @creditsConsumed, 'resultUrl', @resultUrl)),
                   completed_at=COALESCE(completed_at, now()), updated_at=now()
             WHERE id=@operationId AND status NOT IN ('completed','failed','timeout','cancelled');
            """,
            new { operationId, providerStatus, responseJson = KieJsonRedactor.Redact(responseJson), creditsConsumed, resultUrl },
            ct);
    }

    public async Task MarkFailedAsync(Guid operationId, string providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default)
    {
        await ExecuteOptionalAsync(
            """
            UPDATE dance_sell.dance_sell_provider_operations
               SET status='failed', provider_status=@providerStatus, error_json=CAST(@responseJson AS jsonb),
                   error_code=@errorCode, error_message=@errorMessage,
                   failed_at=COALESCE(failed_at, now()), updated_at=now()
             WHERE id=@operationId AND status NOT IN ('completed','failed','timeout','cancelled');
            """,
            new { operationId, providerStatus, responseJson = KieJsonRedactor.Redact(responseJson) ?? "{}", errorCode, errorMessage },
            ct);
    }

    public async Task UpsertAssetAsync(AiOperationAssetDto asset, CancellationToken ct = default)
    {
        asset.Id = asset.Id == Guid.Empty ? Guid.NewGuid() : asset.Id;
        await ExecuteOptionalAsync(
            """
            INSERT INTO public.todox_ai_operation_assets
                (id, operation_id, asset_role, media_id, object_key, public_url, provider_url, mime_type, metadata_json, created_at)
            VALUES
                (@Id, @OperationId, @AssetRole, @MediaId, @ObjectKey, @PublicUrl, @ProviderUrl, @MimeType,
                 CAST(@MetadataJson AS jsonb), now())
            ON CONFLICT (operation_id, asset_role, COALESCE(media_id, '00000000-0000-0000-0000-000000000000'::uuid), COALESCE(public_url, ''), COALESCE(provider_url, ''))
            DO NOTHING;
            """,
            asset,
            ct);
    }

    public async Task<PagedResult<DanceSellOperationLogItemDto>> SearchLogsAsync(DanceSellOperationLogFilter filter, CancellationToken ct = default)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var where = BuildFilterWhere(filter);
            var total = await conn.ExecuteScalarAsync<long>($"SELECT COUNT(*) {where.Sql}", where.Args);
            var rows = await conn.QueryAsync<DanceSellOperationLogItemDto>(
                $"""
                SELECT o.id AS Id, o.dance_sell_job_id AS DanceSellJobId, o.render_job_id AS RenderJobId,
                       o.parent_operation_id AS ParentOperationId, o.operation_type AS OperationType, o.attempt_no AS AttemptNo,
                       o.reference_mode AS ReferenceMode, o.provider_code AS ProviderCode,
                       o.provider_capability_id AS ProviderCapabilityId, o.provider_account_id AS ProviderAccountId,
                       o.provider_model AS ProviderModel, o.provider_task_id AS ProviderTaskId, o.status AS Status,
                       o.provider_status AS ProviderStatus, o.billing_status AS BillingStatus, o.refund_status AS RefundStatus,
                       o.usage_quantity AS UsageQuantity, o.usage_unit AS UsageUnit, o.credits_consumed AS CreditsConsumed,
                       o.provider_cost AS ProviderCost, o.provider_currency AS ProviderCurrency,
                       o.provider_cost_vnd AS ProviderCostVnd, o.todox_points_estimated AS TodoxPointsEstimated,
                       o.todox_points_charged AS TodoxPointsCharged, o.todox_points_refunded AS TodoxPointsRefunded,
                       o.cost_source AS CostSource, o.error_code AS ErrorCode, o.error_message AS ErrorMessage,
                       o.created_at AS CreatedAt, o.started_at AS StartedAt, o.submitted_at AS SubmittedAt,
                       o.completed_at AS CompletedAt, o.failed_at AS FailedAt, o.updated_at AS UpdatedAt,
                       j.title AS Title, j.customer_id AS CustomerId, j.user_id AS UserId,
                       j.current_stage AS CurrentStage, j.result_video_url AS ResultUrl,
                       COALESCE(a.asset_count, 0) AS AssetCount
                  {where.Sql}
                 ORDER BY o.created_at DESC
                 LIMIT @pageSize OFFSET @offset;
                """,
                where.Args.With(new { pageSize, offset = (page - 1) * pageSize }));
            return new PagedResult<DanceSellOperationLogItemDto>(rows.ToList(), page, pageSize, total);
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            return new PagedResult<DanceSellOperationLogItemDto>(Array.Empty<DanceSellOperationLogItemDto>(), page, pageSize, 0);
        }
    }

    public async Task<DanceSellOperationLogDetailDto?> GetLogDetailAsync(Guid id, CancellationToken ct = default)
    {
        var result = await SearchLogsAsync(new DanceSellOperationLogFilter { Page = 1, PageSize = 1 }, ct);
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var operation = await conn.QuerySingleOrDefaultAsync<DanceSellOperationLogItemDto>(
                """
                SELECT o.id AS Id, o.dance_sell_job_id AS DanceSellJobId, o.render_job_id AS RenderJobId,
                       o.operation_type AS OperationType, o.attempt_no AS AttemptNo, o.reference_mode AS ReferenceMode,
                       o.provider_code AS ProviderCode, o.provider_model AS ProviderModel, o.provider_task_id AS ProviderTaskId,
                       o.status AS Status, o.provider_status AS ProviderStatus, o.billing_status AS BillingStatus,
                       o.refund_status AS RefundStatus, o.request_json::text AS RequestJson, o.response_json::text AS ResponseJson,
                       o.callback_json::text AS CallbackJson, o.error_json::text AS ErrorJson, o.provider_usage_json::text AS ProviderUsageJson,
                       o.pricing_snapshot_json::text AS PricingSnapshotJson, o.usage_quantity AS UsageQuantity, o.usage_unit AS UsageUnit,
                       o.credits_consumed AS CreditsConsumed, o.provider_cost AS ProviderCost, o.provider_currency AS ProviderCurrency,
                       o.provider_cost_vnd AS ProviderCostVnd, o.exchange_rate AS ExchangeRate,
                       o.todox_points_estimated AS TodoxPointsEstimated, o.todox_points_charged AS TodoxPointsCharged,
                       o.todox_points_refunded AS TodoxPointsRefunded, o.balance_before AS BalanceBefore, o.balance_after AS BalanceAfter,
                       o.cost_source AS CostSource, o.error_code AS ErrorCode, o.error_message AS ErrorMessage,
                       o.created_at AS CreatedAt, o.started_at AS StartedAt, o.submitted_at AS SubmittedAt,
                       o.completed_at AS CompletedAt, o.failed_at AS FailedAt, o.updated_at AS UpdatedAt,
                       j.title AS Title, j.customer_id AS CustomerId, j.user_id AS UserId,
                       j.current_stage AS CurrentStage, j.result_video_url AS ResultUrl
                  FROM dance_sell.dance_sell_provider_operations o
                  LEFT JOIN dance_sell.dance_sell_jobs j ON j.id = o.dance_sell_job_id
                 WHERE o.id=@id;
                """,
                new { id });
            if (operation is null) return null;

            var assets = (await conn.QueryAsync<AiOperationAssetDto>(
                """
                SELECT id AS Id, operation_id AS OperationId, asset_role AS AssetRole, media_id AS MediaId,
                       object_key AS ObjectKey, public_url AS PublicUrl, provider_url AS ProviderUrl,
                       mime_type AS MimeType, metadata_json::text AS MetadataJson, created_at AS CreatedAt
                  FROM public.todox_ai_operation_assets
                 WHERE operation_id=@id
                 ORDER BY created_at;
                """,
                new { id })).ToList();
            return new DanceSellOperationLogDetailDto { Operation = operation, Assets = assets };
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            return result.Items.FirstOrDefault(x => x.Id == id) is { } item
                ? new DanceSellOperationLogDetailDto { Operation = item }
                : null;
        }
    }

    private async Task ExecuteOptionalAsync(string sql, object args, CancellationToken ct)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            await conn.ExecuteAsync(sql, args);
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
        }
    }

    private static (string Sql, DynamicParameters Args) BuildFilterWhere(DanceSellOperationLogFilter filter)
    {
        var args = new DynamicParameters();
        var clauses = new List<string> { "1=1" };
        void Add(string sql, string name, object? value)
        {
            clauses.Add(sql);
            args.Add(name, value);
        }

        if (filter.DanceSellJobId is Guid jobId) Add("o.dance_sell_job_id=@danceSellJobId", "danceSellJobId", jobId);
        if (filter.RenderJobId is Guid renderJobId) Add("o.render_job_id=@renderJobId", "renderJobId", renderJobId);
        if (!string.IsNullOrWhiteSpace(filter.ProviderTaskId)) Add("o.provider_task_id ILIKE @providerTaskId", "providerTaskId", $"%{filter.ProviderTaskId.Trim()}%");
        if (filter.CustomerId is Guid customerId) Add("j.customer_id=@customerId", "customerId", customerId);
        if (filter.UserId is Guid userId) Add("j.user_id=@userId", "userId", userId);
        if (!string.IsNullOrWhiteSpace(filter.ProviderCode)) Add("o.provider_code=@providerCode", "providerCode", filter.ProviderCode.Trim());
        if (filter.ProviderAccountId is Guid accountId) Add("o.provider_account_id=@providerAccountId", "providerAccountId", accountId);
        if (!string.IsNullOrWhiteSpace(filter.ModelName)) Add("o.provider_model ILIKE @modelName", "modelName", $"%{filter.ModelName.Trim()}%");
        if (!string.IsNullOrWhiteSpace(filter.OperationType)) Add("o.operation_type=@operationType", "operationType", filter.OperationType.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Status)) Add("o.status=@status", "status", filter.Status.Trim());
        if (!string.IsNullOrWhiteSpace(filter.BillingStatus)) Add("o.billing_status=@billingStatus", "billingStatus", filter.BillingStatus.Trim());
        if (!string.IsNullOrWhiteSpace(filter.RefundStatus)) Add("o.refund_status=@refundStatus", "refundStatus", filter.RefundStatus.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ErrorCode)) Add("o.error_code=@errorCode", "errorCode", filter.ErrorCode.Trim());
        if (filter.FromUtc is DateTime from) Add("o.created_at>=@fromUtc", "fromUtc", from);
        if (filter.ToUtc is DateTime to) Add("o.created_at<@toUtc", "toUtc", to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            Add(
                "(o.id::text ILIKE @search OR o.dance_sell_job_id::text ILIKE @search OR COALESCE(o.provider_task_id,'') ILIKE @search OR COALESCE(j.title,'') ILIKE @search OR COALESCE(o.provider_model,'') ILIKE @search)",
                "search",
                $"%{filter.Search.Trim()}%");
        }

        var sql =
            $"""
              FROM dance_sell.dance_sell_provider_operations o
              LEFT JOIN dance_sell.dance_sell_jobs j ON j.id = o.dance_sell_job_id
              LEFT JOIN (
                    SELECT operation_id, COUNT(*) AS asset_count
                      FROM public.todox_ai_operation_assets
                     GROUP BY operation_id
              ) a ON a.operation_id = o.id
             WHERE {string.Join(" AND ", clauses)}
            """;
        return (sql, args);
    }

    private static bool IsSchemaMissing(PostgresException ex)
        => ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InvalidColumnReference;
}

public interface IDanceSellCostEstimator
{
    Task<DanceSellCostEstimate> EstimateAsync(DanceSellProviderRouteDto route, string mode, TimeSpan? duration, CancellationToken ct = default);
}

public sealed class DanceSellCostEstimator : IDanceSellCostEstimator
{
    private readonly IConfiguration _configuration;

    public DanceSellCostEstimator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<DanceSellCostEstimate> EstimateAsync(DanceSellProviderRouteDto route, string mode, TimeSpan? duration, CancellationToken ct = default)
    {
        var exchangeRate = ReadDecimal("DanceSell:ExchangeRateVndPerUsd");
        var vndPerPoint = ReadDecimal("AiImageBilling:TodoXVndPerPoint");
        decimal? unitPrice = ReadDecimal($"DanceSell:Pricing:{route.ProviderCode}:{route.ModelName}:UsdPerRequest");
        var providerCost = unitPrice;
        var vnd = providerCost * exchangeRate;
        var points = vndPerPoint is > 0 ? vnd / vndPerPoint : null;

        return Task.FromResult(new DanceSellCostEstimate
        {
            OperationType = route.OperationType,
            ProviderCode = route.ProviderCode,
            ModelName = route.ModelName,
            UsageUnit = "credits",
            EstimatedUsage = 1,
            ProviderUnitPrice = unitPrice,
            EstimatedProviderCost = providerCost,
            Currency = "USD",
            ProviderCostVnd = vnd,
            EstimatedTodoxPoints = points,
            PricingSource = unitPrice is null ? "missing_config" : "config",
            Warning = unitPrice is null ? "Chua cau hinh don gia provider cho model nay." : null
        });
    }

    private decimal? ReadDecimal(string key)
        => decimal.TryParse(_configuration[key], out var parsed) ? parsed : null;
}

public interface IAiOperationBillingService
{
    Task<DanceSellProviderOperationDto> EstimateAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default);
    Task<DanceSellProviderOperationDto> ReserveAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default);
    Task<DanceSellProviderOperationDto> ChargeAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default);
    Task<DanceSellProviderOperationDto> RefundAsync(Guid operationId, decimal points, string reason, Guid? actorId, CancellationToken ct = default);
    Task<DanceSellProviderOperationDto> RetryChargeAsync(Guid operationId, string reason, Guid? actorId, CancellationToken ct = default);
    Task<DanceSellProviderOperationDto> RetryRefundAsync(Guid operationId, string reason, Guid? actorId, CancellationToken ct = default);
}

public sealed class AiOperationBillingService : IAiOperationBillingService
{
    private readonly IConfiguration _configuration;

    public AiOperationBillingService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public Task<DanceSellProviderOperationDto> EstimateAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
    {
        operation.BillingStatus = IsBillingEnabled() ? DanceSellBillingStatuses.Estimated : DanceSellBillingStatuses.NotRequired;
        operation.RefundStatus = IsBillingEnabled() ? DanceSellRefundStatuses.NotCharged : DanceSellRefundStatuses.NotRequired;
        return Task.FromResult(operation);
    }

    public Task<DanceSellProviderOperationDto> ReserveAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
    {
        operation.BillingStatus = IsBillingEnabled() ? DanceSellBillingStatuses.Reserved : DanceSellBillingStatuses.NotRequired;
        return Task.FromResult(operation);
    }

    public Task<DanceSellProviderOperationDto> ChargeAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
    {
        operation.BillingStatus = IsBillingEnabled() ? DanceSellBillingStatuses.Reconciliation : DanceSellBillingStatuses.NotRequired;
        return Task.FromResult(operation);
    }

    public Task<DanceSellProviderOperationDto> RefundAsync(Guid operationId, decimal points, string reason, Guid? actorId, CancellationToken ct = default)
        => Task.FromResult(new DanceSellProviderOperationDto
        {
            Id = operationId,
            BillingStatus = DanceSellBillingStatuses.NotRequired,
            RefundStatus = IsBillingEnabled() ? DanceSellRefundStatuses.ManualReview : DanceSellRefundStatuses.NotRequired,
            TodoxPointsRefunded = 0,
            ErrorMessage = IsBillingEnabled()
                ? "Refund requires wallet integration policy confirmation."
                : "Dance Sell billing is disabled; no real points were charged."
        });

    public Task<DanceSellProviderOperationDto> RetryChargeAsync(Guid operationId, string reason, Guid? actorId, CancellationToken ct = default)
        => Task.FromResult(new DanceSellProviderOperationDto { Id = operationId, BillingStatus = DanceSellBillingStatuses.Reconciliation });

    public Task<DanceSellProviderOperationDto> RetryRefundAsync(Guid operationId, string reason, Guid? actorId, CancellationToken ct = default)
        => Task.FromResult(new DanceSellProviderOperationDto { Id = operationId, RefundStatus = DanceSellRefundStatuses.ManualReview });

    private bool IsBillingEnabled()
        => bool.TryParse(_configuration[$"DanceSell:{DanceSellConstants.BillingEnabledConfigKey}"], out var enabled) && enabled;
}

public interface IAiProviderBalanceClient
{
    bool SupportsProvider(string providerCode);
    Task<ProviderBalanceResult> FetchBalanceAsync(ProviderAccountDto account, CancellationToken ct);
}

public interface IAiProviderBalanceClientFactory
{
    IAiProviderBalanceClient Resolve(string providerCode);
}

public sealed class AiProviderBalanceClientFactory : IAiProviderBalanceClientFactory
{
    private readonly IEnumerable<IAiProviderBalanceClient> _clients;

    public AiProviderBalanceClientFactory(IEnumerable<IAiProviderBalanceClient> clients)
    {
        _clients = clients;
    }

    public IAiProviderBalanceClient Resolve(string providerCode)
        => _clients.FirstOrDefault(x => x.SupportsProvider(providerCode)) ?? new ManualProviderBalanceClient();
}

public sealed class KieBalanceClient : IAiProviderBalanceClient
{
    public bool SupportsProvider(string providerCode)
        => providerCode.Equals(DanceSellConstants.ProviderCode, StringComparison.OrdinalIgnoreCase);

    public Task<ProviderBalanceResult> FetchBalanceAsync(ProviderAccountDto account, CancellationToken ct)
        => Task.FromResult(new ProviderBalanceResult
        {
            Success = false,
            ProviderCode = account.ProviderCode,
            ProviderAccountId = account.Id,
            BalanceUnit = account.BalanceUnit,
            Source = "manual",
            ErrorCode = "KIE_BALANCE_ENDPOINT_UNCONFIRMED",
            ErrorMessage = "KIE account balance endpoint is not confirmed in local provider contract; use manual ledger."
        });
}

public sealed class ManualProviderBalanceClient : IAiProviderBalanceClient
{
    public bool SupportsProvider(string providerCode) => true;

    public Task<ProviderBalanceResult> FetchBalanceAsync(ProviderAccountDto account, CancellationToken ct)
        => Task.FromResult(new ProviderBalanceResult
        {
            Success = true,
            ProviderCode = account.ProviderCode,
            ProviderAccountId = account.Id,
            Balance = account.LastKnownBalance,
            BalanceUnit = account.BalanceUnit,
            Source = "manual"
        });
}

internal static class DynamicParametersExtensions
{
    public static DynamicParameters With(this DynamicParameters source, object values)
    {
        var copy = new DynamicParameters(source);
        copy.AddDynamicParams(values);
        return copy;
    }
}
