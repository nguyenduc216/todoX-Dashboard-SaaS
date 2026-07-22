using System.Text.Json;
using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Services.AiProviders.Kie;
using Microsoft.Extensions.Options;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellProviderCatalog
{
    Task<IReadOnlyList<DanceSellProviderRouteDto>> GetRoutesAsync(string operationType, bool userSelectableOnly = false, CancellationToken ct = default);
    Task<DanceSellProviderRouteDto> GetDefaultRouteAsync(string operationType, CancellationToken ct = default);
    Task<DanceSellProviderRouteDto> ResolveAsync(string operationType, string? providerCode, string? modelName, CancellationToken ct = default);
}

public sealed class DanceSellSchemaException : InvalidOperationException
{
    public DanceSellSchemaException(string message, string? sqlState = null, string? table = null, string? column = null)
        : base(message)
    {
        SqlState = sqlState;
        Table = table;
        Column = column;
    }

    public string? SqlState { get; }
    public string? Table { get; }
    public string? Column { get; }
}

public sealed class DanceSellReferenceProviderRequest
{
    public DanceSellProviderRouteDto Route { get; set; } = new();
    public string Prompt { get; set; } = string.Empty;
    public string CharacterImageUrl { get; set; } = string.Empty;
    public string ProductImageUrl { get; set; } = string.Empty;
    public string? AspectRatio { get; set; }
    public string? CallbackUrl { get; set; }
    public Guid? ProviderAccountId { get; set; }
}

public sealed class ProviderTaskSubmitResult
{
    public string ProviderCode { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RequestJson { get; set; } = "{}";
    public string ResponseJson { get; set; } = "{}";
}

public interface IDanceSellReferenceProvider
{
    bool Supports(DanceSellProviderRouteDto route);
    Task<ProviderTaskSubmitResult> SubmitAsync(DanceSellReferenceProviderRequest request, CancellationToken ct);
    Task<KieTaskDetailResult> GetTaskAsync(string taskId, CancellationToken ct);
}

public interface IDanceSellReferenceProviderFactory
{
    IDanceSellReferenceProvider Resolve(DanceSellProviderRouteDto route);
}

public sealed class DanceSellReferenceProviderFactory : IDanceSellReferenceProviderFactory
{
    private readonly IEnumerable<IDanceSellReferenceProvider> _providers;

    public DanceSellReferenceProviderFactory(IEnumerable<IDanceSellReferenceProvider> providers)
    {
        _providers = providers;
    }

    public IDanceSellReferenceProvider Resolve(DanceSellProviderRouteDto route)
        => _providers.FirstOrDefault(x => x.Supports(route))
           ?? throw new InvalidOperationException("DANCE_SELL_REFERENCE_PROVIDER_NOT_SUPPORTED");
}

public sealed class KieDanceSellReferenceProvider : IDanceSellReferenceProvider
{
    private readonly IKieClient _client;
    private readonly IOptionsMonitor<KieOptions> _options;
    private readonly IAiProviderCredentialResolver _credentials;

    public KieDanceSellReferenceProvider(IKieClient client, IOptionsMonitor<KieOptions> options, IAiProviderCredentialResolver credentials)
    {
        _client = client;
        _options = options;
        _credentials = credentials;
    }

    public bool Supports(DanceSellProviderRouteDto route)
        => route.ProviderCode.Equals(DanceSellConstants.ProviderCode, StringComparison.OrdinalIgnoreCase);

    public async Task<ProviderTaskSubmitResult> SubmitAsync(DanceSellReferenceProviderRequest request, CancellationToken ct)
    {
        var prompt = request.Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new KieProviderException("Reference image prompt is required.", KieErrorCodes.Unknown, transient: false);
        }

        var characterUrl = KiePayloadBuilder.ValidatePublicHttpsUrl(request.CharacterImageUrl, "input_urls[0]");
        var productUrl = KiePayloadBuilder.ValidatePublicHttpsUrl(request.ProductImageUrl, "input_urls[1]");
        var callback = string.IsNullOrWhiteSpace(request.CallbackUrl)
            ? _options.CurrentValue.GetCallbackUriOrNull()?.ToString()
            : request.CallbackUrl;

