using System.Text.Json;
using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
using TodoX.Web.Models.Timelapse;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.Timelapse;

public interface ITimelapseJobService
{
    Task<TimelapseJobView> CreateDraftAsync(
        TimelapseCreateRequest request,
        byte[] originalImageContent,
        string originalImageFileName,
        string originalImageContentType,
        CurrentUserSession currentUser,
        CancellationToken ct = default);

    Task<TimelapseJobView?> GetOwnedAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<IReadOnlyList<TimelapseJobView>> ListOwnedAsync(CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseJobView> StartOrResumeAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseJobView> RetryImageAsync(Guid jobId, int progressPercent, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseJobView> RetryVideoAsync(Guid jobId, int clipIndex, CurrentUserSession currentUser, CancellationToken ct = default);
    Task<TimelapseJobView> StartFinalizerAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct = default);
}

public sealed class TimelapseJobService : ITimelapseJobService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CatalogRepository _catalog;
    private readonly ITimelapseProfileRepository _profiles;
    private readonly IMediaFileService _media;
    private readonly IRenderJobService _renderJobs;
    private readonly IServiceSellPriceResolver _sellPrices;
    private readonly ITimelapseWorkflowService _workflow;
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ILogger<TimelapseJobService> _logger;

    public TimelapseJobService(
        CatalogRepository catalog,
        ITimelapseProfileRepository profiles,
        IMediaFileService media,
        IRenderJobService renderJobs,
        IServiceSellPriceResolver sellPrices,
        ITimelapseWorkflowService workflow,
        TodoXConnectionFactory factory,
        TenantContext tenant,
        ILogger<TimelapseJobService> logger)
    {
        _catalog = catalog;
        _profiles = profiles;
        _media = media;
        _renderJobs = renderJobs;
        _sellPrices = sellPrices;
        _workflow = workflow;
        _factory = factory;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<TimelapseJobView> CreateDraftAsync(
        TimelapseCreateRequest request,
        byte[] originalImageContent,
        string originalImageFileName,
        string originalImageContentType,
        CurrentUserSession currentUser,
        CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);

        var errors = TimelapseRequestRules.Validate(request, originalImageContent.Length > 0);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        if (!request.ServiceId.HasValue || request.ServiceId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Vui lòng chọn dịch vụ trước khi tạo video.");
        }

        var service = await _catalog.GetServiceByIdAsync(request.ServiceId.Value, ct);
        if (service is null)
        {
            throw new InvalidOperationException("Dịch vụ đã chọn không tồn tại.");
        }

        if (!service.Enabled)
        {
            throw new InvalidOperationException("Dịch vụ này đang tạm ngưng.");
        }

        if (!string.Equals(service.ServiceType, TodoXServiceEngineTypes.Timelapse, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Dịch vụ đã chọn không thuộc nhóm Timelapse.");
        }

        if (!string.IsNullOrWhiteSpace(request.ServiceCode)
            && !string.Equals(request.ServiceCode, service.ServiceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Dịch vụ đã chọn không khớp với mã dịch vụ.");
        }

        var qualityTier = TimelapseSellPricing.QualityTierForMode(request.VideoMode);
        var sellPrice = await _sellPrices.ResolveVideoScenePriceAsync(
            service.Id,
            qualityTier,
            TimelapseRequestRules.RuntimeClipDurationSeconds,
            ct);
        if (!sellPrice.Found || sellPrice.Price is null)
        {
            throw new InvalidOperationException(sellPrice.Message ?? "Chưa cấu hình giá cho lựa chọn này.");
        }

        var videoSubtotal = TimelapseSellPricing.EstimateVideoSubtotal(sellPrice.Price.SellPoints, request.SceneCount);

        var profile = await _profiles.GetEnabledProfileAsync(request.ProfileCode, ct);
        if (profile is null)
        {
            throw new InvalidOperationException("Loại công trình không hợp lệ hoặc đã bị tắt.");
        }

        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(
            originalImageContent,
            originalImageFileName,
            originalImageContentType,
            "timelapse_original_image",
            currentUser.UserId,
            currentUser.CustomerId,
            _tenant.TenantId,
            ct);

        var snapshot = new TimelapseJobSnapshot
        {
            ServiceId = service.Id,
            ServiceCode = service.ServiceCode,
            ProfileCode = profile.ProfileCode,
            ProfileName = profile.ProfileName,
            SceneCount = request.SceneCount,
            ProgressMapping = TimelapseRequestRules.GetProgressMapping(request.SceneCount),
            VideoMode = request.VideoMode.Trim().ToLowerInvariant(),
            Ratio = request.Ratio.Trim().ToLowerInvariant(),
            Title = NormalizeTitle(request.Title),
            SellPrice = new TimelapseSellPriceSnapshot
            {
                QualityTier = qualityTier,
                RuntimeClipDurationSeconds = TimelapseRequestRules.RuntimeClipDurationSeconds,
                SceneCount = request.SceneCount,
                VideoSceneSellPoints = sellPrice.Price.SellPoints,
                VideoSubtotal = videoSubtotal,
                TotalPoints = videoSubtotal
            },
            OriginalImage = new TimelapseOriginalImageSnapshot
            {
                MediaId = media.Id,
                ObjectKey = media.ObjectKey,
                PublicUrl = media.PublicUrl ?? media.FileUrl,
                MimeType = media.MimeType
            }
        };

        var job = await _renderJobs.EnqueueAsync(
            new RenderJobCreateModel
            {
                UserId = currentUser.UserId,
                CustomerId = currentUser.CustomerId,
                JobType = RenderJobTypes.Timelapse,
                InitialStatus = RenderJobStatuses.Draft,
                Input = snapshot,
                References = new[]
                {
                    new
                    {
                        role = "original_image",
                        mediaId = media.Id,
                        media.ObjectKey,
                        url = media.PublicUrl ?? media.FileUrl,
                        media.MimeType
                    }
                },
                PointStatus = RenderPointStatuses.NotRequired,
                MaxAttempts = 1
            },
            ct);

        await _renderJobs.AddEventAsync(
            job.Id,
            "TIMELAPSE_DRAFT_CREATED",
            "Timelapse draft saved. Rendering has not started.",
            new { snapshot.ProfileCode, snapshot.SceneCount, snapshot.VideoMode, snapshot.Ratio },
            ct: ct);

        return new TimelapseJobView
        {
            Id = job.Id,
            Status = job.Status,
            CreatedAt = job.CreatedAt,
            Snapshot = snapshot
        };
    }

    public async Task<TimelapseJobView?> GetOwnedAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);

        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<OwnedTimelapseJobRow>(
            SelectJobByIdSql,
            new
            {
                jobId
            });
        if (row is null)
        {
            LogGetOwnedMiss(jobId, currentUser, null, "not_found");
            return null;
        }

        if (row.TenantId != _tenant.TenantId)
        {
            LogGetOwnedMiss(jobId, currentUser, row, "tenant_mismatch");
            throw new UnauthorizedAccessException("You do not have access to this Timelapse job.");
        }

        if (!string.Equals(row.JobType, RenderJobTypes.Timelapse, StringComparison.OrdinalIgnoreCase))
        {
            LogGetOwnedMiss(jobId, currentUser, row, "job_type_mismatch");
            return null;
        }

        if (!TimelapseJobAccess.CanRead(row.UserId, row.CustomerId, currentUser))
        {
            LogGetOwnedMiss(jobId, currentUser, row, "ownership_mismatch");
            throw new UnauthorizedAccessException("You do not have access to this Timelapse job.");
        }

        var view = ToView(row, currentUser);
        view.Workflow = await _workflow.GetStateAsync(jobId, ct);
        return view;
    }

