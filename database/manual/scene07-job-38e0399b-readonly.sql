/* FEATURE_ID: TODOX-RVIDEO-VIDEO-RERENDER; CHANGE_ID: TODOX-20261025-SCENE07-UI-001.
   Grain: exact requested job, its project/scene operations, versions and intents; no guessed scene ID.
   DBeaver: execute entire script, save results AND Output/NOTICE tab privately. No write/lock/provider call.
   Missing rows/logs never prove no HTTP. Error text can contain private data; redact before sharing.
   SQL cannot verify an IIS-loaded DLL. Operator must independently supply server DLL SHA256,
   package/commit manifest and host identity; a local artifact hash is not production evidence. */
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '30s';
SELECT current_database() AS database_name, current_user AS reader, clock_timestamp() AS captured_at,
       to_regclass('video_render.scene_video_operation_heads') IS NOT NULL AS operation_heads_present,
       to_regclass('video_render.scene_video_operation_transitions') IS NOT NULL AS transitions_present,
       to_regprocedure('video_render.scene_video_job_is_superseded(uuid,uuid)') IS NOT NULL AS superseded_function_present,
       to_regprocedure('video_render.scene_video_job_is_current(uuid,uuid)') IS NOT NULL AS current_function_present,
       to_regprocedure('video_render.assert_video_operation_current(uuid,uuid)') IS NOT NULL AS assertion_function_present,
       (SELECT jsonb_agg(jsonb_build_object('schema',n.nspname,'table',c.relname,'trigger',t.tgname,'enabled',t.tgenabled))
        FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname IN('render','video_render') AND t.tgname IN('scene_video_version_generation_fence',
          'scene_video_job_generation_fence','scene_video_scene_generation_fence',
          'scene_video_project_generation_fence','scene_video_final_generation_fence')) AS generation_fence_triggers;

