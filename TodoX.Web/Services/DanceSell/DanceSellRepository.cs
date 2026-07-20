using Dapper;
using System.Text.Json;
using TodoX.Web.Data;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellRepository
{
    Task<DanceSellJobDto> CreateAsync(DanceSellJobCreateRequest request, CancellationToken ct = default);
    Task<DanceSellJobDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DanceSellJobDto?> GetByRenderJobIdAsync(Guid renderJobId, CancellationToken ct = default);
    Task<DanceSellJobDto?> GetByProviderTaskIdAsync(string providerTaskId, CancellationToken ct = default);
    Task SetRenderJobIdAsync(Guid id, Guid renderJobId, CancellationToken ct = default);
    Task UpdateSubmittedAsync(Guid id, string requestJson, string providerTaskId, string submitResponseJson, CancellationToken ct = default);
    Task UpdatePollingAsync(Guid id, string providerStatus, string pollResponseJson, int pollCount, DateTime nextPollAtUtc, CancellationToken ct = default);
    Task<bool> UpdateCompletedAsync(Guid id, string providerStatus, string pollResponseJson, string resultVideoUrl, CancellationToken ct = default);
    Task UpdateFailedAsync(Guid id, string status, string? providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default);
    Task UpdateCallbackAsync(string providerTaskId, string callbackJson, string providerStatus, string? resultVideoUrl, string? errorCode, string? errorMessage, CancellationToken ct = default);
}