    public async Task<IReadOnlyList<TimelapseJobView>> ListOwnedAsync(CurrentUserSession currentUser, CancellationToken ct = default)
    {
        EnsureCustomer(currentUser);
        await _tenant.EnsureLoadedAsync(ct);

        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<OwnedTimelapseJobRow>(
            SelectOwnedJobSql +
            """
             ORDER BY created_at DESC
             LIMIT 100;
            """,
            new
            {
                tenantId = _tenant.TenantId,
                customerId = currentUser.CustomerId,
                jobType = RenderJobTypes.Timelapse
            });
        return rows.Select(row => ToView(row, currentUser)).ToList();
    }

    public async Task<TimelapseJobView> StartOrResumeAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        var view = await RequireOwnedAsync(jobId, currentUser, ct);
        view.Workflow = await _workflow.StartOrResumeAsync(jobId, view.Snapshot, currentUser, ct);
        view.Status = view.Workflow.ParentStatus;
        return view;
    }

    public async Task<TimelapseJobView> RetryImageAsync(Guid jobId, int progressPercent, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        var view = await RequireOwnedAsync(jobId, currentUser, ct);
        view.Workflow = await _workflow.RetryImageAsync(jobId, progressPercent, view.Snapshot, currentUser, ct);
        view.Status = view.Workflow.ParentStatus;
        return view;
    }

    public async Task<TimelapseJobView> RetryVideoAsync(Guid jobId, int clipIndex, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        var view = await RequireOwnedAsync(jobId, currentUser, ct);
        view.Workflow = await _workflow.RetryVideoAsync(jobId, clipIndex, view.Snapshot, currentUser, ct);
        view.Status = view.Workflow.ParentStatus;
        return view;
    }

    public async Task<TimelapseJobView> StartFinalizerAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct = default)
    {
        var view = await RequireOwnedAsync(jobId, currentUser, ct);
        view.Workflow = await _workflow.StartFinalizerAsync(jobId, view.Snapshot, currentUser, ct);
        view.Status = view.Workflow.ParentStatus;
        return view;
    }

    private async Task<TimelapseJobView> RequireOwnedAsync(Guid jobId, CurrentUserSession currentUser, CancellationToken ct)
    {
        var view = await GetOwnedAsync(jobId, currentUser, ct);
        return view ?? throw new InvalidOperationException("Không tìm thấy job.");
    }

    private static TimelapseJobView ToView(OwnedTimelapseJobRow row, CurrentUserSession currentUser)
    {
        if (!TimelapseJobAccess.CanRead(row.UserId, row.CustomerId, currentUser))
        {
            throw new UnauthorizedAccessException("You do not have access to this Timelapse job.");
        }

        var snapshot = JsonSerializer.Deserialize<TimelapseJobSnapshot>(row.InputJson, JsonOptions);
        if (snapshot is null
            || !string.Equals(snapshot.Engine, TodoXServiceEngineTypes.Timelapse, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Timelapse job snapshot is invalid.");
        }

        return new TimelapseJobView
        {
            Id = row.Id,
            Status = row.Status,
            CreatedAt = row.CreatedAt,
            Snapshot = snapshot
        };
    }

    private static void EnsureCustomer(CurrentUserSession currentUser)
    {
        if (currentUser is not { IsAuthenticated: true, IsCustomer: true } || currentUser.CustomerId is null)
        {
            throw new UnauthorizedAccessException("Customer authentication is required.");
        }
    }

    private static string NormalizeTitle(string? title)
        => string.IsNullOrWhiteSpace(title) ? "Video Timelapse" : title.Trim();

    private sealed class OwnedTimelapseJobRow
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }
        public Guid? UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public string JobType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string InputJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
    }

    private void LogGetOwnedMiss(Guid jobId, CurrentUserSession currentUser, OwnedTimelapseJobRow? row, string reason)
        => _logger.LogWarning(
            "TIMELAPSE_JOB_GET_OWNED_MISS reason={Reason} jobId={JobId} currentTenantId={CurrentTenantId} currentUserId={CurrentUserId} currentCustomerId={CurrentCustomerId} persistedTenantId={PersistedTenantId} persistedUserId={PersistedUserId} persistedCustomerId={PersistedCustomerId} persistedJobType={PersistedJobType}",
            reason,
            jobId,
            _tenant.TenantId,
            currentUser.UserId,
            currentUser.CustomerId,
            row?.TenantId,
            row?.UserId,
            row?.CustomerId,
            row?.JobType);

    private const string SelectJobByIdSql =
        """
        SELECT id AS Id,
               tenant_id AS TenantId,
               user_id AS UserId,
               customer_id AS CustomerId,
               job_type AS JobType,
               status AS Status,
               input_json::text AS InputJson,
               created_at AS CreatedAt
          FROM render.render_jobs
         WHERE id=@jobId
         LIMIT 1;
        """;

    private const string SelectOwnedJobSql =
        """
        SELECT id AS Id,
               tenant_id AS TenantId,
               user_id AS UserId,
               customer_id AS CustomerId,
               job_type AS JobType,
               status AS Status,
               input_json::text AS InputJson,
               created_at AS CreatedAt
          FROM render.render_jobs
         WHERE tenant_id=@tenantId
           AND customer_id IS NOT DISTINCT FROM @customerId
           AND job_type=@jobType
        """;
}
