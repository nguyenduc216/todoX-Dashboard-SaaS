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
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public TimelapseJobService(
        CatalogRepository catalog,
        ITimelapseProfileRepository profiles,
        IMediaFileService media,
        IRenderJobService renderJobs,
        IServiceSellPriceResolver sellPrices,
        TodoXConnectionFactory factory,
        TenantContext tenant)
    {
        _catalog = catalog;
        _profiles = profiles;
        _media = media;
        _renderJobs = renderJobs;
        _sellPrices = sellPrices;
        _factory = factory;
        _tenant = tenant;
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

        var timelapseServices = (await _catalog.GetActiveCatalogServicesAsync())
            .Where(x => string.Equals(x.ServiceType, TodoXServiceEngineTypes.Timelapse, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SortOrder)
            .ToList();
        var service = request.ServiceId.HasValue
            ? timelapseServices.SingleOrDefault(x => x.Id == request.ServiceId.Value)
            : !string.IsNullOrWhiteSpace(request.ServiceCode)
                ? timelapseServices.SingleOrDefault(x => string.Equals(x.ServiceCode, request.ServiceCode, StringComparison.OrdinalIgnoreCase))
                : timelapseServices.FirstOrDefault();
        if (service is null || !service.Enabled)
        {
            throw new InvalidOperationException("Dịch vụ Timelapse hiện chưa khả dụng.");
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
            throw new InvalidOperationException("Loáº¡i cÃ´ng trÃ¬nh khÃ´ng há»£p lá»‡ hoáº·c Ä‘Ã£ bá»‹ táº¯t.");
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
            SelectOwnedJobSql + " AND id=@jobId LIMIT 1;",
            new
            {
                jobId,
                tenantId = _tenant.TenantId,
                userId = currentUser.UserId,
                customerId = currentUser.CustomerId,
                jobType = RenderJobTypes.Timelapse
            });
        return row is null ? null : ToView(row, currentUser);
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
                userId = currentUser.UserId,
                customerId = currentUser.CustomerId,
                jobType = RenderJobTypes.Timelapse
            });
        return rows.Select(row => ToView(row, currentUser)).ToList();
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
        public Guid? UserId { get; set; }
        public Guid? CustomerId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string InputJson { get; set; } = "{}";
        public DateTime CreatedAt { get; set; }
    }

    private const string SelectOwnedJobSql =
        """
        SELECT id AS Id,
               user_id AS UserId,
               customer_id AS CustomerId,
               status AS Status,
               input_json::text AS InputJson,
               created_at AS CreatedAt
          FROM render.render_jobs
         WHERE tenant_id=@tenantId
           AND user_id=@userId
           AND customer_id=@customerId
           AND job_type=@jobType
        """;
}
