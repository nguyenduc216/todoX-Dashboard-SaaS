using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class ReupVideoCacheService
{
    private readonly HttpClient _http;
    private readonly ReupCampaignRepository _repo;
    private readonly ReupStorageService _storage;
    private readonly TikwmVideoResolver _resolver;
    private readonly ReupLogService _logs;

    public ReupVideoCacheService(HttpClient http, ReupCampaignRepository repo, ReupStorageService storage, TikwmVideoResolver resolver, ReupLogService logs)
    {
        _http = http;
        _repo = repo;
        _storage = storage;
        _resolver = resolver;
        _logs = logs;
    }

    public async Task<ReupVideoAssetDto> GetOrCreateAsync(ReupTaskExecutionDto task, CancellationToken ct)
    {
        var ready = await _repo.GetReadyAssetAsync(task.CustomerId, task.ReferenceVideoId, ct);
        if (ready is not null && !string.IsNullOrWhiteSpace(ready.LocalPath) && File.Exists(ready.LocalPath))
        {
            return ready;
        }

        var assetId = await _repo.CreateResolvingAssetAsync(task, ct);
        try
        {
            await _logs.WriteAsync(task.CampaignId, task.Id, "VIDEO_RESOLVE_STARTED", "Resolving TikTok video by TikWM.", data: new { task.ReferenceSourceUrl }, ct: ct);
            var resolved = await _resolver.ResolveAsync(task.ReferenceSourceUrl, ct);
            await _logs.WriteAsync(task.CampaignId, task.Id, "VIDEO_RESOLVE_OK", "TikWM returned video URL.", ct: ct);

            _storage.EnsureCustomerFolder(task.CustomerId);
            var path = _storage.GetVideoPath(task.CustomerId, task.ReferenceVideoId);
            await _logs.WriteAsync(task.CampaignId, task.Id, "VIDEO_DOWNLOAD_STARTED", "Downloading video to cache_video.", data: new { path }, ct: ct);

            using var response = await _http.GetAsync(resolved.VideoUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP_{(int)response.StatusCode}");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(path))
            {
                await source.CopyToAsync(target, ct);
            }

            var info = new FileInfo(path);
            await _repo.MarkAssetReadyAsync(assetId, resolved.VideoUrl, path, info.Length, ct);
            await _logs.WriteAsync(task.CampaignId, task.Id, "VIDEO_DOWNLOAD_OK", "Video cached.", data: new { bytes = info.Length }, ct: ct);

            return new ReupVideoAssetDto
            {
                Id = assetId,
                TenantId = task.TenantId,
                CustomerId = task.CustomerId,
                ReferenceVideoId = task.ReferenceVideoId,
                SourceUrl = task.ReferenceSourceUrl,
                ResolvedVideoUrl = resolved.VideoUrl,
                LocalPath = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = info.Length,
                ContentType = "video/mp4",
                Status = "ready"
            };
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            await _repo.MarkAssetFailedAsync(assetId, "VIDEO_DOWNLOAD_TIMEOUT", ex.Message, ct);
            throw new TimeoutException("VIDEO_DOWNLOAD_TIMEOUT", ex);
        }
        catch (Exception ex)
        {
            var code = ex.Message.StartsWith("TIKWM_", StringComparison.OrdinalIgnoreCase) ? ex.Message : "VIDEO_CACHE_FAILED";
            await _repo.MarkAssetFailedAsync(assetId, code, ex.Message, ct);
            throw;
        }
    }
}
