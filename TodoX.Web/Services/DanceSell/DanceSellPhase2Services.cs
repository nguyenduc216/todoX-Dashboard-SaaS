using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using TodoX.Web.Models;
using TodoX.Web.Models.Catalog;
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
    Task<DanceSellJobDto> AutoPrepareAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> ApproveCharacterAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> ApproveAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> SelectReferenceVersionAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UnapproveAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
}

public interface IDanceSellReferenceComparisonService
{
    Task<IReadOnlyList<DanceSellReferenceComparisonResultDto>> RunAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellReferenceVersionDto> PollAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellReferenceVersionDto> ScoreAsync(Guid jobId, Guid versionId, DanceSellReferenceComparisonScoreRequest score, CurrentUserSession user, CancellationToken ct = default);
}

public sealed class DanceSellReferenceImageService : IDanceSellReferenceImageService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ReferenceLocks = new();
    private static readonly TimeSpan StaleReferenceGenerationThreshold = TimeSpan.FromMinutes(10);

    private readonly IDanceSellRepository _repo;
    private readonly IMediaFileService _media;
    private readonly IDanceSellMotionSourceService _urls;
    private readonly IDanceSellProviderCatalog _catalog;
    private readonly IDanceSellReferenceProviderFactory _referenceProviders;
    private readonly IDanceSellOperationRepository _operations;
    private readonly IDanceSellCostEstimator _costs;
    private readonly TenantContext _tenant;
    private readonly ILogger<DanceSellReferenceImageService> _logger;

    public DanceSellReferenceImageService(
        IDanceSellRepository repo,
        IMediaFileService media,
        IDanceSellMotionSourceService urls,
        IDanceSellProviderCatalog catalog,
        IDanceSellReferenceProviderFactory referenceProviders,
        IDanceSellOperationRepository operations,
        IDanceSellCostEstimator costs,
        TenantContext tenant,
        ILogger<DanceSellReferenceImageService> logger)
    {
        _repo = repo;
        _media = media;
        _urls = urls;
        _catalog = catalog;
        _referenceProviders = referenceProviders;
        _operations = operations;
        _costs = costs;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<DanceSellReferenceVersionDto> GenerateAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_GENERATION_NOT_REQUIRED");
        }

        if (job.CharacterMediaId is null || string.IsNullOrWhiteSpace(job.CharacterImageUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_CHARACTER");
        // Product input is optional; Person Only uses the character image as its reference.

        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Generating, ct: ct);
        var stage = "resolve_route";
        DanceSellProviderRouteDto? route = null;
        DanceSellCostEstimate? estimate = null;
        DanceSellProviderOperationDto? operation = null;
        var versionNo = 1;
        var requestJson = "{}";
        try
        {
            route = await _catalog.ResolveAsync(DanceSellOperationTypes.ReferenceImage, job.ReferenceProviderCode, job.ReferenceProviderModel, ct);
            var isLocalComposite = route.ProviderCode.Equals("local_composite", StringComparison.OrdinalIgnoreCase);
            if (isLocalComposite)
            {
                throw new InvalidOperationException("DANCE_SELL_REFERENCE_AI_ROUTE_REQUIRED");
            }

            if (route.ProviderCode.Equals(DanceSellConstants.ProviderCode, StringComparison.OrdinalIgnoreCase))
            {
                route.ModelName = DanceSellConstants.Ai79ReferenceModel;
            }

            stage = "list_versions";
            var versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
            versionNo = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNo) + 1;
            var referencePrompt = BuildReferencePrompt(job);
            var targetRatio = DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio);

            requestJson = DanceSellRepository.ToJson(new
            {
                model = route.ModelName,
                domain = "79ai.net",
                action_type = "create",
                prompt = referencePrompt,
                sync = false,
                project_id = "default",
                ratio = targetRatio,
                category = "FASHION",
                resolution = "2k",
                mode = "vip",
                num_outputs = 1,
                language = "VI",
                subjects = BuildSubjectUrls(job)
            });

            stage = "estimate_cost";
            estimate = await _costs.EstimateAsync(route, job.Mode, null, ct);
            stage = "next_attempt";
            var attemptNo = await _operations.GetNextAttemptNoAsync(job.Id, DanceSellOperationTypes.ReferenceImage, ct);
            stage = "create_operation";
            operation = await CreateOperationAsync(job, route, estimate, attemptNo, requestJson, ct);

            stage = "provider_submit";
            var provider = _referenceProviders.Resolve(route);
            var submitted = await provider.SubmitAsync(new DanceSellReferenceProviderRequest
            {
                Route = route,
                CharacterMediaId = job.CharacterMediaId,
                ProductMediaId = job.ProductMediaId,
                Prompt = referencePrompt,
                CharacterImageUrl = job.CharacterImageUrl,
                ProductImageUrl = job.ProductImageUrl,
                AspectRatio = targetRatio
            }, ct);

            if (operation is not null)
            {
                await _operations.MarkSubmittedAsync(operation.Id, submitted.TaskId, submitted.ResponseJson, ct);
            }

            return await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
            {
                Id = Guid.NewGuid(),
                DanceSellJobId = job.Id,
                VersionNo = versionNo,
                CharacterMediaId = job.CharacterMediaId,
                ProductMediaId = job.ProductMediaId,
                PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.HoldProduct,
                CustomInstruction = job.CustomPlacementInstruction,
                Prompt = referencePrompt,
                ProviderCode = route.ProviderCode,
                ProviderModel = submitted.ModelName,
                RequestJson = submitted.RequestJson,
                ResponseJson = DanceSellRepository.ToJson(new { submitted.TaskId, submitted.ResponseJson }),
                Status = DanceSellReferenceStatuses.Generating,
                IsSelected = false,
                CreatedBy = user.UserId,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            var errorMessage = $"{stage}: {ex.Message}";
            _logger.LogError(ex, "DanceSell reference generation failed stage={Stage} jobId={JobId}", stage, job.Id);
            if (await IsCurrentSourceAsync(job, ct))
            {
                await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Failed, errorMessage, ct: ct);
            }

            if (route is not null)
            {
                await TryCreateFailedReferenceVersionAsync(job, route, versionNo, requestJson, errorMessage, user, ct);
            }

            if (operation is not null)
            {
                await _operations.MarkFailedAsync(operation.Id, "failed", DanceSellRepository.ToJson(new { stage, error = ex.Message }), "DANCE_SELL_REFERENCE_FAILED", errorMessage, ct);
            }

            throw;
        }
    }

    private async Task<DanceSellProviderOperationDto?> TryCreateOperationMetadataAsync(DanceSellJobDto job, DanceSellProviderRouteDto route, string requestJson, string stagePrefix, CancellationToken ct)
    {
        var stage = $"{stagePrefix}:estimate_cost";
        try
        {
            var estimate = await _costs.EstimateAsync(route, job.Mode, null, ct);
            stage = $"{stagePrefix}:next_attempt";
            var attemptNo = await _operations.GetNextAttemptNoAsync(job.Id, DanceSellOperationTypes.ReferenceImage, ct);
            stage = $"{stagePrefix}:create_operation";
            return await CreateOperationAsync(job, route, estimate, attemptNo, requestJson, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DanceSell optional reference operation metadata failed stage={Stage} jobId={JobId}", stage, job.Id);
            return null;
        }
    }

    private async Task<DanceSellProviderOperationDto?> CreateOperationAsync(
        DanceSellJobDto job,
        DanceSellProviderRouteDto route,
        DanceSellCostEstimate estimate,
        int attemptNo,
        string requestJson,
        CancellationToken ct)
        => await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
        {
            Id = Guid.NewGuid(),
            DanceSellJobId = job.Id,
            OperationType = DanceSellOperationTypes.ReferenceImage,
            AttemptNo = attemptNo,
            ReferenceMode = job.ReferenceMode,
            ProviderCode = route.ProviderCode,
            ProviderCapabilityId = route.ProviderCapabilityId,
            ProviderAccountId = route.ProviderAccountId,
            ProviderModel = route.ModelName,
            Status = DanceSellOperationStatuses.Generating,
            BillingStatus = DanceSellBillingStatuses.Estimated,
            RefundStatus = DanceSellRefundStatuses.NotCharged,
            RequestJson = requestJson,
            UsageUnit = estimate.UsageUnit,
            CreditsEstimated = estimate.EstimatedUsage,
            ProviderCost = estimate.EstimatedProviderCost,
            ProviderCurrency = estimate.Currency,
            ProviderCostVnd = estimate.ProviderCostVnd,
            TodoxPointsEstimated = estimate.EstimatedTodoxPoints,
            CostSource = estimate.PricingSource,
            PricingSnapshotJson = DanceSellRepository.ToJson(estimate),
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        }, ct);

    private async Task TryCreateFailedReferenceVersionAsync(
        DanceSellJobDto job,
        DanceSellProviderRouteDto route,
        int versionNo,
        string requestJson,
        string errorMessage,
        CurrentUserSession user,
        CancellationToken ct)
    {
        try
        {
            await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
            {
                Id = Guid.NewGuid(),
                DanceSellJobId = job.Id,
                VersionNo = versionNo,
                CharacterMediaId = job.CharacterMediaId,
                ProductMediaId = job.ProductMediaId,
                PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.HoldProduct,
                CustomInstruction = job.CustomPlacementInstruction,
                Prompt = BuildReferencePrompt(job),
                ProviderCode = route.ProviderCode,
                ProviderModel = route.ModelName,
                RequestJson = requestJson,
                ErrorJson = DanceSellRepository.ToJson(new { error = errorMessage }),
                Status = DanceSellReferenceStatuses.Failed,
                CreatedBy = user.UserId,
                CreatedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            }, ct);
        }
        catch (Exception versionEx)
        {
            _logger.LogError(versionEx, "DanceSell failed to persist failed reference version jobId={JobId}", job.Id);
        }
    }

    public async Task<DanceSellJobDto> AutoPrepareAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        var gate = ReferenceLocks.GetOrAdd(jobId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var job = await RequireOwnedJobAsync(jobId, user, ct);
            if (job.ReferenceMode == DanceSellReferenceModes.DirectReference
                || job.CharacterMediaId is null
                || string.IsNullOrWhiteSpace(job.CharacterImageUrl))
            {
                return job;
            }

            var versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
            if (await RecoverStaleGenerationAsync(job, versions, ct))
            {
                job = await RequireOwnedJobAsync(jobId, user, ct);
                versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
            }

            job = await RequireOwnedJobAsync(jobId, user, ct);
            versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
            var reusable = versions.FirstOrDefault(version => ReferenceVersionMatches(version, job)
                && version.Status is DanceSellReferenceStatuses.Generating or DanceSellReferenceStatuses.Ready or DanceSellReferenceStatuses.Approved);
            if (reusable is not null)
            {
                if (reusable.Status == DanceSellReferenceStatuses.Generating)
                {
                    await PollGeneratingReferenceAsync(job, reusable, user, ct);
                    return await _repo.GetByIdAsync(job.Id, ct) ?? job;
                }

                if (reusable.Status is DanceSellReferenceStatuses.Ready or DanceSellReferenceStatuses.Approved
                    && !string.IsNullOrWhiteSpace(reusable.PublicUrl))
                {
                    var status = reusable.Status == DanceSellReferenceStatuses.Approved
                        || reusable.IsSelected
                        || job.PreparedReferenceStatus == DanceSellReferenceStatuses.Approved
                        ? DanceSellReferenceStatuses.Approved
                        : DanceSellReferenceStatuses.Ready;
                    if (status == DanceSellReferenceStatuses.Approved)
                    {
                        await _repo.SelectReferenceVersionAsync(job.Id, reusable.Id, ct);
                    }

                    await _repo.UpdateReferenceStatusAsync(
                        job.Id,
                        status,
                        null,
                        reusable.MediaId,
                        reusable.ObjectKey,
                        reusable.PublicUrl,
                        status == DanceSellReferenceStatuses.Approved
                            ? job.PreparedReferenceApprovedAt ?? reusable.CompletedAt ?? DateTime.UtcNow
                            : null,
                        ct);
                    return await _repo.GetByIdAsync(job.Id, ct) ?? job;
                }

                return job;
            }

            if (job.ProductMediaId is null || string.IsNullOrWhiteSpace(job.ProductImageUrl))
            {
                return await PrepareCharacterReferenceAsync(job, user, versions, ct);
            }

            await GenerateAsync(job.Id, user, ct);
            return await _repo.GetByIdAsync(job.Id, ct) ?? job;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1)
            {
                ReferenceLocks.TryRemove(new KeyValuePair<Guid, SemaphoreSlim>(jobId, gate));
            }
        }
    }

    private async Task PollGeneratingReferenceAsync(DanceSellJobDto job, DanceSellReferenceVersionDto version, CurrentUserSession user, CancellationToken ct)
    {
        if (string.Equals(version.ProviderCode, "local_composite", StringComparison.OrdinalIgnoreCase))
        {
            await _repo.FailReferenceVersionAsync(version.Id, DanceSellRepository.ToJson(new { error = "DANCE_SELL_REFERENCE_AI_ROUTE_REQUIRED" }), ct);
            if (await IsCurrentSourceAsync(job, ct))
            {
                await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Failed, "DANCE_SELL_REFERENCE_AI_ROUTE_REQUIRED", ct: ct);
            }

            return;
        }

        var operation = await _operations.GetLatestActiveOperationAsync(job.Id, DanceSellOperationTypes.ReferenceImage, ct);
        if (operation is null || string.IsNullOrWhiteSpace(operation.ProviderTaskId))
        {
            return;
        }

        var route = await _catalog.ResolveAsync(DanceSellOperationTypes.ReferenceImage, version.ProviderCode, version.ProviderModel, ct);
        var provider = _referenceProviders.Resolve(route);
        var detail = await provider.GetTaskAsync(route, operation.ProviderTaskId, ct);
        if (!detail.IsTerminal)
        {
            return;
        }

        if (detail.IsFailure)
        {
            var error = detail.FailMsg ?? "AI reference image generation failed.";
            var errorJson = DanceSellRepository.ToJson(new
            {
                providerStatus = detail.ProviderState ?? detail.Status,
                errorCode = detail.FailCode,
                error,
                response = detail.RawResponse
            });
            await _repo.FailReferenceVersionAsync(version.Id, errorJson, ct);
            if (await IsCurrentSourceAsync(job, ct))
            {
                await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Failed, error, ct: ct);
            }

            await _operations.MarkFailedAsync(operation.Id, detail.ProviderState ?? detail.Status, detail.RawResponse, detail.FailCode ?? "reference_failed", error, ct);
            return;
        }

        var outputUrl = detail.ResultUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(outputUrl))
        {
            const string error = "AI reference image completed without an output URL.";
            await _repo.FailReferenceVersionAsync(version.Id, DanceSellRepository.ToJson(new { error, response = detail.RawResponse }), ct);
            if (await IsCurrentSourceAsync(job, ct))
            {
                await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Failed, error, ct: ct);
            }

            await _operations.MarkFailedAsync(operation.Id, detail.ProviderState ?? detail.Status, detail.RawResponse, "missing_reference_output", error, ct);
            return;
        }

        if (!await IsCurrentSourceAsync(job, ct))
        {
            return;
        }

        await _tenant.EnsureLoadedAsync(ct);
        var objectKey = $"dance-sell/{user.CustomerId:N}/{DateTime.UtcNow:yyyyMM}/reference-{job.Id:N}-v{version.VersionNo}.png";
        var media = await _media.DownloadAndSaveImageAtObjectKeyAsync(outputUrl, objectKey, "dance_sell_reference", user.UserId, user.CustomerId, _tenant.TenantId, ct);
        var publicUrl = _urls.ToProviderUrl(media.PublicUrl ?? media.FileUrl);
        var responseJson = DanceSellRepository.ToJson(new
        {
            providerStatus = detail.ProviderState ?? detail.Status,
            resultUrl = outputUrl,
            savedMediaId = media.Id,
            media.ObjectKey,
            publicUrl,
            response = detail.RawResponse
        });
        await _repo.CompleteReferenceVersionAsync(version.Id, media.Id, media.ObjectKey ?? objectKey, publicUrl, responseJson, ct);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Ready, null, media.Id, media.ObjectKey, publicUrl, ct: ct);
        await _operations.MarkCompletedAsync(operation.Id, detail.ProviderState ?? detail.Status, detail.RawResponse, detail.CreditsConsumed, publicUrl, ct);
        await _operations.UpsertAssetAsync(new AiOperationAssetDto
        {
            OperationId = operation.Id,
            AssetRole = DanceSellAssetRoles.ReferenceOutput,
            MediaId = media.Id,
            ObjectKey = media.ObjectKey,
            PublicUrl = publicUrl,
            ProviderUrl = outputUrl,
            MimeType = media.MimeType,
            MetadataJson = DanceSellRepository.ToJson(new { versionId = version.Id })
        }, ct);
    }

    private async Task<bool> RecoverStaleGenerationAsync(DanceSellJobDto job, IReadOnlyList<DanceSellReferenceVersionDto> versions, CancellationToken ct)
    {
        var updatedAt = job.UpdatedAt?.ToUniversalTime() ?? DateTime.MinValue;
        if (job.PreparedReferenceStatus != DanceSellReferenceStatuses.Generating
            || DateTime.UtcNow - updatedAt <= StaleReferenceGenerationThreshold)
        {
            return false;
        }

        var hasActiveVersion = versions.Any(version => ReferenceVersionMatches(version, job)
            && version.Status == DanceSellReferenceStatuses.Generating);
        if (hasActiveVersion)
        {
            return false;
        }

        var hasActiveOperation = false;
        try
        {
            hasActiveOperation = await _operations.HasActiveOperationAsync(job.Id, DanceSellOperationTypes.ReferenceImage, ct);
        }
        catch (DanceSellSchemaException ex)
        {
            _logger.LogWarning(ex, "DanceSell stale reference recovery could not read operation table jobId={JobId}", job.Id);
        }

        if (hasActiveOperation)
        {
            return false;
        }

        const string error = "stale_generation: Reference generation did not create an active operation or version.";
        _logger.LogWarning("DanceSell stale reference generation recovered jobId={JobId} updatedAt={UpdatedAt}", job.Id, job.UpdatedAt);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Failed, error, ct: ct);
        return true;
    }

    private static string? ReadConfigString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string BuildReferencePrompt(DanceSellJobDto job)
        => !string.IsNullOrWhiteSpace(job.ImagePrompt)
            ? job.ImagePrompt.Trim()
            : job.ProductMediaId is null || string.IsNullOrWhiteSpace(job.ProductImageUrl)
            ? """
PERSON ONLY REFERENCE IMAGE – SINGLE FINAL IMAGE

Use the supplied person image as the sole visual reference.
- Preserve exact identity, face, body, pose, anatomy, camera angle and lighting
- Do not add, infer or mention any product, clothing reference or additional subject

OUTPUT REQUIREMENT:
- Generate exactly ONE final image.
- Show exactly ONE person.
- Use one single full-frame composition.
- Do NOT create a collage.
- Do NOT create a triptych.
- Do NOT create multiple panels.
- Do NOT duplicate the person.
- Do NOT create before/after layouts.
- Do NOT create alternate variants.

Photorealistic, clean image suitable for video generation.
"""
            : """
VIRTUAL TRY-ON – SINGLE FINAL IMAGE

Use IMAGE 1 as FIXED BASE BODY.
- Preserve exact body pose, limb angles, shoulder alignment, head tilt, camera angle
- Do NOT regenerate body, do NOT reinterpret pose
- Only replace clothing region

Apply clothing from IMAGE 2 with exact design, color, texture, pattern
- Clothing must conform to existing body pose
- No pose correction, no body adjustment, no camera shift

If conflict occurs between clothing and pose:
→ Prioritize BODY POSE from IMAGE 1 over clothing realism

OUTPUT REQUIREMENT:
- Generate exactly ONE final image.
- Show exactly ONE person.
- Use one single full-frame composition.
- Do NOT create a collage.
- Do NOT create a triptych.
- Do NOT create multiple panels.
- Do NOT create before/after comparisons.
- Do NOT show multiple clothing variants.
- Do NOT duplicate the person.
- Do NOT show the original outfit beside the new outfit.

Photorealistic, product preview quality.
""";

    private static string[] BuildSubjectUrls(DanceSellJobDto job)
        => string.IsNullOrWhiteSpace(job.ProductImageUrl)
            ? new[] { job.CharacterImageUrl }
            : new[] { job.CharacterImageUrl, job.ProductImageUrl };

    public async Task<DanceSellJobDto> ApproveAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        var version = await _repo.GetReferenceVersionAsync(versionId, ct)
            ?? throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        if (version.DanceSellJobId != job.Id || version.Status != DanceSellReferenceStatuses.Ready || string.IsNullOrWhiteSpace(version.PublicUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        }
        EnsureReferenceVersionRatioMatchesJob(version, job);

        await _repo.SelectReferenceVersionAsync(job.Id, version.Id, ct);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Approved, null, version.MediaId, version.ObjectKey, version.PublicUrl, DateTime.UtcNow, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> SelectReferenceVersionAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        var version = await _repo.GetReferenceVersionAsync(versionId, ct)
            ?? throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        if (version.DanceSellJobId != job.Id
            || version.Status is not (DanceSellReferenceStatuses.Ready or DanceSellReferenceStatuses.Approved)
            || version.MediaId is null
            || string.IsNullOrWhiteSpace(version.PublicUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        }
        EnsureReferenceVersionRatioMatchesJob(version, job);

        await _repo.SelectReferenceVersionAsync(job.Id, version.Id, ct);
        await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Approved, null, version.MediaId, version.ObjectKey, version.PublicUrl, DateTime.UtcNow, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UnapproveAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        if (job.PreparedReferenceStatus != DanceSellReferenceStatuses.Approved)
        {
            return job;
        }

        await _repo.UnapproveReferenceAsync(job.Id, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> ApproveCharacterAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(jobId, user, ct);
        if (job.ProductMediaId is not null
            || job.CharacterMediaId is null
            || string.IsNullOrWhiteSpace(job.CharacterImageUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_CHARACTER_REFERENCE_NOT_ALLOWED");
        }

        var versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
        var ratio = DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio);
        var version = await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
        {
            Id = Guid.NewGuid(),
            DanceSellJobId = job.Id,
            VersionNo = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNo) + 1,
            CharacterMediaId = job.CharacterMediaId,
            PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.HoldProduct,
            Prompt = BuildReferencePrompt(job),
            ProviderCode = "local_composite",
            ProviderModel = "local_composite",
            RequestJson = DanceSellRepository.ToJson(new { source = "character_input", job.CharacterMediaId, ratio }),
            ResponseJson = DanceSellRepository.ToJson(new { source = "character_input", job.CharacterMediaId, ratio }),
            MediaId = job.CharacterMediaId,
            ObjectKey = job.CharacterObjectKey,
            PublicUrl = job.CharacterImageUrl,
            Status = DanceSellReferenceStatuses.Ready,
            IsSelected = false,
            CreatedBy = user.UserId,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        }, ct);

        await _repo.SelectReferenceVersionAsync(job.Id, version.Id, ct);
        await _repo.UpdateReferenceStatusAsync(
            job.Id,
            DanceSellReferenceStatuses.Approved,
            null,
            version.MediaId,
            version.ObjectKey,
            version.PublicUrl,
            DateTime.UtcNow,
            ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    private async Task<DanceSellJobDto> PrepareCharacterReferenceAsync(DanceSellJobDto job, CurrentUserSession user, IReadOnlyList<DanceSellReferenceVersionDto> versions, CancellationToken ct)
    {
        var ratio = DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio);
        var version = await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
        {
            Id = Guid.NewGuid(),
            DanceSellJobId = job.Id,
            VersionNo = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNo) + 1,
            CharacterMediaId = job.CharacterMediaId,
            ProductMediaId = null,
            PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.HoldProduct,
            Prompt = BuildReferencePrompt(job),
            ProviderCode = "local_composite",
            ProviderModel = "character_input",
            RequestJson = DanceSellRepository.ToJson(new { source = "character_input", job.CharacterMediaId, ratio }),
            ResponseJson = DanceSellRepository.ToJson(new { source = "character_input", job.CharacterMediaId, ratio }),
            MediaId = job.CharacterMediaId,
            ObjectKey = job.CharacterObjectKey,
            PublicUrl = job.CharacterImageUrl,
            Status = DanceSellReferenceStatuses.Ready,
            IsSelected = false,
            CreatedBy = user.UserId,
            CreatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        }, ct);

        if (await IsCurrentSourceAsync(job, ct))
        {
            await _repo.UpdateReferenceStatusAsync(job.Id, DanceSellReferenceStatuses.Ready, null, version.MediaId, version.ObjectKey, version.PublicUrl, ct: ct);
        }

        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    private async Task<bool> IsCurrentSourceAsync(DanceSellJobDto snapshot, CancellationToken ct)
    {
        var current = await _repo.GetByIdAsync(snapshot.Id, ct);
        return current is not null
               && current.CharacterMediaId == snapshot.CharacterMediaId
               && current.ProductMediaId == snapshot.ProductMediaId;
    }

    private static bool ReferenceVersionMatches(DanceSellReferenceVersionDto version, DanceSellJobDto job)
        => version.CharacterMediaId == job.CharacterMediaId
           && version.ProductMediaId == job.ProductMediaId
           && (string.Equals(ReadRequestRatio(version.RequestJson), DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio), StringComparison.Ordinal)
               || IsLegacyPersonOnlyReference(version, job));

    private static void EnsureReferenceVersionRatioMatchesJob(DanceSellReferenceVersionDto version, DanceSellJobDto job)
    {
        var versionRatio = ReadRequestRatio(version.RequestJson);
        var jobRatio = DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio);
        if (string.Equals(versionRatio, jobRatio, StringComparison.Ordinal))
        {
            return;
        }

        if (IsLegacyPersonOnlyReference(version, job))
        {
            return;
        }

        throw new InvalidOperationException($"DANCE_SELL_REFERENCE_RATIO_MISMATCH:{versionRatio ?? "unknown"}:{jobRatio}");
    }

    private static bool IsLegacyPersonOnlyReference(DanceSellReferenceVersionDto version, DanceSellJobDto job)
        => job.ProductMediaId is null
           && version.ProductMediaId is null
           && version.CharacterMediaId == job.CharacterMediaId
           && string.Equals(ReadRequestString(version.RequestJson, "source"), "character_input", StringComparison.OrdinalIgnoreCase)
           && string.IsNullOrWhiteSpace(ReadRequestRatio(version.RequestJson));

    private static string? ReadRequestRatio(string? requestJson)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            return doc.RootElement.TryGetProperty("ratio", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadRequestString(string? requestJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(requestJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
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

public sealed class DanceSellReferenceComparisonService : IDanceSellReferenceComparisonService
{
    private readonly IDanceSellRepository _repo;
    private readonly IMediaFileService _media;
    private readonly IDanceSellMotionSourceService _urls;
    private readonly IDanceSellReferenceProviderFactory _referenceProviders;
    private readonly IDanceSellOperationRepository _operations;
    private readonly IDanceSellCostEstimator _costs;
    private readonly TenantContext _tenant;
    private readonly ILogger<DanceSellReferenceComparisonService> _logger;

    public DanceSellReferenceComparisonService(
        IDanceSellRepository repo,
        IMediaFileService media,
        IDanceSellMotionSourceService urls,
        IDanceSellReferenceProviderFactory referenceProviders,
        IDanceSellOperationRepository operations,
        IDanceSellCostEstimator costs,
        TenantContext tenant,
        ILogger<DanceSellReferenceComparisonService> logger)
    {
        _repo = repo;
        _media = media;
        _urls = urls;
        _referenceProviders = referenceProviders;
        _operations = operations;
        _costs = costs;
        _tenant = tenant;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DanceSellReferenceComparisonResultDto>> RunAsync(Guid jobId, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var job = await RequireAccessibleJobAsync(jobId, user, ct);
        if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_GENERATION_NOT_REQUIRED");
        }

        if (job.CharacterMediaId is null || string.IsNullOrWhiteSpace(job.CharacterImageUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_CHARACTER");
        if (job.ProductMediaId is null || string.IsNullOrWhiteSpace(job.ProductImageUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_PRODUCT");

        var versions = await _repo.ListReferenceVersionsAsync(job.Id, ct);
        var nextVersionNo = versions.Count == 0 ? 1 : versions.Max(x => x.VersionNo) + 1;
        var nextAttemptNo = await _operations.GetNextAttemptNoAsync(job.Id, DanceSellOperationTypes.ReferenceImage, ct);
        var prompt = DanceSellReferenceImageService.BuildReferencePrompt(job);
        var results = new List<DanceSellReferenceComparisonResultDto>();

        foreach (var candidate in DanceSellReferenceComparisonCandidates.All)
        {
            var started = DateTime.UtcNow;
            DanceSellProviderOperationDto? operation = null;
            try
            {
                var route = BuildCandidateRoute(candidate);
                var targetRatio = DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio);
                var requestJson = BuildComparisonRequestJson(job, candidate, prompt, route, started, targetRatio);
                var estimate = await _costs.EstimateAsync(route, job.Mode, null, ct);
                operation = await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
                {
                    Id = Guid.NewGuid(),
                    DanceSellJobId = job.Id,
                    OperationType = DanceSellOperationTypes.ReferenceImage,
                    AttemptNo = nextAttemptNo++,
                    ReferenceMode = job.ReferenceMode,
                    ProviderCode = route.ProviderCode,
                    ProviderModel = route.ModelName,
                    Status = DanceSellOperationStatuses.Generating,
                    BillingStatus = DanceSellBillingStatuses.Estimated,
                    RefundStatus = DanceSellRefundStatuses.NotCharged,
                    RequestJson = requestJson,
                    UsageUnit = estimate.UsageUnit,
                    CreditsEstimated = estimate.EstimatedUsage,
                    ProviderCost = estimate.EstimatedProviderCost,
                    ProviderCurrency = estimate.Currency,
                    ProviderCostVnd = estimate.ProviderCostVnd,
                    TodoxPointsEstimated = estimate.EstimatedTodoxPoints,
                    CostSource = estimate.PricingSource,
                    PricingSnapshotJson = DanceSellRepository.ToJson(estimate),
                    CreatedAt = started,
                    StartedAt = started
                }, ct);

                var submitted = await _referenceProviders.Resolve(route).SubmitAsync(new DanceSellReferenceProviderRequest
                {
                    Route = route,
                    CharacterMediaId = job.CharacterMediaId,
                    ProductMediaId = job.ProductMediaId,
                    Prompt = prompt,
                    CharacterImageUrl = job.CharacterImageUrl,
                    ProductImageUrl = job.ProductImageUrl!,
                    AspectRatio = targetRatio
                }, ct);

                if (operation is not null)
                {
                    await _operations.MarkSubmittedAsync(operation.Id, submitted.TaskId, submitted.ResponseJson, ct);
                }

                var version = await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
                {
                    Id = Guid.NewGuid(),
                    DanceSellJobId = job.Id,
                    VersionNo = nextVersionNo++,
                    CharacterMediaId = job.CharacterMediaId,
                    ProductMediaId = job.ProductMediaId,
                    PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.WearProduct,
                    CustomInstruction = job.CustomPlacementInstruction,
                    Prompt = prompt,
                    ProviderCode = candidate.ProviderCode,
                    ProviderModel = candidate.ModelName,
                    RequestJson = submitted.RequestJson,
                    ResponseJson = DanceSellRepository.ToJson(new
                    {
                        experiment = DanceSellConstants.ReferenceComparisonExperiment,
                        candidate.DisplayName,
                        submitted.TaskId,
                        operationId = operation?.Id,
                        submitted.ResponseJson,
                        startedAt = started,
                        elapsedMs = (DateTime.UtcNow - started).TotalMilliseconds
                    }),
                    Status = DanceSellReferenceStatuses.Generating,
                    IsSelected = false,
                    CreatedBy = user.UserId,
                    CreatedAt = started
                }, ct);

                results.Add(new DanceSellReferenceComparisonResultDto
                {
                    Candidate = candidate,
                    Version = version,
                    Status = version.Status,
                    Elapsed = DateTime.UtcNow - started
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DanceSell reference comparison candidate failed jobId={JobId} model={Model}", job.Id, candidate.ModelName);
                if (operation is not null)
                {
                    await _operations.MarkFailedAsync(operation.Id, "failed", DanceSellRepository.ToJson(new { experiment = DanceSellConstants.ReferenceComparisonExperiment, error = ex.Message }), "reference_comparison_failed", ex.Message, ct);
                }

                var failedVersion = await TryCreateFailedVersionAsync(job, candidate, prompt, nextVersionNo++, ex, user, started, ct);
                results.Add(new DanceSellReferenceComparisonResultDto
                {
                    Candidate = candidate,
                    Version = failedVersion,
                    Status = DanceSellReferenceStatuses.Failed,
                    ErrorMessage = ex.Message,
                    Elapsed = DateTime.UtcNow - started
                });
            }
        }

        return results;
    }

    public async Task<DanceSellReferenceVersionDto> PollAsync(Guid jobId, Guid versionId, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var job = await RequireAccessibleJobAsync(jobId, user, ct);
        var version = await _repo.GetReferenceVersionAsync(versionId, ct) ?? throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_FOUND");
        if (version.DanceSellJobId != job.Id || !IsComparisonVersion(version))
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_FOUND");
        }

        if (version.Status != DanceSellReferenceStatuses.Generating)
        {
            return version;
        }

        var operationId = ReadGuid(version.ResponseJson, "operationId");
        var taskId = ReadString(version.ResponseJson, "taskId");
        if (operationId is null || string.IsNullOrWhiteSpace(taskId))
        {
            await _repo.FailReferenceVersionAsync(version.Id, DanceSellRepository.ToJson(new { error = "comparison operation metadata missing" }), ct);
            return await _repo.GetReferenceVersionAsync(version.Id, ct) ?? version;
        }

        var route = BuildCandidateRoute(new DanceSellReferenceComparisonCandidate(version.ProviderCode ?? "79ai", version.ProviderModel ?? string.Empty, DisplayNameFor(version.ProviderModel)));
        var detail = await _referenceProviders.Resolve(route).GetTaskAsync(route, taskId!, ct);
        if (!detail.IsTerminal)
        {
            return version;
        }

        if (detail.IsFailure)
        {
            var error = detail.FailMsg ?? "AI reference comparison generation failed.";
            var errorJson = DanceSellRepository.ToJson(new
            {
                experiment = DanceSellConstants.ReferenceComparisonExperiment,
                providerStatus = detail.ProviderState ?? detail.Status,
                errorCode = detail.FailCode,
                error,
                response = detail.RawResponse
            });
            await _repo.FailReferenceVersionAsync(version.Id, errorJson, ct);
            await _operations.MarkFailedAsync(operationId.Value, detail.ProviderState ?? detail.Status, detail.RawResponse, detail.FailCode ?? "reference_comparison_failed", error, ct);
            return await _repo.GetReferenceVersionAsync(version.Id, ct) ?? version;
        }

        var outputUrl = detail.ResultUrls.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(outputUrl))
        {
            const string error = "AI reference comparison completed without an output URL.";
            await _repo.FailReferenceVersionAsync(version.Id, DanceSellRepository.ToJson(new { experiment = DanceSellConstants.ReferenceComparisonExperiment, error, response = detail.RawResponse }), ct);
            await _operations.MarkFailedAsync(operationId.Value, detail.ProviderState ?? detail.Status, detail.RawResponse, "missing_reference_comparison_output", error, ct);
            return await _repo.GetReferenceVersionAsync(version.Id, ct) ?? version;
        }

        await _tenant.EnsureLoadedAsync(ct);
        var mediaCustomerId = job.CustomerId ?? user.CustomerId;
        var objectKey = $"dance-sell/{mediaCustomerId?.ToString("N") ?? "system"}/{DateTime.UtcNow:yyyyMM}/reference-ab-{job.Id:N}-{version.ProviderModel}-{version.VersionNo}.png";
        var media = await _media.DownloadAndSaveImageAtObjectKeyAsync(outputUrl, objectKey, "dance_sell_reference_ab", user.UserId, mediaCustomerId, _tenant.TenantId, ct);
        var publicUrl = _urls.ToProviderUrl(media.PublicUrl ?? media.FileUrl);
        var responseJson = DanceSellRepository.ToJson(new
        {
            experiment = DanceSellConstants.ReferenceComparisonExperiment,
            providerStatus = detail.ProviderState ?? detail.Status,
            resultUrl = outputUrl,
            savedMediaId = media.Id,
            media.ObjectKey,
            publicUrl,
            response = detail.RawResponse,
            completedAt = DateTime.UtcNow,
            selected = false,
            approved = false
        });
        await _repo.CompleteReferenceVersionAsync(version.Id, media.Id, media.ObjectKey ?? objectKey, publicUrl, responseJson, ct);
        await _operations.MarkCompletedAsync(operationId.Value, detail.ProviderState ?? detail.Status, detail.RawResponse, detail.CreditsConsumed, publicUrl, ct);
        await _operations.UpsertAssetAsync(new AiOperationAssetDto
        {
            OperationId = operationId.Value,
            AssetRole = DanceSellAssetRoles.ReferenceOutput,
            MediaId = media.Id,
            ObjectKey = media.ObjectKey,
            PublicUrl = publicUrl,
            ProviderUrl = outputUrl,
            MimeType = media.MimeType,
            MetadataJson = DanceSellRepository.ToJson(new { versionId = version.Id, experiment = DanceSellConstants.ReferenceComparisonExperiment })
        }, ct);
        return await _repo.GetReferenceVersionAsync(version.Id, ct) ?? version;
    }

    public async Task<DanceSellReferenceVersionDto> ScoreAsync(Guid jobId, Guid versionId, DanceSellReferenceComparisonScoreRequest score, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAdmin(user);
        var job = await RequireAccessibleJobAsync(jobId, user, ct);
        var version = await _repo.GetReferenceVersionAsync(versionId, ct) ?? throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_FOUND");
        if (version.DanceSellJobId != job.Id || !IsComparisonVersion(version))
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_FOUND");
        }

        ValidateScore(score.ShirtColorFidelity);
        ValidateScore(score.GarmentShapeFidelity);
        ValidateScore(score.GraphicTextFidelity);
        ValidateScore(score.LogoArtworkFidelity);
        ValidateScore(score.SelectedBottomFidelity);
        ValidateScore(score.IdentityPreservation);
        await _repo.UpdateReferenceVersionScoreAsync(version.Id, DanceSellRepository.ToJson(new
        {
            score.ShirtColorFidelity,
            score.GarmentShapeFidelity,
            score.GraphicTextFidelity,
            score.LogoArtworkFidelity,
            score.SelectedBottomFidelity,
            score.IdentityPreservation,
            scoredBy = user.UserId,
            scoredAt = DateTime.UtcNow
        }), ct);
        return await _repo.GetReferenceVersionAsync(version.Id, ct) ?? version;
    }

    private async Task<DanceSellReferenceVersionDto?> TryCreateFailedVersionAsync(DanceSellJobDto job, DanceSellReferenceComparisonCandidate candidate, string prompt, int versionNo, Exception ex, CurrentUserSession user, DateTime started, CancellationToken ct)
    {
        try
        {
            return await _repo.CreateReferenceVersionAsync(new DanceSellReferenceVersionDto
            {
                Id = Guid.NewGuid(),
                DanceSellJobId = job.Id,
                VersionNo = versionNo,
                CharacterMediaId = job.CharacterMediaId,
                ProductMediaId = job.ProductMediaId,
                PlacementMode = job.PlacementMode ?? DanceSellPlacementModes.WearProduct,
                CustomInstruction = job.CustomPlacementInstruction,
                Prompt = prompt,
                ProviderCode = candidate.ProviderCode,
                ProviderModel = candidate.ModelName,
                RequestJson = BuildComparisonRequestJson(job, candidate, prompt, BuildCandidateRoute(candidate), started, DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio)),
                ErrorJson = DanceSellRepository.ToJson(new { experiment = DanceSellConstants.ReferenceComparisonExperiment, error = ex.Message }),
                Status = DanceSellReferenceStatuses.Failed,
                IsSelected = false,
                CreatedBy = user.UserId,
                CreatedAt = started,
                CompletedAt = DateTime.UtcNow
            }, ct);
        }
        catch (Exception versionEx)
        {
            _logger.LogError(versionEx, "DanceSell failed to persist failed comparison version jobId={JobId} model={Model}", job.Id, candidate.ModelName);
            return null;
        }
    }

    private async Task<DanceSellJobDto> RequireAccessibleJobAsync(Guid jobId, CurrentUserSession user, CancellationToken ct)
    {
        var job = await _repo.GetByIdAsync(jobId, ct) ?? throw new InvalidOperationException("DANCE_SELL_NOT_FOUND");
        if (!DanceSellSecurity.CanAccess(user, job)) throw new InvalidOperationException("DANCE_SELL_UNAUTHORIZED");
        return job;
    }

    private static DanceSellProviderRouteDto BuildCandidateRoute(DanceSellReferenceComparisonCandidate candidate)
        => new()
        {
            Id = Guid.Empty,
            FeatureCode = DanceSellConstants.FeatureCode,
            OperationType = DanceSellOperationTypes.ReferenceImage,
            ProviderCode = candidate.ProviderCode,
            ModelName = candidate.ModelName,
            Priority = 900,
            Enabled = true,
            IsDefault = false,
            ConfigJson = DanceSellRepository.ToJson(new
            {
                experiment = DanceSellConstants.ReferenceComparisonExperiment,
                displayName = candidate.DisplayName,
                submit_path = "/generateImage",
                poll_path = "/image",
                ratio = "9:16",
                resolution = "2k",
                mode = "vip",
                character_image_field = "base64Image",
                subject_schema = "json_stringified_array_of_image_data_uris"
            })
        };

    private static string BuildComparisonRequestJson(DanceSellJobDto job, DanceSellReferenceComparisonCandidate candidate, string prompt, DanceSellProviderRouteDto route, DateTime started, string targetRatio)
        => DanceSellRepository.ToJson(new
        {
            experiment = DanceSellConstants.ReferenceComparisonExperiment,
            job.Id,
            job.CharacterMediaId,
            job.ProductMediaId,
            provider = candidate.ProviderCode,
            model = candidate.ModelName,
            candidate.DisplayName,
            prompt,
            ratio = targetRatio,
            resolution = ReadConfigString(route.ConfigJson, "resolution"),
            mode = ReadConfigString(route.ConfigJson, "mode"),
            action_type = "create",
            editImage = true,
            project_id = "default",
            subjectsCount = 1,
            subjectSchema = "json_stringified_array_of_image_data_uris",
            characterImageField = "base64Image",
            productImageTransport = "subjects",
            startedAt = started
        });

    private static bool IsComparisonVersion(DanceSellReferenceVersionDto version)
        => string.Equals(ReadString(version.ResponseJson, "experiment"), DanceSellConstants.ReferenceComparisonExperiment, StringComparison.Ordinal)
           || string.Equals(ReadString(version.RequestJson, "experiment"), DanceSellConstants.ReferenceComparisonExperiment, StringComparison.Ordinal)
           || string.Equals(ReadString(version.ErrorJson, "experiment"), DanceSellConstants.ReferenceComparisonExperiment, StringComparison.Ordinal);

    private static void EnsureAdmin(CurrentUserSession user)
    {
        if (!DanceSellSecurity.IsAdmin(user)) throw new InvalidOperationException("DANCE_SELL_ADMIN_REQUIRED");
    }

    private static void ValidateScore(int score)
    {
        if (score is < 0 or > 5) throw new InvalidOperationException("DANCE_SELL_INVALID_SCORE");
    }

    private static string DisplayNameFor(string? modelName)
        => DanceSellReferenceComparisonCandidates.All.FirstOrDefault(x => x.ModelName.Equals(modelName, StringComparison.OrdinalIgnoreCase))?.DisplayName
           ?? modelName
           ?? string.Empty;

    private static Guid? ReadGuid(string? rawJson, string propertyName)
        => Guid.TryParse(ReadString(rawJson, propertyName), out var parsed) ? parsed : null;

    private static string? ReadString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(propertyName, out var value)) return null;
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadConfigString(string? rawJson, string propertyName)
        => ReadString(rawJson, propertyName);
}

public interface IDanceSellPhase2Service
{
    DanceSellCapabilityDto GetCapability();
    Task<IReadOnlyList<DanceSellProviderRouteDto>> GetProvidersAsync(string operationType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellCapabilityDto> GetProviderCapabilityAsync(Guid routeId, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> CreateJobAsync(DanceSellCreateJobRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadCharacterAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadProductAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> RemoveProductAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadDirectReferenceAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> UploadMotionAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> StageTikTokAsync(Guid id, string sourceUrl, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> QueueRenderAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> RetryAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> CancelAsync(Guid id, string reason, CurrentUserSession user, CancellationToken ct = default);
    Task<DanceSellJobDto> GetAsync(Guid id, CurrentUserSession user, CancellationToken ct = default);
    Task<string> GetDownloadTicketAsync(Guid id, string type, CurrentUserSession user, CancellationToken ct = default);
    Task<IReadOnlyList<DanceSellJobDto>> ListAsync(CurrentUserSession user, int limit = 20, int offset = 0, CancellationToken ct = default);
}

public sealed class DanceSellPhase2Service : IDanceSellPhase2Service
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> RetryLocks = new();
    private readonly IDanceSellRepository _repo;
    private readonly IMediaFileService _media;
    private readonly IDanceSellMotionSourceService _motion;
    private readonly IRenderJobService _renderJobs;
    private readonly IDanceSellProviderCatalog _catalog;
    private readonly IDanceSellOperationRepository _operations;
    private readonly IDanceSellCostEstimator _costs;
    private readonly IPointPricingService _pointPricing;
    private readonly WalletService _wallets;
    private readonly IRDanceDownloadTicketService _downloadTickets;
    private readonly IOptionsMonitor<KieOptions> _kie;
    private readonly IOptionsMonitor<DanceSellPhase2Options> _options;
    private readonly TenantContext _tenant;

    public DanceSellPhase2Service(
        IDanceSellRepository repo,
        IMediaFileService media,
        IDanceSellMotionSourceService motion,
        IRenderJobService renderJobs,
        IDanceSellProviderCatalog catalog,
        IDanceSellOperationRepository operations,
        IDanceSellCostEstimator costs,
        IPointPricingService pointPricing,
        WalletService wallets,
        IRDanceDownloadTicketService downloadTickets,
        IOptionsMonitor<KieOptions> kie,
        IOptionsMonitor<DanceSellPhase2Options> options,
        TenantContext tenant)
    {
        _repo = repo;
        _media = media;
        _motion = motion;
        _renderJobs = renderJobs;
        _catalog = catalog;
        _operations = operations;
        _costs = costs;
        _pointPricing = pointPricing;
        _wallets = wallets;
        _downloadTickets = downloadTickets;
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

    public async Task<IReadOnlyList<DanceSellProviderRouteDto>> GetProvidersAsync(string operationType, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        if (!DanceSellOperationTypes.All.Contains(operationType))
        {
            throw new InvalidOperationException("DANCE_SELL_INVALID_OPERATION_TYPE");
        }

        return await _catalog.GetRoutesAsync(operationType, userSelectableOnly: !DanceSellSecurity.IsAdmin(user), ct);
    }

    public async Task<DanceSellCapabilityDto> GetProviderCapabilityAsync(Guid routeId, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        var routes = (await _catalog.GetRoutesAsync(DanceSellOperationTypes.MotionVideo, userSelectableOnly: !DanceSellSecurity.IsAdmin(user), ct))
            .Concat(await _catalog.GetRoutesAsync(DanceSellOperationTypes.ReferenceImage, userSelectableOnly: !DanceSellSecurity.IsAdmin(user), ct));
        var route = routes.FirstOrDefault(x => x.Id == routeId) ?? throw new InvalidOperationException("DANCE_SELL_PROVIDER_ROUTE_INVALID");
        return BuildCapability(route);
    }

    public async Task<DanceSellJobDto> CreateJobAsync(DanceSellCreateJobRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        EnsureAuthenticatedCustomer(user);
        ValidatePromptPlacement(request.Prompt, request.PlacementMode);
        if (!DanceSellReferenceModes.All.Contains(request.ReferenceMode))
        {
            throw new InvalidOperationException("DANCE_SELL_INVALID_REFERENCE_MODE");
        }

        var referenceRoute = await _catalog.ResolveAsync(DanceSellOperationTypes.ReferenceImage, request.ReferenceProviderCode, request.ReferenceProviderModel, ct);
        var motionRoute = await _catalog.ResolveAsync(DanceSellOperationTypes.MotionVideo, request.MotionProviderCode, request.MotionProviderModel, ct);
        ValidateCapability(request.Mode, request.CharacterOrientation, motionRoute);
        await _tenant.EnsureLoadedAsync(ct);
        return await _repo.CreateDraftAsync(new DanceSellDraftCreateRequest
        {
            TenantId = _tenant.TenantId,
            CustomerId = user.CustomerId,
            UserId = user.UserId,
            Title = request.Title ?? string.Empty,
            ReferenceMode = request.ReferenceMode,
            Prompt = request.Prompt,
            Mode = request.Mode,
            Ratio = request.Ratio,
            CharacterOrientation = request.CharacterOrientation,
            PlacementMode = request.PlacementMode,
            CustomPlacementInstruction = request.CustomPlacementInstruction,
            ImagePrompt = request.ImagePrompt,
            ReferenceProviderCode = referenceRoute.ProviderCode,
            ReferenceProviderModel = referenceRoute.ModelName,
            MotionProviderCode = motionRoute.ProviderCode,
            MotionProviderModel = motionRoute.ModelName,
            AutoFinish = request.AutoFinish
        }, ct);
    }

    public async Task<DanceSellJobDto> UpdateBusinessAsync(Guid id, DanceSellUpdateBusinessRequest request, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        ValidatePromptPlacement(request.Prompt, request.PlacementMode);
        if (!DanceSellReferenceModes.All.Contains(request.ReferenceMode))
        {
            throw new InvalidOperationException("DANCE_SELL_INVALID_REFERENCE_MODE");
        }

        var referenceModeChanged =
            !string.Equals(job.ReferenceMode, request.ReferenceMode.Trim(), StringComparison.Ordinal);
        var ratioChanged =
            !string.Equals(DanceSellRatioNormalizer.NormalizeDanceSellRatio(job.Ratio),
                DanceSellRatioNormalizer.NormalizeDanceSellRatio(request.Ratio),
                StringComparison.Ordinal);
        await _repo.UpdateBusinessAsync(job.Id, request, ct);
        if (referenceModeChanged || ratioChanged)
        {
            await _repo.ResetReferenceAsync(job.Id, ct: ct);
        }

        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UploadCharacterAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            throw new InvalidOperationException("DANCE_SELL_DIRECT_REFERENCE_ONLY");
        }
        ValidateImage(content, fileName, contentType);
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, contentType, "dance_sell_character", user.UserId, user.CustomerId, _tenant.TenantId, ct);
        await _repo.UpdateCharacterAsync(job.Id, media.Id, media.ObjectKey ?? string.Empty, _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl), ct);
        await _repo.ResetReferenceAsync(job.Id, ct: ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UploadProductAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            throw new InvalidOperationException("DANCE_SELL_DIRECT_REFERENCE_ONLY");
        }
        ValidateImage(content, fileName, contentType);
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, contentType, "dance_sell_product", user.UserId, user.CustomerId, _tenant.TenantId, ct);
        await _repo.UpdateProductAsync(job.Id, media.Id, media.ObjectKey ?? string.Empty, _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl), ct);
        await _repo.ResetReferenceAsync(job.Id, ct: ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> RemoveProductAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            throw new InvalidOperationException("DANCE_SELL_DIRECT_REFERENCE_ONLY");
        }

        await _repo.RemoveProductAndUseCharacterReferenceAsync(job.Id, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> UploadDirectReferenceAsync(Guid id, byte[] content, string fileName, string contentType, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        if (job.ReferenceMode != DanceSellReferenceModes.DirectReference)
        {
            throw new InvalidOperationException("DANCE_SELL_DIRECT_REFERENCE_NOT_ALLOWED");
        }

        ValidateImage(content, fileName, contentType);
        await _tenant.EnsureLoadedAsync(ct);
        var media = await _media.SaveAsync(content, fileName, contentType, "dance_sell_direct_reference", user.UserId, user.CustomerId, _tenant.TenantId, ct);
        var providerUrl = _motion.ToProviderUrl(media.PublicUrl ?? media.FileUrl);
        await _repo.UpdateDirectReferenceAsync(job.Id, media.Id, media.ObjectKey ?? string.Empty, providerUrl, ct);
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
        var motionRoute = await _catalog.ResolveAsync(DanceSellOperationTypes.MotionVideo, job.MotionProviderCode, job.MotionProviderModel, ct);
        var providerMode = DanceSellMotionProviderContract.ResolveProviderMode(motionRoute, job.Mode);
        var estimate = await _costs.EstimateAsync(motionRoute, providerMode, null, ct);
        var quality = job.Mode.Equals("premium", StringComparison.OrdinalIgnoreCase)
            ? ServiceSellPriceQualityTiers.Premium
            : ServiceSellPriceQualityTiers.Standard;
        var durationSeconds = ResolveMotionDurationSeconds(job, motionRoute, estimate);
        var pointEstimate = await _pointPricing.EstimateAsync(new PointPricingEstimateRequest(
            null,
            0,
            quality,
            durationSeconds,
            quality,
            0,
            ServiceSellPriceQualityTiers.Standard,
            false), ct);
        var charge = await _wallets.ChargeAsync(
            user.CustomerId,
            user.UserId,
            pointEstimate.TotalPoints,
            1,
            "dance_sell_initial_render",
            motionRoute.ProviderCode,
            motionRoute.ModelName,
            "dance_sell",
            "point",
            job.Id,
            "dance_sell_job");
        if (!charge.Ok)
        {
            throw new InvalidOperationException(charge.Error ?? "Insufficient points.");
        }
        var attemptNo = await _operations.GetNextAttemptNoAsync(job.Id, DanceSellOperationTypes.MotionVideo, ct);
        var operation = await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
        {
            Id = Guid.NewGuid(),
            DanceSellJobId = job.Id,
            OperationType = DanceSellOperationTypes.MotionVideo,
            AttemptNo = attemptNo,
            ReferenceMode = job.ReferenceMode,
            ProviderCode = motionRoute.ProviderCode,
            ProviderCapabilityId = motionRoute.ProviderCapabilityId,
            ProviderAccountId = motionRoute.ProviderAccountId,
            ProviderModel = motionRoute.ModelName,
            Status = DanceSellOperationStatuses.Queued,
            BillingStatus = estimate.EstimatedTodoxPoints is null ? DanceSellBillingStatuses.Reconciliation : DanceSellBillingStatuses.Estimated,
            RefundStatus = DanceSellRefundStatuses.NotCharged,
            RequestJson = DanceSellRepository.ToJson(new { job.Id, job.PreparedReferenceUrl, job.MotionVideoUrl, job.Prompt, businessMode = job.Mode, providerMode, job.CharacterOrientation, job.Ratio }),
            UsageUnit = estimate.UsageUnit,
            CreditsEstimated = estimate.EstimatedUsage,
            ProviderCost = estimate.EstimatedProviderCost,
            ProviderCurrency = estimate.Currency,
            ProviderCostVnd = estimate.ProviderCostVnd,
            TodoxPointsEstimated = estimate.EstimatedTodoxPoints,
            CostSource = estimate.PricingSource,
            PricingSnapshotJson = DanceSellRepository.ToJson(estimate),
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (operation is not null && job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            await _operations.UpsertAssetAsync(new AiOperationAssetDto
            {
                OperationId = operation.Id,
                AssetRole = DanceSellAssetRoles.DirectReferenceInput,
                MediaId = job.DirectReferenceMediaId,
                ObjectKey = job.DirectReferenceObjectKey,
                PublicUrl = job.DirectReferenceUrl,
                MimeType = "image/*",
                MetadataJson = "{}"
            }, ct);
        }

        var renderJob = await _renderJobs.EnqueueAsync(new RenderJobCreateModel
        {
            UserId = user.UserId,
            CustomerId = user.CustomerId,
            JobType = RenderJobTypes.DanceSell,
            Priority = 50,
            Input = new DanceSellRenderInput { DanceSellJobId = job.Id, LogicalRequestId = logicalRequestId, OperationId = operation?.Id },
            Prompt = new { job.Prompt, job.PlacementMode, job.Mode, job.CharacterOrientation, job.Ratio },
            References = new { referenceUrl = job.PreparedReferenceUrl!, motionVideoUrl = job.MotionVideoUrl, operationId = operation?.Id },
            ProviderCode = motionRoute.ProviderCode,
            ModelCode = motionRoute.ModelName,
            PointCostEstimate = pointEstimate.TotalPoints,
            PointStatus = pointEstimate.TotalPoints > 0 ? RenderPointStatuses.Pending : RenderPointStatuses.NotRequired,
            MaxAttempts = Math.Max(3, _kie.CurrentValue.MaxPollCount + _kie.CurrentValue.SubmitMaxRetry + 5)
        }, ct);

        await _repo.QueueForRenderAsync(job.Id, renderJob.Id, logicalRequestId, job.PreparedReferenceUrl!, job.MotionVideoUrl, motionRoute, ct);
        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> RetryAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
    {
        var gate = RetryLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var job = await RequireOwnedJobAsync(id, user, ct);
            var coreJob = job.RenderJobId is Guid renderJobId
                ? await _renderJobs.GetAsync(renderJobId, ct)
                : null;
            var coreCancelled = coreJob?.Status == RenderJobStatuses.Cancelled;
            var danceJobRetryable = job.Status is DanceSellJobStatuses.Queued
                or DanceSellJobStatuses.Submitted
                or DanceSellJobStatuses.Rendering
                or DanceSellJobStatuses.Failed
                or DanceSellJobStatuses.Timeout;
            if (job.RenderJobId is null
                || !danceJobRetryable
                || (!coreCancelled && job.Status is not (DanceSellJobStatuses.Failed or DanceSellJobStatuses.Timeout)))
            {
                throw new InvalidOperationException("DANCE_SELL_RETRY_NOT_ALLOWED");
            }

            if (await _operations.GetLatestActiveOperationAsync(job.Id, DanceSellOperationTypes.MotionVideo, ct) is not null)
            {
                return await _repo.GetByIdAsync(job.Id, ct) ?? job;
            }

            var previousOperation = await _operations.GetLatestOperationAsync(job.Id, DanceSellOperationTypes.MotionVideo, ct);
            var motionRoute = await _catalog.ResolveAsync(DanceSellOperationTypes.MotionVideo, job.MotionProviderCode, job.MotionProviderModel, ct);
            var providerMode = DanceSellMotionProviderContract.ResolveProviderMode(motionRoute, job.Mode);
            var estimate = await _costs.EstimateAsync(motionRoute, providerMode, null, ct);
            var pointEstimate = await _pointPricing.EstimateAsync(new PointPricingEstimateRequest(
                null,
                1,
                job.Mode.Equals("premium", StringComparison.OrdinalIgnoreCase) ? ServiceSellPriceQualityTiers.Premium : ServiceSellPriceQualityTiers.Standard,
                10,
                job.Mode.Equals("premium", StringComparison.OrdinalIgnoreCase) ? ServiceSellPriceQualityTiers.Premium : ServiceSellPriceQualityTiers.Standard,
                0,
                ServiceSellPriceQualityTiers.Standard,
                false), ct);
            var attemptNo = await _operations.GetNextAttemptNoAsync(job.Id, DanceSellOperationTypes.MotionVideo, ct);
            var logicalRequestId = string.IsNullOrWhiteSpace(job.LogicalRequestId)
                ? $"dance-sell-{Guid.NewGuid():N}"
                : job.LogicalRequestId;
            var operation = await _operations.UpsertOperationAsync(new DanceSellProviderOperationDto
            {
                Id = Guid.NewGuid(),
                DanceSellJobId = job.Id,
                OperationType = DanceSellOperationTypes.MotionVideo,
                AttemptNo = attemptNo,
                ParentOperationId = previousOperation?.Id,
                ReferenceMode = job.ReferenceMode,
                ProviderCode = motionRoute.ProviderCode,
                ProviderCapabilityId = motionRoute.ProviderCapabilityId,
                ProviderAccountId = motionRoute.ProviderAccountId,
                ProviderModel = motionRoute.ModelName,
                Status = DanceSellOperationStatuses.Queued,
                BillingStatus = estimate.EstimatedTodoxPoints is null ? DanceSellBillingStatuses.Reconciliation : DanceSellBillingStatuses.Estimated,
                RefundStatus = DanceSellRefundStatuses.NotCharged,
                RequestJson = DanceSellRepository.ToJson(new { job.Id, job.PreparedReferenceUrl, job.MotionVideoUrl, job.Prompt, businessMode = job.Mode, providerMode, job.CharacterOrientation, job.Ratio }),
                UsageUnit = estimate.UsageUnit,
                CreditsEstimated = estimate.EstimatedUsage,
                ProviderCost = estimate.EstimatedProviderCost,
                ProviderCurrency = estimate.Currency,
                ProviderCostVnd = estimate.ProviderCostVnd,
                TodoxPointsEstimated = estimate.EstimatedTodoxPoints,
                CostSource = estimate.PricingSource,
                PricingSnapshotJson = DanceSellRepository.ToJson(estimate),
                CreatedAt = DateTime.UtcNow,
                StartedAt = DateTime.UtcNow
            }, ct) ?? throw new InvalidOperationException("DANCE_SELL_MOTION_OPERATION_REQUIRED");

            var retry = await _renderJobs.EnqueueAsync(new RenderJobCreateModel
            {
                UserId = coreJob?.UserId ?? user.UserId,
                CustomerId = coreJob?.CustomerId ?? user.CustomerId,
                JobType = coreJob?.JobType ?? RenderJobTypes.DanceSell,
                Priority = coreJob?.Priority ?? 50,
                Input = new DanceSellRenderInput
                {
                    DanceSellJobId = job.Id,
                    LogicalRequestId = logicalRequestId,
                    OperationId = operation.Id
                },
                Prompt = JsonSerializer.Deserialize<object>(coreJob?.PromptJson ?? "{}"),
                References = JsonSerializer.Deserialize<object>(coreJob?.ReferenceJson ?? "[]"),
                LogCode = coreJob?.LogCode,
                PointCostEstimate = coreJob?.PointCostEstimate ?? pointEstimate.TotalPoints,
                PointStatus = (coreJob?.PointCostEstimate ?? pointEstimate.TotalPoints) > 0
                    ? RenderPointStatuses.Pending
                    : RenderPointStatuses.NotRequired,
                ProviderCode = motionRoute.ProviderCode,
                ModelCode = motionRoute.ModelName,
                MaxAttempts = coreJob?.MaxAttempts ?? Math.Max(3, _kie.CurrentValue.MaxPollCount + _kie.CurrentValue.SubmitMaxRetry + 5)
            }, ct);
            if (job.RenderJobId is Guid sourceRenderJobId)
            {
                await _renderJobs.AddEventAsync(retry.Id, "JOB_RETRY_OF", "Job created as retry of failed job.", new { sourceJobId = sourceRenderJobId, userId = user.UserId }, ct: ct);
                await _renderJobs.AddEventAsync(sourceRenderJobId, "JOB_RETRY_CREATED", "Retry job created.", new { retryJobId = retry.Id, userId = user.UserId }, ct: ct);
            }
            await _repo.ResetMotionRenderStateAsync(job.Id, retry.Id, ct);
            return await _repo.GetByIdAsync(job.Id, ct) ?? job;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DanceSellJobDto> CancelAsync(Guid id, string reason, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        if (job.RenderJobId is null)
        {
            throw new InvalidOperationException("DANCE_SELL_CANCEL_NOT_ALLOWED");
        }

        var coreJob = await _renderJobs.GetAsync(job.RenderJobId.Value, ct);
        if (coreJob?.Status == RenderJobStatuses.Cancelled)
        {
            return await _repo.GetByIdAsync(job.Id, ct) ?? job;
        }

        if (job.Status is not (DanceSellJobStatuses.Queued or DanceSellJobStatuses.Submitted or DanceSellJobStatuses.Rendering))
        {
            throw new InvalidOperationException("DANCE_SELL_CANCEL_NOT_ALLOWED");
        }

        if (!await _renderJobs.CancelAsync(job.RenderJobId.Value, reason, user.UserId, ct))
        {
            coreJob = await _renderJobs.GetAsync(job.RenderJobId.Value, ct);
            if (coreJob?.Status != RenderJobStatuses.Cancelled)
            {
                throw new InvalidOperationException("DANCE_SELL_CANCEL_FAILED");
            }
        }

        return await _repo.GetByIdAsync(job.Id, ct) ?? job;
    }

    public async Task<DanceSellJobDto> GetAsync(Guid id, CurrentUserSession user, CancellationToken ct = default)
        => await RequireOwnedJobAsync(id, user, ct);

    public async Task<string> GetDownloadTicketAsync(Guid id, string type, CurrentUserSession user, CancellationToken ct = default)
    {
        var job = await RequireOwnedJobAsync(id, user, ct);
        var normalizedType = type.Trim().ToLowerInvariant();
        if (!RDanceDownloadTypes.All.Contains(normalizedType, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("DANCE_SELL_DOWNLOAD_TYPE_INVALID");
        }

        if (normalizedType == RDanceDownloadTypes.Result)
        {
            if (!string.Equals(job.Status, DanceSellJobStatuses.Completed, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(job.ResultVideoUrl))
            {
                throw new InvalidOperationException("DANCE_SELL_RESULT_NOT_READY");
            }
        }
        else if (!string.Equals(job.PreparedReferenceStatus, DanceSellReferenceStatuses.Approved, StringComparison.OrdinalIgnoreCase)
                 || string.IsNullOrWhiteSpace(job.PreparedReferenceUrl))
        {
            throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_READY");
        }

        return _downloadTickets.CreateTicket(job.Id, job.CustomerId, user.UserId, normalizedType, TimeSpan.FromMinutes(3));
    }

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

    private static void ValidatePromptPlacement(string prompt, string placementMode)
    {
        if (string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("DANCE_SELL_PROMPT_REQUIRED");
        if (!DanceSellPlacementModes.All.Contains(placementMode)) throw new InvalidOperationException("DANCE_SELL_INVALID_PLACEMENT");
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ValidateCapability(string mode, string orientation, DanceSellProviderRouteDto route)
    {
        var capability = BuildCapability(route);
        if (!capability.Modes.Contains(mode, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("DANCE_SELL_INVALID_MODE");
        if (!capability.CharacterOrientations.Contains(orientation, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("DANCE_SELL_INVALID_ORIENTATION");
    }

    private DanceSellCapabilityDto BuildCapability(DanceSellProviderRouteDto route)
    {
        var modes = ReadStringArray(route.ConfigJson, "allowedModes")
                    ?? ReadStringArray(route.ConfigJson, "allowed_modes")
                    ?? (_kie.CurrentValue.AllowedModes.Length > 0 ? _kie.CurrentValue.AllowedModes : new[] { _options.CurrentValue.DefaultMode });
        var orientations = ReadStringArray(route.ConfigJson, "allowedCharacterOrientations")
                           ?? ReadStringArray(route.ConfigJson, "allowed_character_orientations")
                           ?? (_kie.CurrentValue.AllowedCharacterOrientations.Length > 0 ? _kie.CurrentValue.AllowedCharacterOrientations : new[] { _options.CurrentValue.DefaultOrientation });
        var defaultMode = ReadConfigString(route.ConfigJson, "defaultMode") ?? ReadConfigString(route.ConfigJson, "default_mode") ?? modes[0];
        var defaultOrientation = ReadConfigString(route.ConfigJson, "defaultOrientation") ?? ReadConfigString(route.ConfigJson, "default_orientation") ?? orientations[0];
        return new DanceSellCapabilityDto(modes, orientations, defaultMode, defaultOrientation);
    }

    private static string[]? ReadStringArray(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var items = value.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                .Select(x => x.GetString()!.Trim())
                .ToArray();
            return items.Length == 0 ? null : items;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadConfigString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidateReadyForRender(DanceSellJobDto job)
    {
        if (job.ReferenceMode == DanceSellReferenceModes.DirectReference)
        {
            if (job.DirectReferenceMediaId is null || string.IsNullOrWhiteSpace(job.DirectReferenceUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_DIRECT_REFERENCE");
        }
        else
        {
            if (job.CharacterMediaId is null || string.IsNullOrWhiteSpace(job.CharacterImageUrl)) throw new InvalidOperationException("DANCE_SELL_INVALID_CHARACTER");
            if (job.PreparedReferenceStatus != DanceSellReferenceStatuses.Approved || string.IsNullOrWhiteSpace(job.PreparedReferenceUrl)) throw new InvalidOperationException("DANCE_SELL_REFERENCE_NOT_APPROVED");
        }
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

    private static int ResolveMotionDurationSeconds(
        DanceSellJobDto job,
        DanceSellProviderRouteDto route,
        DanceSellCostEstimate estimate)
    {
        var configured = ReadInt(job.RequestJson, "durationSeconds", "duration_seconds", "videoDurationSeconds", "video_duration_seconds")
            ?? ReadInt(route.ConfigJson, "durationSeconds", "duration_seconds");
        if (configured is > 0)
        {
            return configured.Value;
        }

        if (estimate.UsageUnit.Contains("second", StringComparison.OrdinalIgnoreCase)
            && estimate.EstimatedUsage > 0)
        {
            return (int)Math.Ceiling(estimate.EstimatedUsage);
        }

        throw new InvalidOperationException("DANCE_SELL_VIDEO_DURATION_REQUIRED");
    }

    private static int? ReadInt(string? rawJson, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            foreach (var propertyName in propertyNames)
            {
                if (!document.RootElement.TryGetProperty(propertyName, out var value))
                {
                    continue;
                }

                if (value.TryGetInt32(out var integer))
                {
                    return integer;
                }

                if (value.ValueKind == JsonValueKind.String
                    && int.TryParse(value.GetString(), out integer))
                {
                    return integer;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}

public static class DanceSellSecurity
{
    public static bool IsAdmin(CurrentUserSession user)
        => user.IsAuthenticated
           && (user.IsRoot || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator);

    public static bool CanAccess(CurrentUserSession user, DanceSellJobDto job)
        => user.IsAuthenticated
           && (user.IsRoot
               || user.Role is TodoXUserRole.Admin or TodoXUserRole.SystemOperator
               || (user.CustomerId is Guid customerId && job.CustomerId == customerId)
               || job.UserId == user.UserId);
}
