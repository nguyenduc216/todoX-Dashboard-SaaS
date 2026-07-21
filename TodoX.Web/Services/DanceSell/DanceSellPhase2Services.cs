using System.Text.Json;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TodoX.Web.Models;
using TodoX.Web.Services.AiProviders.Kie;
using TodoX.Web.Services.Media;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.Reup;

namespace TodoX.Web.Services.DanceSell;

public sealed class DanceSellPhase2Options
{
    public const string SectionName = "DanceSell";
    public int MaxImageMb { get; set; } = 20;
    public int MaxVideoMb { get; set; } = 500;
    public string[] AllowedImageTypes { get; set; } = { "image/png", "image/jpeg", "image/webp" };
    public string[] AllowedVideoTypes { get; set; } = { "video/mp4" };
    public string DefaultMode { get; set; } = "720p";
    public string DefaultOrientation { get; set; } = "image";
}

public interface IDanceSellMotionSourceService
{
    Task<MediaFileDto> SaveUploadedVideoAsync(byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<MediaFileDto> StageTikTokAsync(string sourceUrl, CurrentUserSession user, CancellationToken ct = default);
    bool IsValidTikTokUrl(string sourceUrl);
    string ToProviderUrl(string? publicUrl);
}

public sealed class DanceSellMotionSourceService : IDanceSellMotionSourceService
{
    private readonly IMediaFileService _media;
    private readonly TikwmVideoResolver _tikwm;
    private readonly TenantContext _tenant;
    private readonly IConfiguration _config;
    private readonly IOptionsMonitor<DanceSellPhase2Options> _options;

    public DanceSellMotionSourceService(
        IMediaFileService media,
        TikwmVideoResolver tikwm,
        TenantContext tenant,
        IConfiguration config,
        IOptionsMonitor<DanceSellPhase2Options> options)
    {
        _media = media;
        _tikwm = tikwm;
        _tenant = tenant;
        _config = config;
        _options = options;
    }

    public async Task<MediaFileDto> SaveUploadedVideoAsync(byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        ValidateVideo(content, fileName, contentType);
        await _tenant.EnsureLoadedAsync(ct);
        var objectKey = BuildObjectKey(user.CustomerId, "motion-upload", ".mp4");
        return await _media.SaveBinaryAtObjectKeyAsync(content, objectKey, fileName, "video/mp4", "dance_sell_motion", user.UserId, user.CustomerId, _tenant.TenantId, ct);
    }

    public async Task<MediaFileDto> StageTikTokAsync(string sourceUrl, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        if (!IsValidTikTokUrl(sourceUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_TIKTOK_URL_INVALID");
        }

        await _tenant.EnsureLoadedAsync(ct);
        var resolved = await _tikwm.ResolveAsync(sourceUrl.Trim(), ct);
        if (string.IsNullOrWhiteSpace(resolved.VideoUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_TIKTOK_RESOLVE_FAILED");
        }

        var objectKey = BuildObjectKey(user.CustomerId, "motion-tiktok", ".mp4");
        return await _media.DownloadAndSaveBinaryAtObjectKeyAsync(resolved.VideoUrl, objectKey, "dance_sell_motion", "video/mp4", user.UserId, user.CustomerId, _tenant.TenantId, ct);
    }

    public bool IsValidTikTokUrl(string sourceUrl)
    {
        if (!Uri.TryCreate(sourceUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("www.tiktok.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("vm.tiktok.com", StringComparison.OrdinalIgnoreCase)
               || uri.Host.Equals("vt.tiktok.com", StringComparison.OrdinalIgnoreCase);
    }

    public string ToProviderUrl(string? publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_PUBLIC_URL_MISSING");
        }

        var trimmed = publicUrl.Trim();
        if (trimmed.Contains("/browser/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DANCE_SELL_PUBLIC_URL_INVALID");
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("DANCE_SELL_PUBLIC_URL_REQUIRES_HTTPS");
            }

            return absolute.ToString();
        }

        var baseUrl = (_config["Storage:PublicUploadBase"] ?? string.Empty).TrimEnd('/');
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var storageBase))
        {
            var candidate = $"{storageBase.GetLeftPart(UriPartial.Authority).TrimEnd('/')}/{trimmed.TrimStart('/')}";
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var built) && built.Scheme == Uri.UriSchemeHttps)
            {
                return built.ToString();
            }
        }

