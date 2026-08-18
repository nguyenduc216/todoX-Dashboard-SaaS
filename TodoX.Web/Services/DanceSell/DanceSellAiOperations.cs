using System.Text.Json;
using Dapper;
using Npgsql;
using TodoX.Web.Services.AiProviders;
using TodoX.Web.Data;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Timelapse;
using Microsoft.Extensions.Options;
using TodoX.Web.Models;

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
    public Guid? CharacterMediaId { get; set; }
    public Guid? ProductMediaId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public string CharacterImageUrl { get; set; } = string.Empty;
    public string ProductImageUrl { get; set; } = string.Empty;
    public string? AspectRatio { get; set; }
    public string? CallbackUrl { get; set; }
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
    Task<KieTaskDetailResult> GetTaskAsync(DanceSellProviderRouteDto route, string taskId, CancellationToken ct);
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

    public KieDanceSellReferenceProvider(IKieClient client, IOptionsMonitor<KieOptions> options)
    {
        _client = client;
        _options = options;
    }

    public bool Supports(DanceSellProviderRouteDto route)
        => route.ProviderCode.Equals(DanceSellConstants.KieProviderCode, StringComparison.OrdinalIgnoreCase);

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
        var submit = await _client.CreateTaskAsync(payload, ct);
        return new ProviderTaskSubmitResult
        {
            ProviderCode = request.Route.ProviderCode,
            ModelName = request.Route.ModelName,
            TaskId = submit.TaskId!,
            RequestJson = requestJson,
            ResponseJson = KieJsonRedactor.Redact(submit.RawResponse) ?? "{}"
        };
    }

    public async Task<KieTaskDetailResult> GetTaskAsync(DanceSellProviderRouteDto route, string taskId, CancellationToken ct)
        => await _client.GetTaskDetailAsync(taskId, ct);
}

public sealed class Ai79DanceSellReferenceProvider : IDanceSellReferenceProvider
{
    private readonly IAi79TaskClient _client;
    private readonly IProviderCredentialResolver _credentials;
    private readonly ILogger<Ai79DanceSellReferenceProvider> _logger;

    public Ai79DanceSellReferenceProvider(
        IAi79TaskClient client,
        IProviderCredentialResolver credentials,
        ILogger<Ai79DanceSellReferenceProvider> logger)
    {
        _client = client;
        _credentials = credentials;
        _logger = logger;
    }

    public bool Supports(DanceSellProviderRouteDto route)
        => route.ProviderCode.Equals(DanceSellConstants.ProviderCode, StringComparison.OrdinalIgnoreCase);

    public async Task<ProviderTaskSubmitResult> SubmitAsync(DanceSellReferenceProviderRequest request, CancellationToken ct)
    {
        var runtime = await ResolveRuntimeAsync(request.Route, ct);
        runtime = runtime with
        {
            Model = DanceSellConstants.Ai79GptImage2Model,
            BaseUrl = "https://api.gommo.net/ai",
            Domain = "79ai.net",
            SubmitPath = "/generateImage",
            PollPath = "/image"
        };
        var characterUrl = KiePayloadBuilder.ValidatePublicHttpsUrl(request.CharacterImageUrl, "subjects[0][url]");
        var productUrl = KiePayloadBuilder.ValidatePublicHttpsUrl(request.ProductImageUrl, "subjects[1][url]");
        var ratio = "16:9";
        var category = "FASHION";
        var resolution = "1k";
        var mode = "low";
        var projectId = "default";
        var sync = "false";
        var numOutputs = "1";
        var language = "VI";
        var options = new Dictionary<string, string?>
        {
            ["action_type"] = "create",
            ["sync"] = sync,
            ["project_id"] = projectId,
            ["subjects[0][url]"] = characterUrl,
            ["subjects[1][url]"] = productUrl,
            ["ratio"] = ratio,
            ["resolution"] = resolution,
            ["category"] = category,
            ["mode"] = mode,
            ["num_outputs"] = numOutputs,
            ["language"] = language
        };
        var submit = new Ai79TaskSubmitRequest(
            runtime.BaseUrl,
            runtime.SubmitPath,
            runtime.Credential.Secret,
            runtime.Domain,
            runtime.Model,
            BuildReferencePrompt(),
            [],
            options,
            Ai79TaskOperation.Image);
        var formFieldNames = BuildGenerateImageFieldNames(options);
        var requestJson = JsonSerializer.Serialize(new
        {
            providerCode = runtime.ProviderCode,
            model = runtime.Model,
            endpointPath = runtime.SubmitPath,
            domain = runtime.Domain,
            prompt = BuildReferencePrompt(),
            action_type = "create",
            sync = false,
            project_id = projectId,
            ratio,
            resolution,
            mode,
            category,
            num_outputs = 1,
            language,
            subjects = new[]
            {
                new
                {
                    url = characterUrl
                },
                new
                {
                    url = productUrl
                }
            },
            subjectOrder = new[]
            {
                characterUrl,
                productUrl
            }
        }, KieJson.Options);

        _logger.LogInformation("DANCE_SELL_79AI_REFERENCE_OUTBOUND_FORM payload={PayloadJson} formFields={FormFields}",
            requestJson,
            string.Join(",", formFieldNames));

        var submitted = await _client.SubmitAsync(submit, ct);
        return new ProviderTaskSubmitResult
        {
            ProviderCode = request.Route.ProviderCode,
            ModelName = runtime.Model,
            TaskId = submitted.TaskId,
            RequestJson = requestJson,
            ResponseJson = submitted.SanitizedResponseJson
        };
    }

