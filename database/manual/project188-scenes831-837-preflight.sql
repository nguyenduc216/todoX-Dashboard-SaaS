-- OPERATOR ONLY: read-only evidence collection. Execute the complete script in DBeaver.
-- Core job 2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432 / project 188 / scenes 831-837.
-- This inventories ALL matching attempts, not just an assumed set of seven child IDs.
-- No update, lease stealing, requeue, provider call, or inference of provider absence.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_database(), current_user, clock_timestamp() AS inspected_at;
SELECT p.id,p.tenant_id,to_jsonb(p)->>'core_job_id' AS core_job_id_if_present
FROM video_render.video_projects p WHERE p.id=188;
SELECT s.id,s.tenant_id,s.project_id,s.scene_index,s.status,s.selected_video_version_id
FROM video_render.video_project_scenes s WHERE s.project_id=188 AND s.id BETWEEN 831 AND 837 ORDER BY s.id;
SELECT j.id,j.tenant_id,j.status,j.error_code,j.error_message,j.created_at,j.updated_at,
       j.lock_owner,j.worker_key,j.lock_until,j.retry_after,
       j.provider_task_id,j.provider_video_id_base,
       COALESCE(j.input_json->>'logicalRequestId',j.input_json->>'logical_request_id') AS job_logical_request_id,
       v.id AS version_id,v.version_number,v.status AS version_status,v.logical_request_id,v.render_job_id,
       v.project_id,v.scene_id,v.provider_task_id AS version_task,v.provider_video_id_base AS version_base,
       v.render_job_id=j.id AS job_matches,
       v.project_id::text=COALESCE(j.input_json->>'projectId',j.input_json->>'project_id') AS project_matches,
       v.scene_id::text=COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id') AS scene_matches,
       v.logical_request_id=COALESCE(j.input_json->>'logicalRequestId',j.input_json->>'logical_request_id') AS base_request_matches
FROM render.render_jobs j
JOIN video_render.video_projects p ON p.tenant_id=j.tenant_id AND p.id=188
LEFT JOIN video_render.scene_video_versions v ON v.render_job_id=j.id AND v.tenant_id=j.tenant_id
WHERE j.job_type='render_scene_video'
  AND COALESCE(j.input_json->>'projectId',j.input_json->>'project_id')='188'
  AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id') IN ('831','832','833','834','835','836','837')
ORDER BY COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id'),j.created_at,v.version_number;
SELECT i.scene_id,i.logical_request_id,i.render_job_id,i.submit_state,i.owner_token,
       i.provider_task_id,i.provider_video_id_base,i.created_at,i.updated_at,i.reconciled_at
FROM render.scene_video_submit_intents i
JOIN video_render.video_projects p ON p.id=188 AND p.tenant_id=i.tenant_id
WHERE i.scene_id BETWEEN 831 AND 837 ORDER BY i.scene_id,i.created_at;
-- Safe event index only. Operator reviews original sanitized logs separately; no prompts or credentials exported.
SELECT e.job_id,e.event_type,e.level,e.created_at
FROM render.render_job_events e JOIN render.render_jobs j ON j.id=e.job_id AND j.tenant_id=e.tenant_id
JOIN video_render.video_projects p ON p.id=188 AND p.tenant_id=j.tenant_id
WHERE j.job_type='render_scene_video'
 AND COALESCE(j.input_json->>'projectId',j.input_json->>'project_id')='188'
 AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id') IN ('831','832','833','834','835','836','837')
ORDER BY e.created_at;
COMMIT;
