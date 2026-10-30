using System.Data;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Services.DanceSell;

namespace TodoX.Web.Services;

public interface IAdminJobMonitorService
{
    Task<AdminJobMonitorPage> GetJobsAsync(
        CurrentUserSession user,
        AdminJobMonitorQuery? query = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AdminJobMonitorScene>> GetScenesAsync(
        CurrentUserSession user,
        string kind,
        Guid jobId,
        CancellationToken ct = default);

    Task<AdminJobMonitorFilterOptions> GetFilterOptionsAsync(
        CurrentUserSession user,
        CancellationToken ct = default);
}

/// <summary>
/// Read-only administrator view of customer jobs. A job is the unit the customer created:
/// the RVideo core job, the Timelapse job or the RDance job. Worker rows in render.render_jobs
/// (merge, scene image/video/audio, dance attempts) are never listed.
/// </summary>
public sealed class AdminJobMonitorService : IAdminJobMonitorService
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public AdminJobMonitorService(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<AdminJobMonitorPage> GetJobsAsync(
        CurrentUserSession user,
        AdminJobMonitorQuery? query = null,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var normalized = AdminJobMonitorRules.NormalizeQuery(query);
        await _tenant.EnsureLoadedAsync(ct);
        using var connection = await _factory.OpenAsync(ct);

        var rows = (await connection.QueryAsync<JobRow>(new CommandDefinition(
            AdminJobMonitorSql.JobsPage,
            new
            {
                tenant = _tenant.TenantId,
                kind = normalized.Kind,
                status = normalized.Status,
                search = normalized.Search is null ? null : $"%{normalized.Search}%",
                service = normalized.Service,
                userId = normalized.UserId,
                fromUtc = normalized.FromUtc,
                toUtc = normalized.ToUtc,
                limit = normalized.PageSize,
                offset = (normalized.Page - 1) * normalized.PageSize
            },
            cancellationToken: ct))).ToList();

        var rvideoIds = IdsOfKind(rows, AdminJobMonitorKinds.RVideo);
        var rvideo = rvideoIds.Length == 0
            ? new Dictionary<Guid, RVideoTotalsRow>()
            : (await connection.QueryAsync<RVideoTotalsRow>(new CommandDefinition(
                AdminJobMonitorSql.RVideoTotals,
                new { tenant = _tenant.TenantId, ids = rvideoIds },
                cancellationToken: ct))).GroupBy(x => x.JobId).ToDictionary(x => x.Key, x => x.First());

        var timelapseIds = IdsOfKind(rows, AdminJobMonitorKinds.Timelapse);
        var timelapse = timelapseIds.Length == 0
            ? new Dictionary<Guid, int>()
            : (await connection.QueryAsync<TimelapseCountRow>(new CommandDefinition(
                AdminJobMonitorSql.TimelapseSceneCounts,
                new { tenant = _tenant.TenantId, ids = timelapseIds },
                cancellationToken: ct))).ToDictionary(x => x.JobId, x => x.SceneCount);

        var danceIds = IdsOfKind(rows, AdminJobMonitorKinds.RDance);
        var dance = danceIds.Length == 0
            ? new Dictionary<Guid, decimal>()
            : await ReadDanceCreditsAsync(connection, danceIds, ct);

        return new AdminJobMonitorPage
        {
            Items = rows.Select(row =>
            {
                var totals = rvideo.GetValueOrDefault(row.Id);
                return new AdminJobMonitorJob
                {
                    Kind = row.Kind,
                    Id = row.Id,
                    Title = row.Title,
                    ThumbnailUrl = NullIfBlank(row.ThumbnailUrl) ?? NullIfBlank(totals?.FirstImageUrl),
                    ServiceName = row.ServiceName,
                    UserName = row.UserName,
                    UserEmail = row.UserEmail,
                    CreatedAt = row.CreatedAt,
                    Credits = row.Kind switch
                    {
                        AdminJobMonitorKinds.RVideo => totals?.Credits ?? 0m,
                        AdminJobMonitorKinds.RDance => dance.GetValueOrDefault(row.Id),
                        _ => row.ChargedPoints
                    },
                    Status = row.Status,
                    SceneCount = row.Kind switch
                    {
                        AdminJobMonitorKinds.RVideo => totals?.SceneCount ?? 0,
                        AdminJobMonitorKinds.RDance => 1,
                        _ => timelapse.GetValueOrDefault(row.Id)
                    },
                    ResultVideoUrl = NullIfBlank(row.ResultVideoUrl)
                };
            }).ToList(),
            TotalCount = rows.Count == 0 ? 0 : rows[0].TotalCount,
            UnfinishedCount = rows.Count == 0 ? 0 : rows[0].UnfinishedCount,
            Page = normalized.Page,
            PageSize = normalized.PageSize
        };
    }

    // Options come from the jobs the monitor lists, so every choice can return results.
    public async Task<AdminJobMonitorFilterOptions> GetFilterOptionsAsync(
        CurrentUserSession user,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        await _tenant.EnsureLoadedAsync(ct);
        using var connection = await _factory.OpenAsync(ct);
        var parameters = new { tenant = _tenant.TenantId };
        var services = await connection.QueryAsync<string>(new CommandDefinition(
            AdminJobMonitorSql.ServiceOptions, parameters, cancellationToken: ct));
        var users = await connection.QueryAsync<AdminJobMonitorUserOption>(new CommandDefinition(
            AdminJobMonitorSql.UserOptions, parameters, cancellationToken: ct));
        return new AdminJobMonitorFilterOptions { Services = services.ToList(), Users = users.ToList() };
    }

    public async Task<IReadOnlyList<AdminJobMonitorScene>> GetScenesAsync(
        CurrentUserSession user,
        string kind,
        Guid jobId,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        await _tenant.EnsureLoadedAsync(ct);
        using var connection = await _factory.OpenAsync(ct);
        var parameters = new { tenant = _tenant.TenantId, jobId };

        if (kind == AdminJobMonitorKinds.RDance)
        {
            var job = await connection.QuerySingleOrDefaultAsync<SceneRow>(new CommandDefinition(
                AdminJobMonitorSql.RDanceScene, parameters, cancellationToken: ct));
            if (job is null) return Array.Empty<AdminJobMonitorScene>();
            var credits = await ReadDanceCreditsAsync(connection, [jobId], ct);
            return [MapScene(job, credits.GetValueOrDefault(jobId))];
        }

        var sql = kind switch
        {
            AdminJobMonitorKinds.RVideo => AdminJobMonitorSql.RVideoScenes,
            AdminJobMonitorKinds.Timelapse => AdminJobMonitorSql.TimelapseScenes,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown job kind.")
        };
        var rows = await connection.QueryAsync<SceneRow>(new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.Select(row => MapScene(row, row.Credits)).ToList();
    }

    // Same rule the customer sees on My Jobs: points of the job's latest motion operation.
    private static async Task<Dictionary<Guid, decimal>> ReadDanceCreditsAsync(
        IDbConnection connection,
        Guid[] ids,
        CancellationToken ct)
    {
        var rows = await connection.QueryAsync<DanceOperationRow>(new CommandDefinition(
            AdminJobMonitorSql.RDanceLatestMotionOperations,
            new { ids, motion = DanceSellOperationTypes.MotionVideo },
            cancellationToken: ct));
        return rows.ToDictionary(
            row => row.DanceSellJobId,
            row => DanceSellPointDisplay.TryReadChargedPoints(new DanceSellProviderOperationDto
            {
                PricingSnapshotJson = row.PricingSnapshotJson,
                TodoxPointsCharged = row.TodoxPointsCharged
            }) ?? 0m);
    }

    private static AdminJobMonitorScene MapScene(SceneRow row, decimal? credits)
        => new()
        {
            ImageUrl = NullIfBlank(row.ImageUrl),
            VideoUrl = NullIfBlank(row.VideoUrl),
            Status = AdminJobMonitorRules.NormalizeSceneStatus(row.RawStatus),
            ErrorMessage = NullIfBlank(row.ErrorMessage),
            Credits = credits,
            CreatedAt = row.CreatedAt,
            CompletedAt = row.CompletedAt
        };

    private static Guid[] IdsOfKind(IEnumerable<JobRow> rows, string kind)
        => rows.Where(row => row.Kind == kind).Select(row => row.Id).ToArray();

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void EnsureAdmin(CurrentUserSession? user)
    {
        if (!AdminEndpointAuthorization.IsAdmin(user))
        {
            throw new UnauthorizedAccessException("Admin job monitoring requires an administrator session.");
        }
    }

    private sealed class JobRow
    {
        public string Kind { get; init; } = string.Empty;
        public Guid Id { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? ThumbnailUrl { get; init; }
        public string ServiceName { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string UserEmail { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
        public decimal ChargedPoints { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? ResultVideoUrl { get; init; }
        public long TotalCount { get; init; }
        public long UnfinishedCount { get; init; }
    }

    private sealed class RVideoTotalsRow
    {
        public Guid JobId { get; init; }
        public int SceneCount { get; init; }
        public decimal Credits { get; init; }
        public string? FirstImageUrl { get; init; }
    }

    private sealed class TimelapseCountRow
    {
        public Guid JobId { get; init; }
        public int SceneCount { get; init; }
    }

    private sealed class DanceOperationRow
    {
        public Guid DanceSellJobId { get; init; }
        public string? PricingSnapshotJson { get; init; }
        public decimal? TodoxPointsCharged { get; init; }
    }

    private sealed class SceneRow
    {
        public string? ImageUrl { get; init; }
        public string? VideoUrl { get; init; }
        public string? RawStatus { get; init; }
        public string? ErrorMessage { get; init; }
        public decimal? Credits { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
    }
}

public static class AdminJobMonitorRules
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static AdminJobMonitorQuery NormalizeQuery(AdminJobMonitorQuery? query)
    {
        query ??= new AdminJobMonitorQuery();
        return new AdminJobMonitorQuery
        {
            Page = Math.Clamp(query.Page, 1, 100_000),
            PageSize = Math.Clamp(query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            Kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind.Trim(),
            Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.Trim(),
            Service = string.IsNullOrWhiteSpace(query.Service) ? null : query.Service.Trim(),
            UserId = query.UserId,
            FromUtc = query.FromUtc,
            ToUtc = query.ToUtc
        };
    }

    public static DateTime LocalDayStartUtc(DateOnly day, TimeZoneInfo? timeZone = null)
        => TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), timeZone ?? TimeZoneInfo.Local);

    /// <summary>Exclusive end of the local day, so the whole "to" day is included.</summary>
    public static DateTime LocalDayEndUtc(DateOnly day, TimeZoneInfo? timeZone = null)
        => LocalDayStartUtc(day.AddDays(1), timeZone);

    /// <summary>Page numbers to show around the current page; 0 marks a gap between numbers.</summary>
    public static IReadOnlyList<int> PageWindow(int current, int total, int neighbours = 2)
    {
        if (total <= 1) return [1];
        var pages = new List<int>();
        var previous = 0;
        for (var page = 1; page <= total; page++)
        {
            if (page != 1 && page != total && Math.Abs(page - current) > neighbours) continue;
            if (page - previous == 2) pages.Add(previous + 1);
            else if (page - previous > 2) pages.Add(0);
            pages.Add(page);
            previous = page;
        }

        return pages;
    }

    public static string NormalizeSceneStatus(string? rawStatus)
        => (rawStatus?.Trim().ToLowerInvariant() ?? string.Empty) switch
        {
            "draft" or "image_ready" => AdminJobMonitorStatuses.Draft,
            "waiting" or "queued" or "video_queued" => AdminJobMonitorStatuses.Queued,
            "completed" or "video_ready" => AdminJobMonitorStatuses.Completed,
            "failed" or "timeout" => AdminJobMonitorStatuses.Failed,
            "cancelled" or "invalidated" => AdminJobMonitorStatuses.Cancelled,
            _ => AdminJobMonitorStatuses.Processing
        };

    public static string StatusLabel(string? status)
        => status switch
        {
            AdminJobMonitorStatuses.Draft => "Nháp",
            AdminJobMonitorStatuses.Queued => "Đang chờ",
            AdminJobMonitorStatuses.Paused => "Tạm dừng",
            AdminJobMonitorStatuses.Completed => "Hoàn thành",
            AdminJobMonitorStatuses.Failed => "Thất bại",
            AdminJobMonitorStatuses.Cancelled => "Đã hủy",
            _ => "Đang xử lý"
        };

    public static string KindLabel(string? kind)
        => kind switch
        {
            AdminJobMonitorKinds.RVideo => "RVideo",
            AdminJobMonitorKinds.RDance => "RDance",
            AdminJobMonitorKinds.Timelapse => "Timelapse",
            _ => "-"
        };
}

public static class AdminJobMonitorSql
{
    private const string JobsCte = """
        WITH jobs AS (
            SELECT 'rvideo' AS kind,
                   j.id,
                   j.created_at,
                   COALESCE(
                       NULLIF(NULLIF(regexp_replace(j.input_json->>'title', '^\s*[\[{].*$', '', 's'), ''), 'Prompt workspace'),
                       NULLIF(NULLIF(regexp_replace(p.title, '^\s*[\[{].*$', '', 's'), ''), 'Prompt workspace'),
                       NULLIF(left(regexp_replace(p.original_prompt, '^\s*[\[{].*$', '', 's'), 120), ''),
                       s.service_name,
                       '') AS title,
                   p.source_image_url AS thumbnail_url,
                   COALESCE(s.service_name, s.service_code, '') AS service_name,
                   j.user_id,
                   0::numeric AS charged_points,
                   p.final_video_url AS result_video_url,
                   CASE
                       WHEN lower(COALESCE(p.status, '')) = 'completed' THEN 'completed'
                       WHEN lower(COALESCE(p.status, '')) = 'failed' THEN 'failed'
                       WHEN lower(j.status) = 'cancelled' THEN 'cancelled'
                       WHEN lower(COALESCE(p.status, '')) IN ('rendering', 'ready_to_merge', 'merging') THEN 'processing'
                       WHEN lower(j.status) IN ('failed', 'timeout') THEN 'failed'
                       WHEN EXISTS (
                           SELECT 1
                             FROM video_render.video_project_scenes active
                            WHERE active.project_id = p.id
                              AND active.tenant_id = p.tenant_id
                              AND active.status IN ('video_queued', 'video_rendering')
                       ) THEN 'processing'
                       WHEN lower(j.status) = 'completed' THEN 'completed'
                       WHEN lower(j.status) = 'queued' THEN 'queued'
                       WHEN lower(j.status) = 'paused' THEN 'paused'
                       WHEN lower(j.status) = 'draft' THEN 'draft'
                       ELSE 'processing'
                   END AS status
              FROM render.render_jobs j
              LEFT JOIN catalog.services s ON s.id = j.service_id
              LEFT JOIN video_render.video_projects p ON p.core_job_id = j.id AND p.tenant_id = j.tenant_id
             WHERE j.tenant_id = @tenant
               AND j.job_type = 'core_service'
               AND lower(j.operation_type) = 'rvideo'

            UNION ALL

            SELECT 'timelapse',
                   j.id,
                   j.created_at,
                   COALESCE(NULLIF(j.input_json->>'title', ''), NULLIF(j.input_json->>'serviceName', ''), s.service_name, 'Timelapse'),
                   COALESCE(
                       NULLIF(j.input_json#>>'{originalImage,publicUrl}', ''),
                       (SELECT stage.public_url
                          FROM timelapse.timelapse_image_stages stage
                         WHERE stage.job_id = j.id AND stage.public_url IS NOT NULL
                         ORDER BY stage.is_original DESC, stage.stage_index
                         LIMIT 1)),
                   COALESCE(NULLIF(j.input_json->>'serviceName', ''), s.service_name, NULLIF(j.input_json->>'serviceCode', ''), ''),
                   j.user_id,
                   j.point_cost_charged,
                   (SELECT final.public_url
                      FROM timelapse.timelapse_final_outputs final
                     WHERE final.job_id = j.id AND final.status = 'COMPLETED'
                     ORDER BY final.version DESC
                     LIMIT 1),
                   CASE
                       WHEN lower(j.status) = 'completed' THEN 'completed'
                       WHEN lower(j.status) IN ('failed', 'timeout') THEN 'failed'
                       WHEN lower(j.status) = 'cancelled' THEN 'cancelled'
                       WHEN lower(j.status) = 'paused' THEN 'paused'
                       WHEN lower(j.status) = 'draft' THEN 'draft'
                       WHEN lower(j.status) = 'queued' THEN 'queued'
                       ELSE 'processing'
                   END
              FROM render.render_jobs j
              LEFT JOIN catalog.services s ON s.id = j.service_id
             WHERE j.tenant_id = @tenant
               AND j.job_type = 'timelapse'

            UNION ALL

            SELECT 'rdance',
                   d.id,
                   d.created_at,
                   COALESCE(NULLIF(d.title, ''), NULLIF(left(d.prompt, 120), ''), 'Video nhảy quảng cáo thời trang'),
                   COALESCE(NULLIF(d.prepared_reference_url, ''), NULLIF(d.character_image_url, '')),
                   'Video nhảy quảng cáo thời trang',
                   d.user_id,
                   0::numeric,
                   COALESCE(NULLIF(d.result_video_url, ''), NULLIF(d.result_url, '')),
                   CASE
                       WHEN lower(d.status) = 'completed' THEN 'completed'
                       WHEN lower(d.status) IN ('failed', 'timeout') THEN 'failed'
                       WHEN lower(d.status) = 'cancelled' THEN 'cancelled'
                       WHEN lower(d.status) = 'draft' THEN 'draft'
                       WHEN lower(d.status) = 'queued' THEN 'queued'
                       ELSE 'processing'
                   END
              FROM dance_sell.dance_sell_jobs d
             WHERE d.tenant_id = @tenant
        )
        """;

    public const string ServiceOptions = JobsCte + """

        SELECT DISTINCT service_name
          FROM jobs
         WHERE service_name <> ''
         ORDER BY service_name;
        """;

    public const string UserOptions = JobsCte + """

        SELECT u.id AS Id,
               CASE
                   WHEN COALESCE(u.email, '') = '' THEN COALESCE(NULLIF(u.full_name, ''), NULLIF(u.display_name, ''), u.username, u.id::text)
                   ELSE COALESCE(NULLIF(u.full_name, ''), NULLIF(u.display_name, ''), u.username, '') || ' (' || u.email || ')'
               END AS Label
          FROM auth.app_users u
         WHERE u.id IN (SELECT user_id FROM jobs)
         ORDER BY Label;
        """;

    // Unfinished jobs first, newest first inside each group. Failed and cancelled jobs are finished:
    // nothing is running for them any more.
    public const string JobsPage = JobsCte + """
        ,
        filtered AS (
            SELECT jobs.*,
                   COALESCE(NULLIF(u.full_name, ''), NULLIF(u.display_name, ''), u.username, '') AS user_name,
                   COALESCE(u.email, '') AS user_email,
                   jobs.status NOT IN ('completed', 'failed', 'cancelled') AS unfinished
              FROM jobs
              LEFT JOIN auth.app_users u ON u.id = jobs.user_id
             WHERE (@kind::text IS NULL OR jobs.kind = @kind)
               AND (@status::text IS NULL OR jobs.status = @status)
               AND (@service::text IS NULL OR jobs.service_name = @service)
               AND (@userId::uuid IS NULL OR jobs.user_id = @userId)
               AND (@fromUtc::timestamptz IS NULL OR jobs.created_at >= @fromUtc)
               AND (@toUtc::timestamptz IS NULL OR jobs.created_at < @toUtc)
               AND (@search::text IS NULL
                    OR jobs.id::text ILIKE @search
                    OR jobs.title ILIKE @search
                    OR jobs.service_name ILIKE @search
                    OR COALESCE(u.full_name, '') ILIKE @search
                    OR COALESCE(u.display_name, '') ILIKE @search
                    OR COALESCE(u.username, '') ILIKE @search
                    OR COALESCE(u.email, '') ILIKE @search)
        )
        SELECT kind AS Kind,
               id AS Id,
               title AS Title,
               thumbnail_url AS ThumbnailUrl,
               service_name AS ServiceName,
               user_name AS UserName,
               user_email AS UserEmail,
               created_at AS CreatedAt,
               charged_points AS ChargedPoints,
               status AS Status,
               result_video_url AS ResultVideoUrl,
               count(*) OVER () AS TotalCount,
               count(*) FILTER (WHERE unfinished) OVER () AS UnfinishedCount
          FROM filtered
         ORDER BY unfinished DESC, created_at DESC, id DESC
         LIMIT @limit OFFSET @offset;
        """;

    // Credits: the points of completed image/video/audio versions, as on My Jobs. The core job's own
    // point columns stay 0 because RVideo charges per scene version.
    public const string RVideoTotals = """
        SELECT p.core_job_id AS JobId,
               (SELECT count(*)::int
                  FROM video_render.video_project_scenes s
                 WHERE s.project_id = p.id AND s.tenant_id = p.tenant_id) AS SceneCount,
               (SELECT s.static_image_url
                  FROM video_render.video_project_scenes s
                 WHERE s.project_id = p.id AND s.tenant_id = p.tenant_id AND NULLIF(s.static_image_url, '') IS NOT NULL
                 ORDER BY s.scene_index, s.id
                 LIMIT 1) AS FirstImageUrl,
               COALESCE((SELECT SUM(v.charged_points) FROM video_render.scene_image_versions v
                          WHERE v.project_id = p.id AND v.tenant_id = p.tenant_id AND v.status = 'completed'), 0)
             + COALESCE((SELECT SUM(v.charged_points) FROM video_render.scene_video_versions v
                          WHERE v.project_id = p.id AND v.tenant_id = p.tenant_id AND v.status = 'completed'), 0)
             + COALESCE((SELECT SUM(v.charged_points) FROM video_render.scene_audio_versions v
                          WHERE v.project_id = p.id AND v.tenant_id = p.tenant_id AND v.status = 'completed'), 0) AS Credits
          FROM video_render.video_projects p
         WHERE p.tenant_id = @tenant
           AND p.core_job_id = ANY(@ids);
        """;

    public const string TimelapseSceneCounts = """
        SELECT j.id AS JobId,
               COALESCE(
                   NULLIF((SELECT count(*)::int FROM timelapse.timelapse_video_clips c WHERE c.job_id = j.id), 0),
                   CASE WHEN j.input_json->>'sceneCount' ~ '^[0-9]+$' THEN (j.input_json->>'sceneCount')::int END,
                   0) AS SceneCount
          FROM render.render_jobs j
         WHERE j.tenant_id = @tenant
           AND j.id = ANY(@ids);
        """;

    public const string RDanceLatestMotionOperations = """
        SELECT DISTINCT ON (o.dance_sell_job_id)
               o.dance_sell_job_id AS DanceSellJobId,
               o.pricing_snapshot_json::text AS PricingSnapshotJson,
               o.todox_points_charged AS TodoxPointsCharged
          FROM dance_sell.dance_sell_provider_operations o
         WHERE o.dance_sell_job_id = ANY(@ids)
           AND o.operation_type = @motion
         ORDER BY o.dance_sell_job_id, o.attempt_no DESC, o.created_at DESC;
        """;

    public const string RVideoScenes = """
        SELECT s.static_image_url AS ImageUrl,
               s.scene_video_url AS VideoUrl,
               s.status AS RawStatus,
               s.error_message AS ErrorMessage,
               COALESCE((SELECT SUM(v.charged_points) FROM video_render.scene_image_versions v
                          WHERE v.scene_id = s.id AND v.tenant_id = s.tenant_id AND v.status = 'completed'), 0)
             + COALESCE((SELECT SUM(v.charged_points) FROM video_render.scene_video_versions v
                          WHERE v.scene_id = s.id AND v.tenant_id = s.tenant_id AND v.status = 'completed'), 0)
             + COALESCE((SELECT SUM(v.charged_points) FROM video_render.scene_audio_versions v
                          WHERE v.scene_id = s.id AND v.tenant_id = s.tenant_id AND v.status = 'completed'), 0) AS Credits,
               s.created_at AS CreatedAt,
               selected.completed_at AS CompletedAt
          FROM video_render.video_projects p
          JOIN video_render.video_project_scenes s ON s.project_id = p.id AND s.tenant_id = p.tenant_id
          LEFT JOIN video_render.scene_video_versions selected ON selected.id = s.selected_video_version_id
         WHERE p.tenant_id = @tenant
           AND p.core_job_id = @jobId
         ORDER BY s.scene_index, s.id;
        """;

    public const string TimelapseScenes = """
        SELECT stage.public_url AS ImageUrl,
               clip.public_url AS VideoUrl,
               clip.status AS RawStatus,
               clip.error_message AS ErrorMessage,
               NULL::numeric AS Credits,
               clip.created_at AS CreatedAt,
               clip.completed_at AS CompletedAt
          FROM timelapse.timelapse_video_clips clip
          LEFT JOIN timelapse.timelapse_image_stages stage
            ON stage.job_id = clip.job_id AND stage.progress_percent = clip.start_progress_percent
         WHERE clip.tenant_id = @tenant
           AND clip.job_id = @jobId
         ORDER BY clip.clip_index;
        """;

    public const string RDanceScene = """
        SELECT COALESCE(NULLIF(d.prepared_reference_url, ''), NULLIF(d.character_image_url, '')) AS ImageUrl,
               COALESCE(NULLIF(d.result_video_url, ''), NULLIF(d.result_url, '')) AS VideoUrl,
               d.status AS RawStatus,
               d.error_message AS ErrorMessage,
               d.created_at AS CreatedAt,
               d.completed_at AS CompletedAt
          FROM dance_sell.dance_sell_jobs d
         WHERE d.tenant_id = @tenant
           AND d.id = @jobId;
        """;
}
