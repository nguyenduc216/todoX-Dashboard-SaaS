using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.AiCharacters;
using TodoX.Web.Services.DanceSell;
using TodoX.Web.Services.Platform;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services;

public interface ICustomerDashboardService
{
    Task<CustomerDashboardDto> GetDashboardAsync(CurrentUserSession user, int recentLimit = 5, CancellationToken ct = default);
}

public sealed class CustomerDashboardDto
{
    public int ProcessingCount { get; init; }
    public int CompletedThisMonthCount { get; init; }
    public int CharacterCount { get; init; }
    public IReadOnlyList<CustomerRecentJobDto> RecentJobs { get; init; } = Array.Empty<CustomerRecentJobDto>();
}

public sealed class CustomerRecentJobDto
{
    public string Id { get; init; } = string.Empty;
    public string JobType { get; init; } = string.Empty;
    public string Workflow { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string StatusLabel { get; init; } = string.Empty;
    public int? ProgressPercent { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string DetailRoute { get; init; } = string.Empty;
}

public sealed class CustomerDashboardService : ICustomerDashboardService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly IAiCharacterService _characters;

    public CustomerDashboardService(
        TodoXConnectionFactory factory,
        TenantContext tenant,
        IAiCharacterService characters)
    {
        _factory = factory;
        _tenant = tenant;
        _characters = characters;
    }

    public async Task<CustomerDashboardDto> GetDashboardAsync(CurrentUserSession user, int recentLimit = 5, CancellationToken ct = default)
    {
        if (user is not { IsAuthenticated: true, IsCustomer: true } || user.CustomerId is null)
        {
            return new CustomerDashboardDto();
        }

        await _tenant.EnsureLoadedAsync(ct);
        var month = CurrentApplicationMonthUtcRange();
        using var conn = await _factory.OpenAsync(ct);
        var processingStatuses = CustomerDashboardStatusRules.ProcessingStatuses.Select(x => x.ToLowerInvariant()).ToArray();

        var renderCounts = await conn.QuerySingleAsync<DashboardCountsRow>(new CommandDefinition(
            """
            SELECT count(*) FILTER (WHERE lower(status) = ANY(@processingStatuses))::int AS ProcessingCount,
                   count(*) FILTER (WHERE lower(status)=@completedStatus AND completed_at >= @monthStartUtc AND completed_at < @monthEndUtc)::int AS CompletedThisMonthCount
              FROM render.render_jobs
             WHERE tenant_id = @tenantId
               AND customer_id = @customerId
               AND job_type = ANY(@renderJobTypes);
            """,
            new
            {
                tenantId = _tenant.TenantId,
                customerId = user.CustomerId,
                processingStatuses,
                completedStatus = RenderJobStatuses.Completed,
                month.MonthStartUtc,
                month.MonthEndUtc,
                renderJobTypes = CustomerDashboardWorkflowRules.SupportedRenderJobTypes
            },
            cancellationToken: ct));

        var danceCounts = await conn.QuerySingleAsync<DashboardCountsRow>(new CommandDefinition(
            """
            SELECT count(*) FILTER (WHERE lower(status) = ANY(@processingStatuses))::int AS ProcessingCount,
                   count(*) FILTER (WHERE lower(status)=@completedStatus AND completed_at >= @monthStartUtc AND completed_at < @monthEndUtc)::int AS CompletedThisMonthCount
             FROM dance_sell.dance_sell_jobs
             WHERE tenant_id = @tenantId
               AND customer_id = @customerId;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                customerId = user.CustomerId,
                processingStatuses,
                completedStatus = DanceSellJobStatuses.Completed,
                month.MonthStartUtc,
                month.MonthEndUtc
            },
            cancellationToken: ct));

        var recentRender = (await conn.QueryAsync<RecentRenderJobRow>(new CommandDefinition(
            """
            SELECT r.id AS Id,
                   r.job_type AS JobType,
                   r.operation_type AS OperationType,
                   r.status AS Status,
                   r.current_step AS CurrentStep,
                   r.progress_percent AS ProgressPercent,
                   r.input_json::text AS InputJson,
                   r.created_at AS CreatedAt,
                   COALESCE(r.updated_at, r.created_at) AS UpdatedAt,
                   s.name AS ServiceName,
                   s.service_code AS ServiceCode
              FROM render.render_jobs r
              LEFT JOIN catalog.services s ON s.id = r.service_id
             WHERE r.tenant_id = @tenantId
               AND r.customer_id = @customerId
               AND r.job_type = ANY(@renderJobTypes)
             ORDER BY COALESCE(r.updated_at, r.created_at) DESC, r.created_at DESC, r.id DESC
             LIMIT @limit;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                customerId = user.CustomerId,
                renderJobTypes = CustomerDashboardWorkflowRules.SupportedRenderJobTypes,
                limit = Math.Clamp(recentLimit, 1, 20)
            },
            cancellationToken: ct))).ToList();