        var payload = new KieImageToImageRequest
        {
            Model = request.Route.ModelName,
            CallBackUrl = callback,
            Input = new KieImageToImageInput
            {
                Prompt = prompt,
                InputUrls = new List<string> { characterUrl, productUrl },
                AspectRatio = string.IsNullOrWhiteSpace(request.AspectRatio) ? null : request.AspectRatio.Trim()
            }
        };
        var requestJson = KieJsonRedactor.Redact(JsonSerializer.Serialize(payload, KieJson.Options)) ?? "{}";
        var credential = await ResolveCredentialAsync(request.ProviderAccountId, ct);
        var submit = await _client.CreateTaskAsync(payload, credential, ct);
        return new ProviderTaskSubmitResult
        {
            ProviderCode = request.Route.ProviderCode,
            ModelName = request.Route.ModelName,
            TaskId = submit.TaskId!,
            RequestJson = requestJson,
            ResponseJson = KieJsonRedactor.Redact(submit.RawResponse) ?? "{}"
        };
    }

    public async Task<KieTaskDetailResult> GetTaskAsync(string taskId, CancellationToken ct)
        => throw new InvalidOperationException("KIE_REFERENCE_PROVIDER_ACCOUNT_REQUIRED");

    private async Task<ResolvedAiProviderCredential> ResolveCredentialAsync(Guid? providerAccountId, CancellationToken ct)
    {
        if (providerAccountId is not Guid id)
        {
            throw new InvalidOperationException("KIE_PROVIDER_ACCOUNT_REQUIRED");
        }

        return await _credentials.ResolveAsync(id, ct: ct);
    }
}

public sealed class DanceSellProviderCatalog : IDanceSellProviderCatalog
{
    private readonly TodoXConnectionFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DanceSellProviderCatalog> _logger;

    public DanceSellProviderCatalog(TodoXConnectionFactory factory, IConfiguration configuration, ILogger<DanceSellProviderCatalog> logger)
    {
        _factory = factory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DanceSellProviderRouteDto>> GetRoutesAsync(string operationType, bool userSelectableOnly = false, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var capabilityCode = CapabilityCodeFor(operationType);
            var rows = await conn.QueryAsync<DanceSellProviderRouteDto>(
                """
                WITH route_source AS (
                    SELECT c.id AS provider_capability_id,
                           c.provider_code,
                           c.model_name,
                           c.is_default,
                           c.enabled,
                           c.allow_user_select,
                           c.config_json,
                           c.capability_code,
                           p.priority,
                           a.id AS provider_account_id,
                           md5(c.provider_code || ':' || c.capability_code || ':' || c.model_name) AS route_hash
                      FROM public.todox_ai_provider_capability c
                      JOIN public.todox_ai_provider p ON p.id = c.provider_id
                      LEFT JOIN LATERAL (
                            SELECT id
                              FROM public.todox_ai_provider_account
                             WHERE provider_id = p.id
                               AND enabled = true
                             ORDER BY is_default DESC, priority, account_code
                             LIMIT 1
                      ) a ON true
                     WHERE c.capability_code = @capabilityCode
                       AND c.enabled = true
                       AND p.enabled = true
                       AND (@userSelectableOnly = false OR c.allow_user_select = true)
                )
                SELECT (substr(route_hash,1,8) || '-' || substr(route_hash,9,4) || '-' || substr(route_hash,13,4) || '-' || substr(route_hash,17,4) || '-' || substr(route_hash,21,12))::uuid AS Id,
                       @featureCode AS FeatureCode,
                       @operationType AS OperationType,
                       provider_code AS ProviderCode,
                       provider_capability_id AS ProviderCapabilityId,
                       provider_account_id AS ProviderAccountId,
                       model_name AS ModelName,
                       priority AS Priority,
                       is_default AS IsDefault,
                       enabled AS Enabled,
                       allow_user_select AS AllowUserSelect,
                       config_json::text AS ConfigJson
                  FROM route_source
                 ORDER BY is_default DESC, priority, provider_code, model_name;
                """,
                new { featureCode = DanceSellConstants.FeatureCode, operationType, capabilityCode, userSelectableOnly });
            var list = rows.ToList();
            if (list.Count > 0)
            {
                return list;
            }

            if (AllowCodeFallback)
            {
                _logger.LogWarning("DANCE_SELL_PROVIDER_ROUTE_NOT_CONFIGURED operationType={OperationType}; using code fallback because AllowCodeProviderFallback is enabled.", operationType);
                return new[] { Fallback(operationType) };
            }

            throw new InvalidOperationException("DANCE_SELL_PROVIDER_ROUTE_NOT_CONFIGURED");
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            _logger.LogError(ex,
                "DANCE_SELL_DATABASE_SCHEMA_NOT_READY sqlState={SqlState} table={Table} column={Column}",
                ex.SqlState, ex.TableName, ex.ColumnName);
            if (AllowCodeFallback)
            {
                return new[] { Fallback(operationType) };
            }

            throw new DanceSellSchemaException("DANCE_SELL_DATABASE_SCHEMA_NOT_READY", ex.SqlState, ex.TableName, ex.ColumnName);
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

    private static string CapabilityCodeFor(string operationType)
        => operationType == DanceSellOperationTypes.ReferenceImage
            ? DanceSellConstants.ReferenceCapabilityCode
            : DanceSellConstants.CapabilityCode;

    private static bool IsSchemaMissing(PostgresException ex)
        => ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn;

    private bool AllowCodeFallback
        => ReadBool($"DanceSell:{DanceSellConstants.AllowCodeProviderFallbackConfigKey}")
           || ReadBool("DanceSell:AllowCodeProviderFallback");

    private bool ReadBool(string key)
        => bool.TryParse(_configuration[key], out var value) && value;
}

public interface IDanceSellOperationRepository
{
    Task<DanceSellProviderOperationDto?> UpsertOperationAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default);
    Task<int> GetNextAttemptNoAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default);
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
    private readonly ILogger<DanceSellOperationRepository> _logger;

