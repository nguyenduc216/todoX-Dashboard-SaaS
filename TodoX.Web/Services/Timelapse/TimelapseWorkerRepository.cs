using System.Data;
using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models.Timelapse;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseWorkerRepository
{
    Task<TimelapseImageWorkItem?> ClaimImageAsync(string workerKey, TimeSpan claimFor, CancellationToken ct = default);
    Task<TimelapseVideoWorkItem?> ClaimVideoAsync(string workerKey, TimeSpan claimFor, CancellationToken ct = default);
    Task<TimelapseFinalizerWorkItem?> ClaimFinalizerAsync(string workerKey, TimeSpan claimFor, CancellationToken ct = default);
    Task SaveImageSubmittedAsync(Guid stageId, int attempt, string providerCode, string model, string taskId, string requestJson, string responseJson, CancellationToken ct = default);
    Task SaveVideoSubmittedAsync(Guid clipId, int attempt, string providerCode, string model, string taskId, string requestJson, string responseJson, CancellationToken ct = default);
    Task SaveImageCompletedAsync(Guid stageId, int attempt, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct = default);
    Task SaveVideoCompletedAsync(Guid clipId, int attempt, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct = default);
    Task SaveFinalizerCompletedAsync(Guid finalOutputId, Guid jobId, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct = default);
    Task SaveImageSubmitFailedAsync(Guid stageId, int attempt, string providerCode, string model, string? errorCode, string errorMessage, string requestJson, string responseJson, CancellationToken ct = default);
    Task SaveVideoSubmitFailedAsync(Guid clipId, int attempt, string providerCode, string model, string? errorCode, string errorMessage, string requestJson, string responseJson, CancellationToken ct = default);
    Task SaveImageFailedAsync(Guid stageId, int attempt, string? errorCode, string errorMessage, string responseJson, CancellationToken ct = default);
    Task SaveVideoFailedAsync(Guid clipId, int attempt, string? errorCode, string errorMessage, string responseJson, CancellationToken ct = default);
    Task SaveFinalizerFailedAsync(Guid finalOutputId, Guid jobId, string? errorCode, string errorMessage, string responseJson, CancellationToken ct = default);
    Task ReleaseImageClaimAsync(Guid stageId, int attempt, CancellationToken ct = default);
    Task ReleaseVideoClaimAsync(Guid clipId, int attempt, CancellationToken ct = default);
    Task AdvanceAfterImageCompletedAsync(Guid jobId, CancellationToken ct = default);
    Task AdvanceAfterVideoCompletedAsync(Guid jobId, CancellationToken ct = default);
}

