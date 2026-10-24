-- Read-only facts; activation does not prove provider acceptance or completion.
BEGIN READ ONLY;
SELECT t.id,t.tenant_id,t.project_id,t.scene_id,t.transition_state,t.old_provider_outcome,
 t.new_job_id,t.new_version_id,t.proposed_generation,j.status AS new_job_status,
 v.status AS new_version_status,j.status='draft' AS new_job_not_claimable,
 v.render_job_id=t.new_job_id AND v.logical_request_id=t.new_logical_request_id
  AND v.scene_id=t.scene_id AND v.project_id=t.project_id AND v.tenant_id=t.tenant_id AS new_ownership_matches,
 h.generation=t.proposed_generation AND h.active_render_job_id=t.new_job_id
  AND h.active_video_version_id=t.new_version_id AND t.transition_state='ACTIVATED'
  AND t.activated_at IS NOT NULL AS is_current_activated_operation,
 CASE WHEN t.transition_state='PREPARED' THEN j.status='draft' AND h.generation=t.old_generation
   WHEN t.transition_state='ABANDONED' THEN j.status='cancelled' AND t.abandoned_at IS NOT NULL
   ELSE t.activated_at IS NOT NULL AND h.generation>=t.proposed_generation AND j.status<>'draft' END AS state_consistent,
 oldj.provider_task_id IS NOT DISTINCT FROM t.old_job_snapshot->>'provider_task_id'
  AND oldj.provider_video_id_base IS NOT DISTINCT FROM t.old_job_snapshot->>'provider_video_id_base' AS old_provider_identity_retained,
 video_render.scene_video_job_is_current(t.tenant_id,t.old_job_id) AS old_job_still_current,
 s.selected_video_version_id,s.scene_video_url,
 (SELECT count(*) FROM render.scene_video_submit_intents i WHERE i.tenant_id=t.tenant_id AND i.render_job_id=t.new_job_id) AS new_submit_intent_count
FROM video_render.scene_video_operation_transitions t
JOIN video_render.scene_video_operation_heads h USING(tenant_id,project_id,scene_id)
JOIN render.render_jobs j ON j.id=t.new_job_id AND j.tenant_id=t.tenant_id
JOIN render.render_jobs oldj ON oldj.id=t.old_job_id AND oldj.tenant_id=t.tenant_id
JOIN video_render.scene_video_versions v ON v.id=t.new_version_id AND v.tenant_id=t.tenant_id
JOIN video_render.video_project_scenes s ON s.id=t.scene_id AND s.tenant_id=t.tenant_id
ORDER BY t.project_id,t.scene_id,t.proposed_generation,t.created_at;
COMMIT;