    public DanceSellOperationRepository(TodoXConnectionFactory factory, ILogger<DanceSellOperationRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<DanceSellProviderOperationDto?> UpsertOperationAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
    {
        operation.Id = operation.RenderJobId ?? (operation.Id == Guid.Empty ? Guid.NewGuid() : operation.Id);
        operation.RenderJobId = operation.Id;
        operation.RequestJson = KieJsonRedactor.Redact(operation.RequestJson) ?? "{}";
        operation.ResponseJson = KieJsonRedactor.Redact(operation.ResponseJson);
        operation.CallbackJson = KieJsonRedactor.Redact(operation.CallbackJson);
        operation.ErrorJson = KieJsonRedactor.Redact(operation.ErrorJson);
        var jobType = operation.OperationType == DanceSellOperationTypes.ReferenceImage
            ? "dance_sell_reference_image"
            : "dance_sell_motion_video";
        var renderStatus = NormalizeRenderStatus(operation.Status);
        var inputJson = DanceSellRepository.ToJson(new { operation.DanceSellJobId, operation.ReferenceMode });
        var optionsJson = DanceSellRepository.ToJson(new
        {
            operation.ReferenceMode,
            pricingSnapshotJson = operation.PricingSnapshotJson,
            costSource = operation.CostSource,
            legacyAdapter = "render.render_jobs"
        });

        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.QuerySingleAsync<DanceSellProviderOperationDto>(
                """
                INSERT INTO render.render_jobs
                    (id, job_type, operation_type, business_entity_type, business_entity_id,
                     parent_render_job_id, status, provider_code, provider_capability_id,
                     provider_account_id, model_code, provider_task_id, provider_status,
                     provider_request_json, provider_response_json, last_provider_response_json,
                     provider_usage_json, usage_quantity, usage_unit, provider_cost,
                     provider_currency, point_cost_estimate, point_cost_charged, point_status,
                     error_code, error_message, input_json, options, started_at, completed_at,
                     created_at, updated_at)
                VALUES
                    (@Id, @jobType, @OperationType, 'dance_sell_job', @DanceSellJobId,
                     @ParentOperationId, @renderStatus, @ProviderCode, @ProviderCapabilityId,
                     @ProviderAccountId, @ProviderModel, @ProviderTaskId, @ProviderStatus,
                     CAST(@RequestJson AS jsonb), CAST(COALESCE(@ResponseJson, '{}') AS jsonb), CAST(COALESCE(@ResponseJson, '{}') AS jsonb),
                     CAST(COALESCE(@ProviderUsageJson, '{}') AS jsonb), @UsageQuantity, @UsageUnit, @ProviderCost,
                     @ProviderCurrency, COALESCE(@TodoxPointsEstimated,0), COALESCE(@TodoxPointsCharged,0), @pointStatus,
                     @ErrorCode, @ErrorMessage, CAST(@inputJson AS jsonb), CAST(@optionsJson AS jsonb),
                     @StartedAt, @CompletedAt, COALESCE(@CreatedAt, now()), now())
                ON CONFLICT (id)
                DO UPDATE SET status=EXCLUDED.status,
                              provider_code=COALESCE(EXCLUDED.provider_code, render.render_jobs.provider_code),
                              provider_capability_id=COALESCE(EXCLUDED.provider_capability_id, render.render_jobs.provider_capability_id),
                              provider_account_id=COALESCE(EXCLUDED.provider_account_id, render.render_jobs.provider_account_id),
                              model_code=COALESCE(EXCLUDED.model_code, render.render_jobs.model_code),
                              provider_task_id=COALESCE(EXCLUDED.provider_task_id, render.render_jobs.provider_task_id),
                              provider_status=COALESCE(EXCLUDED.provider_status, render.render_jobs.provider_status),
                              provider_request_json=EXCLUDED.provider_request_json,
                              provider_response_json=EXCLUDED.provider_response_json,
                              last_provider_response_json=EXCLUDED.last_provider_response_json,
                              provider_usage_json=EXCLUDED.provider_usage_json,
                              usage_quantity=COALESCE(EXCLUDED.usage_quantity, render.render_jobs.usage_quantity),
                              usage_unit=COALESCE(EXCLUDED.usage_unit, render.render_jobs.usage_unit),
                              provider_cost=COALESCE(EXCLUDED.provider_cost, render.render_jobs.provider_cost),
                              provider_currency=COALESCE(EXCLUDED.provider_currency, render.render_jobs.provider_currency),
                              point_cost_estimate=GREATEST(render.render_jobs.point_cost_estimate, EXCLUDED.point_cost_estimate),
                              point_cost_charged=GREATEST(render.render_jobs.point_cost_charged, EXCLUDED.point_cost_charged),
                              error_code=COALESCE(EXCLUDED.error_code, render.render_jobs.error_code),
                              error_message=COALESCE(EXCLUDED.error_message, render.render_jobs.error_message),
                              updated_at=now()
                RETURNING id AS Id, business_entity_id AS DanceSellJobId, id AS RenderJobId,
                          parent_render_job_id AS ParentOperationId, operation_type AS OperationType,
                          GREATEST(attempt_count, 1) AS AttemptNo, options->>'ReferenceMode' AS ReferenceMode,
                          provider_code AS ProviderCode, provider_capability_id AS ProviderCapabilityId,
                          provider_account_id AS ProviderAccountId, model_code AS ProviderModel,
                          provider_task_id AS ProviderTaskId, status AS Status, provider_status AS ProviderStatus,
                          point_status AS BillingStatus, 'not_required' AS RefundStatus,
                          provider_request_json::text AS RequestJson, provider_response_json::text AS ResponseJson,
                          '{}' AS CallbackJson, '{}' AS ErrorJson, provider_usage_json::text AS ProviderUsageJson,
                          options->>'pricingSnapshotJson' AS PricingSnapshotJson, usage_quantity AS UsageQuantity,
                          usage_unit AS UsageUnit, NULL::numeric AS CreditsEstimated, usage_quantity AS CreditsConsumed,
                          provider_cost AS ProviderCost, provider_currency AS ProviderCurrency,
                          NULL::numeric AS ProviderCostVnd, NULL::numeric AS ExchangeRate,
                          point_cost_estimate AS TodoxPointsEstimated, NULL::numeric AS TodoxPointsReserved,
                          point_cost_charged AS TodoxPointsCharged, NULL::numeric AS TodoxPointsRefunded,
                          NULL::numeric AS BalanceBefore, NULL::numeric AS BalanceAfter,
                          options->>'costSource' AS CostSource, error_code AS ErrorCode, error_message AS ErrorMessage,
                          created_at AS CreatedAt, started_at AS StartedAt, started_at AS SubmittedAt,
                          completed_at AS CompletedAt, completed_at AS FailedAt, NULL::timestamptz AS RefundedAt,
                          updated_at AS UpdatedAt;
                """,
                new
                {
                    operation.Id,
                    operation.DanceSellJobId,
                    operation.ParentOperationId,
                    operation.OperationType,
                    operation.ProviderCode,
                    operation.ProviderCapabilityId,
                    operation.ProviderAccountId,
                    operation.ProviderModel,
                    operation.ProviderTaskId,
                    operation.ProviderStatus,
                    operation.RequestJson,
                    operation.ResponseJson,
                    operation.ProviderUsageJson,
                    operation.UsageQuantity,
                    operation.UsageUnit,
                    operation.ProviderCost,
                    operation.ProviderCurrency,
                    operation.TodoxPointsEstimated,
                    operation.TodoxPointsCharged,
                    operation.ErrorCode,
                    operation.ErrorMessage,
                    operation.StartedAt,
                    operation.CompletedAt,
                    CreatedAt = operation.CreatedAt == default ? (DateTime?)null : operation.CreatedAt,
                    jobType,
                    renderStatus,
                    pointStatus = ToPointStatus(operation.BillingStatus),
                    inputJson,
                    optionsJson
                });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
    }

    public async Task<int> GetNextAttemptNoAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<int>(
                """
                SELECT COALESCE(MAX(attempt_count), 0) + 1
                  FROM render.render_jobs
                 WHERE business_entity_type = 'dance_sell_job'
                   AND business_entity_id = @danceSellJobId
                   AND operation_type = @operationType;
                """,
                new { danceSellJobId, operationType });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
    }

