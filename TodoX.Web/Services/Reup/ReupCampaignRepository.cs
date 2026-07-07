using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Models;

namespace TodoX.Web.Services.Reup;

public sealed class ReupCampaignRepository
{
    private static readonly string[] RunningStates = { "checking_page", "resolving_video", "publishing" };
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;
    private readonly ReupLogService _logs;

    public ReupCampaignRepository(TodoXConnectionFactory factory, TenantContext tenant, ReupLogService logs)
    {
        _factory = factory;
        _tenant = tenant;
        _logs = logs;
    }

    public async Task<IReadOnlyList<ReupCampaignDto>> ListCampaignsAsync(Guid customerId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReupCampaignDto>(
            CampaignSelectSql + """
             WHERE c.tenant_id = @tenant AND c.customer_id = @customerId
             ORDER BY c.created_at DESC;
            """,
            new { tenant = _tenant.TenantId, customerId });
        return rows.ToList();
    }

    public async Task<ReupCampaignDto?> GetCampaignAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<ReupCampaignDto>(
            CampaignSelectSql + """
             WHERE c.tenant_id = @tenant AND c.customer_id = @customerId AND c.id = @campaignId;
            """,
            new { tenant = _tenant.TenantId, customerId, campaignId });
    }

    public async Task<IReadOnlyList<ReupReferenceVideoOption>> ListTikTokVideosAsync(Guid customerId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReupReferenceVideoOption>(
            """
            SELECT rv.id AS Id,
                   rv.platform AS Platform,
                   rv.source_url AS SourceUrl,
                   rv.title AS Title,
                   rv.channel_name AS ChannelName,
                   rv.author_handle AS AuthorHandle,
                   rv.thumbnail_url AS ThumbnailUrl,
                   rv.published_at AS PublishedAt,
                   rv.created_at AS CreatedAt,
                   COALESCE(done.cnt, 0) AS AlreadyPostedCount
              FROM content.reference_videos rv
              LEFT JOIN (
                    SELECT reference_video_id, count(*)::int AS cnt
                      FROM reup.publish_tasks
                     WHERE customer_id = @customerId AND status = 'completed'
                     GROUP BY reference_video_id
              ) done ON done.reference_video_id = rv.id
             WHERE rv.customer_id = @customerId
               AND rv.is_deleted = false
               AND rv.platform = 'tiktok'
             ORDER BY rv.created_at DESC;
            """,
            new { customerId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ReupFacebookPageOption>> ListFacebookPagesAsync(Guid customerId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReupFacebookPageOption>(
            """
            SELECT p.id AS Id,
                   p.page_name AS PageName,
                   p.page_url AS PageUrl,
                   p.external_page_id AS ExternalPageId,
                   p.avatar_url AS AvatarUrl,
                   p.status AS Status,
                   p.verification_status AS VerificationStatus,
                   tok.status AS TokenStatus,
                   tok.token_hint AS TokenHint,
                   tok.last_validated_at AS LastValidatedAt,
                   tok.last_validation_status AS LastValidationStatus
              FROM social.customer_pages p
              LEFT JOIN LATERAL (
                    SELECT t.status, t.token_hint, t.last_validated_at, t.last_validation_status
                      FROM social.page_access_tokens t
                     WHERE t.customer_id = p.customer_id
                       AND t.page_id = p.id
                       AND t.platform = 'facebook'
                     ORDER BY t.created_at DESC
                     LIMIT 1
              ) tok ON true
             WHERE p.tenant_id = @tenant
               AND p.customer_id = @customerId
               AND p.platform = 'facebook'
             ORDER BY p.page_name;
            """,
            new { tenant = _tenant.TenantId, customerId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ReupDuplicateWarningDto>> CheckDuplicatesAsync(Guid customerId, IEnumerable<Guid> referenceVideoIds, IEnumerable<Guid> socialPageIds, CancellationToken ct = default)
    {
        var videoIds = referenceVideoIds.Distinct().ToArray();
        var pageIds = socialPageIds.Distinct().ToArray();
        if (videoIds.Length == 0 || pageIds.Length == 0)
        {
            return Array.Empty<ReupDuplicateWarningDto>();
        }

        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReupDuplicateWarningDto>(
            """
            SELECT pt.id AS PreviousTaskId,
                   pt.campaign_id AS PreviousCampaignId,
                   pt.reference_video_id AS ReferenceVideoId,
                   pt.social_page_id AS SocialPageId,
                   pt.facebook_video_id AS FacebookVideoId,
                   pt.facebook_post_url AS FacebookPostUrl,
                   pt.completed_at AS CompletedAt,
                   rv.title AS Title,
                   rv.source_url AS SourceUrl,
                   cp.page_name AS PageName
              FROM reup.publish_tasks pt
              JOIN content.reference_videos rv ON rv.id = pt.reference_video_id
              JOIN social.customer_pages cp ON cp.id = pt.social_page_id
             WHERE pt.customer_id = @customerId
               AND pt.reference_video_id = ANY(@videoIds)
               AND pt.social_page_id = ANY(@pageIds)
               AND pt.status = 'completed'
             ORDER BY pt.completed_at DESC;
            """,
            new { customerId, videoIds, pageIds });
        return rows.ToList();
    }

    public async Task<Guid> CreateCampaignAsync(Guid customerId, Guid userId, CreateReupCampaignRequest request, CancellationToken ct = default)
    {
        await ValidateRequestAsync(customerId, request, ct);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var id = Guid.NewGuid();

        await conn.ExecuteAsync(
            """
            INSERT INTO reup.campaigns
                (id, tenant_id, customer_id, created_by_user_id, name, description, caption, hashtags, status, created_at, updated_at)
            VALUES
                (@id, @tenant, @customerId, @userId, @name, @description, @caption, @hashtags, 'draft', now(), now());
            """,
            new
            {
                id,
                tenant = _tenant.TenantId,
                customerId,
                userId,
                name = request.Name.Trim(),
                description = EmptyToNull(request.Description),
                caption = EmptyToNull(request.Caption),
                hashtags = EmptyToNull(request.Hashtags)
            }, tx);

        var order = 0;
        foreach (var videoId in request.ReferenceVideoIds.Distinct())
        {
            await conn.ExecuteAsync(
                "INSERT INTO reup.campaign_videos (campaign_id, reference_video_id, order_index) VALUES (@id, @videoId, @order) ON CONFLICT DO NOTHING;",
                new { id, videoId, order = order++ }, tx);
        }

        foreach (var pageId in request.SocialPageIds.Distinct())
        {
            await conn.ExecuteAsync(
                "INSERT INTO reup.campaign_pages (campaign_id, social_page_id) VALUES (@id, @pageId) ON CONFLICT DO NOTHING;",
                new { id, pageId }, tx);
        }

        tx.Commit();
        await _logs.WriteAsync(id, null, "CAMPAIGN_CREATED", "Campaign created.", data: new { request.Name }, ct: ct);
        return id;
    }

    public async Task UpdateCampaignAsync(Guid customerId, Guid campaignId, UpdateReupCampaignRequest request, CancellationToken ct = default)
    {
        await ValidateRequestAsync(customerId, request, ct);
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var status = await conn.ExecuteScalarAsync<string?>("SELECT status FROM reup.campaigns WHERE id=@campaignId AND customer_id=@customerId;", new { campaignId, customerId }, tx);
        if (status is null) throw new InvalidOperationException("Không tìm thấy chiến dịch.");
        if (status is "running" or "stopping") throw new InvalidOperationException("Không thể sửa chiến dịch đang chạy.");

        await conn.ExecuteAsync(
            """
            UPDATE reup.campaigns
               SET name=@name, description=@description, caption=@caption, hashtags=@hashtags
             WHERE id=@campaignId AND customer_id=@customerId;
            """,
            new
            {
                campaignId,
                customerId,
                name = request.Name.Trim(),
                description = EmptyToNull(request.Description),
                caption = EmptyToNull(request.Caption),
                hashtags = EmptyToNull(request.Hashtags)
            }, tx);

        await conn.ExecuteAsync("DELETE FROM reup.campaign_videos WHERE campaign_id=@campaignId;", new { campaignId }, tx);
        await conn.ExecuteAsync("DELETE FROM reup.campaign_pages WHERE campaign_id=@campaignId;", new { campaignId }, tx);
        var order = 0;
        foreach (var videoId in request.ReferenceVideoIds.Distinct())
        {
            await conn.ExecuteAsync("INSERT INTO reup.campaign_videos (campaign_id, reference_video_id, order_index) VALUES (@campaignId, @videoId, @order);", new { campaignId, videoId, order = order++ }, tx);
        }
        foreach (var pageId in request.SocialPageIds.Distinct())
        {
            await conn.ExecuteAsync("INSERT INTO reup.campaign_pages (campaign_id, social_page_id) VALUES (@campaignId, @pageId);", new { campaignId, pageId }, tx);
        }
        tx.Commit();
    }

    public async Task DeleteCampaignAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var status = await conn.ExecuteScalarAsync<string?>("SELECT status FROM reup.campaigns WHERE id=@campaignId AND customer_id=@customerId;", new { campaignId, customerId });
        if (status is "running" or "stopping") throw new InvalidOperationException("Không thể xóa chiến dịch đang chạy.");
        await conn.ExecuteAsync("DELETE FROM reup.campaigns WHERE id=@campaignId AND customer_id=@customerId;", new { campaignId, customerId });
    }

    public async Task RunCampaignAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var campaign = await conn.QueryFirstOrDefaultAsync<ReupCampaignDto>(
            CampaignSelectSql + " WHERE c.tenant_id=@tenant AND c.customer_id=@customerId AND c.id=@campaignId;",
            new { tenant = _tenant.TenantId, customerId, campaignId }, tx)
            ?? throw new InvalidOperationException("Không tìm thấy chiến dịch.");

        if (campaign.Status is "running" or "stopping")
        {
            return;
        }

        var existingTasks = await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM reup.publish_tasks WHERE campaign_id=@campaignId;", new { campaignId }, tx);
        if (existingTasks == 0)
        {
            var videos = (await conn.QueryAsync<Guid>("SELECT reference_video_id FROM reup.campaign_videos WHERE campaign_id=@campaignId AND selected=true ORDER BY order_index;", new { campaignId }, tx)).ToArray();
            var pages = (await conn.QueryAsync<Guid>("SELECT social_page_id FROM reup.campaign_pages WHERE campaign_id=@campaignId AND selected=true;", new { campaignId }, tx)).ToArray();
            var duplicates = (await CheckDuplicatesAsync(customerId, videos, pages, ct)).GroupBy(x => (x.ReferenceVideoId, x.SocialPageId)).ToDictionary(x => x.Key, x => x.First());

            foreach (var videoId in videos)
            {
                foreach (var pageId in pages)
                {
                    var tokenId = await conn.ExecuteScalarAsync<Guid?>(
                        """
                        SELECT id FROM social.page_access_tokens
                         WHERE customer_id=@customerId AND page_id=@pageId AND platform='facebook' AND status='active'
                         ORDER BY created_at DESC LIMIT 1;
                        """,
                        new { customerId, pageId }, tx);
                    duplicates.TryGetValue((videoId, pageId), out var duplicate);
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO reup.publish_tasks
                            (tenant_id, campaign_id, customer_id, reference_video_id, social_page_id, page_access_token_id,
                             status, duplicate_warning, previous_success_task_id, caption_used, hashtags_used, max_attempts, created_at, updated_at)
                        VALUES
                            (@tenant, @campaignId, @customerId, @videoId, @pageId, @tokenId,
                             'pending', @duplicate, @previousTaskId, @caption, @hashtags, 2, now(), now());
                        """,
                        new
                        {
                            tenant = _tenant.TenantId,
                            campaignId,
                            customerId,
                            videoId,
                            pageId,
                            tokenId,
                            duplicate = duplicate is not null,
                            previousTaskId = duplicate?.PreviousTaskId,
                            caption = campaign.Caption,
                            hashtags = campaign.Hashtags
                        }, tx);
                }
            }
        }

        await conn.ExecuteAsync(
            """
            UPDATE reup.campaigns
               SET status='running',
                   started_at=COALESCE(started_at, now()),
                   stop_requested=false,
                   stopped_at=NULL,
                   error_code=NULL,
                   error_message=NULL
             WHERE id=@campaignId AND customer_id=@customerId;
            """,
            new { campaignId, customerId }, tx);
        tx.Commit();
        await RefreshCampaignCountersAsync(campaignId, ct);
        await _logs.WriteAsync(campaignId, null, "CAMPAIGN_STARTED", "Campaign started.", ct: ct);
    }

    public async Task StopCampaignAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE reup.campaigns
               SET stop_requested=true, status='stopping', stopped_at=now()
             WHERE id=@campaignId AND customer_id=@customerId AND status IN ('running','ready','draft');

            UPDATE reup.publish_tasks
               SET status='cancelled', completed_at=now(), error_code='CAMPAIGN_STOPPED', error_message='Campaign stop requested.'
             WHERE campaign_id=@campaignId AND status='pending';
            """,
            new { campaignId, customerId });
        await RefreshCampaignCountersAsync(campaignId, ct);
        await _logs.WriteAsync(campaignId, null, "CAMPAIGN_STOP_REQUESTED", "Campaign stop requested.", "warning", ct: ct);
    }

    public async Task<IReadOnlyList<ReupPublishTaskDto>> GetTasksAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReupPublishTaskDto>(
            """
            SELECT pt.id AS Id, pt.tenant_id AS TenantId, pt.campaign_id AS CampaignId, pt.customer_id AS CustomerId,
                   pt.reference_video_id AS ReferenceVideoId, pt.social_page_id AS SocialPageId,
                   pt.page_access_token_id AS PageAccessTokenId, pt.video_asset_id AS VideoAssetId,
                   pt.status AS Status, pt.duplicate_warning AS DuplicateWarning,
                   pt.previous_success_task_id AS PreviousSuccessTaskId,
                   pt.caption_used AS CaptionUsed, pt.hashtags_used AS HashtagsUsed,
                   pt.facebook_video_id AS FacebookVideoId, pt.facebook_post_url AS FacebookPostUrl,
                   pt.token_check_status AS TokenCheckStatus, pt.token_check_error AS TokenCheckError,
                   pt.error_code AS ErrorCode, pt.error_message AS ErrorMessage,
                   pt.attempt_count AS AttemptCount, pt.max_attempts AS MaxAttempts,
                   pt.started_at AS StartedAt, pt.completed_at AS CompletedAt, pt.created_at AS CreatedAt,
                   rv.title AS VideoTitle, rv.source_url AS VideoSourceUrl, rv.thumbnail_url AS VideoThumbnailUrl,
                   cp.page_name AS PageName, cp.external_page_id AS PageExternalId
              FROM reup.publish_tasks pt
              JOIN reup.campaigns c ON c.id = pt.campaign_id
              JOIN content.reference_videos rv ON rv.id = pt.reference_video_id
              JOIN social.customer_pages cp ON cp.id = pt.social_page_id
             WHERE pt.customer_id=@customerId AND pt.campaign_id=@campaignId
             ORDER BY pt.created_at;
            """,
            new { customerId, campaignId });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<ReupPublishLogDto>> GetLogsAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<ReupPublishLogDto>(
            """
            SELECT l.id AS Id, l.tenant_id AS TenantId, l.campaign_id AS CampaignId, l.task_id AS TaskId,
                   l.level AS Level, l.step AS Step, l.message AS Message, l.data::text AS Data, l.created_at AS CreatedAt
              FROM reup.publish_logs l
              JOIN reup.campaigns c ON c.id = l.campaign_id
             WHERE c.customer_id=@customerId AND l.campaign_id=@campaignId
             ORDER BY l.created_at DESC
             LIMIT 300;
            """,
            new { customerId, campaignId });
        return rows.ToList();
    }

    public async Task<(Guid[] VideoIds, Guid[] PageIds)> GetCampaignSelectionsAsync(Guid customerId, Guid campaignId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var owner = await conn.ExecuteScalarAsync<Guid?>("SELECT id FROM reup.campaigns WHERE id=@campaignId AND customer_id=@customerId;", new { campaignId, customerId });
        if (owner is null) throw new InvalidOperationException("Không tìm thấy chiến dịch.");
        var videoIds = (await conn.QueryAsync<Guid>("SELECT reference_video_id FROM reup.campaign_videos WHERE campaign_id=@campaignId ORDER BY order_index;", new { campaignId })).ToArray();
        var pageIds = (await conn.QueryAsync<Guid>("SELECT social_page_id FROM reup.campaign_pages WHERE campaign_id=@campaignId;", new { campaignId })).ToArray();
        return (videoIds, pageIds);
    }

    public async Task RetryTaskAsync(Guid customerId, Guid taskId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var campaignId = await conn.ExecuteScalarAsync<Guid?>(
            """
            UPDATE reup.publish_tasks
               SET status='pending', attempt_count=0, error_code=NULL, error_message=NULL,
                   token_check_status=NULL, token_check_error=NULL, locked_by=NULL, locked_at=NULL,
                   started_at=NULL, completed_at=NULL
             WHERE id=@taskId AND customer_id=@customerId AND status='failed'
             RETURNING campaign_id;
            """,
            new { taskId, customerId });
        if (campaignId is null) throw new InvalidOperationException("Không tìm thấy task lỗi để retry.");
        await conn.ExecuteAsync("UPDATE reup.campaigns SET status='running', stop_requested=false WHERE id=@campaignId AND status IN ('failed','completed','stopped','running');", new { campaignId });
        await RefreshCampaignCountersAsync(campaignId.Value, ct);
        await _logs.WriteAsync(campaignId, taskId, "TASK_RETRY_SCHEDULED", "Manual retry scheduled.", "warning", ct: ct);
    }

    public async Task<ReupTaskExecutionDto?> LeaseNextTaskAsync(string workerId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        var task = await conn.QueryFirstOrDefaultAsync<ReupTaskExecutionDto>(
            """
            WITH candidate AS (
                SELECT pt.id
                  FROM reup.publish_tasks pt
                  JOIN reup.campaigns c ON c.id = pt.campaign_id
                 WHERE pt.status = 'pending'
                   AND c.stop_requested = false
                   AND NOT EXISTS (
                        SELECT 1 FROM reup.publish_tasks running
                         WHERE running.social_page_id = pt.social_page_id
                           AND running.status = ANY(@runningStates)
                   )
                 ORDER BY pt.created_at
                 FOR UPDATE SKIP LOCKED
                 LIMIT 1
            )
            UPDATE reup.publish_tasks pt
               SET status='checking_page',
                   locked_by=@workerId,
                   locked_at=now(),
                   attempt_count=attempt_count+1,
                   started_at=COALESCE(started_at, now())
              FROM candidate
             WHERE pt.id = candidate.id
            RETURNING pt.id AS Id, pt.tenant_id AS TenantId, pt.campaign_id AS CampaignId, pt.customer_id AS CustomerId,
                      pt.reference_video_id AS ReferenceVideoId, pt.social_page_id AS SocialPageId,
                      pt.page_access_token_id AS PageAccessTokenId, pt.video_asset_id AS VideoAssetId,
                      pt.status AS Status, pt.caption_used AS CaptionUsed, pt.hashtags_used AS HashtagsUsed,
                      pt.attempt_count AS AttemptCount, pt.max_attempts AS MaxAttempts;
            """,
            new { workerId, runningStates = RunningStates }, tx);
        if (task is not null)
        {
            var details = await conn.QueryFirstAsync<(string SourceUrl, string Platform, string? ExternalPageId)>(
                """
                SELECT rv.source_url AS SourceUrl, rv.platform AS Platform, cp.external_page_id AS ExternalPageId
                  FROM reup.publish_tasks pt
                  JOIN content.reference_videos rv ON rv.id = pt.reference_video_id
                  JOIN social.customer_pages cp ON cp.id = pt.social_page_id
                 WHERE pt.id=@id;
                """,
                new { id = task.Id }, tx);
            task.ReferenceSourceUrl = details.SourceUrl;
            task.ReferencePlatform = details.Platform;
            task.PageExternalId = details.ExternalPageId;
        }
        tx.Commit();
        if (task is not null)
        {
            await _logs.WriteAsync(task.CampaignId, task.Id, "TASK_LEASED", "Task leased by worker.", data: new { workerId, task.AttemptCount }, ct: ct);
        }
        return task;
    }

    public async Task<ReupPageTokenDto?> GetActivePageTokenAsync(Guid customerId, Guid pageId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<ReupPageTokenDto>(
            """
            SELECT id AS Id, customer_id AS CustomerId, page_id AS PageId, token_value AS TokenValue, token_hint AS TokenHint
              FROM social.page_access_tokens
             WHERE customer_id=@customerId AND page_id=@pageId AND platform='facebook' AND status='active'
             ORDER BY created_at DESC LIMIT 1;
            """,
            new { customerId, pageId });
    }

    public async Task SetTaskStatusAsync(Guid taskId, string status, string? errorCode = null, string? errorMessage = null, Guid? videoAssetId = null, Guid? tokenId = null, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var campaignId = await conn.ExecuteScalarAsync<Guid>(
            """
            UPDATE reup.publish_tasks
               SET status=@status,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   video_asset_id=COALESCE(@videoAssetId, video_asset_id),
                   page_access_token_id=COALESCE(@tokenId, page_access_token_id)
             WHERE id=@taskId
             RETURNING campaign_id;
            """,
            new { taskId, status, errorCode, errorMessage, videoAssetId, tokenId });
        await RefreshCampaignCountersAsync(campaignId, ct);
    }

    public async Task CompleteTaskAsync(Guid taskId, FacebookPublishResult result, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var campaignId = await conn.ExecuteScalarAsync<Guid>(
            """
            UPDATE reup.publish_tasks
               SET status='completed',
                   facebook_video_id=@videoId,
                   facebook_post_url=@postUrl,
                   facebook_raw_response=@raw::jsonb,
                   error_code=NULL,
                   error_message=NULL,
                   completed_at=now(),
                   locked_by=NULL,
                   locked_at=NULL
             WHERE id=@taskId
             RETURNING campaign_id;
            """,
            new { taskId, videoId = result.FacebookVideoId, postUrl = result.FacebookPostUrl, raw = result.RawJson ?? "{}" });
        await RefreshCampaignCountersAsync(campaignId, ct);
    }

    public async Task FailOrRetryTaskAsync(Guid taskId, string errorCode, string errorMessage, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var row = await conn.QueryFirstAsync<(Guid CampaignId, int AttemptCount, int MaxAttempts)>(
            "SELECT campaign_id AS CampaignId, attempt_count AS AttemptCount, max_attempts AS MaxAttempts FROM reup.publish_tasks WHERE id=@taskId;",
            new { taskId });
        var transient = IsTransient(errorCode);
        var nextStatus = transient && row.AttemptCount < row.MaxAttempts ? "pending" : "failed";
        await conn.ExecuteAsync(
            """
            UPDATE reup.publish_tasks
               SET status=@status,
                   error_code=@errorCode,
                   error_message=@errorMessage,
                   completed_at=CASE WHEN @status='failed' THEN now() ELSE NULL END,
                   locked_by=NULL,
                   locked_at=NULL
             WHERE id=@taskId;
            """,
            new { taskId, status = nextStatus, errorCode, errorMessage });
        await _logs.WriteAsync(row.CampaignId, taskId, nextStatus == "pending" ? "TASK_RETRY_SCHEDULED" : "TASK_FAILED", errorMessage, nextStatus == "pending" ? "warning" : "error", new { errorCode, row.AttemptCount, row.MaxAttempts }, ct);
        await RefreshCampaignCountersAsync(row.CampaignId, ct);
    }

    public async Task<ReupVideoAssetDto?> GetReadyAssetAsync(Guid customerId, Guid referenceVideoId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<ReupVideoAssetDto>(
            """
            SELECT id AS Id, tenant_id AS TenantId, customer_id AS CustomerId, reference_video_id AS ReferenceVideoId,
                   source_url AS SourceUrl, platform AS Platform, provider AS Provider, resolved_video_url AS ResolvedVideoUrl,
                   local_path AS LocalPath, file_name AS FileName, file_size_bytes AS FileSizeBytes,
                   content_type AS ContentType, status AS Status, error_code AS ErrorCode, error_message AS ErrorMessage
              FROM reup.video_assets
             WHERE customer_id=@customerId AND reference_video_id=@referenceVideoId AND status='ready'
             ORDER BY created_at DESC LIMIT 1;
            """,
            new { customerId, referenceVideoId });
    }

    public async Task<Guid> CreateResolvingAssetAsync(ReupTaskExecutionDto task, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteScalarAsync<Guid>(
            """
            INSERT INTO reup.video_assets (tenant_id, customer_id, reference_video_id, source_url, platform, provider, status, created_at, updated_at)
            VALUES (@tenantId, @customerId, @referenceVideoId, @sourceUrl, @platform, 'tikwm', 'resolving', now(), now())
            RETURNING id;
            """,
            new { task.TenantId, task.CustomerId, task.ReferenceVideoId, sourceUrl = task.ReferenceSourceUrl, platform = task.ReferencePlatform });
    }

    public async Task MarkAssetReadyAsync(Guid assetId, string resolvedUrl, string localPath, long bytes, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            UPDATE reup.video_assets
               SET status='ready', resolved_video_url=@resolvedUrl, local_path=@localPath,
                   file_name=@fileName, file_size_bytes=@bytes, content_type='video/mp4',
                   error_code=NULL, error_message=NULL
             WHERE id=@assetId;
            """,
            new { assetId, resolvedUrl, localPath, fileName = Path.GetFileName(localPath), bytes });
    }

    public async Task MarkAssetFailedAsync(Guid assetId, string code, string message, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync("UPDATE reup.video_assets SET status='failed', error_code=@code, error_message=@message WHERE id=@assetId;", new { assetId, code, message });
    }

    public async Task ResetStaleLocksAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        var ids = (await conn.QueryAsync<Guid>(
            """
            SELECT id FROM reup.publish_tasks
             WHERE status = ANY(@runningStates)
               AND locked_at IS NOT NULL
               AND locked_at < now() - @timeout::interval;
            """,
            new { runningStates = RunningStates, timeout = $"{Math.Max(1, timeout.TotalMinutes):0} minutes" })).ToArray();
        foreach (var id in ids)
        {
            await FailOrRetryTaskAsync(id, "TASK_TIMEOUT", "Task timed out and was reset by worker.", ct);
        }
    }

    public async Task RefreshCampaignCountersAsync(Guid campaignId, CancellationToken ct = default)
    {
        using var conn = await _factory.OpenAsync(ct);
        await conn.ExecuteAsync(
            """
            WITH counts AS (
                SELECT campaign_id,
                       count(*)::int AS total,
                       count(*) FILTER (WHERE status='pending')::int AS pending,
                       count(*) FILTER (WHERE status IN ('checking_page','resolving_video','publishing'))::int AS running,
                       count(*) FILTER (WHERE status='completed')::int AS completed,
                       count(*) FILTER (WHERE status='failed')::int AS failed,
                       count(*) FILTER (WHERE status='cancelled')::int AS cancelled,
                       count(*) FILTER (WHERE duplicate_warning)::int AS duplicates
                  FROM reup.publish_tasks
                 WHERE campaign_id=@campaignId
                 GROUP BY campaign_id
            )
            UPDATE reup.campaigns c
               SET total_tasks=COALESCE(counts.total,0),
                   pending_tasks=COALESCE(counts.pending,0),
                   running_tasks=COALESCE(counts.running,0),
                   completed_tasks=COALESCE(counts.completed,0),
                   failed_tasks=COALESCE(counts.failed,0),
                   cancelled_tasks=COALESCE(counts.cancelled,0),
                   duplicate_warning_count=COALESCE(counts.duplicates,0),
                   status = CASE
                       WHEN c.status='stopping' AND COALESCE(counts.running,0)=0 THEN 'stopped'
                       WHEN c.status IN ('running','stopping') AND COALESCE(counts.total,0)>0 AND COALESCE(counts.pending,0)=0 AND COALESCE(counts.running,0)=0 AND COALESCE(counts.failed,0)>0 THEN 'failed'
                       WHEN c.status IN ('running','stopping') AND COALESCE(counts.total,0)>0 AND COALESCE(counts.pending,0)=0 AND COALESCE(counts.running,0)=0 THEN 'completed'
                       ELSE c.status
                   END,
                   completed_at = CASE
                       WHEN c.completed_at IS NULL AND c.status IN ('running','stopping') AND COALESCE(counts.total,0)>0 AND COALESCE(counts.pending,0)=0 AND COALESCE(counts.running,0)=0 THEN now()
                       ELSE c.completed_at
                   END
              FROM counts
             WHERE c.id=counts.campaign_id;
            """,
            new { campaignId });
    }

    private async Task ValidateRequestAsync(Guid customerId, CreateReupCampaignRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new InvalidOperationException("Vui lòng nhập tên chiến dịch.");
        var videoIds = request.ReferenceVideoIds.Distinct().ToArray();
        var pageIds = request.SocialPageIds.Distinct().ToArray();
        if (videoIds.Length == 0) throw new InvalidOperationException("Vui lòng chọn ít nhất 1 video TikTok.");
        if (pageIds.Length == 0) throw new InvalidOperationException("Vui lòng chọn ít nhất 1 Facebook Page.");

        using var conn = await _factory.OpenAsync(ct);
        var validVideos = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM content.reference_videos WHERE customer_id=@customerId AND id=ANY(@videoIds) AND platform='tiktok' AND is_deleted=false;",
            new { customerId, videoIds });
        var validPages = await conn.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM social.customer_pages WHERE customer_id=@customerId AND id=ANY(@pageIds) AND platform='facebook';",
            new { customerId, pageIds });
        if (validVideos != videoIds.Length) throw new InvalidOperationException("Một số video không hợp lệ hoặc không thuộc khách hàng hiện tại.");
        if (validPages != pageIds.Length) throw new InvalidOperationException("Một số Facebook Page không hợp lệ hoặc không thuộc khách hàng hiện tại.");
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTransient(string errorCode)
        => errorCode is "TIKWM_TIMEOUT" or "VIDEO_DOWNLOAD_TIMEOUT" or "FACEBOOK_TIMEOUT" or "HTTP_5XX" or "FACEBOOK_5XX" or "TASK_TIMEOUT";

    private const string CampaignSelectSql =
        """
        SELECT c.id AS Id, c.tenant_id AS TenantId, c.customer_id AS CustomerId, c.created_by_user_id AS CreatedByUserId,
               c.name AS Name, c.description AS Description, c.caption AS Caption, c.hashtags AS Hashtags,
               c.status AS Status, c.total_tasks AS TotalTasks, c.pending_tasks AS PendingTasks,
               c.running_tasks AS RunningTasks, c.completed_tasks AS CompletedTasks, c.failed_tasks AS FailedTasks,
               c.cancelled_tasks AS CancelledTasks, c.duplicate_warning_count AS DuplicateWarningCount,
               c.stop_requested AS StopRequested, c.error_code AS ErrorCode, c.error_message AS ErrorMessage,
               c.started_at AS StartedAt, c.completed_at AS CompletedAt, c.stopped_at AS StoppedAt,
               c.created_at AS CreatedAt, c.updated_at AS UpdatedAt,
               COALESCE(v.video_count,0)::int AS VideoCount,
               COALESCE(p.page_count,0)::int AS PageCount
          FROM reup.campaigns c
          LEFT JOIN (SELECT campaign_id, count(*) AS video_count FROM reup.campaign_videos GROUP BY campaign_id) v ON v.campaign_id=c.id
          LEFT JOIN (SELECT campaign_id, count(*) AS page_count FROM reup.campaign_pages GROUP BY campaign_id) p ON p.campaign_id=c.id
        """;
}
