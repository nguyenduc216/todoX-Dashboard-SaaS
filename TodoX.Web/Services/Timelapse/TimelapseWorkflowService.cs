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
    Task<TimelapseWorkflowState> RetryVideoAsync(Guid jobId, int clipIndex, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default);
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
                generatedImageCount = snapshot.SceneCount,
                promptProfileFields = "to_jsonb(public.todox_timelapse_prompt_profiles)"
            }, ct: ct);

        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> RetryImageAsync(Guid jobId, int progressPercent, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        if (progressPercent >= 100)
        {
            throw new InvalidOperationException("Ảnh gốc 100% không thể render lại bằng AI.");
        }

        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var state = await ReadStateAsync(conn, jobId, tx);
        if (state.HasActiveOperations)
        {
            throw new InvalidOperationException("Vui lòng chờ tác vụ đang chạy hoàn tất trước khi render lại.");
        }

        var plan = TimelapseStageGraphBuilder.PlanImageRerender(snapshot.SceneCount, progressPercent);
        await conn.ExecuteAsync(
            """
            UPDATE timelapse.timelapse_image_stages
               SET status='INVALIDATED', result_media_id=NULL, object_key=NULL, public_url=NULL, updated_at=now()
             WHERE job_id=@jobId
               AND progress_percent = ANY(@progress);
            """,
            new { jobId, progress = plan.ImageProgressions.Concat(new[] { progressPercent }).Distinct().ToArray() }, tx);
        await InvalidateVideosAsync(conn, tx, jobId, plan.VideoClips);
        await InvalidateFinalAsync(conn, tx, jobId);
        await StartNextImageIfReadyAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(jobId, "TIMELAPSE_IMAGE_RERENDER_REQUESTED",
            "Customer requested image rerender; dependent earlier images and related videos were invalidated.",
            new { progressPercent, invalidImages = plan.ImageProgressions, invalidVideos = plan.VideoClips }, ct: ct);

        return await GetStateAsync(jobId, ct);
    }

    public async Task<TimelapseWorkflowState> RetryVideoAsync(Guid jobId, int clipIndex, TimelapseJobSnapshot snapshot, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await LockJobAsync(conn, tx, jobId);

        var state = await ReadStateAsync(conn, jobId, tx);
        if (state.HasActiveOperations)
        {
            throw new InvalidOperationException("Vui lòng chờ tác vụ đang chạy hoàn tất trước khi render lại.");
        }

        await InvalidateVideosAsync(conn, tx, jobId, TimelapseStageGraphBuilder.PlanVideoRerender(snapshot.SceneCount, clipIndex).VideoClips);
        await InvalidateFinalAsync(conn, tx, jobId);
        await StartReadyVideosAsync(conn, tx, jobId);
        tx.Commit();

        await _renderJobs.AddEventAsync(jobId, "TIMELAPSE_VIDEO_RERENDER_REQUESTED",
            "Customer requested video clip rerender; final output was invalidated.", new { clipIndex }, ct: ct);

        return await GetStateAsync(jobId, ct);
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
               AND (s.depends_on_progress_percent IS NULL OR d.status='COMPLETED')
             ORDER BY s.progress_percent DESC
             LIMIT 1;
            """,
            new { jobId }, tx);

        if (stage is null)
        {
            await StartReadyVideosAsync(conn, tx, jobId);
            return;
        }

        var attempt = await conn.QuerySingleAsync<int>(
            "UPDATE timelapse.timelapse_image_stages SET active_attempt=active_attempt+1, status='RENDERING', started_at=now(), updated_at=now() WHERE id=@id RETURNING active_attempt;",
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

    private async Task StartReadyVideosAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction tx, Guid jobId)
    {
        var incompleteImages = await conn.QuerySingleAsync<int>(
            "SELECT count(*) FROM timelapse.timelapse_image_stages WHERE job_id=@jobId AND status <> 'COMPLETED';",
            new { jobId }, tx);
        if (incompleteImages > 0)
        {
            return;
        }

        await conn.ExecuteAsync(
            """
            UPDATE render.render_jobs
               SET status=@status, updated_at=now()
             WHERE id=@jobId AND tenant_id=@tenant;
            """,
            new { jobId, tenant = _tenant.TenantId, status = TimelapseParentStatuses.GeneratingVideos }, tx);

        var clips = (await conn.QueryAsync<(Guid Id, int ClipIndex)>(
            """
            SELECT id AS Id, clip_index AS ClipIndex
              FROM timelapse.timelapse_video_clips
             WHERE job_id=@jobId
               AND status IN ('WAITING','FAILED','INVALIDATED')
             ORDER BY clip_index;
            """,
            new { jobId }, tx)).ToList();

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
               SET status='INVALIDATED', result_media_id=NULL, object_key=NULL, public_url=NULL, updated_at=now()
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

    private async Task<TimelapseWorkflowState> ReadStateAsync(System.Data.IDbConnection conn, Guid jobId, System.Data.IDbTransaction? tx = null)
    {
        var parent = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT status FROM render.render_jobs WHERE id=@jobId AND tenant_id=@tenant;",
            new { jobId, tenant = _tenant.TenantId }, tx) ?? TimelapseParentStatuses.Draft;

        var images = (await conn.QueryAsync<TimelapseStageImage>(
            """
            SELECT stage_index AS StageIndex,
                   progress_percent AS ProgressPercent,
                   is_original AS IsOriginal,
                   depends_on_progress_percent AS DependsOnProgressPercent,
                   status AS Status,
                   active_attempt AS Attempt,
                   result_media_id AS MediaId,
                   public_url AS PublicUrl,
                   object_key AS ObjectKey,
                   provider_task_id AS ProviderTaskId,
                   error_message AS ErrorMessage
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
                   error_message AS ErrorMessage
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
        var canEdit = !hasActive && TimelapseParentStatuses.IsEditableStopped(parent);
        var canStart = !hasActive
                       && !string.Equals(parent, TimelapseParentStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                       && (!images.Any() || images.Any(x => x.Status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Failed or TimelapseOperationStatuses.Invalidated)
                           || videos.Any(x => x.Status is TimelapseOperationStatuses.Waiting or TimelapseOperationStatuses.Failed or TimelapseOperationStatuses.Invalidated));

        return new TimelapseWorkflowState
        {
            ParentStatus = NormalizeParentStatus(parent),
            Images = images,
            Videos = videos,
            FinalOutput = final,
            HasActiveOperations = hasActive,
            CanEditRequest = canEdit,
            CanStartRender = canStart,
            CanFinalize = videosReady && !hasActive && final?.Status != TimelapseOperationStatuses.Completed,
            GeneratedImageCount = images.Count(x => !x.IsOriginal),
            CurrentStep = BuildCurrentStep(images, videos, final)
        };
    }

    private static string NormalizeParentStatus(string? status)
        => status switch
        {
            RenderJobStatuses.Draft => TimelapseParentStatuses.Draft,
            RenderJobStatuses.Failed => TimelapseParentStatuses.Failed,
            RenderJobStatuses.Completed => TimelapseParentStatuses.Completed,
            "paused" => TimelapseParentStatuses.Paused,
            _ when string.Equals(status, TimelapseParentStatuses.GeneratingImages, StringComparison.OrdinalIgnoreCase) => TimelapseParentStatuses.GeneratingImages,
            _ when string.Equals(status, TimelapseParentStatuses.GeneratingVideos, StringComparison.OrdinalIgnoreCase) => TimelapseParentStatuses.GeneratingVideos,
            _ when string.Equals(status, TimelapseParentStatuses.Finalizing, StringComparison.OrdinalIgnoreCase) => TimelapseParentStatuses.Finalizing,
            _ => status?.ToUpperInvariant() ?? TimelapseParentStatuses.Draft
        };

    private static string BuildCurrentStep(
        IReadOnlyList<TimelapseStageImage> images,
        IReadOnlyList<TimelapseVideoClip> videos,
        TimelapseFinalOutput? final)
    {
        var image = images.FirstOrDefault(x => TimelapseOperationStatuses.IsActive(x.Status));
        if (image is not null)
        {
            return $"Đang tạo ảnh {image.ProgressPercent}%";
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

        return "Đang chờ thao tác";
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
}