public sealed class TimelapseWorkerRepository : ITimelapseWorkerRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public TimelapseWorkerRepository(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<TimelapseImageWorkItem?> ClaimImageAsync(string workerKey, TimeSpan claimFor, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var row = await conn.QuerySingleOrDefaultAsync<ImageRow>(
            """
            WITH candidate AS (
                SELECT s.id
                  FROM timelapse.timelapse_image_stages s
                  JOIN timelapse.timelapse_image_stage_versions v
                    ON v.image_stage_id=s.id
                   AND v.attempt=s.active_attempt
                 WHERE s.tenant_id=@tenant
                   AND s.status='RENDERING'
                   AND v.status='RENDERING'
                   AND COALESCE((v.request_json->'worker_claim'->>'until')::timestamptz, '-infinity'::timestamptz) <= now()
                 ORDER BY s.started_at NULLS FIRST, s.stage_index
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
            )
            UPDATE timelapse.timelapse_image_stage_versions v
               SET request_json=jsonb_set(
                       COALESCE(v.request_json, '{}'::jsonb),
                       '{worker_claim}',
                       jsonb_build_object('worker', @workerKey, 'until', (now() + @claimFor::interval)),
                       true),
                   updated_at=now()
              FROM timelapse.timelapse_image_stages s
              JOIN render.render_jobs j ON j.id=s.job_id
              LEFT JOIN timelapse.timelapse_image_stages d
                ON d.job_id=s.job_id
               AND d.progress_percent=s.depends_on_progress_percent
              JOIN candidate c ON c.id=s.id
             WHERE v.image_stage_id=s.id
               AND v.attempt=s.active_attempt
             RETURNING s.id AS Id,
                       s.tenant_id AS TenantId,
                       s.job_id AS JobId,
                       j.user_id AS UserId,
                       j.customer_id AS CustomerId,
                       j.input_json::text AS SnapshotJson,
                       s.stage_index AS StageIndex,
                       s.progress_percent AS ProgressPercent,
                       s.depends_on_progress_percent AS DependsOnProgressPercent,
                       s.active_attempt AS Attempt,
                       s.prompt_snapshot_json::text AS PromptSnapshotJson,
                       s.provider_task_id AS ProviderTaskId,
                       s.provider_code AS ProviderCode,
                       s.provider_model AS ProviderModel,
                       d.result_media_id AS DependencyMediaId,
                       d.public_url AS DependencyPublicUrl,
                       d.object_key AS DependencyObjectKey;
            """,
            new { tenant = _tenant.TenantId, workerKey, claimFor = ToPgInterval(claimFor) }, tx);
        tx.Commit();

        return row is null ? null : ToImageWorkItem(row);
    }

    public async Task<TimelapseVideoWorkItem?> ClaimVideoAsync(string workerKey, TimeSpan claimFor, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var row = await conn.QuerySingleOrDefaultAsync<VideoRow>(
            """
            WITH candidate AS (
                SELECT c.id
                  FROM timelapse.timelapse_video_clips c
                  JOIN timelapse.timelapse_video_clip_versions v
                    ON v.video_clip_id=c.id
                   AND v.attempt=c.active_attempt
                 WHERE c.tenant_id=@tenant
                   AND c.status='RENDERING'
                   AND v.status='RENDERING'
                   AND COALESCE((v.request_json->'worker_claim'->>'until')::timestamptz, '-infinity'::timestamptz) <= now()
                 ORDER BY c.started_at NULLS FIRST, c.clip_index
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
            )
            UPDATE timelapse.timelapse_video_clip_versions v
               SET request_json=jsonb_set(
                       COALESCE(v.request_json, '{}'::jsonb),
                       '{worker_claim}',
                       jsonb_build_object('worker', @workerKey, 'until', (now() + @claimFor::interval)),
                       true),
                   updated_at=now()
              FROM timelapse.timelapse_video_clips c
              JOIN render.render_jobs j ON j.id=c.job_id
              JOIN timelapse.timelapse_image_stages start_img
                ON start_img.job_id=c.job_id
               AND start_img.progress_percent=c.start_progress_percent
               AND start_img.status='COMPLETED'
              LEFT JOIN timelapse.timelapse_image_stage_versions start_v
                ON start_v.image_stage_id=start_img.id
               AND start_v.attempt=start_img.active_attempt
              JOIN timelapse.timelapse_image_stages end_img
                ON end_img.job_id=c.job_id
               AND end_img.progress_percent=c.end_progress_percent
               AND end_img.status='COMPLETED'
              LEFT JOIN timelapse.timelapse_image_stage_versions end_v
                ON end_v.image_stage_id=end_img.id
               AND end_v.attempt=end_img.active_attempt
              JOIN candidate picked ON picked.id=c.id
             WHERE v.video_clip_id=c.id
               AND v.attempt=c.active_attempt
             RETURNING c.id AS Id,
                       c.tenant_id AS TenantId,
                       c.job_id AS JobId,
                       j.user_id AS UserId,
                       j.customer_id AS CustomerId,
                       j.input_json::text AS SnapshotJson,
                       c.clip_index AS ClipIndex,
                       c.start_progress_percent AS StartProgressPercent,
                       c.end_progress_percent AS EndProgressPercent,
                       c.active_attempt AS Attempt,
                       c.provider_task_id AS ProviderTaskId,
                       c.provider_code AS ProviderCode,
                       c.provider_model AS ProviderModel,
                       c.duration_seconds AS DurationSeconds,
                       c.video_mode AS VideoMode,
                       c.ratio AS Ratio,
                       start_img.result_media_id AS StartMediaId,
                       start_img.public_url AS StartPublicUrl,
                       start_img.object_key AS StartObjectKey,
                       start_img.prompt_snapshot_json::text AS StartPromptSnapshotJson,
                       start_v.response_json::text AS StartResponseJson,
                       end_img.result_media_id AS EndMediaId,
                       end_img.public_url AS EndPublicUrl,
                       end_img.object_key AS EndObjectKey,
                       end_img.prompt_snapshot_json::text AS EndPromptSnapshotJson,
                       end_v.response_json::text AS EndResponseJson;
            """,
            new { tenant = _tenant.TenantId, workerKey, claimFor = ToPgInterval(claimFor) }, tx);
        tx.Commit();

        return row is null ? null : ToVideoWorkItem(row);
    }

    public async Task<TimelapseFinalizerWorkItem?> ClaimFinalizerAsync(string workerKey, TimeSpan claimFor, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var row = await conn.QuerySingleOrDefaultAsync<FinalizerRow>(
            """
            WITH candidate AS (
                SELECT f.id
                  FROM timelapse.timelapse_final_outputs f
                 WHERE f.tenant_id=@tenant
                   AND f.status='RENDERING'
                   AND COALESCE((f.request_json->'worker_claim'->>'until')::timestamptz, '-infinity'::timestamptz) <= now()
                 ORDER BY f.started_at NULLS FIRST, f.version DESC
                 LIMIT 1
                 FOR UPDATE SKIP LOCKED
            )
            UPDATE timelapse.timelapse_final_outputs f
               SET request_json=jsonb_set(
                       COALESCE(f.request_json, '{}'::jsonb),
                       '{worker_claim}',
                       jsonb_build_object('worker', @workerKey, 'until', (now() + @claimFor::interval)),
                       true),
                   started_at=COALESCE(f.started_at, now()),
                   updated_at=now()
              FROM render.render_jobs j
              JOIN candidate c ON c.id=f.id
             WHERE j.id=f.job_id
             RETURNING f.id AS Id,
                       f.tenant_id AS TenantId,
                       f.job_id AS JobId,
                       j.user_id AS UserId,
                       j.customer_id AS CustomerId,
                       j.input_json::text AS SnapshotJson,
                       f.version AS Version;
            """,
            new { tenant = _tenant.TenantId, workerKey, claimFor = ToPgInterval(claimFor) }, tx);

        if (row is null)
        {
            tx.Commit();
            return null;
        }

        var clips = (await conn.QueryAsync<TimelapseFinalizerClip>(
            """
            SELECT clip_index AS ClipIndex,
                   result_media_id AS MediaId,
                   object_key AS ObjectKey,
                   public_url AS PublicUrl
              FROM timelapse.timelapse_video_clips
             WHERE tenant_id=@tenant
               AND job_id=@jobId
               AND status='COMPLETED'
               AND result_media_id IS NOT NULL
             ORDER BY clip_index;
            """,
            new { tenant = _tenant.TenantId, row.JobId }, tx)).ToList();
        tx.Commit();

        return new TimelapseFinalizerWorkItem(
            row.Id,
            row.TenantId,
            row.JobId,
            row.UserId,
            row.CustomerId,
            DeserializeSnapshot(row.SnapshotJson),
            row.Version,
            clips);
    }

    public Task SaveImageSubmittedAsync(Guid stageId, int attempt, string providerCode, string model, string taskId, string requestJson, string responseJson, CancellationToken ct = default)
        => UpdateStageSubmittedAsync("timelapse.timelapse_image_stages", "timelapse.timelapse_image_stage_versions", "image_stage_id", stageId, attempt, providerCode, model, taskId, requestJson, responseJson, ct);

    public Task SaveVideoSubmittedAsync(Guid clipId, int attempt, string providerCode, string model, string taskId, string requestJson, string responseJson, CancellationToken ct = default)
        => UpdateStageSubmittedAsync("timelapse.timelapse_video_clips", "timelapse.timelapse_video_clip_versions", "video_clip_id", clipId, attempt, providerCode, model, taskId, requestJson, responseJson, ct);

    public Task SaveImageCompletedAsync(Guid stageId, int attempt, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct = default)
        => UpdateOperationCompletedAsync("timelapse.timelapse_image_stages", "timelapse.timelapse_image_stage_versions", "image_stage_id", stageId, attempt, mediaId, objectKey, publicUrl, responseJson, ct);

    public Task SaveVideoCompletedAsync(Guid clipId, int attempt, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct = default)
        => UpdateOperationCompletedAsync("timelapse.timelapse_video_clips", "timelapse.timelapse_video_clip_versions", "video_clip_id", clipId, attempt, mediaId, objectKey, publicUrl, responseJson, ct);

    public async Task SaveFinalizerCompletedAsync(Guid finalOutputId, Guid jobId, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_final_outputs
               SET status='COMPLETED',
                   result_media_id=@mediaId,
                   object_key=@objectKey,
                   public_url=@publicUrl,
                   response_json=CAST(@responseJson AS jsonb),
                   completed_at=now(),
                   updated_at=now()
             WHERE id=@finalOutputId
               AND status='RENDERING';

            UPDATE render.render_jobs
               SET status=@completed,
                   output_json=jsonb_build_object('mediaId', @mediaId, 'objectKey', @objectKey, 'publicUrl', @publicUrl),
                   completed_at=now(),
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { finalOutputId, jobId, tenant = _tenant.TenantId, mediaId, objectKey, publicUrl, responseJson, completed = TimelapseParentStatuses.Completed }, tx);
        tx.Commit();
    }

    public Task SaveImageFailedAsync(Guid stageId, int attempt, string? errorCode, string errorMessage, string responseJson, CancellationToken ct = default)
        => UpdateOperationFailedAsync("timelapse.timelapse_image_stages", "timelapse.timelapse_image_stage_versions", "image_stage_id", stageId, attempt, errorCode, errorMessage, responseJson, null, null, null, false, ct);

    public Task SaveVideoFailedAsync(Guid clipId, int attempt, string? errorCode, string errorMessage, string responseJson, CancellationToken ct = default)
        => UpdateOperationFailedAsync("timelapse.timelapse_video_clips", "timelapse.timelapse_video_clip_versions", "video_clip_id", clipId, attempt, errorCode, errorMessage, responseJson, null, null, null, false, ct);

    public Task SaveImageSubmitFailedAsync(Guid stageId, int attempt, string providerCode, string model, string? errorCode, string errorMessage, string requestJson, string responseJson, CancellationToken ct = default)
        => UpdateOperationFailedAsync("timelapse.timelapse_image_stages", "timelapse.timelapse_image_stage_versions", "image_stage_id", stageId, attempt, errorCode, errorMessage, responseJson, requestJson, providerCode, model, true, ct);

    public Task SaveVideoSubmitFailedAsync(Guid clipId, int attempt, string providerCode, string model, string? errorCode, string errorMessage, string requestJson, string responseJson, CancellationToken ct = default)
        => UpdateOperationFailedAsync("timelapse.timelapse_video_clips", "timelapse.timelapse_video_clip_versions", "video_clip_id", clipId, attempt, errorCode, errorMessage, responseJson, requestJson, providerCode, model, true, ct);

    public async Task SaveFinalizerFailedAsync(Guid finalOutputId, Guid jobId, string? errorCode, string errorMessage, string responseJson, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_final_outputs
               SET status='FAILED',
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   response_json=CAST(@responseJson AS jsonb),
                   updated_at=now()
             WHERE id=@finalOutputId
               AND status='RENDERING';

            UPDATE render.render_jobs
               SET status=@failed,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { finalOutputId, jobId, tenant = _tenant.TenantId, errorCode, errorMessage = Clip(errorMessage), responseJson, failed = TimelapseParentStatuses.Failed }, tx);
        tx.Commit();
    }

    public Task ReleaseImageClaimAsync(Guid stageId, int attempt, CancellationToken ct = default)
        => ReleaseClaimAsync("timelapse.timelapse_image_stage_versions", "image_stage_id", stageId, attempt, ct);

    public Task ReleaseVideoClaimAsync(Guid clipId, int attempt, CancellationToken ct = default)
        => ReleaseClaimAsync("timelapse.timelapse_video_clip_versions", "video_clip_id", clipId, attempt, ct);

    public async Task AdvanceAfterImageCompletedAsync(Guid jobId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);
        await StartNextImageIfReadyAsync(conn, tx, jobId);
        tx.Commit();
    }

    public async Task AdvanceAfterVideoCompletedAsync(Guid jobId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);
        var statusCounts = await conn.QuerySingleAsync<(int Active, int Failed, int IncompleteVideos)>(
            """
            SELECT
                (
                    SELECT count(*)
                      FROM timelapse.timelapse_image_stages
                     WHERE job_id=@jobId
                       AND status='RENDERING'
                ) + (
                    SELECT count(*)
                      FROM timelapse.timelapse_video_clips
                     WHERE job_id=@jobId
                       AND status='RENDERING'
                ) AS Active,
                (
                    SELECT count(*)
                      FROM timelapse.timelapse_image_stages
                     WHERE job_id=@jobId
                       AND status='FAILED'
                ) + (
                    SELECT count(*)
                      FROM timelapse.timelapse_video_clips
                     WHERE job_id=@jobId
                       AND status='FAILED'
                ) AS Failed,
                (
                    SELECT count(*)
                      FROM timelapse.timelapse_video_clips
                     WHERE job_id=@jobId
                       AND status <> 'COMPLETED'
                ) AS IncompleteVideos;
            """,
            new { jobId }, tx);
        if (statusCounts.Active == 0 && statusCounts.Failed > 0)
        {
            await conn.ExecuteAsync(
                """
                UPDATE render.render_jobs
                   SET status=@status,
                       updated_at=now()
                 WHERE id=@jobId
                   AND tenant_id=@tenant;
                """,
                new { jobId, tenant = _tenant.TenantId, status = TimelapseParentStatuses.Failed }, tx);
        }
        else if (statusCounts.IncompleteVideos == 0)
        {
            await conn.ExecuteAsync(
                """
                UPDATE render.render_jobs
                   SET status=@status,
                       updated_at=now()
                 WHERE id=@jobId
                   AND tenant_id=@tenant;
                """,
                new { jobId, tenant = _tenant.TenantId, status = TimelapseParentStatuses.VideosReady }, tx);
        }
        tx.Commit();
    }

    private async Task UpdateStageSubmittedAsync(string table, string versionTable, string versionFk, Guid id, int attempt, string providerCode, string model, string taskId, string requestJson, string responseJson, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE {table}
               SET provider_code=@providerCode,
                   provider_model=@model,
                   provider_task_id=@taskId,
                   updated_at=now()
             WHERE id=@id
               AND status='RENDERING';

            UPDATE {versionTable}
               SET provider_code=@providerCode,
                   provider_model=@model,
                   provider_task_id=@taskId,
                   request_json=CAST(@requestJson AS jsonb),
                   response_json=CAST(@responseJson AS jsonb),
                   updated_at=now()
             WHERE {versionFk}=@id
               AND attempt=@attempt
               AND status='RENDERING';
            """,
            new { id, attempt, providerCode, model, taskId, requestJson, responseJson });
    }

    private async Task UpdateOperationCompletedAsync(string table, string versionTable, string versionFk, Guid id, int attempt, Guid mediaId, string objectKey, string publicUrl, string responseJson, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE {table}
               SET status='COMPLETED',
                   result_media_id=@mediaId,
                   object_key=@objectKey,
                   public_url=@publicUrl,
                   error_code=NULL,
                   error_message=NULL,
                   completed_at=now(),
                   updated_at=now()
             WHERE id=@id
               AND status='RENDERING';

            UPDATE {versionTable}
               SET status='COMPLETED',
                   result_media_id=@mediaId,
                   object_key=@objectKey,
                   public_url=@publicUrl,
                   response_json=CAST(@responseJson AS jsonb),
                   error_code=NULL,
                   error_message=NULL,
                   completed_at=now(),
                   updated_at=now()
             WHERE {versionFk}=@id
               AND attempt=@attempt
               AND status='RENDERING';
            """,
            new { id, attempt, mediaId, objectKey, publicUrl, responseJson });
    }

    private async Task UpdateOperationFailedAsync(
        string table,
        string versionTable,
        string versionFk,
        Guid id,
        int attempt,
        string? errorCode,
        string errorMessage,
        string responseJson,
        string? requestJson,
        string? providerCode,
        string? model,
        bool clearProviderTaskId,
        CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            $"""
            UPDATE {table}
               SET status='FAILED',
                   provider_code=COALESCE(@providerCode, provider_code),
                   provider_model=COALESCE(@model, provider_model),
                   provider_task_id=CASE WHEN @clearProviderTaskId THEN NULL ELSE provider_task_id END,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   updated_at=now()
             WHERE id=@id
               AND status='RENDERING';

            UPDATE {versionTable}
               SET status='FAILED',
                   provider_code=COALESCE(@providerCode, provider_code),
                   provider_model=COALESCE(@model, provider_model),
                   provider_task_id=CASE WHEN @clearProviderTaskId THEN NULL ELSE provider_task_id END,
                   request_json=CASE
                       WHEN @requestJson IS NULL THEN request_json
                       ELSE CAST(@requestJson AS jsonb)
                   END,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   response_json=CAST(@responseJson AS jsonb),
                   updated_at=now()
             WHERE {versionFk}=@id
               AND attempt=@attempt
               AND status='RENDERING';
            """,
            new
            {
                id,
                attempt,
                providerCode,
                model,
                clearProviderTaskId,
                requestJson,
                errorCode,
                errorMessage = Clip(errorMessage),
                responseJson
            },
            tx);

        var jobId = await conn.QuerySingleOrDefaultAsync<Guid?>($"SELECT job_id FROM {table} WHERE id=@id;", new { id }, tx);
        if (jobId is not null)
        {
            var active = await conn.QuerySingleAsync<int>(
                """
                SELECT
                    (SELECT count(*) FROM timelapse.timelapse_image_stages WHERE job_id=@jobId AND status='RENDERING')
                  + (SELECT count(*) FROM timelapse.timelapse_video_clips WHERE job_id=@jobId AND status='RENDERING')
                  + (SELECT count(*) FROM timelapse.timelapse_final_outputs WHERE job_id=@jobId AND status='RENDERING');
                """,
                new { jobId }, tx);
            if (active == 0)
            {
                await conn.ExecuteAsync(
                    """
                    UPDATE render.render_jobs
                       SET status=@failed,
                           error_code=@errorCode,
                           error_message=@errorMessage,
                           updated_at=now()
                     WHERE id=@jobId
                       AND tenant_id=@tenant;
                    """,
                    new { jobId, tenant = _tenant.TenantId, failed = TimelapseParentStatuses.Failed, errorCode, errorMessage = Clip(errorMessage) }, tx);
            }
        }

        tx.Commit();
    }

    private async Task ReleaseClaimAsync(string versionTable, string versionFk, Guid id, int attempt, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            $"""
            UPDATE {versionTable}
               SET request_json=COALESCE(request_json, jsonb_build_object()) - 'worker_claim',
                   updated_at=now()
             WHERE {versionFk}=@id
               AND attempt=@attempt
               AND status='RENDERING';
            """,
            new { id, attempt });
    }

    private async Task StartNextImageIfReadyAsync(IDbConnection conn, IDbTransaction tx, Guid jobId)
    {
        var stage = await conn.QuerySingleOrDefaultAsync<ImageStageRow>(
            """
            SELECT s.id AS Id
              FROM timelapse.timelapse_image_stages s
              LEFT JOIN timelapse.timelapse_image_stages d
                ON d.job_id=s.job_id
               AND d.progress_percent=s.depends_on_progress_percent
             WHERE s.job_id=@jobId
               AND s.is_original=false
               AND s.status IN ('WAITING','FAILED','INVALIDATED')
               AND (s.depends_on_progress_percent IS NULL OR d.status='COMPLETED')
             ORDER BY s.progress_percent DESC
             LIMIT 1;
            """,
            new { jobId }, tx);

        if (stage is not null)
        {
            var attempt = await conn.QuerySingleAsync<int>(
                "UPDATE timelapse.timelapse_image_stages SET active_attempt=active_attempt+1, status='RENDERING', provider_task_id=NULL, started_at=now(), updated_at=now() WHERE id=@id RETURNING active_attempt;",
                new { stage.Id }, tx);
            await conn.ExecuteAsync(
                """
                INSERT INTO timelapse.timelapse_image_stage_versions
                    (tenant_id, image_stage_id, job_id, attempt, status, prompt_snapshot_json, request_json, started_at)
                SELECT tenant_id, id, job_id, @attempt, 'RENDERING', prompt_snapshot_json,
                       jsonb_build_object('progress_percent', progress_percent, 'depends_on_progress_percent', depends_on_progress_percent),
                       now()
                  FROM timelapse.timelapse_image_stages
                 WHERE id=@id
                ON CONFLICT (image_stage_id, attempt) DO NOTHING;
                """,
                new { stage.Id, attempt }, tx);
        }

        await StartReadyVideosAsync(conn, tx, jobId);
    }

    private async Task StartReadyVideosAsync(IDbConnection conn, IDbTransaction tx, Guid jobId)
    {
        var clips = (await conn.QueryAsync<(Guid Id, int ClipIndex)>(
            """
            SELECT c.id AS Id, c.clip_index AS ClipIndex
              FROM timelapse.timelapse_video_clips c
              JOIN timelapse.timelapse_image_stages start_img
                ON start_img.job_id=c.job_id
               AND start_img.progress_percent=c.start_progress_percent
               AND start_img.status='COMPLETED'
              JOIN timelapse.timelapse_image_stages end_img
                ON end_img.job_id=c.job_id
               AND end_img.progress_percent=c.end_progress_percent
               AND end_img.status='COMPLETED'
              JOIN render.render_jobs j
                ON j.id=c.job_id
               AND j.tenant_id=@tenant
             WHERE c.job_id=@jobId
               AND c.status IN ('WAITING','INVALIDATED')
               AND (
                   COALESCE((j.input_json->>'requireVideoConfirmation')::boolean, false)=false
                   OR COALESCE((j.input_json->>'videoRenderConfirmed')::boolean, false)=true
               )
             ORDER BY c.clip_index;
            """,
            new { jobId, tenant = _tenant.TenantId }, tx)).ToList();

        foreach (var clip in clips)
        {
            var attempt = await conn.QuerySingleAsync<int>(
                "UPDATE timelapse.timelapse_video_clips SET active_attempt=active_attempt+1, status='RENDERING', provider_task_id=NULL, started_at=now(), updated_at=now() WHERE id=@id RETURNING active_attempt;",
                new { clip.Id }, tx);
            await conn.ExecuteAsync(
                """
                INSERT INTO timelapse.timelapse_video_clip_versions
                    (tenant_id, video_clip_id, job_id, attempt, status, request_json, started_at)
                SELECT tenant_id, id, job_id, @attempt, 'RENDERING',
                       jsonb_build_object('clip_index', clip_index, 'start_progress_percent', start_progress_percent, 'end_progress_percent', end_progress_percent, 'duration_seconds', duration_seconds, 'video_mode', video_mode, 'ratio', ratio),
                       now()
                  FROM timelapse.timelapse_video_clips
                 WHERE id=@id
                ON CONFLICT (video_clip_id, attempt) DO NOTHING;
                """,
                new { clip.Id, attempt }, tx);
        }

        var incompleteImages = await conn.QuerySingleAsync<int>(
            """
            SELECT count(*)
              FROM timelapse.timelapse_image_stages
             WHERE job_id=@jobId
               AND is_original=false
               AND status <> 'COMPLETED';
            """,
            new { jobId }, tx);
        if (incompleteImages == 0)
        {
            await conn.ExecuteAsync(
                """
                UPDATE render.render_jobs
                   SET status=CASE
                           WHEN EXISTS (
                               SELECT 1
                                 FROM timelapse.timelapse_video_clips
                                WHERE job_id=@jobId
                                  AND status='RENDERING')
                               THEN @generatingVideos
                           WHEN EXISTS (
                               SELECT 1
                                 FROM timelapse.timelapse_video_clips
                                WHERE job_id=@jobId
                                  AND status='FAILED')
                               THEN @failed
                           WHEN NOT EXISTS (
                               SELECT 1
                                 FROM timelapse.timelapse_video_clips
                                WHERE job_id=@jobId
                                  AND status <> 'COMPLETED')
                               THEN @videosReady
                           ELSE @imagesReady
                       END,
                       updated_at=now()
                 WHERE id=@jobId
                   AND tenant_id=@tenant;
                """,
                new
                {
                    jobId,
                    tenant = _tenant.TenantId,
                    failed = TimelapseParentStatuses.Failed,
                    imagesReady = TimelapseParentStatuses.ImagesReady,
                    generatingVideos = TimelapseParentStatuses.GeneratingVideos,
                    videosReady = TimelapseParentStatuses.VideosReady
                }, tx);
        }
    }

    private static Task LockJobAsync(IDbConnection conn, IDbTransaction tx, Guid jobId)
        => conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));", new { lockName = $"timelapse:{jobId:N}" }, tx);

    private static string ToPgInterval(TimeSpan value)
        => $"{Math.Max(1, (int)value.TotalSeconds)} seconds";

    private static string Clip(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Timelapse provider operation failed." : value.Trim()[..Math.Min(value.Trim().Length, 1000)];

    private static TimelapseJobSnapshot DeserializeSnapshot(string json)
        => JsonSerializer.Deserialize<TimelapseJobSnapshot>(json, JsonOptions)
           ?? throw new InvalidOperationException("Timelapse job snapshot is invalid.");

    private static TimelapseImageWorkItem ToImageWorkItem(ImageRow row)
        => new(
            row.Id,
            row.TenantId,
            row.JobId,
            row.UserId,
            row.CustomerId,
            DeserializeSnapshot(row.SnapshotJson),
            row.StageIndex,
            row.ProgressPercent,
            row.DependsOnProgressPercent,
            row.Attempt,
            row.PromptSnapshotJson,
            row.ProviderTaskId,
            row.ProviderCode,
            row.ProviderModel,
            row.DependencyMediaId,
            row.DependencyPublicUrl,
            row.DependencyObjectKey);

    private static TimelapseVideoWorkItem ToVideoWorkItem(VideoRow row)
        => new(
            row.Id,
            row.TenantId,
            row.JobId,
            row.UserId,
            row.CustomerId,
            DeserializeSnapshot(row.SnapshotJson),
            row.ClipIndex,
            row.StartProgressPercent,
            row.EndProgressPercent,
            row.Attempt,
            row.ProviderTaskId,
            row.ProviderCode,
            row.ProviderModel,
            row.DurationSeconds,
            row.VideoMode,
            row.Ratio,
            row.StartMediaId,
            row.StartPublicUrl,
            row.StartObjectKey,
            row.StartPromptSnapshotJson,
            row.StartResponseJson,
            row.EndMediaId,
            row.EndPublicUrl,
            row.EndObjectKey,
            row.EndPromptSnapshotJson,
            row.EndResponseJson);

    private sealed class ImageStageRow
    {
        public Guid Id { get; set; }
    }

    private sealed class ImageRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid JobId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public string SnapshotJson { get; set; } = "{}";
        public int StageIndex { get; set; }
        public int ProgressPercent { get; set; }
        public int? DependsOnProgressPercent { get; set; }
        public int Attempt { get; set; }
        public string PromptSnapshotJson { get; set; } = "{}";
        public string? ProviderTaskId { get; set; }
        public string? ProviderCode { get; set; }
        public string? ProviderModel { get; set; }
        public Guid? DependencyMediaId { get; set; }
        public string? DependencyPublicUrl { get; set; }
        public string? DependencyObjectKey { get; set; }
    }

    private sealed class VideoRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid JobId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public string SnapshotJson { get; set; } = "{}";
        public int ClipIndex { get; set; }
        public int StartProgressPercent { get; set; }
        public int EndProgressPercent { get; set; }
        public int Attempt { get; set; }
        public string? ProviderTaskId { get; set; }
        public string? ProviderCode { get; set; }
        public string? ProviderModel { get; set; }
        public int DurationSeconds { get; set; }
        public string VideoMode { get; set; } = string.Empty;
        public string Ratio { get; set; } = string.Empty;
        public Guid? StartMediaId { get; set; }
        public string? StartPublicUrl { get; set; }
        public string? StartObjectKey { get; set; }
        public string? StartPromptSnapshotJson { get; set; }
        public string? StartResponseJson { get; set; }
        public Guid? EndMediaId { get; set; }
        public string? EndPublicUrl { get; set; }
        public string? EndObjectKey { get; set; }
        public string? EndPromptSnapshotJson { get; set; }
        public string? EndResponseJson { get; set; }
    }

    private sealed class FinalizerRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid JobId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public string SnapshotJson { get; set; } = "{}";
        public int Version { get; set; }
    }
}