        var appBase = (_config["TodoX:PublicBaseUrl"] ?? _config["App:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
        if (Uri.TryCreate(appBase, UriKind.Absolute, out var appUri))
        {
            var candidate = $"{appUri.GetLeftPart(UriPartial.Authority).TrimEnd('/')}/{trimmed.TrimStart('/')}";
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var built) && built.Scheme == Uri.UriSchemeHttps)
            {
                return built.ToString();
            }
        }

        throw new InvalidOperationException("DANCE_SELL_PUBLIC_URL_REQUIRES_HTTPS");
    }

    private void ValidateVideo(byte[] content, string fileName, string contentType)
    {
        if (content.Length == 0) throw new InvalidOperationException("DANCE_SELL_INVALID_MOTION");
        if (content.Length > (long)_options.CurrentValue.MaxVideoMb * 1024 * 1024) throw new InvalidOperationException("DANCE_SELL_VIDEO_TOO_LARGE");
        if (!contentType.Equals("video/mp4", StringComparison.OrdinalIgnoreCase)
            && !Path.GetExtension(fileName).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DANCE_SELL_INVALID_MOTION");
        }
    }

    private static string BuildObjectKey(Guid? customerId, string prefix, string ext)
        => $"dance-sell/{customerId?.ToString("N") ?? "system"}/{DateTime.UtcNow:yyyyMM}/{prefix}-{Guid.NewGuid():N}{ext}";

    private static void EnsureAuthenticatedCustomer(CurrentUserSession user)
    {
        if (user.IsAuthenticated != true || user.CustomerId is null)
        {
            throw new InvalidOperationException("DANCE_SELL_UNAUTHORIZED");
        }
    }
}

