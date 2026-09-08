using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services;

public interface IAdminJobMonitorService
{
    Task<AdminJobMonitorPage> GetJobsAsync(
        CurrentUserSession user,
        AdminJobMonitorQuery? query = null,
        CancellationToken ct = default);

    Task<AdminJobMonitorStats> GetStatsAsync(
        CurrentUserSession user,
        AdminJobMonitorQuery? query = null,
        CancellationToken ct = default);

    Task<AdminJobMonitorFilterOptions> GetFilterOptionsAsync(
        CurrentUserSession user,
        CancellationToken ct = default);

    Task<AdminJobMonitorDetail?> GetDetailAsync(
        CurrentUserSession user,
        Guid jobId,
        CancellationToken ct = default);

    Task RecordImpersonationAuditAsync(
        CurrentUserSession admin,
        Guid targetCustomerUserId,
        Guid? targetCustomerId,
        Guid jobId,
        CancellationToken ct = default);
}

public sealed class AdminJobMonitorService : IAdminJobMonitorService
{
    private static readonly string[] ProcessingStatuses =
    [
        "pending",
        "queued",
        "preparing",
        "processing",
        "rendering",
        "post_processing",
        "pending_reconciliation",
        "submitted",
        "polling",
        "running",
        "generating",
        "generating_images",
        "generating_videos",
        "finalizing"
    ];

    private static readonly string[] CompletedStatuses = ["completed"];
    private static readonly string[] FailedStatuses = ["failed", "timeout"];

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly AuditRepository _audit;