    public async Task<KieTaskDetailResult> GetTaskAsync(DanceSellProviderRouteDto route, string taskId, CancellationToken ct)
    {
        var runtime = await ResolveRuntimeAsync(route, ct);
        var status = await _client.GetStatusAsync(new Ai79TaskStatusRequest(
            runtime.BaseUrl,
            runtime.PollPath,
            runtime.Credential.Secret,
            runtime.Domain,
            taskId,
            Ai79TaskOperation.Image), ct);

        return new KieTaskDetailResult
        {
            TaskId = taskId,
            ProviderState = status.NormalizedStatus,
            Status = status.NormalizedStatus switch
            {
                Ai79TaskStatusNormalizer.Success => KieTaskStatuses.Completed,
                Ai79TaskStatusNormalizer.Failed => KieTaskStatuses.Failed,
                _ => KieTaskStatuses.Rendering
            },
            ResultUrls = string.IsNullOrWhiteSpace(status.OutputUrl) ? Array.Empty<string>() : new[] { status.OutputUrl! },
            FailCode = status.ErrorCode,
            FailMsg = status.ErrorMessage,
            Model = runtime.Model,
            RawResponse = status.SanitizedResponseJson
        };
    }

    private async Task<Ai79ReferenceRuntime> ResolveRuntimeAsync(DanceSellProviderRouteDto route, CancellationToken ct)
    {
        var credential = await _credentials.ResolveAsync(route.ProviderCode, "access_token", ct);
        return new Ai79ReferenceRuntime(
            route.ProviderCode,
            route.ModelName,
            FirstNonBlank(ReadConfigString(route.ConfigJson, "base_url"), "https://api.gommo.net/ai")!,
            FirstNonBlank(ReadConfigString(route.ConfigJson, "domain"), "79ai.net")!,
            FirstNonBlank(ReadConfigString(route.ConfigJson, "submit_path"), "/generateImage")!,
            FirstNonBlank(ReadConfigString(route.ConfigJson, "poll_path"), "/image")!,
            credential);
    }