WITH root AS (
 SELECT * FROM render.render_jobs WHERE id='38e0399b-9d50-42a0-8ebe-a9d6de5e3adf'::uuid
), projects AS (
 SELECT p.* FROM video_render.video_projects p JOIN root r ON p.tenant_id=r.tenant_id
 WHERE p.core_job_id=r.id OR p.id::text=COALESCE(r.input_json->>'projectId',r.input_json->>'project_id')
    OR EXISTS(SELECT 1 FROM video_render.scene_video_versions v WHERE v.tenant_id=r.tenant_id AND v.render_job_id=r.id AND v.project_id=p.id)
), scenes AS (
 SELECT s.* FROM video_render.video_project_scenes s JOIN projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
 WHERE EXISTS(SELECT 1 FROM root r WHERE p.core_job_id=r.id
    OR s.id::text=COALESCE(r.input_json->>'sceneId',r.input_json->>'scene_id')
    OR COALESCE(r.input_json->'sceneIds','[]'::jsonb) @> jsonb_build_array(s.id)
    OR EXISTS(SELECT 1 FROM video_render.scene_video_versions v WHERE v.tenant_id=r.tenant_id AND v.render_job_id=r.id AND v.scene_id=s.id))
), jobs AS (
 SELECT j.* FROM render.render_jobs j WHERE EXISTS(SELECT 1 FROM root r WHERE r.tenant_id=j.tenant_id AND r.id=j.id)
 OR EXISTS(SELECT 1 FROM scenes s WHERE s.tenant_id=j.tenant_id
    AND (s.id::text=COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')
      OR COALESCE(j.input_json->'sceneIds','[]'::jsonb) @> jsonb_build_array(s.id)))
), versions AS (
 SELECT v.* FROM video_render.scene_video_versions v
 WHERE EXISTS(SELECT 1 FROM scenes s WHERE s.tenant_id=v.tenant_id AND s.id=v.scene_id)
    OR EXISTS(SELECT 1 FROM jobs j WHERE j.tenant_id=v.tenant_id AND j.id=v.render_job_id)
)
SELECT * FROM (
 SELECT '01_identity' AS section,jsonb_build_object('requestedJobId','38e0399b-9d50-42a0-8ebe-a9d6de5e3adf',
   'found',EXISTS(SELECT 1 FROM root),'jobType',(SELECT job_type FROM root),
   'isSceneVideoChild',(SELECT job_type='render_scene_video' FROM root),
   'isProjectCoreJob',EXISTS(SELECT 1 FROM projects p JOIN root r ON p.core_job_id=r.id),
   'note','Core job scope may contain multiple scenes. UI Scene 07 is not a database identity.') AS evidence
 UNION ALL
 SELECT '02_jobs',jsonb_build_object('id',j.id,'tenantId',j.tenant_id,'jobType',j.job_type,'status',j.status,
   'errorCode',j.error_code,'errorMessage',j.error_message,'providerTaskId',j.provider_task_id,'providerVideoIdBase',j.provider_video_id_base,
   'projectId',COALESCE(j.input_json->>'projectId',j.input_json->>'project_id'),
   'sceneId',COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id'),'sceneIds',j.input_json->'sceneIds',
   'parentJobId',COALESCE(j.input_json->>'parentJobId',j.input_json->>'parent_job_id'),
   'logicalRequestId',j.input_json->>'logicalRequestId','operationGeneration',j.input_json->'operationGeneration',
   'createdAt',j.created_at,'updatedAt',j.updated_at,'attemptCount',j.attempt_count,
   'workerKey',j.worker_key,'lockOwner',j.lock_owner,'lockUntil',j.lock_until) FROM jobs j
 UNION ALL
 SELECT '03_projects',jsonb_build_object('id',p.id,'tenantId',p.tenant_id,'coreJobId',p.core_job_id,'status',p.status) FROM projects p
 UNION ALL
 SELECT '04_scenes',jsonb_build_object('id',s.id,'projectId',s.project_id,'sceneIndex',s.scene_index,'status',s.status,
   'selectedVideoVersionId',s.selected_video_version_id,'hasSourceImage',NULLIF(btrim(COALESCE(s.static_image_url,s.static_image_path)),'') IS NOT NULL,
   'hasMotionPrompt',NULLIF(btrim(s.video_prompt),'') IS NOT NULL,'errorMessage',s.error_message) FROM scenes s
 UNION ALL
 SELECT '05_video_versions',jsonb_build_object('id',v.id,'projectId',v.project_id,'sceneId',v.scene_id,'versionNumber',v.version_number,
   'status',v.status,'renderJobId',v.render_job_id,'logicalRequestId',v.logical_request_id,'selected',v.is_selected,
   'providerTaskId',v.provider_task_id,'providerVideoIdBase',v.provider_video_id_base,'sourceImageVersionId',v.source_image_version_id,
   'errorCode',v.error_code,'errorMessage',v.error_message,'createdAt',v.created_at) FROM versions v
 UNION ALL
 SELECT '06_intents',jsonb_build_object('tenantId',i.tenant_id,'sceneId',i.scene_id,'renderJobId',i.render_job_id,
   'logicalRequestId',i.logical_request_id,'submitState',i.submit_state,'providerTaskId',i.provider_task_id,
   'providerVideoIdBase',i.provider_video_id_base,'reconciledAt',i.reconciled_at,'updatedAt',i.updated_at)
 FROM render.scene_video_submit_intents i WHERE EXISTS(SELECT 1 FROM scenes s WHERE s.tenant_id=i.tenant_id AND s.id=i.scene_id)
 UNION ALL
 SELECT '07_image_versions',jsonb_build_object('id',v.id,'sceneId',v.scene_id,'versionNumber',v.version_number,'status',v.status,
   'renderJobId',v.render_job_id,'selected',v.is_selected)
 FROM video_render.scene_image_versions v WHERE EXISTS(SELECT 1 FROM scenes s WHERE s.tenant_id=v.tenant_id AND s.id=v.scene_id)
 UNION ALL
 SELECT '08_job_events',jsonb_build_object('id',e.id,'jobId',e.job_id,'eventType',e.event_type,'createdAt',e.created_at,
   'message',e.message,'errorCode',e.data_json->'errorCode','blockReason',e.data_json->'blockReason')
 FROM render.render_job_events e WHERE EXISTS(SELECT 1 FROM jobs j WHERE j.tenant_id=e.tenant_id AND j.id=e.job_id)
 UNION ALL
 SELECT '09_project_event_indexes',jsonb_build_object('id',e.id,'projectId',e.project_id,'eventType',e.event_type,
   'createdAt',e.created_at,'jobId',COALESCE(e.data_json->>'renderJobId',e.data_json->>'jobId'),
   'sceneId',e.data_json->'sceneId','versionId',e.data_json->'versionId','logicalRequestId',e.data_json->'logicalRequestId')
 FROM video_render.video_project_events e WHERE EXISTS(SELECT 1 FROM projects p WHERE p.tenant_id=e.tenant_id AND p.id=e.project_id)
) evidence_rows ORDER BY section,evidence->>'createdAt',evidence->>'id';