    public async Task MarkSubmittedAsync(Guid operationId, string providerTaskId, string responseJson, CancellationToken ct = default)
    {
        await ExecuteOptionalAsync(
            """
            UPDATE render.render_jobs
               SET status='rendering', provider_task_id=COALESCE(provider_task_id, @providerTaskId),
                   provider_status='submitted', provider_response_json=CAST(@responseJson AS jsonb),
                   last_provider_response_json=CAST(@responseJson AS jsonb),
                   started_at=COALESCE(started_at, now()), updated_at=now()
             WHERE id=@operationId AND status NOT IN ('completed','failed','timeout','cancelled');
            """,
            new { operationId, providerTaskId, responseJson = KieJsonRedactor.Redact(responseJson) ?? "{}" },
            ct);
    }

    public async Task MarkCompletedAsync(Guid operationId, string providerStatus, string responseJson, decimal? creditsConsumed, string? resultUrl, CancellationToken ct = default)
    {
        await ExecuteOptionalAsync(
            """
            UPDATE render.render_jobs
               SET status='completed', provider_status=@providerStatus,
                   provider_response_json=COALESCE(CAST(@responseJson AS jsonb), provider_response_json),
                   last_provider_response_json=COALESCE(CAST(@responseJson AS jsonb), last_provider_response_json),
                   usage_quantity=COALESCE(@creditsConsumed, usage_quantity),
                   usage_unit=CASE WHEN @creditsConsumed IS NULL THEN usage_unit ELSE 'credits' END,
                   provider_usage_json=jsonb_strip_nulls(jsonb_build_object('creditsConsumed', @creditsConsumed, 'resultUrl', @resultUrl)),
                   output_json=CASE WHEN @resultUrl IS NULL THEN output_json ELSE jsonb_build_array(jsonb_build_object('url', @resultUrl)) END,
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
            UPDATE render.render_jobs
               SET status='failed', provider_status=@providerStatus,
                   provider_response_json=CAST(@responseJson AS jsonb),
                   last_provider_response_json=CAST(@responseJson AS jsonb),
                   error_code=@errorCode, error_message=@errorMessage,
                   completed_at=COALESCE(completed_at, now()), updated_at=now()
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
            INSERT INTO render.render_artifacts
                (id, render_job_id, artifact_type, media_id, object_key, public_url, provider_url, mime_type, metadata_json, created_at)
            VALUES
                (@Id, @OperationId, @ArtifactType, @MediaId, @ObjectKey, @PublicUrl, @ProviderUrl, @MimeType,
                 CAST(@MetadataJson AS jsonb), now())
            ON CONFLICT DO NOTHING;
            """,
            new
            {
                asset.Id,
                asset.OperationId,
                ArtifactType = ToArtifactType(asset.AssetRole),
                asset.MediaId,
                asset.ObjectKey,
                asset.PublicUrl,
                asset.ProviderUrl,
                asset.MimeType,
                MetadataJson = KieJsonRedactor.Redact(asset.MetadataJson) ?? "{}"
            },
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
                SELECT o.id AS Id, o.business_entity_id AS DanceSellJobId, o.id AS RenderJobId,
                       o.parent_render_job_id AS ParentOperationId, o.operation_type AS OperationType, GREATEST(o.attempt_count, 1) AS AttemptNo,
                       o.options->>'ReferenceMode' AS ReferenceMode, o.provider_code AS ProviderCode,
                       o.provider_capability_id AS ProviderCapabilityId, o.provider_account_id AS ProviderAccountId,
                       o.model_code AS ProviderModel, o.provider_task_id AS ProviderTaskId, o.status AS Status,
                       o.provider_status AS ProviderStatus, o.point_status AS BillingStatus, 'not_required' AS RefundStatus,
                       o.usage_quantity AS UsageQuantity, o.usage_unit AS UsageUnit, o.usage_quantity AS CreditsConsumed,
                       o.provider_cost AS ProviderCost, o.provider_currency AS ProviderCurrency,
                       NULL::numeric AS ProviderCostVnd, o.point_cost_estimate AS TodoxPointsEstimated,
                       o.point_cost_charged AS TodoxPointsCharged, NULL::numeric AS TodoxPointsRefunded,
                       o.options->>'costSource' AS CostSource, o.error_code AS ErrorCode, o.error_message AS ErrorMessage,
                       o.created_at AS CreatedAt, o.started_at AS StartedAt, o.started_at AS SubmittedAt,
                       o.completed_at AS CompletedAt, o.completed_at AS FailedAt, o.updated_at AS UpdatedAt,
                       j.title AS Title, j.customer_id AS CustomerId, j.user_id AS UserId,
                       j.current_stage AS CurrentStage, j.result_url AS ResultUrl,
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
            throw SchemaNotReady(ex);
        }
    }

