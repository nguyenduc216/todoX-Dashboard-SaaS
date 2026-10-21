-- READ-ONLY operator preflight. Requires psql -v ON_ERROR_STOP=1 -v tenant_id=<UUID>.
-- Review all rows and production schema/constraints before approving reconciliation.
-- No DDL, DML, locks or writes. Do not interpret empty provider IDs as proof of no HTTP;
-- independently verify provider request logs and obtain operator confirmation.
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