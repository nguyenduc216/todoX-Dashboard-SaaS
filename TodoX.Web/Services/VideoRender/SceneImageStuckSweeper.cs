using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

/// <summary>
/// Releases scene images that nothing is working on any more, so a scene never shows "Đang tạo ảnh" forever
/// with the "Tạo ảnh" button locked (scene 04 of job 246d162f, 16/11/2026: the 79AI task was cancelled after the
/// job had used up its retry budget, and the job stayed "rendering").
/// 1. An image job abandoned mid-run (stale lock) is queued again as a provider poll, at most
///    <see cref="MaxRequeues"/> times: the handler then reads the 79AI task once more and, on error or timeout,
///    moves on to the next image model.
/// 2. An image version with no live job for <see cref="OrphanMinutes"/> minutes is marked failed.
/// No points are involved: scene images are charged only after a successful result.
/// </summary>
public sealed class SceneImageStuckSweeper : BackgroundService
{
    public const string StuckCode = "SCENE_IMAGE_STUCK";
    public const int MaxRequeues = 3;
    public const int StaleLockMinutes = 10;
    public const int OrphanMinutes = SceneImageRenderWorkItemHandler.DefaultPollTimeoutMinutes + 30;

    internal const string ActiveStatuses = "'queued','submitted','pending','processing','pending_reconciliation'";

    internal const string RequeueSql = $"""
        UPDATE render.render_jobs j
           SET status='queued', retry_after=now(), lock_owner=NULL, lock_until=NULL, updated_at=now(),
               input_json=COALESCE(j.input_json,'{"{}"}'::jsonb) || jsonb_build_object(
                   'providerPoll', true,
                   'stuckRequeueCount', COALESCE(NULLIF(j.input_json->>'stuckRequeueCount','')::int,0)+1),
               error_code='SCENE_IMAGE_STUCK_REQUEUED',
               error_message='Image job was abandoned mid-run; queued again to read the provider task.'
         WHERE j.tenant_id=@tenant AND j.job_type='render_scene_image'
           AND j.status IN ('preparing','rendering','post_processing')
           AND (j.lock_until IS NULL OR j.lock_until < now() - make_interval(mins => @staleMinutes))
           AND j.updated_at < now() - make_interval(mins => @staleMinutes)
           AND COALESCE(NULLIF(j.input_json->>'stuckRequeueCount','')::int,0) < @maxRequeues
           AND EXISTS (SELECT 1 FROM video_render.scene_image_versions v
                        WHERE v.tenant_id=j.tenant_id AND v.id::text=j.input_json->>'imageVersionId'
                          AND v.status IN ({ActiveStatuses}))
        RETURNING j.id;
        """;

    internal const string ReleaseSql = $"""
        UPDATE video_render.scene_image_versions v
           SET status='failed', error_code=@code, error_message=@message, updated_at=now()
         WHERE v.tenant_id=@tenant AND v.status IN ({ActiveStatuses})
           AND v.updated_at < now() - make_interval(mins => @orphanMinutes)
           AND NOT EXISTS (
               SELECT 1 FROM render.render_jobs j
                WHERE j.tenant_id=v.tenant_id AND j.job_type='render_scene_image'
                  AND j.input_json->>'imageVersionId'=v.id::text
                  AND (j.status='queued'
                       OR (j.status IN ('preparing','rendering','post_processing')
                           AND ((j.lock_until IS NOT NULL AND j.lock_until > now() - make_interval(mins => @staleMinutes))
                                OR COALESCE(NULLIF(j.input_json->>'stuckRequeueCount','')::int,0) < @maxRequeues))))
        RETURNING v.id AS VersionId, v.project_id AS ProjectId, v.scene_id AS SceneId;
        """;

    internal const string FailJobsSql = """
        UPDATE render.render_jobs
           SET status='failed', error_code=@code, error_message=@message, lock_owner=NULL, lock_until=NULL, updated_at=now()
         WHERE tenant_id=@tenant AND job_type='render_scene_image'
           AND input_json->>'imageVersionId' = ANY(@ids)
           AND status IN ('preparing','rendering','post_processing');
        """;

    private const string ReleaseMessage = "Ảnh bị treo (không còn tiến trình xử lý); hệ thống đã gỡ để tạo lại.";

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SceneImageStuckSweeper> _logger;

    public SceneImageStuckSweeper(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<SceneImageStuckSweeper> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configuration.GetValue("SceneImageStuckSweep:Enabled", true)) return;
        var interval = TimeSpan.FromMinutes(Math.Clamp(_configuration.GetValue("SceneImageStuckSweep:IntervalMinutes", 5), 1, 60));
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "SCENE_IMAGE_STUCK_SWEEP_FAILED"); }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantContext>();
        await tenant.EnsureLoadedAsync(ct);
        var factory = scope.ServiceProvider.GetRequiredService<TodoXConnectionFactory>();
        var jobs = scope.ServiceProvider.GetRequiredService<IRenderJobService>();
        var repo = scope.ServiceProvider.GetRequiredService<VideoRenderRepository>();
        using var conn = await factory.OpenAsync(ct);

        var requeued = (await conn.QueryAsync<Guid>(new CommandDefinition(RequeueSql,
            new { tenant = tenant.TenantId, staleMinutes = StaleLockMinutes, maxRequeues = MaxRequeues }, cancellationToken: ct))).ToList();
        foreach (var jobId in requeued)
        {
            _logger.LogWarning("SCENE_IMAGE_STUCK_REQUEUED jobId={JobId}", jobId);
            await jobs.AddEventAsync(jobId, "SCENE_IMAGE_STUCK_REQUEUED",
                "Image job was abandoned mid-run; queued again to read the provider task.", null, "warning", ct);
        }

        var released = (await conn.QueryAsync<(Guid VersionId, long ProjectId, long SceneId)>(new CommandDefinition(ReleaseSql,
            new { tenant = tenant.TenantId, code = StuckCode, message = ReleaseMessage, orphanMinutes = OrphanMinutes,
                staleMinutes = StaleLockMinutes, maxRequeues = MaxRequeues }, cancellationToken: ct))).ToList();
        if (released.Count == 0) return;

        await conn.ExecuteAsync(new CommandDefinition(FailJobsSql,
            new { tenant = tenant.TenantId, code = StuckCode, message = ReleaseMessage,
                ids = released.Select(x => x.VersionId.ToString()).ToArray() }, cancellationToken: ct));
        foreach (var row in released)
        {
            _logger.LogWarning("SCENE_IMAGE_STUCK_RELEASED versionId={VersionId} projectId={ProjectId} sceneId={SceneId}",
                row.VersionId, row.ProjectId, row.SceneId);
            await repo.AddProjectEventAsync(row.ProjectId, "SCENE_IMAGE_STUCK_RELEASED", "warning", ReleaseMessage,
                new { versionId = row.VersionId, sceneId = row.SceneId }, ct);
        }
    }
}