public interface IDanceSellReferenceImageService
{
    Task<DanceSellReferenceVersionDto> GenerateAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> ApproveAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class DanceSellReferenceImageService : IDanceSellReferenceImageService
{
    private readonly IDanceSellRepository _repo;
    private readonly IMediaFileService _media;
    private readonly IDanceSellMotionSourceService _urls;
    private readonly TenantContext _tenant;

    public DanceSellReferenceImageService(
        IDanceSellRepository repo,
        IMediaFileService media,
        IDanceSellMotionSourceService urls,
        TenantContext tenant)
    {
        _repo = repo;
        _media = media;
        _urls = urls;
        _tenant = tenant;
    }

    public async Task<DanceSellReferenceVersionDto> GenerateAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        if (job.CharacterMediaId is null) throw new InvalidOperationException("DANCE_SELL_INVALID_CHARACTER");
        if (job.ProductMediaId is null) throw new InvalidOperationException("DANCE_SELL_INVALID_PRODUCT");

        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Generating, ct: ct);
        var versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
        var versionNo = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNo) + 1;
        var requestJson = DanceSellRepository.ToJson(new
        {
            job.Id,
            job.CharacterMediaId,
            job.ProductMediaId,
            placementMode = job.PlacementMode,
            job.CustomPlacementInstruction,
            job.Prompt,
            phase = "phase2_local_composite_no_billing"
        });

        try
        {
            var character = await _media.ReadBytesAsync(job.CharacterMediaId.Value, ct) ?? throw new InvalidOperationException("DANCE_SELL_INVALID_CHARACTER");
            var product = await _media.ReadBytesAsync(job.ProductMediaId.Value, ct) ?? throw new InvalidOperationException("DANCE_SELL_INVALID_PRODUCT");
            var bytes = await BuildCompositeAsync(character, product, ct);
            await _tenant.EnsureLoadedAsync(ct);
            var objectKey = $"dance-sell/{user.CustomerId:N}/{DateTime.UtcNow:yyyyMM}/reference-{job.Id:N}-v{versionNo}.png";
            var saved = await _media.SaveAtObjectKeyAsync(bytes, objectKey, $"dance-sell-reference-v{versionNo}.png", "image/png", "dance_sell_reference", user.UserId, user.CustomerId, _tenant.TenantId, ct);
            var providerUrl = _urls.ToProviderUrl(saved.PublicUrl ?? saved.FileUrl);
            var version = await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
            {
                Id = Guid.NewGuid(),
                DanceSellJobId = job.Id,
                VersionNo = versionNo,
                CharacterMediaId = job.CharacterMediaId,
                ProductMediaId = job.ProductMediaId,
                PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.HoldProduct,
                CustomInstruction = job.CustomPlacementInstruction,
                Prompt = job.Prompt,
                ProviderCode = "local_composite",
                ProviderModel = "imagesharp-v1",
                RequestJson = requestJson,
                ResponseJson = DanceSellRepository.ToJson(new { saved.Id, saved.ObjectKey, publicUrl = providerUrl }),
                ErrorJson = null,
                MediaId = saved.Id,
                ObjectKey = saved.ObjectKey,
                PublicUrl = providerUrl,
                Status = DanceSellReferenceStatuses.Ready,
                IsSelected = false,
                CreatedBy = user.UserId,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            }, ct);

            await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Ready, null, saved.Id, saved.ObjectKey, providerUrl, ct: ct);
            return version;
        }
        catch (Exception ex)
        {
            await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Failed, ex.Message, ct: ct);
            await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
            {
                Id = Guid.NewGuid(),
                DanceSellJobId = job.Id,
                VersionNo = versionNo,
                CharacterMediaId = job.CharacterMediaId,
                ProductMediaId = job.ProductMediaId,
                PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.HoldProduct,
                CustomInstruction = job.CustomPlacementInstruction,
                Prompt = job.Prompt,
                ProviderCode = "local_composite",
                ProviderModel = "imagesharp-v1",
                RequestJson = requestJson,
                ErrorJson = DanceSellRepository.ToJson(new { error = ex.Message }),
                Status = DanceSellReferenceStatuses.Failed,
                CreatedBy = user.UserId,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            }, ct);
            throw;
        }
    }

    public async Task<DanceSellJobDto> ApproveAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        var version = await _repo.GetReferenceVersionAsync(versionId, ct)
            ?? throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        if (version.DanceSellJobId != job.Id || version.Status != DanceSellReferenceStatuses.Ready || string.IsNullOrWhiteSpace(version.PublicUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        }

        await _repo.SelectReferenceVersionAsync(job.Id, version.Id, ct);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Approved, null, version.MediaId, version.ObjectKey, version.PublicUrl, DateTime.UtcNow, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    private async Task<DanceSellJobDto> RequireOwnedJobAsync(Guid id, CurrentUserSession user, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("DANCE_SELL_NOT_FOUND");
        if (!DanceSellSecurity.CanAccess(user, job))
        {
            throw new InvalidOperationException("DANCE_SELL_UNAUTHORIZED");
        }

        return job;
    }

    private static async Task<byte[]> BuildCompositeAsync(byte[] characterBytes, byte[] productBytes, CancellationToken ct)
    {
        using var canvas = new Image<Rgba32>(1080, 1440, Color.White);
        await using var characterStream = new MemoryStream(characterBytes);
        await using var productStream = new MemoryStream(productBytes);
        using var character = await Image.LoadAsync<Rgba32>(characterStream, ct);
        using var product = await Image.LoadAsync<Rgba32>(productStream, ct);

        character.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(760, 1220),
            Mode = ResizeMode.Max
        }));
        product.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(360, 360),
            Mode = ResizeMode.Max
        }));

        var characterPoint = new Point((canvas.Width - character.Width) / 2, 80);
        var productPoint = new Point(canvas.Width - product.Width - 90, canvas.Height - product.Height - 130);
        canvas.Mutate(x =>
        {
            x.BackgroundColor(Color.WhiteSmoke);
            x.DrawImage(character, characterPoint, 1f);
            x.DrawImage(product, productPoint, 1f);
        });

        await using var ms = new MemoryStream();
        await canvas.SaveAsync(ms, new PngEncoder(), ct);
        return ms.ToArray();
    }
}

