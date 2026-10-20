using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;

namespace TodoX.Web.Services.VideoRender;

public sealed record SceneVideoSubmitState(string SubmitState, Guid OwnerToken, string? ProviderTaskId, string? ProviderVideoIdBase);

public sealed class SceneVideoSubmitCoordinator
{
    private readonly TodoXConnectionFactory _factory;
    private readonly TenantContext _tenant;

    public SceneVideoSubmitCoordinator(TodoXConnectionFactory factory, TenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    public async Task<Guid?> ClaimSubmitAsync(long projectId, long sceneId, string logicalRequestId, Guid jobId, CancellationToken ct = default)
    {
        if (projectId <= 0 || sceneId <= 0 || jobId == Guid.Empty || string.IsNullOrWhiteSpace(logicalRequestId))
            throw new ArgumentException("Invalid scene-video submit identity.");
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new { key = RenderJobService.BuildSceneJobLockName(RenderJobTypes.RenderSceneVideo, sceneId) }, tx);
        var owned = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM video_render.video_project_scenes s
                JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
                JOIN render.render_jobs j ON j.id=@jobId AND j.tenant_id=s.tenant_id
                    AND j.job_type='render_scene_video'
                WHERE s.id=@sceneId AND s.project_id=@projectId AND s.tenant_id=@tenant
                    AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=s.id::text
                    AND COALESCE(j.input_json->>'projectId', j.input_json->>'project_id')=p.id::text);
            """, new { projectId, sceneId, jobId, tenant = _tenant.TenantId }, tx);
        if (!owned) throw new InvalidOperationException("Scene-video submit tenant or job ownership mismatch.");

        var blocked = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
                JOIN render.render_jobs prior ON prior.id=i.render_job_id AND prior.tenant_id=i.tenant_id
                WHERE i.tenant_id=@tenant AND i.scene_id=@sceneId
                  AND NOT ((i.submit_state='CONFIRMED_PROVIDER_TASK' AND prior.status='completed')
                           OR (i.submit_state='CONFIRMED_NO_TASK' AND i.render_job_id=@jobId)))
              OR EXISTS (SELECT 1 FROM render.render_jobs j
                WHERE j.tenant_id=@tenant AND j.job_type='render_scene_video'
                  AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')=@sceneText
                  AND (j.status='pending_reconciliation'
                    OR j.error_code='SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                    OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
                    OR (j.status NOT IN ('completed', 'failed') AND
                        (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
                         OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL))))
              OR EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                WHERE v.tenant_id=@tenant AND v.scene_id=@sceneId
                  AND ((v.status NOT IN ('completed', 'failed') AND
                        (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                         OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
                    OR v.status='pending_reconciliation'));
            """, new { tenant = _tenant.TenantId, sceneId, sceneText = sceneId.ToString() }, tx);
        if (blocked) { tx.Commit(); return null; }
        var token = Guid.NewGuid();
        var inserted = await conn.ExecuteAsync(
            """
            INSERT INTO render.scene_video_submit_intents
                (tenant_id, scene_id, logical_request_id, render_job_id, owner_token)
            VALUES (@tenant, @sceneId, @logicalRequestId, @jobId, @token)
            ON CONFLICT (tenant_id, scene_id, logical_request_id) DO NOTHING;
            """, new { tenant = _tenant.TenantId, sceneId, logicalRequestId, jobId, token }, tx);
        tx.Commit();
        return inserted == 1 ? token : null;
    }

    public async Task<bool> BeginSubmitAsync(Guid token, CancellationToken ct = default)
        => await UpdateAsync(token, "SUBMIT_IN_FLIGHT", null, null, "SUBMIT_INTENT_PERSISTED", ct);

    public async Task<bool> ConfirmSubmittedAsync(Guid token, string taskId, string? videoIdBase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(taskId)) throw new ArgumentException("Provider task ID required.", nameof(taskId));
        return await UpdateAsync(token, "CONFIRMED_PROVIDER_TASK", taskId, videoIdBase, "SUBMIT_IN_FLIGHT", ct);
    }

    public Task<bool> ConfirmNoTaskAsync(Guid token, CancellationToken ct = default)
        => UpdateAsync(token, "CONFIRMED_NO_TASK", null, null, "SUBMIT_IN_FLIGHT", ct);

    public Task<bool> MarkAmbiguousAsync(Guid token, CancellationToken ct = default)
        => UpdateAsync(token, "AMBIGUOUS_SUBMIT", null, null, "SUBMIT_IN_FLIGHT", ct);

    public async Task<SceneVideoSubmitState?> GetSubmitStateAsync(long sceneId, string logicalRequestId, CancellationToken ct = default)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<SceneVideoSubmitState>(
            """
            SELECT submit_state AS SubmitState, owner_token AS OwnerToken,
                   provider_task_id AS ProviderTaskId, provider_video_id_base AS ProviderVideoIdBase
              FROM render.scene_video_submit_intents
             WHERE tenant_id=@tenant AND scene_id=@sceneId AND logical_request_id=@logicalRequestId;
            """, new { tenant = _tenant.TenantId, sceneId, logicalRequestId });
    }

    private async Task<bool> UpdateAsync(Guid token, string next, string? taskId, string? videoIdBase, string expected, CancellationToken ct)
    {
        await _tenant.EnsureLoadedAsync(ct);
        using var conn = await _factory.OpenAsync(ct);
        return await conn.ExecuteAsync(
            """
            UPDATE render.scene_video_submit_intents
               SET submit_state=@next, provider_task_id=COALESCE(@taskId, provider_task_id),
                   provider_video_id_base=COALESCE(@videoIdBase, provider_video_id_base),
                   reconciled_at=CASE WHEN @next IN ('CONFIRMED_PROVIDER_TASK', 'CONFIRMED_NO_TASK') THEN now() ELSE reconciled_at END,
                   updated_at=now()
             WHERE tenant_id=@tenant AND owner_token=@token AND submit_state=@expected;
            """, new { tenant = _tenant.TenantId, token, next, expected, taskId, videoIdBase }) == 1;
    }
}