public sealed class DanceSellRepository : IDanceSellRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public DanceSellRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<DanceSellJobDto> CreateAsync(DanceSellJobCreateRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        var id = Guid.NewGuid();
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleAsync<DanceSellJobDto>(
            """
            INSERT INTO dance_sell.dance_sell_jobs
                (id, tenant_id, customer_id, user_id, render_job_id, logical_request_id, status,
                 prompt, character_image_url, motion_video_url, mode, character_orientation,
                 provider_code, provider_model, request_json, created_at, updated_at)
            VALUES
                (@id, @tenant, @customer, @user, @renderJobId, @logicalRequestId, 'queued',
                 @prompt, @characterImageUrl, @motionVideoUrl, @mode, @orientation,
                 @providerCode, @providerModel, '{}'::jsonb, now(), now())
            RETURNING id AS Id, tenant_id AS TenantId, customer_id AS CustomerId, user_id AS UserId,
                      render_job_id AS RenderJobId, logical_request_id AS LogicalRequestId,
                      status AS Status, prompt AS Prompt, character_image_url AS CharacterImageUrl,
                      motion_video_url AS MotionVideoUrl, mode AS Mode, character_orientation AS CharacterOrientation,
                      provider_code AS ProviderCode, provider_model AS ProviderModel,
                      provider_task_id AS ProviderTaskId, provider_status AS ProviderStatus,
                      request_json::text AS RequestJson, submit_response_json::text AS SubmitResponseJson,
                      poll_response_json::text AS PollResponseJson, callback_json::text AS CallbackJson,
                      error_json::text AS ErrorJson, result_video_url AS ResultVideoUrl,
                      poll_count AS PollCount, next_poll_at AS NextPollAt, submitted_at AS SubmittedAt,
                      last_polled_at AS LastPolledAt, completed_at AS CompletedAt,
                      created_at AS CreatedAt, updated_at AS UpdatedAt,
                      error_code AS ErrorCode, error_message AS ErrorMessage;
            """,
            new
            {
                id,
                tenant = request.TenantId ?? _tenant.TenantId,
                customer = request.CustomerId,
                user = request.UserId,
                renderJobId = request.RenderJobId,
                logicalRequestId = request.LogicalRequestId,
                prompt = request.Prompt.Trim(),
                characterImageUrl = request.CharacterImageUrl.Trim(),
                motionVideoUrl = request.MotionVideoUrl.Trim(),
                mode = request.Mode.Trim(),
                orientation = request.CharacterOrientation.Trim(),
                providerCode = request.ProviderCode,
                providerModel = request.ProviderModel
            });
    }

    public async Task<DanceSellJobDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DanceSellJobDto>(SelectSql + " WHERE id=@id;", new { id });
    }

    public async Task<DanceSellJobDto?> GetByRenderJobIdAsync(Guid renderJobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DanceSellJobDto>(SelectSql + " WHERE render_job_id=@renderJobId;", new { renderJobId });
    }

    public async Task<DanceSellJobDto?> GetByProviderTaskIdAsync(string providerTaskId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DanceSellJobDto>(
            SelectSql + " WHERE provider_task_id=@providerTaskId ORDER BY created_at DESC LIMIT 1;",
            new { providerTaskId });
    }

    public async Task SetRenderJobIdAsync(Guid id, Guid renderJobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE dance_sell.dance_sell_jobs SET render_job_id=@renderJobId, updated_at=now() WHERE id=@id AND render_job_id IS NULL;",
            new { id, renderJobId });
    }

    public async Task UpdateSubmittedAsync(Guid id, string requestJson, string providerTaskId, string submitResponseJson, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET status='submitted',
                   provider_task_id=COALESCE(provider_task_id, @providerTaskId),
                   provider_status='submitted',
                   request_json=CAST(@requestJson AS jsonb),
                   submit_response_json=CAST(@submitResponseJson AS jsonb),
                   submitted_at=COALESCE(submitted_at, now()),
                   updated_at=now()
             WHERE id=@id
               AND status NOT IN ('completed','failed','timeout');
            """,
            new { id, providerTaskId, requestJson, submitResponseJson });
    }

    public async Task UpdatePollingAsync(Guid id, string providerStatus, string pollResponseJson, int pollCount, DateTime nextPollAtUtc, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET status='rendering',
                   provider_status=@providerStatus,
                   poll_response_json=CAST(@pollResponseJson AS jsonb),
                   poll_count=@pollCount,
                   next_poll_at=@nextPollAtUtc,
                   last_polled_at=now(),
                   updated_at=now()
             WHERE id=@id
               AND status NOT IN ('completed','failed','timeout');
            """,
            new { id, providerStatus, pollResponseJson, pollCount, nextPollAtUtc });
    }

    public const string UpdateCompletedSql =
        """
        UPDATE dance_sell.dance_sell_jobs
           SET status='completed',
               provider_status=@providerStatus,
               poll_response_json=CAST(@pollResponseJson AS jsonb),
               result_video_url=COALESCE(result_video_url, @resultVideoUrl),
               last_polled_at=now(),
               completed_at=COALESCE(completed_at, now()),
               updated_at=now()
         WHERE id=@id
           AND status NOT IN ('completed','failed','timeout');
        """;

    public async Task<bool> UpdateCompletedAsync(Guid id, string providerStatus, string pollResponseJson, string resultVideoUrl, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            UpdateCompletedSql,
            new { id, providerStatus, pollResponseJson, resultVideoUrl });
        return changed > 0;
    }

    public async Task UpdateFailedAsync(Guid id, string status, string? providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET status=@status,
                   provider_status=COALESCE(@providerStatus, provider_status),
                   error_json=CASE WHEN @responseJson IS NULL THEN error_json ELSE CAST(@responseJson AS jsonb) END,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE id=@id
               AND status NOT IN ('completed','failed','timeout');
            """,
            new { id, status, providerStatus, responseJson, errorCode, errorMessage });
    }

    public async Task UpdateCallbackAsync(string providerTaskId, string callbackJson, string providerStatus, string? resultVideoUrl, string? errorCode, string? errorMessage, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET callback_json=CAST(@callbackJson AS jsonb),
                   provider_status=@providerStatus,
                   result_video_url=COALESCE(result_video_url, @resultVideoUrl),
                   status = CASE
                       WHEN status IN ('completed','failed','timeout') THEN status
                       WHEN @resultVideoUrl IS NOT NULL THEN 'completed'
                       WHEN @errorCode IS NOT NULL THEN 'failed'
                       ELSE status
                   END,
                   error_code=COALESCE(error_code, @errorCode),
                   error_message=COALESCE(error_message, @errorMessage),
                   completed_at = CASE WHEN (@resultVideoUrl IS NOT NULL OR @errorCode IS NOT NULL) THEN COALESCE(completed_at, now()) ELSE completed_at END,
                   updated_at=now()
             WHERE provider_task_id=@providerTaskId;
            """,
            new { providerTaskId, callbackJson, providerStatus, resultVideoUrl, errorCode, errorMessage });
    }

    private const string SelectSql =
        """
        SELECT id AS Id, tenant_id AS TenantId, customer_id AS CustomerId, user_id AS UserId,
               render_job_id AS RenderJobId, logical_request_id AS LogicalRequestId,
               status AS Status, prompt AS Prompt, character_image_url AS CharacterImageUrl,
               motion_video_url AS MotionVideoUrl, mode AS Mode, character_orientation AS CharacterOrientation,
               provider_code AS ProviderCode, provider_model AS ProviderModel,
               provider_task_id AS ProviderTaskId, provider_status AS ProviderStatus,
               request_json::text AS RequestJson, submit_response_json::text AS SubmitResponseJson,
               poll_response_json::text AS PollResponseJson, callback_json::text AS CallbackJson,
               error_json::text AS ErrorJson, result_video_url AS ResultVideoUrl,
               poll_count AS PollCount, next_poll_at AS NextPollAt, submitted_at AS SubmittedAt,
               last_polled_at AS LastPolledAt, completed_at AS CompletedAt,
               created_at AS CreatedAt, updated_at AS UpdatedAt,
               error_code AS ErrorCode, error_message AS ErrorMessage
          FROM dance_sell.dance_sell_jobs
        """;

    public static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