-- Dynamic SELECT only, so an absent V1 migration is reported instead of breaking this collector.
-- These NOTICE rows contain exact heads/transitions for the identified project(s), including current
-- and superseded operation identities. Never treat a historical row as the active UI job.
DO $readonly$
DECLARE result jsonb; table_name text;
BEGIN
 FOREACH table_name IN ARRAY ARRAY['scene_video_operation_heads','scene_video_operation_transitions'] LOOP
  IF to_regclass('video_render.'||table_name) IS NULL THEN
   RAISE NOTICE 'DEPLOYMENT_MISMATCH: missing video_render.%',table_name;
  ELSE
   EXECUTE format($q$
    SELECT COALESCE(jsonb_agg(to_jsonb(x)-'old_job_snapshot'-'old_version_snapshot'-'request_hash'),'[]'::jsonb)
    FROM video_render.%I x WHERE EXISTS (
      SELECT 1 FROM render.render_jobs r JOIN video_render.video_projects p ON p.tenant_id=r.tenant_id
      WHERE r.id=$1 AND x.tenant_id=p.tenant_id AND x.project_id=p.id
        AND (p.core_job_id=r.id OR p.id::text=COALESCE(r.input_json->>'projectId',r.input_json->>'project_id')))
    $q$,table_name) INTO result USING '38e0399b-9d50-42a0-8ebe-a9d6de5e3adf'::uuid;
   RAISE NOTICE '%: %',table_name,result;
  END IF;
 END LOOP;
 IF to_regclass('video_render.scene_video_operation_heads') IS NOT NULL
    AND to_regprocedure('video_render.scene_video_job_is_superseded(uuid,uuid)') IS NOT NULL THEN
  EXECUTE $q$
   SELECT COALESCE(jsonb_agg(jsonb_build_object('sceneId',s.id,'sceneIndex',s.scene_index,
     'sceneStatus',s.status,'generation',h.generation,'headJobId',h.active_render_job_id,
     'latestJobId',j.id,'latestJobStatus',j.status,'latestJobVersionId',v.id,
     'latestVersionStatus',v.status,'selectedVersionId',selected.id,
     'hasActiveJob',EXISTS(SELECT 1 FROM render.render_jobs active WHERE active.tenant_id=s.tenant_id
       AND active.job_type='render_scene_video' AND NOT video_render.scene_video_job_is_superseded(active.tenant_id,active.id)
       AND COALESCE(active.input_json->>'sceneId',active.input_json->>'scene_id')=s.id::text
       AND active.status IN('queued','preparing','rendering','processing','post_processing','pending_reconciliation')))), '[]'::jsonb)
   FROM video_render.video_project_scenes s JOIN video_render.video_projects p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
   JOIN render.render_jobs root ON root.tenant_id=p.tenant_id AND root.id=$1
     AND (p.core_job_id=root.id OR p.id::text=COALESCE(root.input_json->>'projectId',root.input_json->>'project_id'))
   LEFT JOIN video_render.scene_video_operation_heads h ON h.tenant_id=s.tenant_id AND h.scene_id=s.id
   LEFT JOIN LATERAL(SELECT j.* FROM render.render_jobs j WHERE j.tenant_id=s.tenant_id AND j.job_type='render_scene_video'
     AND NOT video_render.scene_video_job_is_superseded(j.tenant_id,j.id)
     AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')=s.id::text
     ORDER BY j.created_at DESC,j.queued_at DESC,j.id DESC LIMIT 1) j ON true
   LEFT JOIN LATERAL(SELECT v.* FROM video_render.scene_video_versions v WHERE v.tenant_id=s.tenant_id AND v.scene_id=s.id
     AND v.render_job_id=j.id ORDER BY v.version_number DESC LIMIT 1) v ON true
   LEFT JOIN LATERAL(SELECT v.id FROM video_render.scene_video_versions v WHERE v.tenant_id=s.tenant_id AND v.scene_id=s.id
     AND v.is_selected ORDER BY v.version_number DESC LIMIT 1) selected ON true
  $q$ INTO result USING '38e0399b-9d50-42a0-8ebe-a9d6de5e3adf'::uuid;
  RAISE NOTICE 'ui_current_snapshot: %',result;
 END IF;
END $readonly$;
COMMIT;
-- Classify only after correlating: SUPERSEDED_HISTORICAL first; current nonterminal head ->
-- ACTIVE_GENERATION; pending/ambiguous intent -> PENDING_RECONCILIATION; failed with identity ->
-- FAILED_WITH_PROVIDER_TASK; failed without identity still needs the full submit guard before retry.
-- Compare the same current snapshot with UI to establish UI_STATE_MISMATCH. Missing input/config ->
-- MISSING_INPUT_OR_CONFIGURATION. Missing instrumentation/binary evidence remains unverified.