    private static string? ReadConfigString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(propertyName, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildReferencePrompt()
        => """
VIRTUAL TRY-ON – PREVIEW ONLY

Use IMAGE 1 as FIXED BASE BODY.
- Preserve exact body pose, limb angles, shoulder alignment, head tilt, camera angle
- Do NOT regenerate body, do NOT reinterpret pose
- Only replace clothing region

Apply clothing from IMAGE 2 with exact design, color, texture, pattern
- Clothing must conform to existing body pose
- No pose correction, no body adjustment, no camera shift

If conflict occurs between clothing and pose:
→ Prioritize BODY POSE from IMAGE 1 over clothing realism

Photorealistic, product preview quality.
""";

    private static string[] BuildGenerateImageFieldNames(IReadOnlyDictionary<string, string?> options)
        => new[] { "access_token", "domain", "model", "prompt" }
            .Concat(options.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => x.Key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed record Ai79ReferenceRuntime(
        string ProviderCode,
        string Model,
        string BaseUrl,
        string Domain,
        string SubmitPath,
        string PollPath,
        ResolvedProviderCredential Credential);
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
            var rows = await conn.QueryAsync<DanceSellProviderRouteDto>(
                """
                SELECT id AS Id, feature_code AS FeatureCode, operation_type AS OperationType,
                       provider_code AS ProviderCode, model_name AS ModelName, model_mode AS ModelMode,
                       route_priority AS Priority, is_default AS IsDefault, enabled AS Enabled,
                       fallback_on AS FallbackOn, config_json::text AS ConfigJson
                  FROM public.todox_ai_feature_provider_route
                 WHERE feature_code = @featureCode
                   AND operation_type = @operationType
                   AND enabled = true
                 ORDER BY is_default DESC, route_priority, provider_code, model_name;
                """,
                new { featureCode = DanceSellConstants.FeatureCode, operationType });
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
                ? DanceSellConstants.Ai79ReferenceModel
                : DanceSellConstants.Model,
            Priority = 100,
            Enabled = true,
            IsDefault = true,
            AllowUserSelect = true,
            FallbackOn = Array.Empty<string>(),
            ConfigJson = JsonSerializer.Serialize(new
            {
                source = "code_fallback_until_manual_sql_seeded",
                requiresManualSql = "database/manual/ai-operation-logs"
            }, KieJson.Options)
        };

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
    Task<DanceSellProviderOperationDto?> GetLatestActiveOperationAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default);
    Task<bool> HasActiveOperationAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default);
    Task MarkSubmittedAsync(Guid operationId, string providerTaskId, string responseJson, CancellationToken ct = default);
    Task<int> BeginMotionSubmitAttemptAsync(Guid operationId, string requestJson, CancellationToken ct = default);
    Task ResetMotionForRetryAsync(Guid operationId, Guid renderJobId, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid operationId, string providerStatus, string responseJson, decimal? creditsConsumed, string? resultUrl, CancellationToken ct = default);
    Task MarkFailedAsync(Guid operationId, string providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default);
    Task<AiOperationAssetDto?> GetLatestAssetAsync(Guid danceSellJobId, string operationType, string assetRole, Guid? mediaId, string? objectKey, CancellationToken ct = default);
    Task<AiOperationAssetDto?> GetLatestAssetForRenderJobAsync(Guid renderJobId, string assetRole, Guid? mediaId, string? objectKey, CancellationToken ct = default);
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
                DO UPDATE SET
                    render_job_id = COALESCE(
                        EXCLUDED.render_job_id,
                        dance_sell.dance_sell_provider_operations.render_job_id),
                    updated_at = now()
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
                SELECT COALESCE(MAX(attempt_no), 0) + 1
                  FROM dance_sell.dance_sell_provider_operations
                 WHERE dance_sell_job_id = @danceSellJobId
                   AND operation_type = @operationType;
                """,
                new { danceSellJobId, operationType });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
    }

    public async Task<DanceSellProviderOperationDto?> GetLatestActiveOperationAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<DanceSellProviderOperationDto>(
                """
                SELECT id AS Id, dance_sell_job_id AS DanceSellJobId, render_job_id AS RenderJobId,
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
                       refunded_at AS RefundedAt, updated_at AS UpdatedAt
                  FROM dance_sell.dance_sell_provider_operations
                 WHERE dance_sell_job_id = @danceSellJobId
                   AND operation_type = @operationType
                   AND status IN ('queued','submitted','generating')
                 ORDER BY attempt_no DESC, created_at DESC
                 LIMIT 1;
                """,
                new { danceSellJobId, operationType });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
    }

    public async Task<bool> HasActiveOperationAsync(Guid danceSellJobId, string operationType, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<bool>(
                """
                SELECT EXISTS (
                    SELECT 1
                      FROM dance_sell.dance_sell_provider_operations
                     WHERE dance_sell_job_id = @danceSellJobId
                       AND operation_type = @operationType
                       AND status IN ('queued','submitted','generating')
                );
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

    public async Task<int> BeginMotionSubmitAttemptAsync(Guid operationId, string requestJson, CancellationToken ct = default)
    {
        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.ExecuteScalarAsync<int>(
                """
                WITH next_attempt AS (
                    SELECT COALESCE(NULLIF(request_json->>'submitAttempt', '')::int, 0) + 1 AS attempt_no
                      FROM dance_sell.dance_sell_provider_operations
                     WHERE id=@operationId
                )
                UPDATE dance_sell.dance_sell_provider_operations o
                   SET request_json=jsonb_set(
                           CAST(@requestJson AS jsonb),
                           '{submitAttempt}',
                           to_jsonb(next_attempt.attempt_no),
                           true),
                       updated_at=now()
                  FROM next_attempt
                 WHERE o.id=@operationId
                RETURNING (o.request_json->>'submitAttempt')::int;
                """,
                new { operationId, requestJson = KieJsonRedactor.Redact(requestJson) ?? "{}" });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
    }

    public async Task ResetMotionForRetryAsync(Guid operationId, Guid renderJobId, CancellationToken ct = default)
    {
        await ExecuteOptionalAsync(
            """
            UPDATE dance_sell.dance_sell_provider_operations
               SET render_job_id=@renderJobId,
                   provider_task_id=NULL,
                   status='queued',
                   provider_status=NULL,
                   response_json=NULL,
                   callback_json=NULL,
                   error_json=NULL,
                   provider_usage_json=NULL,
                   error_code=NULL,
                   error_message=NULL,
                   started_at=now(),
                   submitted_at=NULL,
                   completed_at=NULL,
                   failed_at=NULL,
                   request_json='{}'::jsonb,
                   updated_at=now()
             WHERE id=@operationId;
            """,
            new { operationId, renderJobId },
            ct);
    }

    public async Task<AiOperationAssetDto?> GetLatestAssetAsync(Guid danceSellJobId, string operationType, string assetRole, Guid? mediaId, string? objectKey, CancellationToken ct = default)
    {
        if (mediaId is null && string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<AiOperationAssetDto>(
                """
                SELECT a.id AS Id, a.operation_id AS OperationId, a.asset_role AS AssetRole,
                       a.media_id AS MediaId, a.object_key AS ObjectKey, a.public_url AS PublicUrl,
                       a.provider_url AS ProviderUrl, a.mime_type AS MimeType,
                       a.metadata_json::text AS MetadataJson, a.created_at AS CreatedAt
                  FROM public.todox_ai_operation_assets a
                  JOIN dance_sell.dance_sell_provider_operations o ON o.id = a.operation_id
                 WHERE o.dance_sell_job_id = @danceSellJobId
                   AND o.operation_type = @operationType
                   AND a.asset_role = @assetRole
                   AND COALESCE(a.provider_url, '') <> ''
                   AND (@mediaId IS NULL OR a.media_id = @mediaId)
                   AND (@objectKey IS NULL OR a.object_key = @objectKey)
                 ORDER BY a.created_at DESC
                 LIMIT 1;
                """,
                new { danceSellJobId, operationType, assetRole, mediaId, objectKey = string.IsNullOrWhiteSpace(objectKey) ? null : objectKey.Trim() });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
    }

    public async Task<AiOperationAssetDto?> GetLatestAssetForRenderJobAsync(
        Guid renderJobId,
        string assetRole,
        Guid? mediaId,
        string? objectKey,
        CancellationToken ct = default)
    {
        if (mediaId is null && string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        try
        {
            using var conn = await _factory.OpenAsync(ct);
            return await conn.QuerySingleOrDefaultAsync<AiOperationAssetDto>(
                """
                SELECT a.id AS Id, a.operation_id AS OperationId, a.asset_role AS AssetRole,
                       a.media_id AS MediaId, a.object_key AS ObjectKey, a.public_url AS PublicUrl,
                       a.provider_url AS ProviderUrl, a.mime_type AS MimeType,
                       a.metadata_json::text AS MetadataJson, a.created_at AS CreatedAt
                  FROM public.todox_ai_operation_assets a
                  JOIN dance_sell.dance_sell_provider_operations o ON o.id = a.operation_id
                 WHERE o.render_job_id = @renderJobId
                   AND a.asset_role = @assetRole
                   AND COALESCE(a.provider_url, '') <> ''
                   AND COALESCE(a.metadata_json->>'verificationMatched', 'false') = 'true'
                   AND (@mediaId IS NULL OR a.media_id = @mediaId)
                   AND (@objectKey IS NULL OR a.object_key = @objectKey)
                 ORDER BY a.created_at DESC
                 LIMIT 1;
                """,
                new
                {
                    renderJobId,
                    assetRole,
                    mediaId,
                    objectKey = string.IsNullOrWhiteSpace(objectKey) ? null : objectKey.Trim()
                });
        }
        catch (PostgresException ex) when (IsSchemaMissing(ex))
        {
            throw SchemaNotReady(ex);
        }
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
            throw SchemaNotReady(ex);
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
    private readonly IAiPricingService _pricing;

    public DanceSellCostEstimator(IConfiguration configuration, IAiPricingService pricing)
    {
        _configuration = configuration;
        _pricing = pricing;
    }

    public async Task<DanceSellCostEstimate> EstimateAsync(DanceSellProviderRouteDto route, string mode, TimeSpan? duration, CancellationToken ct = default)
    {
        var estimatedUsage = duration is null ? 1 : Math.Max(1, (decimal)duration.Value.TotalSeconds);
        EstimateCostResponseDto? catalogEstimate = null;
        try
        {
            catalogEstimate = await _pricing.EstimateAsync(new EstimateCostRequestDto
            {
                ProviderCode = route.ProviderCode,
                ProviderModelCode = route.ModelName,
                Mode = mode,
                DurationSeconds = duration is null ? null : (int)Math.Ceiling(duration.Value.TotalSeconds),
                Quantity = estimatedUsage
            }, ct);
        }
        catch (Exception ex)
        {
            catalogEstimate = new EstimateCostResponseDto
            {
                Success = false,
                ErrorCode = ex.GetType().Name,
                Message = ex.Message
            };
        }

        if (catalogEstimate.Success)
        {
            var matched = catalogEstimate.MatchedPrice;
            return new DanceSellCostEstimate
            {
                OperationType = route.OperationType,
                ProviderCode = route.ProviderCode,
                ModelName = route.ModelName,
                ProviderMode = mode,
                UsageUnit = matched?.UnitType ?? "request",
                PricingUnit = matched?.RateType ?? matched?.UnitType,
                EstimatedUsage = estimatedUsage,
                ProviderUnitPrice = matched?.ProviderPrice,
                EstimatedProviderCost = catalogEstimate.ProviderTotalCost,
                Currency = "USD",
                EstimatedTodoxPoints = catalogEstimate.EstimatedTodoXPoints,
                PricingSource = "provider_catalog",
                Warning = matched?.RateType?.Equals("per_second", StringComparison.OrdinalIgnoreCase) == true && duration is null
                    ? $"Da tim thay pricing {route.ProviderCode}/{route.ModelName}/{mode} theo per_second nhung chua co duration metadata; dang uoc tinh 1 giay."
                    : null
            };
        }

        using var configDoc = TryParseJson(route.ConfigJson);
        var pricingUnit = ReadString(configDoc, "pricingUnit")
                          ?? ReadString(configDoc, "pricing_unit")
                          ?? ReadString(configDoc, "usageUnit")
                          ?? "request";
        estimatedUsage = ReadDecimal(configDoc, "estimatedUsage")
                         ?? ReadDecimal(configDoc, "estimated_usage")
                         ?? pricingUnit switch
                         {
                             "fixed" => 0,
                             "video_second" or "second" or "per_second" when duration is not null => (decimal)duration.Value.TotalSeconds,
                             _ => estimatedUsage
                         };
        var unitPrice = ReadDecimal(configDoc, "providerUnitPrice")
                        ?? ReadDecimal(configDoc, "provider_unit_price")
                        ?? ReadDecimal(configDoc, "usdPerRequest")
                        ?? ReadDecimal($"DanceSell:Pricing:{route.ProviderCode}:{route.ModelName}:{mode}:UsdPerRequest")
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

        return new DanceSellCostEstimate
        {
            OperationType = route.OperationType,
            ProviderCode = route.ProviderCode,
            ModelName = route.ModelName,
            ProviderMode = mode,
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
            Warning = unitPrice is null
                ? $"Chua tim thay pricing provider/catalog hoac config cho {route.ProviderCode}/{route.ModelName}/{mode}; catalogError={catalogEstimate?.ErrorCode ?? catalogEstimate?.Message ?? "unknown"}."
                : null
        };
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
        => providerCode.Equals(DanceSellConstants.KieProviderCode, StringComparison.OrdinalIgnoreCase);

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
