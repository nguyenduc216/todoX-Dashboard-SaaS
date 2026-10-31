using Dapper;
using TodoX.Web.Data;
using TodoX.Web.Services.Render;
using TodoX.Web.Services.VideoRender;

namespace TodoX.Web.Services.JobRepair;

public sealed class JobRepairRepository(TodoXConnectionFactory factory, TenantContext tenant)
{
    public async Task<IReadOnlyList<JobRepairSceneSnapshot>> GetSceneSnapshotsAsync(Guid coreJobId, CancellationToken ct = default)
    {
        await tenant.EnsureLoadedAsync(ct);
        using var conn = await factory.OpenAsync(ct);
        var rows = await conn.QueryAsync<JobRepairSceneSnapshot>(new CommandDefinition($"""
            SELECT p.id AS ProjectId, s.id AS SceneId, s.scene_index AS SceneIndex, s.status AS SceneStatus,
                   core.service_id AS ServiceId, h.generation AS Generation, h.active_render_job_id AS HeadJobId,
                   hj.status AS HeadJobStatus, hj.error_code AS HeadErrorCode,
                   NULLIF(btrim(hj.provider_task_id),'') IS NOT NULL AS HeadHasProviderTask,
                   NULLIF(btrim(hj.provider_video_id_base),'') IS NOT NULL AS HeadHasProviderVideoId,
                   COALESCE(hj.lock_until > now(), false) AS HeadLockActive,
                   COALESCE((EXTRACT(EPOCH FROM now() - COALESCE(hj.updated_at, hj.created_at)) / 60)::int, 0) AS HeadIdleMinutes,
                   hv.status AS HeadVersionStatus,
                   (SELECT string_agg(i.submit_state, ',') FROM render.scene_video_submit_intents i
                     WHERE i.tenant_id=s.tenant_id AND i.render_job_id=h.active_render_job_id) AS SubmitStates,
                   hj.id IS NOT NULL AND hv.id IS NOT NULL AND hj.status<>'draft' AS AckFound,
                   (SELECT count(*)::int FROM video_render.scene_video_operation_transitions t
                     WHERE t.tenant_id=s.tenant_id AND t.scene_id=s.id AND t.transition_state='PREPARED') AS PreparedCount,
                   (SELECT count(*)::int FROM render.render_jobs j
                     WHERE j.tenant_id=s.tenant_id AND j.job_type='render_scene_video'
                       AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=s.id::text
                       AND ({SceneVideoOperationDraftService.NeverSubmittedOrphanJobSql})) AS OrphanJobCount,
                   (SELECT count(*)::int FROM render.render_jobs j
                     WHERE j.tenant_id=s.tenant_id AND j.job_type='render_scene_video'
                       AND NOT video_render.scene_video_job_is_superseded(j.tenant_id,j.id)
                       AND j.id IS DISTINCT FROM h.active_render_job_id
                       AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=s.id::text
                       AND j.status NOT IN('completed','failed','cancelled')
                       AND NOT ({SceneVideoOperationDraftService.NeverSubmittedOrphanJobSql})) AS CompetingJobCount
              FROM render.render_jobs core
              JOIN video_render.video_projects p ON p.core_job_id=core.id AND p.tenant_id=core.tenant_id
              JOIN video_render.video_project_scenes s ON s.project_id=p.id AND s.tenant_id=p.tenant_id
              LEFT JOIN video_render.scene_video_operation_heads h ON h.tenant_id=s.tenant_id AND h.scene_id=s.id
              LEFT JOIN render.render_jobs hj ON hj.id=h.active_render_job_id AND hj.tenant_id=s.tenant_id
              LEFT JOIN video_render.scene_video_versions hv ON hv.id=h.active_video_version_id AND hv.tenant_id=s.tenant_id
             WHERE core.id=@coreJobId AND core.tenant_id=@tenant
             ORDER BY s.scene_index;
            """, new { coreJobId, tenant = tenant.TenantId }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>
    /// Gives a known provider task a fresh, still finite recovery budget. Nothing is sent to the provider
    /// here; the provider identifiers and the poll clock stay as they are.
    /// </summary>
    public async Task<bool> ResetKnownTaskRecoveryBudgetAsync(long projectId, long sceneId, Guid headJobId, CancellationToken ct = default)
    {
        await tenant.EnsureLoadedAsync(ct);
        using var conn = await factory.OpenAsync(ct);
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@lockName, 0));",
            new { lockName = RenderJobService.BuildSceneJobLockName(RenderJobTypes.RenderSceneVideo, sceneId) }, tx);
        var changed = await conn.ExecuteAsync("""
            UPDATE render.render_jobs j
               SET input_json=input_json - 'providerRecoveryStartedAt' - 'providerRecoveryPollCount', updated_at=now()
             WHERE j.id=@headJobId AND j.tenant_id=@tenant AND j.job_type='render_scene_video'
               AND j.status='pending_reconciliation'
               AND j.error_code IS DISTINCT FROM 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
               AND NOT (COALESCE(j.input_json,'{}'::jsonb) ? 'sceneVideoManualReviewAt')
               AND NULLIF(btrim(j.provider_task_id),'') IS NOT NULL
               AND NULLIF(btrim(j.provider_video_id_base),'') IS NOT NULL
               AND j.input_json->>'projectId'=@project AND j.input_json->>'sceneId'=@scene
               AND EXISTS(SELECT 1 FROM video_render.scene_video_operation_heads h
                           WHERE h.tenant_id=j.tenant_id AND h.scene_id=@sceneId AND h.active_render_job_id=j.id);
            """, new { headJobId, tenant = tenant.TenantId, project = projectId.ToString(), scene = sceneId.ToString(), sceneId }, tx);
        tx.Commit();
        return changed == 1;
    }
}
