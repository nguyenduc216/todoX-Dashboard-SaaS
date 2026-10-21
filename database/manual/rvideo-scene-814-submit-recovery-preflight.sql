-- Operator only. READ-ONLY. Run and record output BEFORE any Scene 814 repair.
-- Purpose: prove which guard clause refused the Scene 814 video submit, so V4 is only
-- unlocked by reconciliation evidence and never by assumption.
-- Contract: TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs (ClaimSubmitAsync guard).
-- Set @scene_id to the Scene 814 scene id before running.

-- 1. Scene identity and its selected/completed output (must stay untouched).
SELECT s.id AS scene_id,
       s.project_id,
       s.status AS scene_status,
       s.selected_video_version_id,
       s.video_url
  FROM video_render.video_project_scenes s
 WHERE s.id = :scene_id;

-- 2. Every scene-video version: proves V2 completed/selected, V3 failed, V4 state.
SELECT v.id,
       v.version_number,
       v.logical_request_id,
       v.status,
       v.error_code,
       v.error_message,
       v.is_selected,
       v.provider_task_id,
       v.provider_video_id_base,
       v.render_job_id,
       v.created_at,
       v.updated_at
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = :scene_id
 ORDER BY v.version_number;

-- 3. Every scene-video render job for this scene, including the manual-review hold.
SELECT j.id,
       j.status,
       j.error_code,
       j.error_message,
       j.provider_task_id,
       j.provider_video_id_base,
       j.attempt_count,
       j.max_attempts,
       COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') AS scene_id,
       COALESCE(j.input_json->>'logicalRequestId', j.input_json->>'logical_request_id') AS logical_request_id,
       (COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt') AS has_manual_review_stamp,
       j.input_json->>'sceneVideoManualReviewAt' AS manual_review_at,
       j.created_at,
       j.updated_at
  FROM render.render_jobs j
 WHERE j.job_type = 'render_scene_video'
   AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = :scene_id::text
 ORDER BY j.created_at;

-- 4. Durable submit intents for this scene (requires the forward migration).
SELECT i.logical_request_id,
       i.render_job_id,
       i.owner_token,
       i.submit_state,
       i.provider_task_id,
       i.provider_video_id_base,
       i.created_at,
       i.reconciled_at
  FROM render.scene_video_submit_intents i
 WHERE i.scene_id = :scene_id
 ORDER BY i.created_at;

-- 5. Billing records for every attempt of this scene (reservation vs. reconciliation).
SELECT b.logical_request_id,
       b.status,
       b.reserved_points,
       b.charged_points,
       b.refunded_points,
       b.provider_task_id,
       b.pending_reconciliation_at,
       b.failed_at,
       b.error_message
  FROM billing.ai_image_billing_records b
 WHERE b.logical_request_id IN (
       SELECT v.logical_request_id
         FROM video_render.scene_video_versions v
        WHERE v.scene_id = :scene_id
 )
 ORDER BY b.created_at;

-- 6. Guard evaluation for a brand-new attempt: every flag must be false to permit V5 submit.
SELECT
  EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
      JOIN render.render_jobs prior ON prior.id = i.render_job_id AND prior.tenant_id = i.tenant_id
     WHERE i.scene_id = :scene_id
       AND NOT ((i.submit_state = 'CONFIRMED_PROVIDER_TASK' AND prior.status = 'completed')
                OR i.submit_state = 'CONFIRMED_NO_TASK'))            AS flag_unresolved_intent,
  EXISTS (SELECT 1 FROM render.render_jobs j
     WHERE j.job_type = 'render_scene_video'
       AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = :scene_id::text
       AND (j.status = 'pending_reconciliation'
         OR j.error_code = 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
         OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
         OR (j.status NOT IN ('completed', 'failed')
             AND (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
                  OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL))))  AS flag_job_in_flight_or_manual_review,
  EXISTS (SELECT 1 FROM video_render.scene_video_versions v
     WHERE v.scene_id = :scene_id
       AND ((v.status NOT IN ('completed', 'failed')
             AND (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                  OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
         OR v.status = 'pending_reconciliation'))                         AS flag_version_in_flight_or_reconciliation;

-- 7. Honest uncertainty note: application logs for the 20/10 14:50 refusal are NOT queryable
--    from these tables. If steps 2-5 show no evidence of a provider task for the refused
--    logical request, the previous attempt is a proven pre-HTTP refusal and V4 may be closed
--    with the repair script. If any provider_task_id exists, STOP: use reconciliation instead.
