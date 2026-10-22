-- DBeaver: execute the entire script. READ ONLY; no functions with application side effects.
-- Save all result rows for diagnosis. No provider failure is inferred from an application failure.
-- Event data may contain signed URLs/prompts; share through an authorized private channel.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_database() AS database_name,current_user AS reader,clock_timestamp() AS captured_at;

WITH RECURSIVE
root AS (SELECT * FROM render.render_jobs WHERE id='2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432'::uuid),
family AS (
    SELECT id,tenant_id,ARRAY[id] AS visited FROM root
    UNION ALL
    SELECT j.id,j.tenant_id,f.visited||j.id FROM render.render_jobs j JOIN family f
      ON j.tenant_id=f.tenant_id AND NOT j.id=ANY(f.visited)
     AND COALESCE(j.input_json->>'parentJobId',j.input_json->>'parent_job_id')=f.id::text
),
projects AS (
    SELECT p.* FROM video_render.video_projects p JOIN root r ON p.tenant_id=r.tenant_id
    WHERE p.core_job_id=r.id
       OR p.id::text=COALESCE(r.input_json->>'projectId',r.input_json->>'project_id')
       OR EXISTS (SELECT 1 FROM family f JOIN render.render_jobs j ON j.id=f.id
           WHERE p.id::text=COALESCE(j.input_json->>'projectId',j.input_json->>'project_id'))
),
jobs AS (
    SELECT j.* FROM render.render_jobs j JOIN root r ON r.tenant_id=j.tenant_id
    WHERE j.id IN (SELECT id FROM family)
       OR EXISTS (SELECT 1 FROM projects p
           WHERE p.id::text=COALESCE(j.input_json->>'projectId',j.input_json->>'project_id'))
),
scenes AS (SELECT s.* FROM video_render.video_project_scenes s JOIN projects p ON s.project_id=p.id AND s.tenant_id=p.tenant_id),
versions AS (SELECT v.* FROM video_render.scene_video_versions v
    WHERE EXISTS (SELECT 1 FROM scenes s WHERE s.id=v.scene_id AND s.tenant_id=v.tenant_id)
       OR EXISTS (SELECT 1 FROM jobs j WHERE j.id=v.render_job_id AND j.tenant_id=v.tenant_id)),
intents AS (SELECT i.* FROM render.scene_video_submit_intents i
    WHERE EXISTS (SELECT 1 FROM scenes s WHERE s.id=i.scene_id AND s.tenant_id=i.tenant_id)
       OR EXISTS (SELECT 1 FROM jobs j WHERE j.id=i.render_job_id AND j.tenant_id=i.tenant_id))
SELECT * FROM (
SELECT '01_root_presence' AS section,jsonb_build_object('count',(SELECT count(*) FROM root),
    'requestedJobId','2f3664eb-cdf6-44bd-aa6f-8ef12cc8b432') AS evidence
UNION ALL
SELECT '02_jobs',jsonb_build_object('id',j.id,'jobType',j.job_type,'status',j.status,
    'tenantId',j.tenant_id,'createdAt',j.created_at,'updatedAt',j.updated_at,
    'workerKey',j.worker_key,'lockOwner',j.lock_owner,'lockUntil',j.lock_until,
    'liveLease',j.lock_until>clock_timestamp(),'retryAfter',j.retry_after,
    'attemptCount',j.attempt_count,'maxAttempts',j.max_attempts,
    'provider',j.provider_code,'model',j.model_code,'providerTaskId',j.provider_task_id,
    'providerVideoIdBase',j.provider_video_id_base,'errorCode',j.error_code,'errorMessage',j.error_message,
    'projectId',j.input_json->>'projectId','sceneId',j.input_json->>'sceneId',
    'logicalRequestId',j.input_json->>'logicalRequestId','parentJobId',j.input_json->>'parentJobId',
    'providerPoll',j.input_json->'providerPoll','providerPollCount',j.input_json->'providerPollCount',
    'output',j.output_json) FROM jobs j
UNION ALL
SELECT '03_projects',jsonb_build_object('id',p.id,'coreJobId',p.core_job_id,'tenantId',p.tenant_id,
    'status',p.status,'errorMessage',p.error_message,'createdAt',p.created_at,'updatedAt',p.updated_at) FROM projects p
UNION ALL
SELECT '04_scenes',jsonb_build_object('id',s.id,'projectId',s.project_id,'sceneIndex',s.scene_index,
    'status',s.status,'selectedVideoVersionId',s.selected_video_version_id,
    'hasSourceImage',NULLIF(btrim(COALESCE(s.static_image_url,s.static_image_path)), '') IS NOT NULL,
    'hasMotionPrompt',NULLIF(btrim(s.video_prompt),'') IS NOT NULL,
    'sceneVideoUrl',s.scene_video_url,'sceneVideoPath',s.scene_video_path,'errorMessage',s.error_message) FROM scenes s
UNION ALL
SELECT '05_versions',to_jsonb(v)-'scene_snapshot_json'-'render_config_json'-'image_prompt_snapshot'-'video_prompt_snapshot' FROM versions v
UNION ALL
SELECT '06_submit_intents',to_jsonb(i) FROM intents i
UNION ALL
SELECT '07_job_events',to_jsonb(e) FROM render.render_job_events e
WHERE EXISTS (SELECT 1 FROM jobs j WHERE j.id=e.job_id AND j.tenant_id=e.tenant_id)
UNION ALL
SELECT '08_project_events',to_jsonb(e) FROM video_render.video_project_events e
WHERE EXISTS (SELECT 1 FROM projects p WHERE p.id=e.project_id AND p.tenant_id=e.tenant_id)
) AS collected
ORDER BY section,evidence->>'created_at',evidence->>'createdAt';
COMMIT;

-- Interpret the event chronology, not just the last status:
-- No version: inspect dispatch/claim, source/prompt/payer validation and version allocation errors.
-- Guard rejection: inspect blockerCategory/blockReason and all same-scene intents/jobs.
-- Intent persisted vs in-flight: distinguish ClaimSubmitAsync from BeginSubmitAsync failure.
-- HTTP diagnostics/submitted events: establish whether Adapter.SubmitAsync actually sent a request.
-- Task ID + video base: correlate exact owner/intent, provider poll response and normalized status.
-- Download/persist events and result_media_id: distinguish storage from version completion failure.
-- Finalizer/audio/merge events: distinguish post-completion lifecycle failures from provider failure.
-- Missing records/events do not prove a provider request was never sent. Supply worker application
-- log excerpts for this job/child jobs and deployed /system/version commit if these rows are inconclusive.