public sealed record TimelapseImageWorkItem(
    Guid Id,
    Guid TenantId,
    Guid JobId,
    Guid? UserId,
    Guid? CustomerId,
    TimelapseJobSnapshot Snapshot,
    int StageIndex,
    int ProgressPercent,
    int? DependsOnProgressPercent,
    int Attempt,
    string PromptSnapshotJson,
    string? ProviderTaskId,
    string? ProviderCode,
    string? ProviderModel,
    Guid? DependencyMediaId,
    string? DependencyPublicUrl,
    string? DependencyObjectKey);

public sealed record TimelapseVideoWorkItem(
    Guid Id,
    Guid TenantId,
    Guid JobId,
    Guid? UserId,
    Guid? CustomerId,
    TimelapseJobSnapshot Snapshot,
    int ClipIndex,
    int StartProgressPercent,
    int EndProgressPercent,
    int Attempt,
    string? ProviderTaskId,
    string? ProviderCode,
    string? ProviderModel,
    int DurationSeconds,
    string VideoMode,
    string Ratio,
    Guid? StartMediaId,
    string? StartPublicUrl,
    string? StartObjectKey,
    string? StartPromptSnapshotJson,
    string? StartResponseJson,
    Guid? EndMediaId,
    string? EndPublicUrl,
    string? EndObjectKey,
    string? EndPromptSnapshotJson,
    string? EndResponseJson);

public sealed record TimelapseFinalizerWorkItem(
    Guid Id,
    Guid TenantId,
    Guid JobId,
    Guid? UserId,
    Guid? CustomerId,
    TimelapseJobSnapshot Snapshot,
    int Version,
    IReadOnlyList<TimelapseFinalizerClip> Clips);

public sealed record TimelapseFinalizerClip(int ClipIndex, Guid? MediaId, string? ObjectKey, string? PublicUrl);
