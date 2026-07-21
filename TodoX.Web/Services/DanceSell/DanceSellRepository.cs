using Dapper;
using System.Text.Json;
using TodoX.Web.Data;

namespace TodoX.Web.Services.DanceSell;

public interface IDanceSellRepository
{
    Task<DanceSellJobDto> CreateDraftAsync(DanceSellDraftCreateRequest request, CancellationToken ct = default);
    Task<DanceSellJobDto> CreateAsync(DanceSellJobCreateRequest request, CancellationToken ct = default);
    Task<DanceSellJobDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DanceSellJobDto>> ListAsync(Guid? customerId = null, int limit = 20, int offset = 0, CancellationToken ct = default);
    Task<DanceSellJobDto?> GetByRenderJobIdAsync(Guid renderJobId, CancellationToken ct = default);
    Task<DanceSellJobDto?> GetByProviderTaskIdAsync(string providerTaskId, CancellationToken ct = default);
    Task SetRenderJobIdAsync(Guid id, Guid renderJobId, CancellationToken ct = default);
    Task QueueForRenderAsync(Guid id, Guid renderJobId, string logicalRequestId, string preparedReferenceUrl, string motionVideoUrl, CancellationToken ct = default);
    Task UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CancellationToken ct = default);
    Task UpdateCharacterAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default);
    Task UpdateProductAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default);
    Task UpdateMotionUploadAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default);
    Task UpdateMotionTikTokAsync(Guid id, string sourceUrl, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default);
    Task UpdateReferenceStatusAsync(Guid id, string status, string? error = null, Guid? mediaId = null, string? objectKey = null, string? publicUrl = null, DateTime? approvedAt = null, CancellationToken ct = default);
    Task<IReadOnlyList<DanceSellReferenceVersionDto>> ListReferenceVersionsAsync(Guid danceSellJobId, CancellationToken ct = default);
    Task<DanceSellReferenceVersionDto?> GetReferenceVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<DanceSellReferenceVersionDto> CreateReferenceVersionAsync(DanceSellReferenceVersionDto version, CancellationToken ct = default);
    Task<bool> SelectReferenceVersionAsync(Guid danceSellJobId, Guid versionId, CancellationToken ct = default);
    Task UpdateSubmittedAsync(Guid id, string requestJson, string providerTaskId, string submitResponseJson, CancellationToken ct = default);
    Task UpdatePollingAsync(Guid id, string providerStatus, string pollResponseJson, int pollCount, DateTime nextPollAtUtc, CancellationToken ct = default);
    Task<bool> UpdateCompletedAsync(Guid id, string providerStatus, string pollResponseJson, string resultVideoUrl, CancellationToken ct = default);
    Task<bool> UpdateFailedAsync(Guid id, string status, string? providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default);
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

    public async Task<DanceSellJobDto> CreateDraftAsync(DanceSellDraftCreateRequest request, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleAsync<DanceSellJobDto>(
            """
            INSERT INTO dance_sell.dance_sell_jobs
                (tenant_id, customer_id, user_id, logical_request_id, title, prompt,
                 character_image_url, motion_video_url, mode, character_orientation,
                 placement_mode, custom_placement_instruction, status, request_json, created_by, updated_by,
                 created_at, updated_at)
            VALUES
                (@tenant, @customer, @user, @logicalRequestId, @title, @prompt,
                 '', '', @mode, @orientation,
                 @placementMode, @customInstruction, 'draft', '{}'::jsonb, @user, @user,
                 now(), now())
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
                      error_code AS ErrorCode, error_message AS ErrorMessage,
                      title AS Title, character_media_id AS CharacterMediaId, character_object_key AS CharacterObjectKey,
                      product_media_id AS ProductMediaId, product_object_key AS ProductObjectKey, product_image_url AS ProductImageUrl,
                      motion_source_type AS MotionSourceType, motion_source_url AS MotionSourceUrl,
                      motion_video_media_id AS MotionVideoMediaId, motion_video_object_key AS MotionVideoObjectKey,
                      placement_mode AS PlacementMode, custom_placement_instruction AS CustomPlacementInstruction,
                      prepared_reference_media_id AS PreparedReferenceMediaId, prepared_reference_object_key AS PreparedReferenceObjectKey,
                      prepared_reference_url AS PreparedReferenceUrl, prepared_reference_status AS PreparedReferenceStatus,
                      prepared_reference_approved_at AS PreparedReferenceApprovedAt, source_stage_status AS SourceStageStatus,
                      source_stage_error AS SourceStageError, created_by AS CreatedBy, updated_by AS UpdatedBy;
            """,
            new
            {
                tenant = request.TenantId ?? _tenant.TenantId,
                customer = request.CustomerId,
                user = request.UserId,
                logicalRequestId = $"dance-sell-{Guid.NewGuid():N}",
                title = NormalizeTitle(request.Title),
                prompt = request.Prompt.Trim(),
                mode = request.Mode.Trim(),
                orientation = request.CharacterOrientation.Trim(),
                placementMode = request.PlacementMode.Trim(),
                customInstruction = string.IsNullOrWhiteSpace(request.CustomPlacementInstruction) ? null : request.CustomPlacementInstruction.Trim()
            });
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

    public async Task<IReadOnlyList<DanceSellJobDto>> ListAsync(Guid? customerId = null, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<DanceSellJobDto>(
            SelectSql + """
             WHERE (@customerId IS NULL OR customer_id = @customerId)
             ORDER BY created_at DESC
             LIMIT @limit OFFSET @offset;
            """,
            new { customerId, limit = Math.Clamp(limit, 1, 100), offset = Math.Max(0, offset) });
        return rows.ToList();
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

    public async Task QueueForRenderAsync(Guid id, Guid renderJobId, string logicalRequestId, string preparedReferenceUrl, string motionVideoUrl, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET status='queued',
                   render_job_id=@renderJobId,
                   logical_request_id=@logicalRequestId,
                   character_image_url=@preparedReferenceUrl,
                   motion_video_url=@motionVideoUrl,
                   provider_task_id=NULL,
                   provider_status=NULL,
                   submitted_at=NULL,
                   last_polled_at=NULL,
                   completed_at=NULL,
                   error_code=NULL,
                   error_message=NULL,
                   error_json=NULL,
                   updated_at=now()
             WHERE id=@id
               AND status NOT IN ('submitted','rendering','completed');
            """,
            new { id, renderJobId, logicalRequestId, preparedReferenceUrl, motionVideoUrl });
    }

    public async Task UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET title=@title,
                   prompt=@prompt,
                   placement_mode=@placementMode,
                   custom_placement_instruction=@customInstruction,
                   mode=@mode,
                   character_orientation=@orientation,
                   updated_at=now()
             WHERE id=@id;
            """,
            new
            {
                id,
                title = NormalizeTitle(request.Title),
                prompt = request.Prompt.Trim(),
                placementMode = request.PlacementMode.Trim(),
                customInstruction = string.IsNullOrWhiteSpace(request.CustomPlacementInstruction) ? null : request.CustomPlacementInstruction.Trim(),
                mode = request.Mode.Trim(),
                orientation = request.CharacterOrientation.Trim()
            });
    }

    public async Task UpdateCharacterAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default)
        => await UpdateMediaAsync(id, "character_media_id", mediaId, "character_object_key", objectKey, "character_image_url", publicUrl, ct);

    public async Task UpdateProductAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default)
        => await UpdateMediaAsync(id, "product_media_id", mediaId, "product_object_key", objectKey, "product_image_url", publicUrl, ct);

    public async Task UpdateMotionUploadAsync(Guid id, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default)
        => await UpdateMotionAsync(id, DanceSellMotionSourceTypes.Upload, publicUrl, mediaId, objectKey, publicUrl, ct);

    public async Task UpdateMotionTikTokAsync(Guid id, string sourceUrl, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct = default)
        => await UpdateMotionAsync(id, DanceSellMotionSourceTypes.TikTok, sourceUrl, mediaId, objectKey, publicUrl, ct);

    public async Task UpdateReferenceStatusAsync(Guid id, string status, string? error = null, Guid? mediaId = null, string? objectKey = null, string? publicUrl = null, DateTime? approvedAt = null, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET prepared_reference_status=@status,
                   source_stage_error=@error,
                   prepared_reference_media_id=COALESCE(@mediaId, prepared_reference_media_id),
                   prepared_reference_object_key=COALESCE(@objectKey, prepared_reference_object_key),
                   prepared_reference_url=COALESCE(@publicUrl, prepared_reference_url),
                   prepared_reference_approved_at=COALESCE(@approvedAt, prepared_reference_approved_at),
                   updated_at=now()
             WHERE id=@id;
            """,
            new { id, status, error, mediaId, objectKey, publicUrl, approvedAt });
    }

    public async Task<IReadOnlyList<DanceSellReferenceVersionDto>> ListReferenceVersionsAsync(Guid danceSellJobId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<DanceSellReferenceVersionDto>(
            """
            SELECT id AS Id, dance_sell_job_id AS DanceSellJobId, version_no AS VersionNo,
                   character_media_id AS CharacterMediaId, product_media_id AS ProductMediaId,
                   placement_mode AS PlacementMode, custom_instruction AS CustomInstruction, prompt AS Prompt,
                   provider_code AS ProviderCode, provider_model AS ProviderModel, request_json::text AS RequestJson,
                   response_json::text AS ResponseJson, error_json::text AS ErrorJson, media_id AS MediaId,
                   object_key AS ObjectKey, public_url AS PublicUrl, status AS Status, is_selected AS IsSelected,
                   created_by AS CreatedBy, created_at AS CreatedAt, completed_at AS CompletedAt
              FROM dance_sell.dance_sell_reference_versions
             WHERE dance_sell_job_id=@danceSellJobId
             ORDER BY version_no DESC;
            """,
            new { danceSellJobId });
        return rows.ToList();
    }

    public async Task<DanceSellReferenceVersionDto?> GetReferenceVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<DanceSellReferenceVersionDto>(
            """
            SELECT id AS Id, dance_sell_job_id AS DanceSellJobId, version_no AS VersionNo,
                   character_media_id AS CharacterMediaId, product_media_id AS ProductMediaId,
                   placement_mode AS PlacementMode, custom_instruction AS CustomInstruction, prompt AS Prompt,
                   provider_code AS ProviderCode, provider_model AS ProviderModel, request_json::text AS RequestJson,
                   response_json::text AS ResponseJson, error_json::text AS ErrorJson, media_id AS MediaId,
                   object_key AS ObjectKey, public_url AS PublicUrl, status AS Status, is_selected AS IsSelected,
                   created_by AS CreatedBy, created_at AS CreatedAt, completed_at AS CompletedAt
              FROM dance_sell.dance_sell_reference_versions
             WHERE id=@versionId;
            """,
            new { versionId });
    }

    public async Task<DanceSellReferenceVersionDto> CreateReferenceVersionAsync(DanceSellReferenceVersionDto version, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleAsync<DanceSellReferenceVersionDto>(
            """
            INSERT INTO dance_sell.dance_sell_reference_versions
                (id, dance_sell_job_id, version_no, character_media_id, product_media_id, placement_mode,
                 custom_instruction, prompt, provider_code, provider_model, request_json, response_json,
                 error_json, media_id, object_key, public_url, status, is_selected, created_by, created_at, completed_at)
            VALUES
                (@Id, @DanceSellJobId, @VersionNo, @CharacterMediaId, @ProductMediaId, @PlacementMode,
                 @CustomInstruction, @Prompt, @ProviderCode, @ProviderModel, CAST(@RequestJson AS jsonb),
                 CAST(@ResponseJson AS jsonb), CAST(@ErrorJson AS jsonb), @MediaId, @ObjectKey, @PublicUrl,
                 @Status, @IsSelected, @CreatedBy, @CreatedAt, @CompletedAt)
            RETURNING id AS Id, dance_sell_job_id AS DanceSellJobId, version_no AS VersionNo,
                      character_media_id AS CharacterMediaId, product_media_id AS ProductMediaId,
                      placement_mode AS PlacementMode, custom_instruction AS CustomInstruction, prompt AS Prompt,
                      provider_code AS ProviderCode, provider_model AS ProviderModel, request_json::text AS RequestJson,
                      response_json::text AS ResponseJson, error_json::text AS ErrorJson, media_id AS MediaId,
                      object_key AS ObjectKey, public_url AS PublicUrl, status AS Status, is_selected AS IsSelected,
                      created_by AS CreatedBy, created_at AS CreatedAt, completed_at AS CompletedAt;
            """,
            version);
    }

    public async Task<bool> SelectReferenceVersionAsync(Guid danceSellJobId, Guid versionId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_reference_versions
               SET is_selected = CASE WHEN id = @versionId THEN true ELSE false END
             WHERE dance_sell_job_id = @danceSellJobId;
            """,
            new { danceSellJobId, versionId });
        return changed > 0;
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

    public const string UpdateFailedSql =
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
        """;

    public async Task<bool> UpdateFailedAsync(Guid id, string status, string? providerStatus, string? responseJson, string errorCode, string errorMessage, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var changed = await conn.ExecuteAsync(
            UpdateFailedSql,
            new { id, status, providerStatus, responseJson, errorCode, errorMessage });
        return changed > 0;
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

    private async Task UpdateMotionAsync(Guid id, string sourceType, string sourceUrl, Guid mediaId, string objectKey, string publicUrl, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE dance_sell.dance_sell_jobs
               SET motion_source_type=@sourceType,
                   motion_source_url=@sourceUrl,
                   motion_video_media_id=@mediaId,
                   motion_video_object_key=@objectKey,
                   motion_video_url=@publicUrl,
                   source_stage_status='ready',
                   source_stage_error=NULL,
                   updated_at=now()
             WHERE id=@id;
            """,
            new { id, sourceType, sourceUrl, mediaId, objectKey, publicUrl });
    }

    private static string NormalizeTitle(string? title)
        => string.IsNullOrWhiteSpace(title) ? $"Dance Sell {DateTime.UtcNow:yyyyMMddHHmmss}" : title.Trim();

    private async Task UpdateMediaAsync(Guid id, string mediaColumn, Guid mediaId, string keyColumn, string objectKey, string urlColumn, string publicUrl, CancellationToken ct)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE dance_sell.dance_sell_jobs
               SET {mediaColumn}=@mediaId,
                   {keyColumn}=@objectKey,
                   {urlColumn}=@publicUrl,
                   updated_at=now()
             WHERE id=@id;
            """,
            new { id, mediaId, objectKey, publicUrl });
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
               error_code AS ErrorCode, error_message AS ErrorMessage,
               title AS Title, character_media_id AS CharacterMediaId, character_object_key AS CharacterObjectKey,
               product_media_id AS ProductMediaId, product_object_key AS ProductObjectKey, product_image_url AS ProductImageUrl,
               motion_source_type AS MotionSourceType, motion_source_url AS MotionSourceUrl,
               motion_video_media_id AS MotionVideoMediaId, motion_video_object_key AS MotionVideoObjectKey,
               placement_mode AS PlacementMode, custom_placement_instruction AS CustomPlacementInstruction,
               prepared_reference_media_id AS PreparedReferenceMediaId, prepared_reference_object_key AS PreparedReferenceObjectKey,
               prepared_reference_url AS PreparedReferenceUrl, prepared_reference_status AS PreparedReferenceStatus,
               prepared_reference_approved_at AS PreparedReferenceApprovedAt, source_stage_status AS SourceStageStatus,
               source_stage_error AS SourceStageError, created_by AS CreatedBy, updated_by AS UpdatedBy
          FROM dance_sell.dance_sell_jobs
        """;

    public static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