    public async Task<DanceSellOperationLogDetailDto?> GetLogDetailAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            var operation = await conn.QuerySingleOrDefaultAsync<DanceSellOperationLogItemDto>(
                """
                SELECT o.id AS Id, o.business_entity_id AS DanceSellJobId, o.id AS RenderJobId,
                       o.operation_type AS OperationType, GREATEST(o.attempt_count, 1) AS AttemptNo, o.options->>'ReferenceMode' AS ReferenceMode,
                       o.provider_code AS ProviderCode, o.model_code AS ProviderModel, o.provider_task_id AS ProviderTaskId,
                       o.status AS Status, o.provider_status AS ProviderStatus, o.point_status AS BillingStatus,
                       'not_required' AS RefundStatus, o.provider_request_json::text AS RequestJson,
                       o.provider_response_json::text AS ResponseJson, '{}' AS CallbackJson, '{}' AS ErrorJson,
                       o.provider_usage_json::text AS ProviderUsageJson, o.options->>'pricingSnapshotJson' AS PricingSnapshotJson,
                       o.usage_quantity AS UsageQuantity, o.usage_unit AS UsageUnit,
                       o.usage_quantity AS CreditsConsumed, o.provider_cost AS ProviderCost, o.provider_currency AS ProviderCurrency,
                       NULL::numeric AS ProviderCostVnd, NULL::numeric AS ExchangeRate,
                       o.point_cost_estimate AS TodoxPointsEstimated, o.point_cost_charged AS TodoxPointsCharged,
                       NULL::numeric AS TodoxPointsRefunded, NULL::numeric AS BalanceBefore, NULL::numeric AS BalanceAfter,
                       o.options->>'costSource' AS CostSource, o.error_code AS ErrorCode, o.error_message AS ErrorMessage,
                       o.created_at AS CreatedAt, o.started_at AS StartedAt, o.started_at AS SubmittedAt,
                       o.completed_at AS CompletedAt, o.completed_at AS FailedAt, o.updated_at AS UpdatedAt,
                       j.title AS Title, j.customer_id AS CustomerId, j.user_id AS UserId,
                       j.current_stage AS CurrentStage, j.result_url AS ResultUrl
                  FROM render.render_jobs o
                  LEFT JOIN dance_sell.dance_sell_jobs j ON j.id = o.business_entity_id
                 WHERE o.id=@id;
                """,
                new { id });
            if (operation is null) return null;

