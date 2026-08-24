using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseWorkflowService
{
    Task<TimelapseWorkflowState> GetStateAsync(Guid jobId, CancellationToken ct = default);
    Task<TimelapseWorkflowState> StartOrResumeAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> RetryImageAsync(Guid jobId, int progressPercent, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> UpdateImagePromptAsync(Guid jobId, Guid imageStageId, string prompt, bool rerender, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> RetryVideoAsync(Guid jobId, int clipIndex, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> CancelJobAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> CancelImageAsync(Guid jobId, int progressPercent, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> CancelVideoAsync(Guid jobId, int clipIndex, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> ConfirmVideoRenderAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseWorkflowState> StartFinalizerAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
}

public sealed class TimelapseWorkflowService : ITimelapseWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ITimelapseProfileRepository _profiles;
    private readonly IRenderJobService _renderJobs;

    public TimelapseWorkflowService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ITimelapseProfileRepository profiles,
        IRenderJobService renderJobs)
    {
        _factory = factory;
        _tenant = tenant;
        _profiles = profiles;
        _renderJobs = renderJobs;
    }

    public async Task<TimelapseWorkflowState> GetStateAsync(Guid jobId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await ReadStateAsync(conn, jobId);
    }

    public async Task<TimelapseWorkflowState> StartOrResumeAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);

        var profile = await _profiles.GetRenderProfileAsync(snapshot.ProfileCode, ct)
            ?? throw new InvalidOperationException("Không tải được profile Timelapse để render.");

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var current = await ReadStateAsync(conn, jobId, tx);
        if (current.HasActiveOperations)
        {
            tx.Commit();
            return current;
        }

        await EnsureGraphAsync(conn, tx, jobId, snapshot, profile);
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status, updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { jobId, tenant = _tenant.TenantId, status = TimelapseParentStatuses.GeneratingImages }, tx);

        await StartNextImageIfReadyAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(jobId, "TIMELAPSE_RENDER_STARTED", "Timelapse render workflow started or resumed.",
            new
            {
                snapshot.ProfileCode,
                snapshot.SceneCount,
                generatedImageCount = TimelapseStageGraphBuilder.Build(snapshot.SceneCount).GeneratedImageOrder.Count,
                promptProfileFields = "to_jsonb(public.todox_timelapse_prompt_profiles)"
            }, ct: ct);

        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> ConfirmVideoRenderAsync(
        Guid jobId,
        TimelapseJobSnapshot snapshot,
        CurrentUserSession currentUser,
        CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        if (!snapshot.RequireVideoConfirmation)
        {
            return await GetStateAsync(jobId, ct);
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET input_json=jsonb_set(
                       COALESCE(input_json, '{}'::jsonb),
                       '{videoRenderConfirmed}',
                       'true'::jsonb,
                       true),
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { jobId, tenant = _tenant.TenantId }, tx);
        await StartReadyVideosAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(
            jobId,
            "TIMELAPSE_VIDEO_RENDER_CONFIRMED",
            "Customer confirmed Timelapse video rendering.",
            new { readyClipsStarted = true },
            ct: ct);
        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> RetryImageAsync(Guid jobId, int progressPercent, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
        => await UpdateImageStageAsync(jobId, null, progressPercent, null, false, true, snapshot, currentUser, ct);

    public async Task<TimelapseWorkflowState> UpdateImagePromptAsync(
        Guid jobId,
        Guid imageStageId,
        string prompt,
        bool rerender,
        TimelapseJobSnapshot snapshot,
        CurrentUserSession currentUser,
        CancellationToken ct = default)
        => await UpdateImageStageAsync(jobId, imageStageId, null, prompt, true, rerender, snapshot, currentUser, ct);

    private async Task<TimelapseWorkflowState> UpdateImageStageAsync(
        Guid jobId,
        Guid? imageStageId,
        int? requestedProgressPercent,
        string? prompt,
        bool updatePrompt,
        bool rerender,
        TimelapseJobSnapshot snapshot,
        CurrentUserSession currentUser,
        CancellationToken ct)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);

        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var stage = await conn.QuerySingleOrDefaultAsync<EditableImageStageRow>(
            """
            SELECT id AS Id,
                   progress_percent AS ProgressPercent,
                   is_original AS IsOriginal,
                   status AS Status,
                   prompt_snapshot_json::text AS PromptSnapshotJson
              FROM timelapse.timelapse_image_stages
             WHERE tenant_id=@tenant
               AND job_id=@jobId
               AND (@imageStageId IS NULL OR id=@imageStageId)
               AND (@progressPercent IS NULL OR progress_percent=@progressPercent)
             FOR UPDATE;
            """,
            new
            {
                tenant = _tenant.TenantId,
                jobId,
                imageStageId,
                progressPercent = requestedProgressPercent
            }, tx);
        if (stage is null)
        {
            throw new InvalidOperationException("Không tìm thấy ảnh Timelapse thuộc job này.");
        }

        if (stage.IsOriginal || stage.ProgressPercent >= 100)
        {
            throw new InvalidOperationException("Ảnh thành phẩm 100% không thể chỉnh prompt hoặc render lại bằng AI.");
        }

        if (updatePrompt && !TimelapsePromptSnapshot.CanEdit(stage.Status))
        {
            throw new InvalidOperationException("Không thể chỉnh prompt khi ảnh đang render.");
        }

        if (updatePrompt)
        {
            var updatedPromptSnapshot = TimelapsePromptSnapshot.WithCustomerOverride(stage.PromptSnapshotJson, prompt!);
            await conn.ExecuteAsync(
                """
                UPDATE timelapse.timelapse_image_stages
                   SET prompt_snapshot_json=CAST(@promptSnapshotJson AS jsonb),
                       updated_at=now()
                 WHERE id=@stageId
                   AND tenant_id=@tenant
                   AND job_id=@jobId;
                """,
                new
                {
                    promptSnapshotJson = updatedPromptSnapshot,
                    stageId = stage.Id,
                    tenant = _tenant.TenantId,
                    jobId
                }, tx);
        }

        if (!rerender)
        {
            tx.Commit();
            await AddPromptUpdatedEventAsync(jobId, stage, ct);
            return await GetStateAsync(jobId, ct);
        }

        await EnsureImageRetryAllowedAsync(conn, tx, jobId, stage, snapshot.SceneCount);
        TimelapsePromptResolver.ValidateProviderPrompt(
            TimelapsePromptResolver.ResolveImagePrompt(
                snapshot,
                stage.ProgressPercent,
                updatePrompt
                    ? TimelapsePromptSnapshot.WithCustomerOverride(stage.PromptSnapshotJson, prompt!)
                    : stage.PromptSnapshotJson));

        var impact = TimelapseRerenderImpactPlanner.Plan(snapshot.SceneCount, stage.ProgressPercent);
        await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_image_stages
               SET status='INVALIDATED',
                   provider_code=NULL,
                   provider_model=NULL,
                   provider_task_id=NULL,
                   result_media_id=NULL,
                   object_key=NULL,
                   public_url=NULL,
                   error_code=NULL,
                   error_message=NULL,
                   started_at=NULL,
                   completed_at=NULL,
                   updated_at=now()
             WHERE job_id=@jobId
               AND progress_percent = ANY(@progress);
            """,
            new { jobId, progress = impact.ImageProgressesToInvalidate.ToArray() }, tx);

        var graph = TimelapseStageGraphBuilder.Build(snapshot.SceneCount);
        var affectedClips = graph.VideoClips
            .Where(x => impact.VideoClipIndexesToInvalidate.Contains(x.ClipIndex))
            .ToArray();
        await InvalidateVideosAsync(conn, tx, jobId, affectedClips);
        await InvalidateFinalAsync(conn, tx, jobId);
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   completed_at=NULL,
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { jobId, tenant = _tenant.TenantId, status = TimelapseParentStatuses.GeneratingImages }, tx);
        await StartNextImageIfReadyAsync(conn, tx, jobId);
        tx.Commit();

        if (updatePrompt)
        {
            await AddPromptUpdatedEventAsync(jobId, stage, ct);
        }

        await _renderJobs.AddEventAsync(
            jobId,
            "TIMELAPSE_IMAGE_RERENDER_REQUESTED",
            "Customer requested image rerender; dependent earlier images and related videos were invalidated.",
            new
            {
                imageStageId = stage.Id,
                progressPercent = stage.ProgressPercent,
                invalidImages = impact.ImageProgressesToInvalidate,
                invalidVideos = impact.VideoClipIndexesToInvalidate,
                impact.InvalidatesFinalOutput
            },
            ct: ct);

        return await GetStateAsync(jobId, ct);
    }

    private Task AddPromptUpdatedEventAsync(Guid jobId, EditableImageStageRow stage, CancellationToken ct)
        => _renderJobs.AddEventAsync(
            jobId,
            "TIMELAPSE_IMAGE_PROMPT_UPDATED",
            "Customer updated the prompt override for a Timelapse image stage.",
            new { imageStageId = stage.Id, progressPercent = stage.ProgressPercent },
            ct: ct);

    private async Task EnsureImageRetryAllowedAsync(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid jobId,
        EditableImageStageRow stage,
        int sceneCount)
    {
        if (TimelapseOperationStatuses.IsActive(stage.Status))
        {
            throw new InvalidOperationException("Ảnh này đang được tạo. Vui lòng chờ hoàn tất trước khi render lại.");
        }

        if (stage.Status is not TimelapseOperationStatuses.Failed
            and not TimelapseOperationStatuses.Completed
            and not TimelapseOperationStatuses.Invalidated
            and not TimelapseOperationStatuses.Cancelled)
        {
            throw new InvalidOperationException("Ảnh này chưa sẵn sàng để render lại.");
        }

        var finalizerActive = await conn.QuerySingleAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                  FROM timelapse.timelapse_final_outputs
                 WHERE tenant_id=@tenant
                   AND job_id=@jobId
                   AND status='RENDERING'
            );
            """,
            new { tenant = _tenant.TenantId, jobId }, tx);
        if (finalizerActive)
        {
            throw new InvalidOperationException("Video cuối đang được hoàn thiện. Vui lòng chờ xong trước khi render lại ảnh.");
        }

        var impact = TimelapseRerenderImpactPlanner.Plan(sceneCount, stage.ProgressPercent);
        var activeConflicts = await conn.QuerySingleAsync<int>(
            """
            SELECT
                (SELECT count(*)
                   FROM timelapse.timelapse_image_stages
                  WHERE tenant_id=@tenant
                    AND job_id=@jobId
                    AND progress_percent = ANY(@progress)
                    AND status='RENDERING')
              + (SELECT count(*)
                   FROM timelapse.timelapse_video_clips
                  WHERE tenant_id=@tenant
                    AND job_id=@jobId
                    AND clip_index = ANY(@clipIndexes)
                    AND status='RENDERING');
            """,
            new
            {
                tenant = _tenant.TenantId,
                jobId,
                progress = impact.ImageProgressesToInvalidate.ToArray(),
                clipIndexes = impact.VideoClipIndexesToInvalidate.ToArray()
            }, tx);
        if (activeConflicts > 0)
        {
            throw new InvalidOperationException("Một tác vụ phụ thuộc đang được xử lý. Vui lòng chờ xong trước khi render lại ảnh này.");
        }
    }

    public async Task<TimelapseWorkflowState> RetryVideoAsync(Guid jobId, int clipIndex, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);
        await EnsureVideoRetryAllowedAsync(conn, tx, jobId, clipIndex);
        await InvalidateVideosAsync(conn, tx, jobId, TimelapseStageGraphBuilder.PlanVideoRerender(snapshot.SceneCount, clipIndex).VideoClips);
        await InvalidateFinalAsync(conn, tx, jobId);
        await StartReadyVideosAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(jobId, "TIMELAPSE_VIDEO_RERENDER_REQUESTED",
            "Customer requested video clip rerender; final output was invalidated.", new { clipIndex }, ct: ct);

        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> CancelJobAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        await CancelImagesAsync(conn, tx, jobId, null);
        await CancelVideosAsync(conn, tx, jobId, null);
        await CancelFinalizerAsync(conn, tx, jobId);
        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status,
                   cancel_reason=@reason,
                   cancelled_at=now(),
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE id=@jobId
               AND tenant_id=@tenant
               AND status <> @completed;
            """,
            new
            {
                jobId,
                tenant = _tenant.TenantId,
                status = RenderJobStatuses.Cancelled,
                completed = RenderJobStatuses.Completed,
                reason = "user_requested"
            }, tx);
        tx.Commit();

        await _renderJobs.AddEventAsync(
            jobId,
            "TIMELAPSE_JOB_CANCELLED",
            "Customer cancelled the Timelapse job.",
            new { jobId, userId = currentUser.UserId, reason = "user_requested" },
            "warning",
            ct);

        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> CancelImageAsync(Guid jobId, int progressPercent, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var stage = await conn.QuerySingleOrDefaultAsync<EditableImageStageRow>(
            """
            SELECT id AS Id,
                   progress_percent AS ProgressPercent,
                   is_original AS IsOriginal,
                   status AS Status,
                   prompt_snapshot_json::text AS PromptSnapshotJson
              FROM timelapse.timelapse_image_stages
             WHERE tenant_id=@tenant
               AND job_id=@jobId
               AND progress_percent=@progressPercent
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, jobId, progressPercent }, tx);
        if (stage is null || stage.IsOriginal || stage.ProgressPercent >= 100)
        {
            throw new InvalidOperationException("Không tìm thấy ảnh Timelapse có thể dừng.");
        }

        if (stage.Status == TimelapseOperationStatuses.Completed)
        {
            throw new InvalidOperationException("Ảnh đã hoàn thành nên không thể dừng.");
        }

        if (!IsCancellableOperation(stage.Status))
        {
            throw new InvalidOperationException("Ảnh này không ở trạng thái có thể dừng.");
        }

        var impact = TimelapseRerenderImpactPlanner.Plan(snapshot.SceneCount, stage.ProgressPercent);
        var cancelledProgress = impact.ImageProgressesToInvalidate
            .Append(stage.ProgressPercent)
            .Distinct()
            .ToArray();
        await CancelImagesAsync(conn, tx, jobId, cancelledProgress);

        var graph = TimelapseStageGraphBuilder.Build(snapshot.SceneCount);
        var affectedClipIndexes = graph.VideoClips
            .Where(x => cancelledProgress.Contains(x.StartProgressPercent) || cancelledProgress.Contains(x.EndProgressPercent))
            .Select(x => x.ClipIndex)
            .Distinct()
            .ToArray();
        await CancelVideosAsync(conn, tx, jobId, affectedClipIndexes);
        await CancelFinalizerAsync(conn, tx, jobId);
        await SetParentStoppedIfNoActiveAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(
            jobId,
            "TIMELAPSE_IMAGE_CANCELLED",
            "Customer cancelled a Timelapse image stage.",
            new
            {
                jobId,
                progressPercent = stage.ProgressPercent,
                cancelledProgress,
                cancelledClipIndexes = affectedClipIndexes,
                userId = currentUser.UserId,
                reason = "user_requested"
            },
            "warning",
            ct);

        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> CancelVideoAsync(Guid jobId, int clipIndex, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var clip = await conn.QuerySingleOrDefaultAsync<VideoRetryClipRow>(
            """
            SELECT id AS Id,
                   clip_index AS ClipIndex,
                   start_progress_percent AS StartProgressPercent,
                   end_progress_percent AS EndProgressPercent,
                   status AS Status,
                   active_attempt AS ActiveAttempt
              FROM timelapse.timelapse_video_clips
             WHERE tenant_id=@tenant
               AND job_id=@jobId
               AND clip_index=@clipIndex
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, jobId, clipIndex }, tx);
        if (clip is null)
        {
            throw new InvalidOperationException("Không tìm thấy video clip Timelapse thuộc job này.");
        }

        if (clip.Status == TimelapseOperationStatuses.Completed)
        {
            throw new InvalidOperationException("Video clip đã hoàn thành nên không thể dừng.");
        }

        if (!IsCancellableOperation(clip.Status))
        {
            throw new InvalidOperationException("Video clip này không ở trạng thái có thể dừng.");
        }

        await CancelVideosAsync(conn, tx, jobId, new[] { clip.ClipIndex });
        await CancelFinalizerAsync(conn, tx, jobId);
        await SetParentStoppedIfNoActiveAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(
            jobId,
            "TIMELAPSE_VIDEO_CANCELLED",
            "Customer cancelled a Timelapse video clip.",
            new
            {
                jobId,
                clipIndex = clip.ClipIndex,
                attempt = clip.ActiveAttempt,
                userId = currentUser.UserId,
                reason = "user_requested"
            },
            "warning",
            ct);

        return await GetStateAsync(jobId, ct);
    }

    private async Task EnsureVideoRetryAllowedAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId, int clipIndex)
    {
        var clip = await conn.QuerySingleOrDefaultAsync<VideoRetryClipRow>(
            """
            SELECT id AS Id,
                   clip_index AS ClipIndex,
                   start_progress_percent AS StartProgressPercent,
                   end_progress_percent AS EndProgressPercent,
                   status AS Status,
                   active_attempt AS ActiveAttempt
              FROM timelapse.timelapse_video_clips
             WHERE tenant_id=@tenant
               AND job_id=@jobId
               AND clip_index=@clipIndex
             FOR UPDATE;
            """,
            new { tenant = _tenant.TenantId, jobId, clipIndex }, tx);
        if (clip is null)
        {
            throw new InvalidOperationException("Không tìm thấy video clip Timelapse thuộc job này.");
        }

        if (TimelapseOperationStatuses.IsActive(clip.Status))
        {
            throw new InvalidOperationException("Video clip này đang được tạo. Vui lòng chờ hoàn tất trước khi render lại.");
        }

        if (clip.Status is not TimelapseOperationStatuses.Failed
            and not TimelapseOperationStatuses.Completed
            and not TimelapseOperationStatuses.Invalidated
            and not TimelapseOperationStatuses.Cancelled)
        {
            throw new InvalidOperationException("Video clip này chưa sẵn sàng để render lại.");
        }

        var hasActiveCurrentAttempt = await conn.QuerySingleAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                  FROM timelapse.timelapse_video_clip_versions
                 WHERE video_clip_id=@clipId
                   AND attempt=@attempt
                   AND status='RENDERING'
            );
            """,
            new { clipId = clip.Id, attempt = clip.ActiveAttempt }, tx);
        if (hasActiveCurrentAttempt)
        {
            throw new InvalidOperationException("Video clip này đang có lần render đang chạy.");
        }

        var dependencyStatuses = (await conn.QueryAsync<ImageDependencyStatusRow>(
            """
            SELECT progress_percent AS ProgressPercent,
                   status AS Status
              FROM timelapse.timelapse_image_stages
             WHERE tenant_id=@tenant
               AND job_id=@jobId
               AND progress_percent = ANY(@progress)
             FOR UPDATE;
            """,
            new
            {
                tenant = _tenant.TenantId,
                jobId,
                progress = new[] { clip.StartProgressPercent, clip.EndProgressPercent }
            }, tx)).ToDictionary(x => x.ProgressPercent);
        EnsureCompletedDependency(dependencyStatuses, clip.StartProgressPercent);
        EnsureCompletedDependency(dependencyStatuses, clip.EndProgressPercent);

        var finalizerActive = await conn.QuerySingleAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                  FROM timelapse.timelapse_final_outputs
                 WHERE tenant_id=@tenant
                   AND job_id=@jobId
                   AND status='RENDERING'
            );
            """,
            new { tenant = _tenant.TenantId, jobId }, tx);
        if (finalizerActive)
        {
            throw new InvalidOperationException("Video cuối đang được hoàn thiện. Vui lòng chờ xong trước khi render lại clip.");
        }
    }

    private static void EnsureCompletedDependency(
        IReadOnlyDictionary<int, ImageDependencyStatusRow> statuses,
        int progress)
    {
        if (!statuses.TryGetValue(progress, out var image)
            || !TimelapseOperationStatuses.IsCurrentCompleted(image.Status))
        {
            throw new InvalidOperationException($"Ảnh phụ thuộc {progress}% chưa hoàn thành nên chưa thể render lại video clip này.");
        }
    }

    public async Task<TimelapseWorkflowState> StartFinalizerAsync(Guid jobId, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var state = await ReadStateAsync(conn, jobId, tx);
        if (!state.CanFinalize)
        {
            throw new InvalidOperationException("Chưa đủ video clip hoàn tất để tạo video cuối.");
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO timelapse.timelapse_final_outputs
                (tenant_id, job_id, version, status, request_json)
            VALUES
                (@tenant, @jobId,
                 COALESCE((SELECT max(version) + 1 FROM timelapse.timelapse_final_outputs WHERE job_id=@jobId), 1),
                 'RENDERING',
                 CAST(@requestJson AS jsonb));

            UPDATE render.render_jobs
               SET status=@status, updated_at=now()
             WHERE id=@jobId AND tenant_id=@tenant;
            """,
            new
            {
                tenant = _tenant.TenantId,
                jobId,
                status = TimelapseParentStatuses.Finalizing,
                requestJson = JsonSerializer.Serialize(new
                {
                    clipOrder = TimelapseStageGraphBuilder.Build(snapshot.SceneCount).VideoClips,
                    snapshot.Ratio,
                    snapshot.VideoMode,
                    durationSeconds = TimelapseRequestRules.RuntimeClipDurationSeconds
                }, JsonOptions)
            }, tx);
        tx.Commit();

        await _renderJobs.AddEventAsync(jobId, "TIMELAPSE_FINALIZER_STARTED",
            "Final merge operation was queued by customer.", new { snapshot.SceneCount }, ct: ct);

        return await GetStateAsync(jobId, ct);
    }

    private async Task EnsureGraphAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId, TimelapseJobSnapshot snapshot, TimelapseRenderProfileDto profile)
    {
        var graph = TimelapseStageGraphBuilder.Build(snapshot.SceneCount);
        var profileSnapshot = JsonSerializer.Serialize(new
        {
            profile.ProfileCode,
            profile.ProfileName,
            profile.ProfileJson,
            capturedAtUtc = DateTimeOffset.UtcNow
        }, JsonOptions);

        foreach (var item in graph.ImageProgressions.Select((progress, index) => new { progress, index }))
        {
            var isOriginal = item.progress == 100;
            var dependsOn = isOriginal ? (int?)null : graph.ImageProgressions.Where(x => x > item.progress).OrderBy(x => x).First();
            await conn.ExecuteAsync(
                """
                INSERT INTO timelapse.timelapse_image_stages
                    (tenant_id, job_id, stage_index, progress_percent, is_original, depends_on_progress_percent,
                     status, active_attempt, prompt_snapshot_json, result_media_id, object_key, public_url)
                VALUES
                    (@tenant, @jobId, @stageIndex, @progress, @isOriginal, @dependsOn,
                     @status, @activeAttempt, CAST(@promptSnapshot AS jsonb), @mediaId, @objectKey, @publicUrl)
                ON CONFLICT (job_id, progress_percent)
                DO NOTHING;
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    jobId,
                    stageIndex = item.index + 1,
                    progress = item.progress,
                    isOriginal,
                    dependsOn,
                    status = isOriginal ? TimelapseOperationStatuses.Completed : TimelapseOperationStatuses.Waiting,
                    activeAttempt = isOriginal ? 1 : 0,
                    mediaId = isOriginal ? snapshot.OriginalImage.MediaId : (Guid?)null,
                    objectKey = isOriginal ? snapshot.OriginalImage.ObjectKey : null,
                    publicUrl = isOriginal ? snapshot.OriginalImage.PublicUrl : null,
                    promptSnapshot = profileSnapshot
                }, tx);
        }

        foreach (var clip in graph.VideoClips)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO timelapse.timelapse_video_clips
                    (tenant_id, job_id, clip_index, start_progress_percent, end_progress_percent,
                     status, active_attempt, duration_seconds, video_mode, ratio)
                VALUES
                    (@tenant, @jobId, @clipIndex, @start, @end, 'WAITING', 0, @duration, @mode, @ratio)
                ON CONFLICT (job_id, clip_index)
                DO NOTHING;
                """,
                new
                {
                    tenant = _tenant.TenantId,
                    jobId,
                    clipIndex = clip.ClipIndex,
                    start = clip.StartProgressPercent,
                    end = clip.EndProgressPercent,
                    duration = TimelapseRequestRules.RuntimeClipDurationSeconds,
                    mode = snapshot.VideoMode,
                    ratio = snapshot.Ratio
                }, tx);
        }
    }

    private async Task StartNextImageIfReadyAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
    {
        var stage = await conn.QuerySingleOrDefaultAsync<ImageStageRow>(
            """
            SELECT s.id AS Id,
                   s.progress_percent AS ProgressPercent,
                   s.depends_on_progress_percent AS DependsOnProgressPercent
              FROM timelapse.timelapse_image_stages s
              LEFT JOIN timelapse.timelapse_image_stages d
                ON d.job_id=s.job_id
               AND d.progress_percent=s.depends_on_progress_percent
             WHERE s.job_id=@jobId
               AND s.is_original=false
               AND s.status IN ('WAITING','FAILED','INVALIDATED')
               AND (
                   s.depends_on_progress_percent IS NULL
                   OR (
                       d.status='COMPLETED'
                       AND d.result_media_id IS NOT NULL
                       AND (NULLIF(d.public_url,'') IS NOT NULL OR NULLIF(d.object_key,'') IS NOT NULL)
                   )
               )
             ORDER BY s.progress_percent DESC
             LIMIT 1;
            """,
            new { jobId }, tx);

        if (stage is not null)
        {
            var attempt = await conn.QuerySingleAsync<int>(
                """
                UPDATE timelapse.timelapse_image_stages
                   SET active_attempt=active_attempt+1,
                       status='RENDERING',
                       provider_task_id=NULL,
                       error_code=NULL,
                       error_message=NULL,
                       started_at=now(),
                       completed_at=NULL,
                       updated_at=now()
                 WHERE id=@id
                 RETURNING active_attempt;
                """,
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

    private async Task StartReadyVideosAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
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
                   OR COALESCE((j.input_json->>'autoFinish')::boolean, false)=true
               )
             ORDER BY c.clip_index;
            """,
            new { jobId, tenant = _tenant.TenantId }, tx)).ToList();

        foreach (var clip in clips)
        {
            var attempt = await conn.QuerySingleAsync<int>(
                "UPDATE timelapse.timelapse_video_clips SET active_attempt=active_attempt+1, status='RENDERING', started_at=now(), updated_at=now() WHERE id=@id RETURNING active_attempt;",
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

    private static async Task InvalidateVideosAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId, IReadOnlyList<TimelapseVideoEdge> clips)
    {
        if (clips.Count == 0)
        {
            return;
        }

        await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_video_clips
               SET status='INVALIDATED',
                   provider_code=NULL,
                   provider_model=NULL,
                   provider_task_id=NULL,
                   result_media_id=NULL,
                   object_key=NULL,
                   public_url=NULL,
                   error_code=NULL,
                   error_message=NULL,
                   started_at=NULL,
                   completed_at=NULL,
                   updated_at=now()
             WHERE job_id=@jobId
               AND clip_index = ANY(@clipIndexes);
            """,
            new { jobId, clipIndexes = clips.Select(x => x.ClipIndex).ToArray() }, tx);
    }

    private static async Task InvalidateFinalAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
        => await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_final_outputs
               SET status='INVALIDATED', updated_at=now()
             WHERE job_id=@jobId
               AND status='COMPLETED';
            """,
            new { jobId }, tx);

    private static async Task CancelImagesAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId, IReadOnlyList<int>? progress)
    {
        await conn.ExecuteAsync(
            """
            WITH cancelled AS (
                UPDATE timelapse.timelapse_image_stages
                   SET status='CANCELLED',
                       error_code='user_cancelled',
                       error_message='User requested cancellation.',
                       completed_at=COALESCE(completed_at, now()),
                       updated_at=now()
                 WHERE job_id=@jobId
                   AND is_original=false
                   AND status IN ('WAITING','RENDERING','INVALIDATED','FAILED')
                   AND (@progress::integer[] IS NULL OR progress_percent = ANY(@progress::integer[]))
                 RETURNING id, active_attempt
            )
            UPDATE timelapse.timelapse_image_stage_versions v
               SET status='CANCELLED',
                   request_json=COALESCE(v.request_json, jsonb_build_object()) - 'worker_claim',
                   error_code='user_cancelled',
                   error_message='User requested cancellation.',
                   completed_at=COALESCE(v.completed_at, now()),
                   updated_at=now()
              FROM cancelled c
             WHERE v.image_stage_id=c.id
               AND v.attempt=c.active_attempt
               AND v.status IN ('WAITING','RENDERING','INVALIDATED','FAILED');
            """,
            new { jobId, progress = progress?.ToArray() }, tx);
    }

    private static async Task CancelVideosAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId, IReadOnlyList<int>? clipIndexes)
    {
        await conn.ExecuteAsync(
            """
            WITH cancelled AS (
                UPDATE timelapse.timelapse_video_clips
                   SET status='CANCELLED',
                       error_code='user_cancelled',
                       error_message='User requested cancellation.',
                       completed_at=COALESCE(completed_at, now()),
                       updated_at=now()
                 WHERE job_id=@jobId
                   AND status IN ('WAITING','RENDERING','INVALIDATED','FAILED')
                   AND (@clipIndexes::integer[] IS NULL OR clip_index = ANY(@clipIndexes::integer[]))
                 RETURNING id, active_attempt
            )
            UPDATE timelapse.timelapse_video_clip_versions v
               SET status='CANCELLED',
                   request_json=COALESCE(v.request_json, jsonb_build_object()) - 'worker_claim',
                   error_code='user_cancelled',
                   error_message='User requested cancellation.',
                   completed_at=COALESCE(v.completed_at, now()),
                   updated_at=now()
              FROM cancelled c
             WHERE v.video_clip_id=c.id
               AND v.attempt=c.active_attempt
               AND v.status IN ('WAITING','RENDERING','INVALIDATED','FAILED');
            """,
            new { jobId, clipIndexes = clipIndexes?.ToArray() }, tx);
    }

    private static async Task CancelFinalizerAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
        => await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_final_outputs
               SET status='CANCELLED',
                   request_json=COALESCE(request_json, jsonb_build_object()) - 'worker_claim',
                   error_code='user_cancelled',
                   error_message='User requested cancellation.',
                   completed_at=COALESCE(completed_at, now()),
                   updated_at=now()
             WHERE job_id=@jobId
               AND status IN ('WAITING','RENDERING','INVALIDATED','FAILED');
            """,
            new { jobId }, tx);

    private async Task SetParentStoppedIfNoActiveAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
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
                   SET status=@status,
                       cancel_reason=COALESCE(cancel_reason, @reason),
                       cancelled_at=COALESCE(cancelled_at, now()),
                       updated_at=now()
                 WHERE id=@jobId
                   AND tenant_id=@tenant
                   AND status <> @completed;
                """,
                new
                {
                    jobId,
                    tenant = _tenant.TenantId,
                    status = RenderJobStatuses.Cancelled,
                    completed = RenderJobStatuses.Completed,
                    reason = "user_requested"
                }, tx);
        }
    }

    private static bool IsCancellableOperation(string? status)
        => status is TimelapseOperationStatuses.Rendering
            or TimelapseOperationStatuses.Waiting
            or TimelapseOperationStatuses.Invalidated
            or TimelapseOperationStatuses.Failed;

    private async Task<TimelapseWorkflowState> ReadStateAsync(System.Data.IDbConnection conn, Guid jobId, System.Data.IDbTransaction? tx = null)
    {
        var parent = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT status FROM render.render_jobs WHERE id=@jobId AND tenant_id=@tenant;",
            new { jobId, tenant = _tenant.TenantId }, tx) ?? TimelapseParentStatuses.Draft;

        var images = (await conn.QueryAsync<TimelapseStageImage>(
            """
            SELECT id AS Id,
                   stage_index AS StageIndex,
                   progress_percent AS ProgressPercent,
                   is_original AS IsOriginal,
                   depends_on_progress_percent AS DependsOnProgressPercent,
                   status AS Status,
                   active_attempt AS Attempt,
                   result_media_id AS MediaId,
                   public_url AS PublicUrl,
                   object_key AS ObjectKey,
                   provider_task_id AS ProviderTaskId,
                   error_message AS ErrorMessage,
                   prompt_snapshot_json::text AS PromptSnapshotJson,
                   started_at AS StartedAt,
                   completed_at AS CompletedAt
              FROM timelapse.timelapse_image_stages
             WHERE job_id=@jobId
             ORDER BY progress_percent;
            """,
            new { jobId }, tx)).ToList();
        var videos = (await conn.QueryAsync<TimelapseVideoClip>(
            """
            SELECT clip_index AS ClipIndex,
                   start_progress_percent AS StartProgressPercent,
                   end_progress_percent AS EndProgressPercent,
                   status AS Status,
                   active_attempt AS Attempt,
                   result_media_id AS MediaId,
                   public_url AS PublicUrl,
                   object_key AS ObjectKey,
                   provider_task_id AS ProviderTaskId,
                   error_message AS ErrorMessage,
                   started_at AS StartedAt,
                   completed_at AS CompletedAt
              FROM timelapse.timelapse_video_clips
             WHERE job_id=@jobId
             ORDER BY clip_index;
            """,
            new { jobId }, tx)).ToList();
        var final = await conn.QuerySingleOrDefaultAsync<TimelapseFinalOutput>(
            """
            SELECT status AS Status,
                   version AS Version,
                   result_media_id AS MediaId,
                   public_url AS PublicUrl,
                   object_key AS ObjectKey,
                   error_message AS ErrorMessage,
                   completed_at AS CompletedAt
              FROM timelapse.timelapse_final_outputs
             WHERE job_id=@jobId
             ORDER BY version DESC
             LIMIT 1;
            """,
            new { jobId }, tx);

        var hasActive = images.Any(x => TimelapseOperationStatuses.IsActive(x.Status))
                        || videos.Any(x => TimelapseOperationStatuses.IsActive(x.Status))
                        || TimelapseOperationStatuses.IsActive(final?.Status);
        var videosReady = videos.Count > 0 && videos.All(x => TimelapseOperationStatuses.IsCurrentCompleted(x.Status));
        var imageProgress = TimelapseProgress.CalculateImageProgress(images);
        var readyVideoCount = videos.Count(clip =>
            clip.Status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Invalidated
            && TimelapseVideoOrchestration.IsReady(clip, images));
        var requiresVideoConfirmation = await conn.QuerySingleAsync<bool>(
            """
            SELECT COALESCE((input_json->>'requireVideoConfirmation')::boolean, false)
              FROM render.render_jobs
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { jobId, tenant = _tenant.TenantId }, tx);
        var videoRenderConfirmed = await conn.QuerySingleAsync<bool>(
            """
            SELECT COALESCE((input_json->>'videoRenderConfirmed')::boolean, false)
              FROM render.render_jobs
             WHERE id=@jobId
               AND tenant_id=@tenant;
            """,
            new { jobId, tenant = _tenant.TenantId }, tx);
        var canEdit = !hasActive && TimelapseParentStatuses.IsEditableStopped(parent);
        var canStart = !hasActive
                       && !string.Equals(parent, TimelapseParentStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                       && (!images.Any() || images.Any(x => x.Status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Failed or TimelapseOperationStatuses.Invalidated or TimelapseOperationStatuses.Cancelled)
                           || videos.Any(x => x.Status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Failed or TimelapseOperationStatuses.Invalidated or TimelapseOperationStatuses.Cancelled));

        var normalizedParent = NormalizeParentStatus(parent);
        return new TimelapseWorkflowState
        {
            ParentStatus = normalizedParent,
            Images = images,
            Videos = videos,
            FinalOutput = final,
            HasActiveOperations = hasActive,
            CanEditRequest = canEdit,
            CanStartRender = canStart,
            CanFinalize = videosReady && !hasActive && final?.Status != TimelapseOperationStatuses.Completed,
            RequiresVideoConfirmation = requiresVideoConfirmation,
            CanConfirmVideoRender = requiresVideoConfirmation && !videoRenderConfirmed && readyVideoCount > 0,
            ReadyVideoCount = readyVideoCount,
            GeneratedImageCount = images.Count(x => !x.IsOriginal),
            CurrentStep = normalizedParent == TimelapseParentStatuses.Cancelled
                ? "Đã dừng"
                : BuildCurrentStep(images, videos, final, imageProgress)
        };
    }

    private static string NormalizeParentStatus(string? status)
        => status switch
        {
            RenderJobStatuses.Draft => TimelapseParentStatuses.Draft,
            RenderJobStatuses.Failed => TimelapseParentStatuses.Failed,
            RenderJobStatuses.Completed => TimelapseParentStatuses.Completed,
            RenderJobStatuses.Cancelled => TimelapseParentStatuses.Cancelled,
            "paused" => TimelapseParentStatuses.Paused,
            _ when string.Equals(status, TimelapseParentStatuses.GeneratingImages, StringComparison.OrdinalIgnoreCase) => TimelapseParentStatuses.GeneratingImages,
            _ when string.Equals(status, TimelapseParentStatuses.GeneratingVideos, StringComparison.OrdinalIgnoreCase) => TimelapseParentStatuses.GeneratingVideos,
            _ when string.Equals(status, TimelapseParentStatuses.Finalizing, StringComparison.OrdinalIgnoreCase) => TimelapseParentStatuses.Finalizing,
            _ => status?.ToUpperInvariant() ?? TimelapseParentStatuses.Draft
        };

    private static string BuildCurrentStep(
        IReadOnlyList<TimelapseStageImage> images,
        IReadOnlyList<TimelapseVideoClip> videos,
        TimelapseFinalOutput? final,
        TimelapseImageProgressSummary imageProgress)
    {
        if (images.Any(x => x.Status == TimelapseOperationStatuses.Cancelled)
            || videos.Any(x => x.Status == TimelapseOperationStatuses.Cancelled)
            || final?.Status == TimelapseOperationStatuses.Cancelled)
        {
            return "Đã dừng";
        }

        var image = images.FirstOrDefault(x => TimelapseOperationStatuses.IsActive(x.Status));
        if (image is not null)
        {
            if (TimelapseImageExecutionPhase.IsWaitingForWorker(image))
            {
                return TimelapseImageExecutionPhase.IsStuckWaitingForWorker(image, DateTime.UtcNow, TimeSpan.FromMinutes(2))
                    ? $"Tiến độ ảnh: {imageProgress.Percent}% · Đang chờ hệ thống xử lý lâu hơn bình thường"
                    : $"Tiến độ ảnh: {imageProgress.Percent}% · Đang chờ xử lý";
            }

            return $"Tiến độ ảnh: {imageProgress.Percent}% · Đang tạo ảnh {image.ProgressPercent}%";
        }

        var video = videos.FirstOrDefault(x => TimelapseOperationStatuses.IsActive(x.Status));
        if (video is not null)
        {
            return $"Đang tạo video {video.StartProgressPercent}% -> {video.EndProgressPercent}%";
        }

        if (TimelapseOperationStatuses.IsActive(final?.Status))
        {
            return "Đang hoàn thiện video";
        }

        if (imageProgress.Total > 0 && imageProgress.Completed < imageProgress.Total)
        {
            return images.Any(x => x.Status == TimelapseOperationStatuses.Failed)
                ? $"Tiến độ ảnh: {imageProgress.Percent}% · Cần tạo lại ảnh lỗi"
                : $"Tiến độ ảnh: {imageProgress.Percent}% · Đang chờ";
        }

        if (videos.Count > 0 && videos.All(x => TimelapseOperationStatuses.IsCurrentCompleted(x.Status)))
        {
            return "Video đã sẵn sàng";
        }

        return imageProgress.Total > 0 && imageProgress.Percent == 100
            ? "Ảnh đã sẵn sàng"
            : "Chưa bắt đầu";
    }

    private static async Task LockJobAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
        => await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));", new { lockName = $"timelapse:{jobId:N}" }, tx);

    private static void EnsureCustomer(CurrentUserSession currentUser)
    {
        if (currentUser is not { IsAuthenticated: true, IsCustomer: true } || currentUser.CustomerId is null)
        {
            throw new UnauthorizedAccessException("Customer authentication is required.");
        }
    }

    private sealed class ImageStageRow
    {
        public Guid Id { get; set; }
        public int ProgressPercent { get; set; }
        public int? DependsOnProgressPercent { get; set; }
    }

    private sealed class EditableImageStageRow
    {
        public Guid Id { get; set; }
        public int ProgressPercent { get; set; }
        public bool IsOriginal { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PromptSnapshotJson { get; set; } = "{}";
    }

    private sealed class VideoRetryClipRow
    {
        public Guid Id { get; set; }
        public int ClipIndex { get; set; }
        public int StartProgressPercent { get; set; }
        public int EndProgressPercent { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ActiveAttempt { get; set; }
    }

    private sealed class ImageDependencyStatusRow
    {
        public int ProgressPercent { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