    public AdminJobMonitorService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        AuditRepository audit)
    {
        _factory = factory;
        _tenant = tenant;
        _audit = audit;
    }

    public async Task<AdminJobMonitorPage> GetJobsAsync(
        CurrentUserSession user,
        AdminJobMonitorQuery? query = null,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var normalized = NormalizeQuery(query);
        await _tenant.EnsureLoadedAsync(ct);

        using var connection = await _factory.OpenAsync(ct);
        var parameters = CreateParameters(normalized);
        parameters.Add("tenant", _tenant.TenantId);
        parameters.Add("limit", normalized.PageSize);
        parameters.Add("offset", (normalized.Page - 1) * normalized.PageSize);
        var where = BuildWhere(normalized, parameters);

        var sql = $"""
            SELECT count(*)::bigint AS Total,
                   count(*) FILTER (WHERE lower(coalesce(r.status, '')) = ANY(@processingStatuses))::bigint AS Processing,
                   count(*) FILTER (WHERE lower(coalesce(r.status, '')) = ANY(@completedStatuses))::bigint AS Completed,
                   count(*) FILTER (WHERE lower(coalesce(r.status, '')) = ANY(@failedStatuses))::bigint AS Failed
              FROM render.render_jobs r
              LEFT JOIN catalog.services s ON s.id = r.service_id
              LEFT JOIN auth.app_users u ON u.id = r.user_id AND u.tenant_id = r.tenant_id
              LEFT JOIN crm.customers c ON c.id = r.customer_id AND c.tenant_id = r.tenant_id
            {where};

            SELECT r.id AS Id,
                   r.customer_id AS CustomerId,
                   r.user_id AS UserId,
                   COALESCE(NULLIF(c.company_name, ''), NULLIF(c.full_name, ''), '') AS CustomerName,
                   COALESCE(c.email, '') AS CustomerEmail,
                   COALESCE(NULLIF(u.full_name, ''), NULLIF(u.display_name, ''), u.username, '') AS AccountName,
                   COALESCE(u.email, '') AS AccountEmail,
                   COALESCE(r.job_type, '') AS JobType,
                   COALESCE(s.service_code, '') AS ServiceCode,
                   COALESCE(s.service_name, '') AS ServiceName,
                   COALESCE(r.status, '') AS Status,
                   COALESCE(r.current_step, '') AS CurrentStage,
                   COALESCE(r.progress_percent, 0) AS ProgressPercent,
                   r.input_json::text AS InputJson,
                   r.output_json::text AS OutputJson,
                   r.point_cost_estimate AS EstimatedPoints,
                   r.point_cost_charged AS ConsumedPoints,
                   r.provider_code AS ProviderCode,
                   r.model_code AS ModelCode,
                   r.created_at AS CreatedAt,
                   COALESCE(r.updated_at, r.created_at) AS UpdatedAt
              FROM render.render_jobs r
              LEFT JOIN catalog.services s ON s.id = r.service_id
              LEFT JOIN auth.app_users u ON u.id = r.user_id AND u.tenant_id = r.tenant_id
              LEFT JOIN crm.customers c ON c.id = r.customer_id AND c.tenant_id = r.tenant_id
            {where}
             ORDER BY {ResolveOrder(normalized.Sort)}
             LIMIT @limit OFFSET @offset;
            """;

        using var result = await connection.QueryMultipleAsync(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: ct));
        var statsRow = await result.ReadSingleAsync<StatsRow>();
        var rows = (await result.ReadAsync<JobRow>()).ToList();
        var danceOutputs = await TryGetDanceOutputsAsync(connection, rows.Select(row => row.Id), ct);
        var items = rows.Select(row => MapSummary(
            row,
            danceOutputs.TryGetValue(row.Id, out var resultVideoUrl)
                ? MergeDanceOutput(row.OutputJson, resultVideoUrl)
                : null)).ToList();

        return new AdminJobMonitorPage
        {
            Items = items,
            Stats = MapStats(statsRow),
            Query = normalized,
            TotalCount = statsRow.Total,
            Page = normalized.Page,
            PageSize = normalized.PageSize
        };
    }

    public async Task<AdminJobMonitorStats> GetStatsAsync(
        CurrentUserSession user,
        AdminJobMonitorQuery? query = null,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var normalized = NormalizeQuery(query);
        await _tenant.EnsureLoadedAsync(ct);

        using var connection = await _factory.OpenAsync(ct);
        var parameters = CreateParameters(normalized);
        parameters.Add("tenant", _tenant.TenantId);
        var where = BuildWhere(normalized, parameters);
        var row = await connection.QuerySingleAsync<StatsRow>(new CommandDefinition(
            $"""
            SELECT count(*)::bigint AS Total,
                   count(*) FILTER (WHERE lower(coalesce(r.status, '')) = ANY(@processingStatuses))::bigint AS Processing,
                   count(*) FILTER (WHERE lower(coalesce(r.status, '')) = ANY(@completedStatuses))::bigint AS Completed,
                   count(*) FILTER (WHERE lower(coalesce(r.status, '')) = ANY(@failedStatuses))::bigint AS Failed
              FROM render.render_jobs r
              LEFT JOIN catalog.services s ON s.id = r.service_id
              LEFT JOIN auth.app_users u ON u.id = r.user_id AND u.tenant_id = r.tenant_id
              LEFT JOIN crm.customers c ON c.id = r.customer_id AND c.tenant_id = r.tenant_id
            {where};
            """,
            parameters,
            cancellationToken: ct));
        return MapStats(row);
    }

    public async Task<AdminJobMonitorFilterOptions> GetFilterOptionsAsync(
        CurrentUserSession user,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        await _tenant.EnsureLoadedAsync(ct);
        using var connection = await _factory.OpenAsync(ct);

        var services = await connection.QueryAsync<AdminJobMonitorFilterOption>(new CommandDefinition(
            """
            SELECT DISTINCT COALESCE(NULLIF(s.service_code, ''), r.job_type) AS Value,
                            COALESCE(NULLIF(s.service_name, ''), NULLIF(s.service_code, ''), r.job_type) AS Label
              FROM render.render_jobs r
              LEFT JOIN catalog.services s ON s.id = r.service_id
             WHERE r.tenant_id = @tenant
             ORDER BY Label, Value;
            """,
            new { tenant = _tenant.TenantId },
            cancellationToken: ct));

        var statuses = new[]
        {
            new AdminJobMonitorFilterOption { Value = "pending", Label = "Pending" },
            new AdminJobMonitorFilterOption { Value = "processing", Label = "Processing" },
            new AdminJobMonitorFilterOption { Value = "completed", Label = "Completed" },
            new AdminJobMonitorFilterOption { Value = "failed", Label = "Failed" }
        };

        return new AdminJobMonitorFilterOptions
        {
            Services = services.ToList(),
            Statuses = statuses
        };
    }

    public async Task<AdminJobMonitorDetail?> GetDetailAsync(
        CurrentUserSession user,
        Guid jobId,
        CancellationToken ct = default)
    {
        EnsureAdmin(user);
        await _tenant.EnsureLoadedAsync(ct);
        using var connection = await _factory.OpenAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<JobDetailRow>(new CommandDefinition(
            """
            SELECT r.id AS Id,
                   r.customer_id AS CustomerId,
                   r.user_id AS UserId,
                   COALESCE(NULLIF(c.company_name, ''), NULLIF(c.full_name, ''), '') AS CustomerName,
                   COALESCE(c.email, '') AS CustomerEmail,
                   COALESCE(NULLIF(u.full_name, ''), NULLIF(u.display_name, ''), u.username, '') AS AccountName,
                   COALESCE(u.email, '') AS AccountEmail,
                   COALESCE(r.job_type, '') AS JobType,
                   COALESCE(s.service_code, '') AS ServiceCode,
                   COALESCE(s.service_name, '') AS ServiceName,
                   COALESCE(r.status, '') AS Status,
                   COALESCE(r.current_step, '') AS CurrentStage,
                   COALESCE(r.progress_percent, 0) AS ProgressPercent,
                   r.input_json::text AS InputJson,
                   r.prompt_json::text AS PromptJson,
                   r.reference_json::text AS ReferenceJson,
                   COALESCE(r.options, '{}'::jsonb)::text AS OptionsJson,
                   r.output_json::text AS OutputJson,
                   r.error_code AS ErrorCode,
                   r.error_message AS ErrorMessage,
                   r.point_cost_estimate AS EstimatedPoints,
                   r.point_cost_charged AS ConsumedPoints,
                   COALESCE(r.point_status, '') AS PointStatus,
                   r.provider_code AS ProviderCode,
                   r.model_code AS ModelCode,
                   r.retry_of_job_id AS RetryOfJobId,
                   r.started_at AS StartedAt,
                   r.completed_at AS CompletedAt,
                   r.cancelled_at AS CancelledAt,
                   r.created_at AS CreatedAt,
                   COALESCE(r.updated_at, r.created_at) AS UpdatedAt
              FROM render.render_jobs r
              LEFT JOIN catalog.services s ON s.id = r.service_id
              LEFT JOIN auth.app_users u ON u.id = r.user_id AND u.tenant_id = r.tenant_id
              LEFT JOIN crm.customers c ON c.id = r.customer_id AND c.tenant_id = r.tenant_id
             WHERE r.tenant_id = @tenant
               AND r.id = @jobId
             LIMIT 1;
            """,
            new { tenant = _tenant.TenantId, jobId },
            cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        var dance = await TryGetDanceSellAsync(connection, jobId, ct);
        var outputJson = MergeDanceOutput(row.OutputJson, dance?.ResultVideoUrl);
        var summary = MapSummary(row, outputJson);
        var inputLinks = JsonUrlExtractor.Extract(row.InputJson)
            .Concat(JsonUrlExtractor.Extract(row.ReferenceJson))
            .Concat(ExtractDanceInputs(dance))
            .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var outputLinks = JsonUrlExtractor.Extract(outputJson)
            .Concat(ExtractDanceOutputs(dance))
            .DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var events = (await connection.QueryAsync<JobEventRow>(new CommandDefinition(
            """
            SELECT e.id AS Id,
                   e.event_type AS EventType,
                   COALESCE(e.level, 'info') AS Level,
                   e.message AS Message,
                   COALESCE(e.data_json, '{}'::jsonb)::text AS DataJson,
                   e.provider_code AS ProviderCode,
                   e.model_code AS ModelCode,
                   e.created_at AS CreatedAt
              FROM render.render_job_events e
             WHERE e.tenant_id = @tenant
               AND e.job_id = @jobId
             ORDER BY e.created_at, e.id;
            """,
            new { tenant = _tenant.TenantId, jobId },
            cancellationToken: ct))).ToList();

        return new AdminJobMonitorDetail
        {
            Overview = new AdminJobMonitorOverview
            {
                Summary = summary,
                ErrorCode = row.ErrorCode ?? dance?.ErrorCode,
                ErrorMessage = row.ErrorMessage ?? dance?.ErrorMessage,
                AspectRatio = ReadFirstString(row.InputJson, "aspectRatio", "ratio")
                    ?? ReadFirstString(row.OptionsJson, "aspectRatio", "ratio")
                    ?? dance?.Ratio,
                DurationSeconds = ReadFirstInt(row.InputJson, "durationSeconds", "duration")
                    ?? ReadFirstInt(row.OptionsJson, "durationSeconds", "duration"),
                StartedAt = row.StartedAt,
                CompletedAt = row.CompletedAt,
                CancelledAt = row.CancelledAt,
                PointStatus = row.PointStatus,
                RetryOfJobId = row.RetryOfJobId?.ToString()
            },
            Timeline = BuildTimeline(row, events),
            InputOutput = new AdminJobMonitorInputOutput
            {
                InputJson = row.InputJson,
                PromptJson = row.PromptJson,
                ReferenceJson = row.ReferenceJson,
                OptionsJson = row.OptionsJson,
                OutputJson = outputJson,
                InputLinks = inputLinks,
                OutputLinks = outputLinks
            },
            Logs = events.Select(e => new AdminJobMonitorLogItem
            {
                Timestamp = e.CreatedAt,
                Service = row.ServiceCode,
                ProviderCode = e.ProviderCode ?? row.ProviderCode ?? dance?.ProviderCode,
                ModelCode = e.ModelCode ?? row.ModelCode ?? dance?.ProviderModel,
                EventType = e.EventType,
                Level = e.Level,
                Message = e.Message,
                DataJson = e.DataJson
            }).ToList()
        };
    }

    public async Task RecordImpersonationAuditAsync(
        CurrentUserSession admin,
        Guid targetCustomerUserId,
        Guid? targetCustomerId,
        Guid jobId,
        CancellationToken ct = default)
    {
        EnsureAdmin(admin);
        ct.ThrowIfCancellationRequested();
        await _tenant.EnsureLoadedAsync(ct);
        using var connection = await _factory.OpenAsync(ct);
        var target = await connection.QuerySingleOrDefaultAsync<ImpersonationTarget>(new CommandDefinition(
            """
            SELECT customer_id AS CustomerId,
                   user_id AS UserId
              FROM render.render_jobs
             WHERE tenant_id = @tenant
               AND id = @jobId
             LIMIT 1;
            """,
            new { tenant = _tenant.TenantId, jobId },
            cancellationToken: ct));
        if (target is null
            || target.UserId != targetCustomerUserId
            || target.CustomerId != targetCustomerId)
        {
            throw new InvalidOperationException("The impersonation target does not match the selected job.");
        }

        await _audit.LogAsync(
            admin,
            "admin_job_monitor",
            "impersonate_job",
            entityDisplay: jobId.ToString(),
            message: $"TargetUserId={targetCustomerUserId}; TargetCustomerId={targetCustomerId?.ToString() ?? "unknown"}; JobId={jobId}.");
    }

    private static void EnsureAdmin(CurrentUserSession? user)
    {
        if (!AdminEndpointAuthorization.IsAdmin(user))
        {
            throw new UnauthorizedAccessException("Admin job monitoring requires an administrator session.");
        }
    }

    private static AdminJobMonitorQuery NormalizeQuery(AdminJobMonitorQuery? query)
    {
        query ??= new AdminJobMonitorQuery();
        return new AdminJobMonitorQuery
        {
            Page = Math.Clamp(query.Page, 1, 100_000),
            PageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 100),
            Search = Normalize(query.Search),
            Service = Normalize(query.Service),
            Status = Normalize(query.Status),
            FromUtc = query.FromUtc,
            ToUtc = query.ToUtc,
            Sort = Normalize(query.Sort) ?? "newest"
        };
    }

    private static DynamicParameters CreateParameters(AdminJobMonitorQuery query)
    {
        var parameters = new DynamicParameters();
        parameters.Add("processingStatuses", ProcessingStatuses);
        parameters.Add("completedStatuses", CompletedStatuses);
        parameters.Add("failedStatuses", FailedStatuses);
        return parameters;
    }

    private static string BuildWhere(AdminJobMonitorQuery query, DynamicParameters parameters)
    {
        var where = new StringBuilder(" WHERE r.tenant_id = @tenant");

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add("search", $"%{query.Search}%");
            where.Append("""
                 AND (
                     r.id::text ILIKE @search
                     OR COALESCE(c.company_name, '') ILIKE @search
                     OR COALESCE(c.full_name, '') ILIKE @search
                     OR COALESCE(c.email, '') ILIKE @search
                     OR COALESCE(u.username, '') ILIKE @search
                     OR COALESCE(u.full_name, '') ILIKE @search
                     OR COALESCE(u.email, '') ILIKE @search
                     OR COALESCE(s.service_code, '') ILIKE @search
                     OR COALESCE(s.service_name, '') ILIKE @search
                 )
                """);
        }

        if (!string.IsNullOrWhiteSpace(query.Service))
        {
            parameters.Add("service", query.Service);
            where.Append("""
                 AND (
                     upper(COALESCE(s.service_code, '')) = upper(@service)
                     OR upper(COALESCE(s.service_name, '')) = upper(@service)
                     OR upper(COALESCE(r.job_type, '')) = upper(@service)
                 )
                """);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var statuses = ExpandStatusFilter(query.Status);
            parameters.Add("statusFilter", statuses);
            where.Append(" AND lower(COALESCE(r.status, '')) = ANY(@statusFilter)");
        }

        if (query.FromUtc is not null)
        {
            parameters.Add("fromUtc", query.FromUtc.Value);
            where.Append(" AND r.created_at >= @fromUtc");
        }

        if (query.ToUtc is not null)
        {
            parameters.Add("toUtc", query.ToUtc.Value);
            where.Append(" AND r.created_at < @toUtc");
        }

        return where.ToString();
    }

    private static string[] ExpandStatusFilter(string status)
        => status.Trim().ToLowerInvariant() switch
        {
            "pending" => ["draft", "pending", "queued"],
            "processing" => ProcessingStatuses,
            "completed" => CompletedStatuses,
            "failed" => FailedStatuses,
            _ => [status.Trim().ToLowerInvariant()]
        };

    private static string ResolveOrder(string? sort)
        => sort?.Trim().ToLowerInvariant() switch
        {
            "oldest" => "r.created_at ASC, r.id ASC",
            "updated" => "COALESCE(r.updated_at, r.created_at) DESC, r.id DESC",
            _ => "r.created_at DESC, r.id DESC"
        };

    private static AdminJobMonitorStats MapStats(StatsRow row)
        => new()
        {
            Total = row.Total,
            Processing = row.Processing,
            Completed = row.Completed,
            Failed = row.Failed
        };

    private static AdminJobMonitorJobSummary MapSummary(JobRow row, string? outputJson = null)
    {
        var outputLinks = JsonUrlExtractor.Extract(outputJson ?? row.OutputJson);
        return new AdminJobMonitorJobSummary
        {
            Id = row.Id,
            CustomerId = row.CustomerId,
            UserId = row.UserId,
            CustomerName = row.CustomerName,
            CustomerEmail = row.CustomerEmail,
            AccountName = row.AccountName,
            AccountEmail = row.AccountEmail,
            JobType = row.JobType,
            ServiceCode = row.ServiceCode,
            ServiceName = row.ServiceName,
            Status = row.Status,
            CurrentStage = row.CurrentStage,
            ProgressPercent = Math.Clamp(row.ProgressPercent, 0, 100),
            ThumbnailUrl = PickThumbnail(outputLinks),
            VideoUrl = outputLinks.FirstOrDefault(x => x.Kind == "video")?.Url
                ?? outputLinks.FirstOrDefault()?.Url,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            EstimatedPoints = row.EstimatedPoints,
            ConsumedPoints = row.ConsumedPoints,
            ProviderCode = row.ProviderCode,
            ModelCode = row.ModelCode
        };
    }

    private static IReadOnlyList<AdminJobMonitorTimelineItem> BuildTimeline(
        JobDetailRow job,
        IReadOnlyList<JobEventRow> events)
    {
        var points = new List<AdminJobMonitorTimelineItem>
        {
            new()
            {
                Stage = "Created",
                EventType = "JOB_CREATED",
                Level = "info",
                Status = "completed",
                Timestamp = job.CreatedAt,
                Message = "Job created.",
                ProviderCode = job.ProviderCode,
                ModelCode = job.ModelCode
            }
        };

        points.AddRange(events.Select(e => new AdminJobMonitorTimelineItem
        {
            Stage = ResolveStage(e.EventType, e.Message),
            EventType = e.EventType,
            Level = e.Level,
            Status = ResolveEventStatus(e.Level, e.EventType),
            Message = e.Message,
            Timestamp = e.CreatedAt,
            ProviderCode = e.ProviderCode ?? job.ProviderCode,
            ModelCode = e.ModelCode ?? job.ModelCode
        }));

        for (var index = 0; index < points.Count; index++)
        {
            var end = index + 1 < points.Count
                ? points[index + 1].Timestamp
                : job.CompletedAt ?? job.UpdatedAt;
            points[index] = new AdminJobMonitorTimelineItem
            {
                Stage = points[index].Stage,
                EventType = points[index].EventType,
                Level = points[index].Level,
                Status = points[index].Status,
                Message = points[index].Message,
                Timestamp = points[index].Timestamp,
                Duration = end >= points[index].Timestamp ? end - points[index].Timestamp : null,
                ProviderCode = points[index].ProviderCode,
                ModelCode = points[index].ModelCode
            };
        }

        return points;
    }

    private static string ResolveStage(string? eventType, string? message)
    {
        var value = $"{eventType} {message}".ToLowerInvariant();
        if (value.Contains("voice") || value.Contains("audio")) return "Voice Processing";
        if (value.Contains("render") || value.Contains("video")) return "Video Rendering";
        if (value.Contains("image") || value.Contains("reference")) return "Image Generated";
        if (value.Contains("output") || value.Contains("complete") || value.Contains("finish")) return "Final Output";
        return string.IsNullOrWhiteSpace(eventType) ? "Processing" : eventType!;
    }

    private static string ResolveEventStatus(string? level, string? eventType)
        => string.Equals(level, "error", StringComparison.OrdinalIgnoreCase)
            || (eventType?.Contains("fail", StringComparison.OrdinalIgnoreCase) ?? false)
            ? "failed"
            : "completed";

    private static string? PickThumbnail(IEnumerable<AdminJobMonitorMediaLink> links)
    {
        var list = links.ToList();
        return list.FirstOrDefault(x => x.Source?.Contains("thumbnail", StringComparison.OrdinalIgnoreCase) == true)?.Url
            ?? list.FirstOrDefault(x => x.Source?.Contains("poster", StringComparison.OrdinalIgnoreCase) == true)?.Url
            ?? list.FirstOrDefault(x => x.Kind == "image")?.Url
            ?? list.FirstOrDefault()?.Url;
    }

    private async Task<DanceSellRow?> TryGetDanceSellAsync(
        IDbConnection connection,
        Guid renderJobId,
        CancellationToken ct)
    {
        try
        {
            return await connection.QuerySingleOrDefaultAsync<DanceSellRow>(new CommandDefinition(
                """
                SELECT id AS Id,
                       status AS Status,
                       current_stage AS CurrentStage,
                       result_video_url AS ResultVideoUrl,
                       character_image_url AS CharacterImageUrl,
                       product_image_url AS ProductImageUrl,
                       motion_video_url AS MotionVideoUrl,
                       COALESCE(NULLIF(request_json->>'ratio', ''), '9:16') AS Ratio,
                       provider_code AS ProviderCode,
                       provider_model AS ProviderModel,
                       motion_provider_code AS MotionProviderCode,
                       motion_provider_model AS MotionProviderModel,
                       error_code AS ErrorCode,
                       error_message AS ErrorMessage
                  FROM dance_sell.dance_sell_jobs
                 WHERE tenant_id = @tenant
                   AND render_job_id = @renderJobId
                 LIMIT 1;
                """,
                new { tenant = _tenant.TenantId, renderJobId },
                cancellationToken: ct));
        }
        catch (PostgresException ex) when (
            ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>> TryGetDanceOutputsAsync(
        IDbConnection connection,
        IEnumerable<Guid> renderJobIds,
        CancellationToken ct)
    {
        var ids = renderJobIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();

        try
        {
            var rows = await connection.QueryAsync<DanceSellOutputRow>(new CommandDefinition(
                """
                SELECT render_job_id AS RenderJobId,
                       result_video_url AS ResultVideoUrl
                  FROM dance_sell.dance_sell_jobs
                 WHERE tenant_id = @tenant
                   AND render_job_id = ANY(@renderJobIds)
                   AND result_video_url IS NOT NULL
                   AND result_video_url <> '';
                """,
                new { tenant = _tenant.TenantId, renderJobIds = ids },
                cancellationToken: ct));

            return rows
                .Where(row => IsHttpUrl(row.ResultVideoUrl))
                .GroupBy(row => row.RenderJobId)
                .ToDictionary(group => group.Key, group => group.First().ResultVideoUrl!);
        }
        catch (PostgresException ex) when (
            ex.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.UndefinedColumn)
        {
            return new Dictionary<Guid, string>();
        }
    }

    private static IEnumerable<AdminJobMonitorMediaLink> ExtractDanceInputs(DanceSellRow? dance)
    {
        if (dance is null) return Array.Empty<AdminJobMonitorMediaLink>();
        return new[] { dance.CharacterImageUrl, dance.ProductImageUrl, dance.MotionVideoUrl }
            .Where(IsHttpUrl)
            .Select(url => new AdminJobMonitorMediaLink
            {
                Url = url!,
                Kind = url!.Contains("video", StringComparison.OrdinalIgnoreCase) ? "video" : "image",
                Source = "dance_sell"
            });
    }

    private static IEnumerable<AdminJobMonitorMediaLink> ExtractDanceOutputs(DanceSellRow? dance)
        => IsHttpUrl(dance?.ResultVideoUrl)
            ? new[]
            {
                new AdminJobMonitorMediaLink
                {
                    Url = dance!.ResultVideoUrl!,
                    Kind = "video",
                    Source = "resultVideoUrl"
                }
            }
            : Array.Empty<AdminJobMonitorMediaLink>();

    private static string MergeDanceOutput(string outputJson, string? resultVideoUrl)
    {
        if (!IsHttpUrl(resultVideoUrl)) return outputJson;
        var links = JsonUrlExtractor.Extract(outputJson).ToList();
        return links.Any(x => string.Equals(x.Url, resultVideoUrl, StringComparison.OrdinalIgnoreCase))
            ? outputJson
            : JsonSerializer.Serialize(new
            {
                existing = ParseJsonOrString(outputJson),
                resultVideoUrl
            });
    }

    private static object ParseJsonOrString(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string? ReadFirstString(string json, params string[] names)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            foreach (var name in names)
            {
                var value = FindProperty(document.RootElement, name);
                if (value?.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.Value.GetString()))
                {
                    return value.Value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static int? ReadFirstInt(string json, params string[] names)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            foreach (var name in names)
            {
                var value = FindProperty(document.RootElement, name);
                if (value?.ValueKind == JsonValueKind.Number && value.Value.TryGetInt32(out var number))
                {
                    return number;
                }
                if (value?.ValueKind == JsonValueKind.String
                    && int.TryParse(value.Value.GetString(), out number))
                {
                    return number;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value;
                }

                var nested = FindProperty(property.Value, name);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindProperty(item, name);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private sealed class StatsRow
    {
        public long Total { get; init; }
        public long Processing { get; init; }
        public long Completed { get; init; }
        public long Failed { get; init; }
    }

    private sealed class ImpersonationTarget
    {
        public Guid? CustomerId { get; init; }
        public Guid? UserId { get; init; }
    }

    private class JobRow
    {
        public Guid Id { get; init; }
        public Guid? CustomerId { get; init; }
        public Guid? UserId { get; init; }
        public string CustomerName { get; init; } = string.Empty;
        public string CustomerEmail { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public string AccountEmail { get; init; } = string.Empty;
        public string JobType { get; init; } = string.Empty;
        public string ServiceCode { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string CurrentStage { get; init; } = string.Empty;
        public int ProgressPercent { get; init; }
        public string InputJson { get; init; } = "{}";
        public string OutputJson { get; init; } = "{}";
        public decimal EstimatedPoints { get; init; }
        public decimal ConsumedPoints { get; init; }
        public string? ProviderCode { get; init; }
        public string? ModelCode { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class JobDetailRow : JobRow
    {
        public string PromptJson { get; init; } = "{}";
        public string ReferenceJson { get; init; } = "[]";
        public string OptionsJson { get; init; } = "{}";
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public string PointStatus { get; init; } = string.Empty;
        public Guid? RetryOfJobId { get; init; }
        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public DateTime? CancelledAt { get; init; }
    }

    private sealed class JobEventRow
    {
        public Guid Id { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string Level { get; init; } = "info";
        public string? Message { get; init; }
        public string DataJson { get; init; } = "{}";
        public string? ProviderCode { get; init; }
        public string? ModelCode { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    private sealed class DanceSellRow
    {
        public Guid Id { get; init; }
        public string? Status { get; init; }
        public string? CurrentStage { get; init; }
        public string? ResultVideoUrl { get; init; }
        public string? CharacterImageUrl { get; init; }
        public string? ProductImageUrl { get; init; }
        public string? MotionVideoUrl { get; init; }
        public string? Ratio { get; init; }
        public string? ProviderCode { get; init; }
        public string? ProviderModel { get; init; }
        public string? MotionProviderCode { get; init; }
        public string? MotionProviderModel { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
    }

    private sealed class DanceSellOutputRow
    {
        public Guid RenderJobId { get; init; }
        public string? ResultVideoUrl { get; init; }
    }

    private static class JsonUrlExtractor
    {
        public static IReadOnlyList<AdminJobMonitorMediaLink> Extract(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<AdminJobMonitorMediaLink>();
            try
            {
                using var document = JsonDocument.Parse(json);
                var links = new List<AdminJobMonitorMediaLink>();
                Visit(document.RootElement, null, links);
                return links
                    .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<AdminJobMonitorMediaLink>();
            }
        }

        private static void Visit(
            JsonElement element,
            string? source,
            ICollection<AdminJobMonitorMediaLink> links)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Visit(property.Value, property.Name, links);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Visit(item, source, links);
                    }
                    break;
                case JsonValueKind.String:
                    var value = element.GetString();
                    if (!IsHttpUrl(value)) return;
                    links.Add(new AdminJobMonitorMediaLink
                    {
                        Url = value!,
                        Kind = Classify(source),
                        Source = source
                    });
                    break;
            }
        }

        private static string Classify(string? source)
        {
            var value = source?.ToLowerInvariant() ?? string.Empty;
            if (value.Contains("audio") || value.Contains("sound")) return "audio";
            if (value.Contains("video") || value.Contains("movie")) return "video";
            if (value.Contains("image") || value.Contains("thumbnail") || value.Contains("poster") || value.Contains("photo")) return "image";
            return "url";
        }
    }
}
