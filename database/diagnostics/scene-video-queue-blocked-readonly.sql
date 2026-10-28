-- Operator-run READ ONLY collector. No credentials, repairs, resets or provider calls.
-- Compare complete function definitions with the reviewed deployment source, not just names.
BEGIN READ ONLY;
SELECT current_database(), current_setting('server_version');
SELECT n.nspname AS schema_name,p.proname,
       pg_get_function_identity_arguments(p.oid) AS arguments,
       md5(pg_get_functiondef(p.oid)) AS definition_md5,
       pg_get_functiondef(p.oid) AS definition
FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
WHERE n.nspname='video_render'
  AND p.proname IN ('scene_video_job_is_current','scene_video_job_is_superseded',
                    'assert_video_operation_current','fence_video_operation_write')
ORDER BY p.proname;
SELECT n.nspname AS schema_name,c.relname,t.tgname,t.tgenabled,
       pg_get_triggerdef(t.oid,true) AS definition
FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE NOT t.tgisinternal AND t.tgname='scene_video_job_generation_fence';

SELECT j.id,j.tenant_id,j.status,j.attempt_count,j.max_attempts,j.priority,j.queued_at,
       j.input_json->>'projectId' AS project_id,j.input_json->>'sceneId' AS scene_id,
       j.input_json->>'logicalRequestId' AS logical_request_id,
       j.input_json->>'operationGeneration' AS job_generation,
       j.input_json->>'providerPoll' AS provider_poll,
       h.generation AS head_generation,h.active_render_job_id,h.active_video_version_id,
       CASE WHEN h.scene_id IS NULL THEN 'NO_OPERATION_HEAD'
            WHEN h.active_render_job_id<>j.id THEN 'NOT_ACTIVE_JOB'
            WHEN NOT video_render.scene_video_job_is_current(j.tenant_id,j.id) THEN 'GENERATION_MISMATCH'
            ELSE 'CURRENT' END AS ownership_check,
       j.provider_task_id,j.provider_video_id_base,j.point_status,j.point_cost_charged,
       (SELECT jsonb_agg(jsonb_build_object('state',i.submit_state,'taskId',i.provider_task_id,
                       'videoId',i.provider_video_id_base,'logicalRequestId',i.logical_request_id))
          FROM render.scene_video_submit_intents i
         WHERE i.tenant_id=j.tenant_id AND i.render_job_id=j.id) AS submit_evidence,
       (SELECT jsonb_agg(jsonb_build_object('id',v.id,'jobId',v.render_job_id,'status',v.status,
                       'selected',v.is_selected,'taskId',v.provider_task_id,'videoId',v.provider_video_id_base))
          FROM video_render.scene_video_versions v
         WHERE v.tenant_id=j.tenant_id AND (v.render_job_id=j.id
             OR v.logical_request_id=j.input_json->>'logicalRequestId')) AS version_evidence,
       (SELECT jsonb_agg(jsonb_build_object('createdAt',e.created_at,'data',e.data_json))
          FROM render.render_job_events e
         WHERE e.tenant_id=j.tenant_id AND e.job_id=j.id
           AND e.event_type='SCENE_VIDEO_QUEUE_QUARANTINED') AS quarantine_audit
FROM render.render_jobs j
LEFT JOIN video_render.scene_video_operation_heads h ON h.tenant_id=j.tenant_id
 AND h.scene_id::text=COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')
WHERE j.job_type='render_scene_video' AND j.status IN ('queued','preparing','pending_reconciliation')
ORDER BY j.priority,j.queued_at,j.id;
ROLLBACK;
