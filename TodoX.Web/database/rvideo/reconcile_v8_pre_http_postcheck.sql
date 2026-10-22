-- READ-ONLY postcheck. Run after successful COMMIT with the same tenant_id.
-- Expected: V8 job/version failed, intent CONFIRMED_NO_TASK, exactly one audit row;
-- ownership orphan may remain pending; V3-V6 may remain queued without provider
-- evidence. Other unresolved rows in the blocking queries must be investigated.
-- V8 submitted_at remains the pre-HTTP timestamp; reconciliation does not clear it.
SELECT a.*, j.status AS job_status, j.provider_task_id AS job_provider_task,
       v.version_number, v.status AS version_status, v.submitted_at,
       v.provider_task_id AS version_provider_task, v.provider_video_id_base AS version_provider_video,
       v.is_selected,
       i.submit_state, i.provider_task_id AS intent_provider_task,
       s.selected_video_version_id
FROM render.scene_video_operator_reconciliations a
JOIN render.render_jobs j ON j.id=a.render_job_id AND j.tenant_id=a.tenant_id
JOIN video_render.scene_video_versions v ON v.id=a.version_id AND v.tenant_id=a.tenant_id
JOIN render.scene_video_submit_intents i ON i.render_job_id=a.render_job_id
  AND i.tenant_id=a.tenant_id AND i.scene_id=a.scene_id
  AND i.logical_request_id=a.logical_request_id
JOIN video_render.video_project_scenes s ON s.id=a.scene_id AND s.tenant_id=a.tenant_id
WHERE a.tenant_id=:'tenant_id'::uuid AND a.render_job_id='d555306e-9c55-4552-9896-113a1334bfa2'
  AND a.reason_code='PRE_HTTP_SUBMIT_TOKEN_BUG';

SELECT j.id, j.status FROM render.render_jobs j WHERE j.tenant_id=:'tenant_id'::uuid
 AND j.job_type='render_scene_video'
 AND COALESCE(j.input_json->>'sceneId',j.input_json->>'scene_id')='814'
 AND j.status IN ('queued','preparing','rendering','processing','post_processing','pending_reconciliation')
 AND NOT (j.id='4190c02c-5279-4534-8ec6-95fbcbbf05b9'
   AND j.status='pending_reconciliation' AND j.error_code='RVIDEO_JOB_VERSION_OWNERSHIP_REQUIRED'
   AND NULLIF(btrim(j.provider_task_id),'') IS NULL AND NULLIF(btrim(j.provider_video_id_base),'') IS NULL
   AND (j.lock_until IS NULL OR j.lock_until<=now())
   AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions ov WHERE ov.tenant_id=j.tenant_id AND ov.render_job_id=j.id)
   AND NOT EXISTS (SELECT 1 FROM render.scene_video_submit_intents oi WHERE oi.tenant_id=j.tenant_id AND oi.render_job_id=j.id));
SELECT v.id, v.status FROM video_render.scene_video_versions v
WHERE v.tenant_id=:'tenant_id'::uuid AND v.scene_id=814
 AND (v.status='pending_reconciliation' OR
     v.status NOT IN ('completed','failed') AND
     (NULLIF(btrim(v.provider_task_id),'') IS NOT NULL
      OR NULLIF(btrim(v.provider_video_id_base),'') IS NOT NULL));
SELECT i.render_job_id, i.submit_state FROM render.scene_video_submit_intents i
JOIN render.render_jobs j ON j.id=i.render_job_id AND j.tenant_id=i.tenant_id
WHERE i.tenant_id=:'tenant_id'::uuid AND i.scene_id=814
  AND (i.submit_state IN ('SUBMIT_IN_FLIGHT','AMBIGUOUS_SUBMIT','MANUAL_REVIEW_REQUIRED')
       OR i.submit_state='SUBMIT_INTENT_PERSISTED' AND j.status NOT IN ('completed','failed')
       OR i.submit_state='CONFIRMED_PROVIDER_TASK' AND j.status <> 'completed');
SELECT v.id, v.version_number, v.status, v.is_selected
FROM video_render.scene_video_versions v
JOIN video_render.video_project_scenes s ON s.selected_video_version_id=v.id
WHERE s.tenant_id=:'tenant_id'::uuid AND s.id=814 AND v.tenant_id=s.tenant_id;