-- Operator only. READ-ONLY. Run after the Scene 814 repair and after pressing Render for V5.
-- Purpose: prove the acceptance criteria were met.
-- Contract: TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs.
-- Set @scene_id before running.

-- 1. The originally selected completed version (V2) must still be completed and selected.
SELECT v.version_number, v.status, v.is_selected, v.provider_task_id
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = :scene_id
   AND v.is_selected = true;

-- 2. No version may remain stuck in a false pending_reconciliation without a provider task.
SELECT v.id, v.version_number, v.status, v.error_code, v.provider_task_id
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = :scene_id
   AND v.status = 'pending_reconciliation'
   AND NULLIF(btrim(v.provider_task_id), '') IS NULL;

-- 3. The new V5 attempt must exist with exactly one provider task (no duplicate sends).
SELECT v.version_number,
       v.logical_request_id,
       v.status,
       v.provider_task_id,
       v.provider_video_id_base,
       v.submitted_at,
       v.created_at
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = :scene_id
 ORDER BY v.version_number;

-- 4. Exactly one durable submit intent must exist per logical request (proves no duplicate claim).
SELECT i.logical_request_id, count(*) AS intent_rows, min(i.submit_state) AS state
  FROM render.scene_video_submit_intents i
 WHERE i.scene_id = :scene_id
 GROUP BY i.logical_request_id
HAVING count(*) <> 1;

-- 5. Billing: the new attempt must be reserved/completed and no orphan reconciliation must remain.
SELECT b.logical_request_id, b.status, b.reserved_points, b.charged_points, b.provider_task_id
  FROM billing.ai_image_billing_records b
 WHERE b.logical_request_id IN (
       SELECT v.logical_request_id
         FROM video_render.scene_video_versions v
        WHERE v.scene_id = :scene_id
 )
 ORDER BY b.created_at;

-- 6. Guard must now permit (all flags false) or reflect the new in-flight V5 attempt.
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
         OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt')) AS flag_manual_review_hold,
  EXISTS (SELECT 1 FROM video_render.scene_video_versions v
     WHERE v.scene_id = :scene_id
       AND v.status = 'pending_reconciliation'
       AND NULLIF(btrim(v.provider_task_id), '') IS NULL)              AS flag_false_reconciliation;
