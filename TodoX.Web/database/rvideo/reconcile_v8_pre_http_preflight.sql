-- READ-ONLY operator preflight. Requires psql -v ON_ERROR_STOP=1 -v tenant_id=<UUID>.
-- Review all rows and production schema/constraints before approving reconciliation.
-- No DDL, DML, locks or writes. Do not interpret empty provider IDs as proof of no HTTP;
-- independently verify provider request logs and obtain operator confirmation.
-- Production snapshot: V8 manual-review error is expected. The ownership orphan
-- 4190c02c-5279-4534-8ec6-95fbcbbf05b9 must have no lease, version or intent.
-- V3-V6 queued versions attached to failed jobs are historical only if their
-- provider IDs, submit timestamps and events show no unresolved submission.
-- V8 ONLY: expected submitted_at = 2026-10-22 00:50:42.110+07.
-- Before 3b9ca718, MarkSceneVideoVersionSubmittedAsync ran even when the
-- null submitToken skipped BeginSubmitAsync/adapter.SubmitAsync. Independently
-- confirm no HTTP; reject any differing timestamp or provider/submit evidence.
SELECT j.id, j.tenant_id, j.job_type, j.status, j.error_code, j.input_json,
       j.provider_task_id, j.provider_video_id_base, j.lock_owner, j.lock_until,
       j.worker_key, j.completed_at
FROM render.render_jobs j WHERE j.id='d555306e-9c55-4552-9896-113a1334bfa2'
  AND j.tenant_id=:'tenant_id'::uuid;

SELECT v.id AS expected_version_id, v.version_number, v.tenant_id, v.project_id,
       v.scene_id, v.render_job_id, v.logical_request_id AS expected_logical_request_id,
       v.status, v.error_code AS expected_version_error_code, v.provider_task_id,
       v.provider_video_id_base, v.submitted_at, v.completed_at, v.result_media_id,
       v.public_url, v.is_selected
FROM video_render.scene_video_versions v
WHERE v.tenant_id=:'tenant_id'::uuid AND v.project_id=183 AND v.scene_id=814
ORDER BY v.version_number DESC;

SELECT i.* FROM render.scene_video_submit_intents i
WHERE i.tenant_id=:'tenant_id'::uuid AND i.scene_id=814
ORDER BY i.created_at DESC;

SELECT j.id, j.status, j.error_code, j.provider_task_id, j.provider_video_id_base,
       j.lock_owner, j.lock_until, j.input_json
FROM render.render_jobs j WHERE j.tenant_id=:'tenant_id'::uuid
  AND j.job_type='render_scene_video'
  AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')='814'
ORDER BY j.created_at DESC;

SELECT s.id, s.project_id, s.tenant_id, s.status, s.selected_video_version_id
FROM video_render.video_project_scenes s
WHERE s.id=814 AND s.project_id=183 AND s.tenant_id=:'tenant_id'::uuid;

SELECT e.event_type, e.created_at, e.data_json FROM render.render_job_events e
WHERE e.tenant_id=:'tenant_id'::uuid AND e.job_id='d555306e-9c55-4552-9896-113a1334bfa2'
ORDER BY e.created_at;

SELECT j.id, j.status, j.error_code, j.lock_owner, j.lock_until,
       v.id AS version_id, i.submit_state
FROM render.render_jobs j
LEFT JOIN video_render.scene_video_versions v ON v.tenant_id=j.tenant_id AND v.render_job_id=j.id
LEFT JOIN render.scene_video_submit_intents i ON i.tenant_id=j.tenant_id AND i.render_job_id=j.id
WHERE j.tenant_id=:'tenant_id'::uuid AND j.id='4190c02c-5279-4534-8ec6-95fbcbbf05b9';

SELECT v.id, v.version_number, v.status AS version_status, v.submitted_at,
       v.provider_task_id, v.provider_video_id_base, j.status AS job_status,
       j.provider_task_id AS job_provider_task, j.provider_video_id_base AS job_provider_video,
       e.event_type, e.data_json
FROM video_render.scene_video_versions v
LEFT JOIN render.render_jobs j ON j.id=v.render_job_id AND j.tenant_id=v.tenant_id
LEFT JOIN render.render_job_events e ON e.job_id=j.id AND e.tenant_id=j.tenant_id
WHERE v.tenant_id=:'tenant_id'::uuid AND v.scene_id=814 AND v.version_number BETWEEN 3 AND 6
ORDER BY v.version_number, e.created_at;

-- Check the actual production columns and constraints: source migrations are not a snapshot.
SELECT table_schema, table_name, column_name, data_type FROM information_schema.columns
WHERE (table_schema,table_name) IN (('render','render_jobs'),
    ('render','scene_video_submit_intents'),('video_render','scene_video_versions'))
  AND column_name IN ('tenant_id','render_job_id','logical_request_id','status',
    'submit_state','provider_task_id','provider_video_id_base','error_code',
    'lock_owner','lock_until','worker_key','submitted_at','completed_at',
    'result_media_id','public_url','reconciled_at','updated_at','is_selected')
ORDER BY table_schema,table_name,column_name;

SELECT n.nspname, c.relname, con.conname, pg_get_constraintdef(con.oid) AS definition
FROM pg_constraint con JOIN pg_class c ON c.oid=con.conrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE (n.nspname,c.relname) IN (('render','render_jobs'),
    ('render','scene_video_submit_intents'),('video_render','scene_video_versions'))
ORDER BY n.nspname,c.relname,con.conname;