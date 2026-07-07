using Microsoft.Extensions.Options;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class ReupCampaignWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReupCampaignWorker> _logger;
    private readonly ReupCampaignOptions _options;
    private readonly SemaphoreSlim _globalGate;
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public ReupCampaignWorker(IServiceScopeFactory scopeFactory, ILogger<ReupCampaignWorker> logger, IOptions<ReupCampaignOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _globalGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentPublishTasks));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WorkerEnabled)
        {
            _logger.LogInformation("Reup campaign worker is disabled. Set ReupCampaign:WorkerEnabled=true to enable.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var storage = scope.ServiceProvider.GetRequiredService<ReupStorageService>();
                storage.EnsureReady();
                var repo = scope.ServiceProvider.GetRequiredService<ReupCampaignRepository>();
                await repo.ResetStaleLocksAsync(TimeSpan.FromMinutes(Math.Max(1, _options.TaskTimeoutMinutes)), stoppingToken);

                var launched = 0;
                while (_globalGate.CurrentCount > 0)
                {
                    await _globalGate.WaitAsync(stoppingToken);
                    var task = await repo.LeaseNextTaskAsync(_workerId, stoppingToken);
                    if (task is null)
                    {
                        _globalGate.Release();
                        break;
                    }

                    launched++;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessTaskAsync(task, stoppingToken);
                        }
                        finally
                        {
                            _globalGate.Release();
                        }
                    }, stoppingToken);
                }

                if (launched == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.WorkerPollSeconds)), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reup campaign worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.WorkerPollSeconds)), stoppingToken);
            }
        }
    }

    private async Task ProcessTaskAsync(ReupTaskExecutionDto task, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ReupCampaignRepository>();
        var logs = scope.ServiceProvider.GetRequiredService<ReupLogService>();
        try
        {
            if (!string.Equals(task.ReferencePlatform, "tiktok", StringComparison.OrdinalIgnoreCase))
            {
                await repo.FailOrRetryTaskAsync(task.Id, "UNSUPPORTED_PLATFORM", "Sprint 1 chỉ hỗ trợ nguồn video TikTok.", ct);
                return;
            }

            await logs.WriteAsync(task.CampaignId, task.Id, "PAGE_TOKEN_CHECK_STARTED", "Checking Facebook Page token.", ct: ct);
            var token = await repo.GetActivePageTokenAsync(task.CustomerId, task.SocialPageId, ct);
            if (token is null)
            {
                await repo.FailOrRetryTaskAsync(task.Id, "PAGE_TOKEN_MISSING", "Page chưa có token active. Vui lòng cập nhật token thủ công ở Quản lý page.", ct);
                return;
            }

            var checker = scope.ServiceProvider.GetRequiredService<FacebookPageTokenChecker>();
            var check = await checker.CheckAsync(task.SocialPageId, task.PageExternalId ?? string.Empty, token, ct);
            if (!check.Ok)
            {
                await logs.WriteAsync(task.CampaignId, task.Id, "PAGE_TOKEN_CHECK_FAILED", check.ErrorMessage ?? "Token check failed.", "error", new { check.ErrorCode }, ct);
                await repo.FailOrRetryTaskAsync(task.Id, check.ErrorCode ?? "PAGE_TOKEN_INVALID", check.ErrorMessage ?? "Token Facebook không hợp lệ.", ct);
                return;
            }

            await logs.WriteAsync(task.CampaignId, task.Id, "PAGE_TOKEN_CHECK_OK", "Facebook Page token is valid.", ct: ct);
            await repo.SetTaskStatusAsync(task.Id, "resolving_video", tokenId: token.Id, ct: ct);

            var cache = scope.ServiceProvider.GetRequiredService<ReupVideoCacheService>();
            var asset = await cache.GetOrCreateAsync(task, ct);
            await repo.SetTaskStatusAsync(task.Id, "publishing", videoAssetId: asset.Id, ct: ct);

            var publisher = scope.ServiceProvider.GetRequiredService<FacebookPageVideoPublisher>();
            var description = BuildDescription(task.CaptionUsed, task.HashtagsUsed);
            await logs.WriteAsync(task.CampaignId, task.Id, "FACEBOOK_UPLOAD_STARTED", "Uploading video to Facebook Page.", ct: ct);
            var result = await publisher.PublishAsync(task.CampaignId, task.Id, task.PageExternalId!, token.TokenValue, asset.LocalPath!, asset.ResolvedVideoUrl, description, ct);
            if (!result.Ok)
            {
                await logs.WriteAsync(task.CampaignId, task.Id, "FACEBOOK_UPLOAD_FAILED", result.ErrorMessage ?? "Facebook upload failed.", "error", new { result.ErrorCode }, ct);
                await repo.FailOrRetryTaskAsync(task.Id, result.ErrorCode ?? "FACEBOOK_UPLOAD_FAILED", result.ErrorMessage ?? "Facebook upload failed.", ct);
                return;
            }

            await repo.CompleteTaskAsync(task.Id, result, ct);
            await logs.WriteAsync(task.CampaignId, task.Id, "TASK_COMPLETED", "Task completed.", data: new { result.FacebookVideoId, result.FacebookPostUrl }, ct: ct);
        }
        catch (TimeoutException ex)
        {
            await repo.FailOrRetryTaskAsync(task.Id, ex.Message.StartsWith("TIKWM_", StringComparison.OrdinalIgnoreCase) ? ex.Message : "HTTP_5XX", ex.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reup task failed. TaskId={TaskId}", task.Id);
            await repo.FailOrRetryTaskAsync(task.Id, ClassifyException(ex), ex.Message, ct);
        }
    }

    private static string? BuildDescription(string? caption, string? hashtags)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(caption)) parts.Add(caption.Trim());
        if (!string.IsNullOrWhiteSpace(hashtags)) parts.Add(hashtags.Trim());
        var result = string.Join(Environment.NewLine + Environment.NewLine, parts).Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string ClassifyException(Exception ex)
    {
        if (ex is TimeoutException) return "HTTP_5XX";
        var message = ex.Message;
        if (message.Contains("TIKWM_TIMEOUT", StringComparison.OrdinalIgnoreCase)) return "TIKWM_TIMEOUT";
        if (message.Contains("TIKWM_NO_VIDEO_URL", StringComparison.OrdinalIgnoreCase)) return "TIKWM_NO_VIDEO_URL";
        if (message.Contains("HTTP_5", StringComparison.OrdinalIgnoreCase)) return "HTTP_5XX";
        return "TASK_FAILED";
    }
}
