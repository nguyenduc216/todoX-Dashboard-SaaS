-- Read-only. Run against the target database before deploying coordinator-enabled workers.
SELECT to_regclass('render.render_jobs') AS render_jobs,
       to_regclass('video_render.video_project_scenes') AS video_project_scenes,
       to_regclass('render.scene_video_submit_intents') AS existing_submit_intents;
SELECT column_name, data_type
FROM information_schema.columns
WHERE table_schema = 'render' AND table_name = 'render_jobs'
  AND column_name IN ('id', 'tenant_id', 'input_json', 'provider_task_id', 'provider_video_id_base')
ORDER BY column_name;
-- Execute these only after confirming the referenced tables exist above.
SELECT current_user AS executing_role,
       has_schema_privilege(current_user, 'render', 'USAGE') AS render_usage,
       has_schema_privilege(current_user, 'render', 'CREATE') AS render_create,
       has_table_privilege(current_user, 'render.render_jobs', 'SELECT') AS render_jobs_select;
SELECT j.id, j.tenant_id, j.status, j.provider_task_id, j.provider_video_id_base,
       j.input_json->>'sceneId' AS scene_id, j.input_json->>'logicalRequestId' AS logical_request_id
FROM render.render_jobs j
WHERE j.job_type='render_scene_video'
  AND j.status IN ('queued', 'preparing', 'rendering', 'processing', 'post_processing', 'pending_reconciliation')
ORDER BY j.created_at;