        var recentDance = (await conn.QueryAsync<RecentDanceJobRow>(new CommandDefinition(
            """
            SELECT id AS Id,
                   status AS Status,
                   current_stage AS CurrentStage,
                   title AS Title,
                   prompt AS Prompt,
                   created_at AS CreatedAt,
                   COALESCE(updated_at, created_at) AS UpdatedAt
             FROM dance_sell.dance_sell_jobs
             WHERE tenant_id = @tenantId
               AND customer_id = @customerId
             ORDER BY COALESCE(updated_at, created_at) DESC, created_at DESC, id DESC
             LIMIT @limit;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                customerId = user.CustomerId,
                limit = Math.Clamp(recentLimit, 1, 20)
            },
            cancellationToken: ct))).ToList();

        var characters = await _characters.GetActiveCharactersAsync(user);

        var recentJobs = recentRender
            .Select(MapRenderJob)
            .Concat(recentDance.Select(MapDanceJob))
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(recentLimit, 1, 20))
            .ToList();

        return new CustomerDashboardDto
        {
            ProcessingCount = renderCounts.ProcessingCount + danceCounts.ProcessingCount,
            CompletedThisMonthCount = renderCounts.CompletedThisMonthCount + danceCounts.CompletedThisMonthCount,
            CharacterCount = characters.Count,
            RecentJobs = recentJobs
        };
    }

    internal static (DateTime MonthStartUtc, DateTime MonthEndUtc) CurrentApplicationMonthUtcRange(DateTime? nowUtc = null, TimeZoneInfo? timeZone = null)
    {
        timeZone ??= TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc ?? DateTime.UtcNow, timeZone);
        var localStart = new DateTime(localNow.Year, localNow.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var localEnd = localStart.AddMonths(1);
        return (
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), DateTimeKind.Utc),
            DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone), DateTimeKind.Utc));
    }

    private static CustomerRecentJobDto MapRenderJob(RecentRenderJobRow row)
    {
        var routeKind = CustomerDashboardWorkflowRules.ResolveRenderRouteKind(row.JobType, row.OperationType, row.ServiceCode);
        var workflow = CustomerDashboardWorkflowRules.ResolveRenderWorkflowLabel(routeKind);
        var title = routeKind == CustomerDashboardRenderRouteKind.RVideo
            ? ReadCorePayloadString(row.InputJson, "title") ?? ReadJsonString(row.InputJson, "title")
            : ReadJsonString(row.InputJson, "title") ?? ReadCorePayloadString(row.InputJson, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            title = row.ServiceName ?? row.ServiceCode ?? workflow;
        }

        return new CustomerRecentJobDto
        {
            Id = row.Id.ToString(),
            JobType = CustomerDashboardWorkflowRules.ResolveRenderJobType(routeKind),
            Workflow = workflow,
            Title = title,
            Status = row.Status,
            StatusLabel = routeKind == CustomerDashboardRenderRouteKind.Timelapse
                ? TimelapseStatusText.Parent(row.Status)
                : CustomerDashboardStatusRules.Label(row.Status, row.CurrentStep),
            ProgressPercent = NormalizeProgress(row.ProgressPercent),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            DetailRoute = CustomerDashboardWorkflowRules.ResolveRenderDetailRoute(routeKind, row.Id)
        };
    }

    private static CustomerRecentJobDto MapDanceJob(RecentDanceJobRow row)
        => new()
        {
            Id = row.Id.ToString(),
            JobType = TodoXServiceEngineTypes.RDance,
            Workflow = "Video nhảy quảng cáo thời trang",
            Title = !string.IsNullOrWhiteSpace(row.Title)
                ? row.Title!
                : !string.IsNullOrWhiteSpace(row.Prompt) ? row.Prompt! : "Video nhảy quảng cáo thời trang",
            Status = row.Status,
            StatusLabel = CustomerDashboardStatusRules.Label(row.Status, row.CurrentStage),
            ProgressPercent = null,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            DetailRoute = $"/jobs/rdance/{row.Id}"
        };

    private static int? NormalizeProgress(int progress)
        => progress is > 0 and <= 100 ? progress : null;

    private static string? ReadCorePayloadString(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.TryGetProperty("payload", out var payload))
            {
                return ReadJsonString(payload, propertyName);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? ReadJsonString(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return ReadJsonString(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private sealed class DashboardCountsRow
    {
        public int ProcessingCount { get; init; }
        public int CompletedThisMonthCount { get; init; }
    }

    private sealed class RecentRenderJobRow
    {
        public Guid Id { get; init; }
        public string JobType { get; init; } = string.Empty;
        public string? OperationType { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? CurrentStep { get; init; }
        public int ProgressPercent { get; init; }
        public string InputJson { get; init; } = "{}";
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string? ServiceName { get; init; }
        public string? ServiceCode { get; init; }
    }

    private sealed class RecentDanceJobRow
    {
        public Guid Id { get; init; }
        public string Status { get; init; } = string.Empty;
        public string? CurrentStage { get; init; }
        public string? Title { get; init; }
        public string? Prompt { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}

public enum CustomerDashboardRenderRouteKind
{
    Timelapse,
    RVideo,
    RDance,
    Unknown
}

public static class CustomerDashboardWorkflowRules
{
    public static readonly string[] SupportedRenderJobTypes =
    [
        RenderJobTypes.Timelapse,
        RenderJobTypes.CoreService
    ];

    public static CustomerDashboardRenderRouteKind ResolveRenderRouteKind(string? jobType, string? operationType, string? serviceCode)
    {
        if (string.Equals(jobType, TodoX.Web.Services.Render.RenderJobTypes.Timelapse, StringComparison.OrdinalIgnoreCase)
            || string.Equals(serviceCode, Timelapse.ConstructionTimelapseAdapter.ConstructionServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerDashboardRenderRouteKind.Timelapse;
        }

        if (string.Equals(jobType, TodoX.Web.Services.Render.RenderJobTypes.CoreService, StringComparison.OrdinalIgnoreCase)
            && string.Equals(operationType, TodoXServiceEngineTypes.RVideo, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerDashboardRenderRouteKind.RVideo;
        }

        if (string.Equals(jobType, TodoX.Web.Services.Render.RenderJobTypes.DanceSell, StringComparison.OrdinalIgnoreCase))
        {
            return CustomerDashboardRenderRouteKind.RDance;
        }

        return CustomerDashboardRenderRouteKind.Unknown;
    }

    public static string ResolveRenderJobType(CustomerDashboardRenderRouteKind kind)
        => kind switch
        {
            CustomerDashboardRenderRouteKind.RVideo => TodoXServiceEngineTypes.RVideo,
            CustomerDashboardRenderRouteKind.RDance => TodoXServiceEngineTypes.RDance,
            _ => TodoXServiceEngineTypes.Timelapse
        };

    public static string ResolveRenderWorkflowLabel(CustomerDashboardRenderRouteKind kind)
        => kind switch
        {
            CustomerDashboardRenderRouteKind.Timelapse => "Video Timelapse AI",
            CustomerDashboardRenderRouteKind.RVideo => "rVideo",
            CustomerDashboardRenderRouteKind.RDance => "rDance",
            _ => "Video"
        };

    public static string ResolveRenderDetailRoute(CustomerDashboardRenderRouteKind kind, Guid id)
        => kind switch
        {
            CustomerDashboardRenderRouteKind.Timelapse => $"/jobs/timelapse/{id}",
            CustomerDashboardRenderRouteKind.RVideo => $"/jobs/rvideo/{id}",
            CustomerDashboardRenderRouteKind.RDance => $"/jobs/rdance/{id}",
            _ => $"/jobs/{id}"
        };
}

public static class CustomerDashboardStatusRules
{
    public static readonly string[] ProcessingStatuses =
    {
        RenderJobStatuses.Queued,
        RenderJobStatuses.Preparing,
        RenderJobStatuses.Rendering,
        RenderJobStatuses.PostProcessing,
        RenderJobStatuses.PendingReconciliation,
        RenderJobStatuses.Processing,
        VideoProjectStatuses.ReadyToMerge,
        VideoProjectStatuses.Merging,
        "pending",
        "submitted",
        "polling",
        "running",
        "generating",
        DanceSellJobStatuses.Queued,
        DanceSellJobStatuses.Submitted,
        DanceSellJobStatuses.Rendering,
        TimelapseParentStatuses.GeneratingImages,
        TimelapseParentStatuses.GeneratingVideos,
        TimelapseParentStatuses.Finalizing
    };

    private static readonly HashSet<string> ProcessingSet = new(ProcessingStatuses, StringComparer.OrdinalIgnoreCase);

    public static bool IsProcessingStatus(string? status)
        => !string.IsNullOrWhiteSpace(status) && ProcessingSet.Contains(status);

    public static bool IsCompletedStatus(string? status)
        => string.Equals(status, RenderJobStatuses.Completed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, DanceSellJobStatuses.Completed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, TimelapseParentStatuses.Completed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, VideoProjectStatuses.Completed, StringComparison.OrdinalIgnoreCase);

    public static bool IsFailedStatus(string? status)
        => string.Equals(status, RenderJobStatuses.Failed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, DanceSellJobStatuses.Failed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, DanceSellJobStatuses.Timeout, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, TimelapseParentStatuses.Failed, StringComparison.OrdinalIgnoreCase)
           || string.Equals(status, VideoProjectStatuses.Failed, StringComparison.OrdinalIgnoreCase);

    public static string Label(string? status, string? currentStep = null)
        => status?.ToLowerInvariant() switch
        {
            RenderJobStatuses.Completed or DanceSellJobStatuses.Completed => "Hoàn thành",
            RenderJobStatuses.Failed or DanceSellJobStatuses.Failed => "Thất bại",
            DanceSellJobStatuses.Timeout => "Quá thời gian",
            RenderJobStatuses.Cancelled => "Đã dừng",
            RenderJobStatuses.Queued or DanceSellJobStatuses.Queued => "Đang chờ",
            RenderJobStatuses.Rendering or DanceSellJobStatuses.Rendering => "Đang xử lý",
            RenderJobStatuses.PostProcessing => "Đang hoàn thiện",
            RenderJobStatuses.PendingReconciliation => "Chờ đối soát",
            RenderJobStatuses.Draft => "Bản nháp",
            _ when !string.IsNullOrWhiteSpace(currentStep) => currentStep!,
            _ => string.IsNullOrWhiteSpace(status) ? "Chưa bắt đầu" : status!
        };
}