public interface IDanceSellPhase2Service
{
    DanceSellCapabilityDto GetCapability();
    Task<DanceSellJobDto> CreateJobAsync(DanceSellCreateJobRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadCharacterAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadProductAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadMotionAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> StageTikTokAsync(Guid id, string sourceUrl, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> QueueRenderAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> RetryAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> GetAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<IReadOnlyList<DanceSellJobDto>> ListAsync(CurrentUserSession user, int limit = 20, int offset = 0, CancellationToken ct = default);
}

public sealed class DanceSellPhase2Service : IDanceSellPhase2Service
{
    private readonly IDanceSellRepository _repo;
    private readonly IMediaFileService _media;
    private readonly IDanceSellMotionSourceService _motion;
    private readonly IRenderJobService _renderJobs;
    private readonly IOptionsMonitor<KieOptions> _kie;
    private readonly IOptionsMonitor<DanceSellPhase2Options> _options;
    private readonly TenantContext _tenant;

    public DanceSellPhase2Service(
        IDanceSellRepository repo,
        IMediaFileService media,
        IDanceSellMotionSourceService motion,
        IRenderJobService renderJobs,
        IOptionsMonitor<KieOptions> kie,
        IOptionsMonitor<DanceSellPhase2Options> options,
        TenantContext tenant)
    {
        _repo = repo;
        _media = media;
        _motion = motion;
        _renderJobs = renderJobs;
        _kie = kie;
        _options = options;
        _tenant = tenant;
    }

    public DanceSellCapabilityDto GetCapability()
        => new(
            _kie.CurrentValue.AllowedModes.Length > 0 ? _kie.CurrentValue.AllowedModes : new[] { _options.CurrentValue.DefaultMode },
            _kie.CurrentValue.AllowedCharacterOrientations.Length > 0 ? _kie.CurrentValue.AllowedCharacterOrientations : new[] { _options.CurrentValue.DefaultOrientation },
            _kie.CurrentValue.DefaultMode,
            _options.CurrentValue.DefaultOrientation);

    public async Task<DanceSellJobDto> CreateJobAsync(DanceSellCreateJobRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        ValidateBusiness(request.Prompt, request.Mode, request.CharacterOrientation, request.PlacementMode);
        await _tenant.EnsureLoadedAsync(ct);
        return await _repo.CreateDraftAsync(new DanceSellDraftCreateRequest
        {
            TenantId = _tenant.TenantId,
            CustomerId = user.CustomerId,
            UserId = user.UserId,
            Title = request.Title ?? string.Empty,
            Prompt = request.Prompt,
            Mode = request.Mode,
            CharacterOrientation = request.CharacterOrientation,
            PlacementMode = request.PlacementMode,
            CustomPlacementInstruction = request.CustomPlacementInstruction
        }, ct);
    }

    public async Task<DanceSellJobDto> UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        ValidateBusiness(request.Prompt, request.Mode, request.CharacterOrientation, request.PlacementMode);
        await _repo.UpdateBusinessAsync(job.Id, request, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UploadCharacterAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        ValidateImage(content, fileName, contentType);
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, contentType, "dance_sell_character", user.UserId, user.CustomerId, _tenant.TenantId, ct);
        await _repo.UpdateCharacterAsync(job.Id, media.Id, media.ObjectKey ?? string.Empty, _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl), ct);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.NotCreated, ct: ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UploadProductAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        ValidateImage(content, fileName, contentType);
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, contentType, "dance_sell_product", user.UserId, user.CustomerId, _tenant.TenantId, ct);
        await _repo.UpdateProductAsync(job.Id, media.Id, media.ObjectKey ?? string.Empty, _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl), ct);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.NotCreated, ct: ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UploadMotionAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        var media = await _motion.SaveUploadedVideoAsync(content, fileName, contentType, user, ct);
        await _repo.UpdateMotionUploadAsync(job.Id, media.Id, media.ObjectKey ?? string.Empty, _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl), ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> StageTikTokAsync(Guid id, string sourceUrl, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        var media = await _motion.StageTikTokAsync(sourceUrl, user, ct);
        await _repo.UpdateMotionTikTokAsync(job.Id, sourceUrl.Trim(), media.Id, media.ObjectKey ?? string.Empty, _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl), ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> QueueRenderAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        ValidateReadyForRender(job);
        if (job.Status is DanceSellJobStatuses.Queued or DanceSellJobStatuses.Submitted or DanceSellJobStatuses.Rendering)
        {
            throw new InvalidOperationException("DANCE_SELL_JOB_ALREADY_ACTIVE");
        }

        var logicalRequestId = string.IsNullOrWhiteSpace(job.LogicalRequestId) ? $"dance-sell-{Guid.NewGuid():N}" : job.LogicalRequestId;
        var renderJob = await _renderJobs.EnqueueAsync(new RenderJobCreateModel
        {
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            JobType = RenderJobTypes.DanceSell,
            Priority = 50,
            Input = new DanceSellRenderInput { DanceSellJobId = job.Id, LogicalRequestId = logicalRequestId },
            Prompt = new { job.Prompt, job.PlacementMode, job.Mode, job.CharacterOrientation },
            References = new[] { job.PreparedReferenceUrl!, job.MotionVideoUrl },
            ProviderCode = DanceSellConstants.ProviderCode,
            ModelCode = _kie.CurrentValue.MotionControlModel,
            PointCostEstimate = 0,
            PointStatus = RenderPointStatuses.NotRequired,
            MaxAttempts = Math.Max(3, _kie.CurrentValue.MaxPollCount + _kie.CurrentValue.SubmitMaxRetry + 5)
        }, ct);

        await _repo.QueueForRenderAsync(job.Id, renderJob.Id, logicalRequestId, job.PreparedReferenceUrl!, job.MotionVideoUrl, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> RetryAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        if (job.RenderJobId is null || job.Status is not (DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout))
        {
            throw new InvalidOperationException("DANCE_SELL_RETRY_NOT_ALLOWED");
        }

        var retry = await _renderJobs.RetryAsync(job.RenderJobId.Value, user.UserId, ct)
            ?? throw new InvalidOperationException("DANCE_SELL_RENDER_ENQUEUE_FAILED");
        await _repo.SetRenderJobIdAsync(job.Id, retry.Id, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> GetAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
        => await RequireOwnedJobAsync(id, user, ct);

    public async Task<IReadOnlyList<DanceSellJobDto>> ListAsync(CurrentUserSession user, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        return await _repo.ListAsync(user.IsRoot || !user.IsCustomer ? null : user.CustomerId, limit, offset, ct);
    }

    private async Task<DanceSellJobDto> RequireOwnedJobAsync(Guid id, CurrentUserSession user, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("DANCE_SELL_NOT_FOUND");
        if (!DanceSellSecurity.CanAccess(user, job))
        {
            throw new InvalidOperationException("DANCE_SELL_UNAUTHORIZED");
        }

        return job;
    }

    private void ValidateImage(byte[] content, string fileName, string contentType)
    {
        if (content.Length == 0) throw new InvalidOperationException("DANCE_SELL_INVALID_IMAGE");
        if (content.Length > (long)_options.CurrentValue.MaxImageMb * 1024 * 1024) throw new InvalidOperationException("DANCE_SELL_IMAGE_TOO_LARGE");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_options.CurrentValue.AllowedImageTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase)
            && ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
        {
            throw new InvalidOperationException("DANCE_SELL_INVALID_IMAGE");
        }
    }

    private void ValidateBusiness(string prompt, string mode, string orientation, string placementMode)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("DANCE_SELL_PROMPT_REQUIRED");
        if (!GetCapability().Modes.Contains(mode, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("DANCE_SELL_INVALID_MODE");
        if (!GetCapability().CharacterOrientations.Contains(orientation, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("DANCE_SELL_INVALID_ORIENTATION");
        if (!DanceSellPlacementModes.All.Contains(placementMode)) throw new InvalidOperationException("DANCE_SELL_INVALID_PLACEMENT");
    }

    private static void ValidateReadyForRender(DanceSellJobDto job)
    {
        if (job.CharacterMediaId is null || string.IsNullOrWhiteSpace(job.CharacterImageUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_CHARACTER");
        if (job.ProductMediaId is null || string.IsNullOrWhiteSpace(job.ProductImageUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_PRODUCT");
        if (job.MotionVideoMediaId is null || string.IsNullOrWhiteSpace(job.MotionVideoUrl) || job.SourceStageStatus != DanceSellSourceStageStatuses.Ready) throw new InvalidOperationException("DANCE_SELL_INVALID_MOTION");
        if (job.PreparedReferenceStatus != DanceSellReferenceStatuses.Approved || string.IsNullOrWhiteSpace(job.PreparedReferenceUrl)) throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_APPROVED");
    }

    private static void EnsureAuthenticatedCustomer(CurrentUserSession user)
    {
        if (user.IsAuthenticated != true || user.CustomerId is null)
        {
            throw new InvalidOperationException("DANCE_SELL_UNAUTHORIZED");
        }
    }
}

public static class DanceSellSecurity
{
    public static bool CanAccess(CurrentUserSession user, DanceSellJobDto job)
        => user.IsAuthenticated
           && (user.IsRoot
               || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator
               || (user.CustomerId is Guid customerId && job.CustomerId == customerId)
               || job.UserId == user.UserId);
}