            var assets = (await conn.QueryAsync<AiOperationAssetDto>(
                """
                SELECT id AS Id, render_job_id AS OperationId, artifact_type AS AssetRole, media_id AS MediaId,
                       object_key AS ObjectKey, public_url AS PublicUrl, provider_url AS ProviderUrl,
                       mime_type AS MimeType, metadata_json::text AS MetadataJson, created_at AS CreatedAt
                  FROM render.render_artifacts
                 WHERE render_job_id=@id
                 ORDER BY created_at;
                """,
                new { id })).ToList();
            return new DanceSellOperationLogDetailDto { Operation = operation, Assets = assets };
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
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
            throw SchemaNotReady(ex);
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

        if (filter.DanceSellJobId is Guid jobId) Add("o.business_entity_id=@danceSellJobId", "danceSellJobId", jobId);
        if (filter.RenderJobId is Guid renderJobId) Add("o.id=@renderJobId", "renderJobId", renderJobId);
        if (!string.IsNullOrWhiteSpace(filter.ProviderTaskId)) Add("o.provider_task_id ILIKE @providerTaskId", "providerTaskId", $"%{filter.ProviderTaskId.Trim()}%");
        if (filter.CustomerId is Guid customerId) Add("j.customer_id=@customerId", "customerId", customerId);
        if (filter.UserId is Guid userId) Add("j.user_id=@userId", "userId", userId);
        if (!string.IsNullOrWhiteSpace(filter.ProviderCode)) Add("o.provider_code=@providerCode", "providerCode", filter.ProviderCode.Trim());
        if (filter.ProviderAccountId is Guid accountId) Add("o.provider_account_id=@providerAccountId", "providerAccountId", accountId);
        if (!string.IsNullOrWhiteSpace(filter.ModelName)) Add("o.model_code ILIKE @modelName", "modelName", $"%{filter.ModelName.Trim()}%");
        if (!string.IsNullOrWhiteSpace(filter.OperationType)) Add("o.operation_type=@operationType", "operationType", filter.OperationType.Trim());
        if (!string.IsNullOrWhiteSpace(filter.Status)) Add("o.status=@status", "status", filter.Status.Trim());
        if (!string.IsNullOrWhiteSpace(filter.BillingStatus)) Add("o.point_status=@billingStatus", "billingStatus", filter.BillingStatus.Trim());
        if (!string.IsNullOrWhiteSpace(filter.ErrorCode)) Add("o.error_code=@errorCode", "errorCode", filter.ErrorCode.Trim());
        if (filter.FromUtc is DateTime from) Add("o.created_at>=@fromUtc", "fromUtc", from);
        if (filter.ToUtc is DateTime to) Add("o.created_at<@toUtc", "toUtc", to);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            Add(
                "(o.id::text ILIKE @search OR o.business_entity_id::text ILIKE @search OR COALESCE(o.provider_task_id,'') ILIKE @search OR COALESCE(j.title,'') ILIKE @search OR COALESCE(o.model_code,'') ILIKE @search)",
                "search",
                $"%{filter.Search.Trim()}%");
        }

        var sql =
            $"""
              FROM render.render_jobs o
              LEFT JOIN dance_sell.dance_sell_jobs j ON j.id = o.business_entity_id
              LEFT JOIN (
                    SELECT render_job_id, COUNT(*) AS asset_count
                      FROM render.render_artifacts
                     GROUP BY render_job_id
              ) a ON a.render_job_id = o.id
             WHERE {string.Join(" AND ", clauses)}
               AND o.business_entity_type = 'dance_sell_job'
               AND o.operation_type IN ('reference_image','motion_video')
            """;
        return (sql, args);
    }

    private static string NormalizeRenderStatus(string status)
        => status switch
        {
            DanceSellOperationStatuses.Draft => "draft",
            DanceSellOperationStatuses.Queued => "queued",
            DanceSellOperationStatuses.Submitted => "rendering",
            DanceSellOperationStatuses.Generating => "rendering",
            DanceSellOperationStatuses.Completed => "completed",
            DanceSellOperationStatuses.Failed => "failed",
            DanceSellOperationStatuses.Timeout => "timeout",
            DanceSellOperationStatuses.Cancelled => "cancelled",
            _ => "queued"
        };

    private static string ToPointStatus(string billingStatus)
        => billingStatus switch
        {
            DanceSellBillingStatuses.Reserved => "pending",
            DanceSellBillingStatuses.Charged => "charged",
            DanceSellBillingStatuses.Refunded => "refunded",
            DanceSellBillingStatuses.ChargeFailed => "insufficient",
            _ => "not_required"
        };

    private static string ToArtifactType(string assetRole)
        => assetRole switch
        {
            DanceSellAssetRoles.ReferenceOutput => "reference_image",
            DanceSellAssetRoles.VideoOutput => "final_video",
            DanceSellAssetRoles.ProviderRawOutput => "provider_raw_output",
            _ => "other"
        };

    private static bool IsSchemaMissing(PostgresException ex)
        => ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn or PostgresErrorCodes.InvalidColumnReference;

    private DanceSellSchemaException SchemaNotReady(PostgresException ex)
    {
        _logger.LogError(ex,
            "DANCE_SELL_DATABASE_SCHEMA_NOT_READY sqlState={SqlState} table={Table} column={Column}",
            ex.SqlState, ex.TableName, ex.ColumnName);
        return new DanceSellSchemaException("DANCE_SELL_DATABASE_SCHEMA_NOT_READY", ex.SqlState, ex.TableName, ex.ColumnName);
    }
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
        using var configDoc = TryParseJson(route.ConfigJson);
        var pricingUnit = ReadString(configDoc, "pricingUnit")
                          ?? ReadString(configDoc, "pricing_unit")
                          ?? ReadString(configDoc, "usageUnit")
                          ?? "request";
        var estimatedUsage = ReadDecimal(configDoc, "estimatedUsage")
                             ?? ReadDecimal(configDoc, "estimated_usage")
                             ?? pricingUnit switch
                             {
                                 "fixed" => 0,
                                 "video_second" or "second" when duration is not null => (decimal)duration.Value.TotalSeconds,
                                 _ => 1
                             };
        var unitPrice = ReadDecimal(configDoc, "providerUnitPrice")
                        ?? ReadDecimal(configDoc, "provider_unit_price")
                        ?? ReadDecimal(configDoc, "usdPerRequest")
                        ?? ReadDecimal($"DanceSell:Pricing:{route.ProviderCode}:{route.ModelName}:UsdPerRequest");
        var exchangeRate = ReadDecimal(configDoc, "exchangeRate")
                           ?? ReadDecimal(configDoc, "exchange_rate")
                           ?? ReadDecimal("DanceSell:ExchangeRateVndPerUsd");
        var vndPerPoint = ReadDecimal(configDoc, "todoxVndPerPoint")
                          ?? ReadDecimal(configDoc, "todox_vnd_per_point")
                          ?? ReadDecimal("AiImageBilling:TodoXVndPerPoint");
        var providerCost = unitPrice * estimatedUsage;
        var vnd = providerCost * exchangeRate;
        var points = vndPerPoint is > 0 ? vnd / vndPerPoint : null;
        var source = unitPrice is not null && configDoc is not null ? "route_config"
            : unitPrice is not null ? "configuration"
            : "missing_config";

        return Task.FromResult(new DanceSellCostEstimate
        {
            OperationType = route.OperationType,
            ProviderCode = route.ProviderCode,
            ModelName = route.ModelName,
            UsageUnit = pricingUnit,
            PricingUnit = pricingUnit,
            EstimatedUsage = estimatedUsage,
            ProviderUnitPrice = unitPrice,
            EstimatedProviderCost = providerCost,
            Currency = ReadString(configDoc, "currency") ?? "USD",
            ExchangeRate = exchangeRate,
            ProviderCostVnd = vnd,
            EstimatedTodoxPoints = points,
            TodoXVndPerPoint = vndPerPoint,
            PricingSource = source,
            Warning = unitPrice is null ? "Chua cau hinh don gia provider cho model nay." : null
        });
    }

    private decimal? ReadDecimal(string key)
        => decimal.TryParse(_configuration[key], out var parsed) ? parsed : null;

    private static JsonDocument? TryParseJson(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(rawJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonDocument? doc, string propertyName)
        => doc is not null
           && doc.RootElement.ValueKind == JsonValueKind.Object
           && doc.RootElement.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadDecimal(JsonDocument? doc, string propertyName)
    {
        if (doc is null
            || doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
    }
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
        EnsureBillingEnabled();
        operation.BillingStatus = IsBillingEnabled() ? DanceSellBillingStatuses.Reserved : DanceSellBillingStatuses.NotRequired;
        return Task.FromResult(operation);
    }

    public Task<DanceSellProviderOperationDto> ChargeAsync(DanceSellProviderOperationDto operation, CancellationToken ct = default)
    {
        EnsureBillingEnabled();
        operation.BillingStatus = IsBillingEnabled() ? DanceSellBillingStatuses.Reconciliation : DanceSellBillingStatuses.NotRequired;
        return Task.FromResult(operation);
    }

    public Task<DanceSellProviderOperationDto> RefundAsync(Guid operationId, decimal points, string reason, Guid? actorId, CancellationToken ct = default)
    {
        EnsureBillingEnabled();
        return Task.FromResult(new DanceSellProviderOperationDto
        {
            Id = operationId,
            RefundStatus = DanceSellRefundStatuses.ManualReview,
            ErrorMessage = "Refund requires wallet integration policy confirmation."
        });
    }

    public Task<DanceSellProviderOperationDto> RetryChargeAsync(Guid operationId, string reason, Guid? actorId, CancellationToken ct = default)
    {
        EnsureBillingEnabled();
        return Task.FromResult(new DanceSellProviderOperationDto { Id = operationId, BillingStatus = DanceSellBillingStatuses.Reconciliation });
    }

    public Task<DanceSellProviderOperationDto> RetryRefundAsync(Guid operationId, string reason, Guid? actorId, CancellationToken ct = default)
    {
        EnsureBillingEnabled();
        return Task.FromResult(new DanceSellProviderOperationDto { Id = operationId, RefundStatus = DanceSellRefundStatuses.ManualReview });
    }

    private bool IsBillingEnabled()
        => bool.TryParse(_configuration[$"DanceSell:{DanceSellConstants.BillingEnabledConfigKey}"], out var enabled) && enabled
           || bool.TryParse(_configuration["DanceSell:BillingEnabled"], out var enabledAlias) && enabledAlias;

    private void EnsureBillingEnabled()
    {
        if (!IsBillingEnabled())
        {
            throw new InvalidOperationException("DANCE_SELL_BILLING_DISABLED");
        }
    }
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
