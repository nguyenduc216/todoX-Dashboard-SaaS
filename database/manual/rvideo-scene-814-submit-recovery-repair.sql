-- Operator only. Run ONLY after reviewing rvideo-scene-814-submit-recovery-preflight.sql.
-- Purpose: close the Scene 814 V4 attempt that was refused BEFORE any provider request, so the
-- user can press Render again and create V5 without a false pending_reconciliation hold.
--
-- HARD PRECONDITIONS (the script aborts the whole transaction if any is violated):
--   A. No scene-video version for this scene is in a live in-flight state with a provider task.
--   B. No scene-video render job for this scene holds a provider task while not terminal.
--   C. No durable submit intent for this scene is unresolved.
--   D. The selected completed version (V2) is never modified.
--
-- DO NOT run this if the preflight found a provider_task_id for the refused logical request.
-- In that case the attempt is ambiguous and must be reconciled, not failed.
-- Set @scene_id before running. Does not restart IIS and does not call any provider.

BEGIN;

-- Precondition A + B + C: refuse to repair while any live provider activity is provable.
DO $$
DECLARE
    v_scene_id bigint := :scene_id;
    v_live_versions int;
    v_live_jobs int;
    v_unresolved_intents int;
BEGIN
    SELECT count(*) INTO v_live_versions
      FROM video_render.scene_video_versions v
     WHERE v.scene_id = v_scene_id
       AND v.status NOT IN ('completed', 'failed')
       AND (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
            OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL);

    SELECT count(*) INTO v_live_jobs
      FROM render.render_jobs j
     WHERE j.job_type = 'render_scene_video'
       AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = v_scene_id::text
       AND j.status NOT IN ('completed', 'failed')
       AND (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
            OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL);

    SELECT count(*) INTO v_unresolved_intents
      FROM render.scene_video_submit_intents i
      JOIN render.render_jobs prior ON prior.id = i.render_job_id AND prior.tenant_id = i.tenant_id
     WHERE i.scene_id = v_scene_id
       AND NOT ((i.submit_state = 'CONFIRMED_PROVIDER_TASK' AND prior.status = 'completed')
                OR i.submit_state = 'CONFIRMED_NO_TASK');

    IF v_live_versions > 0 OR v_live_jobs > 0 OR v_unresolved_intents > 0 THEN
        RAISE EXCEPTION
            'Scene 814 repair refused: live provider activity or unresolved intent detected (versions=%, jobs=%, intents=%). Reconcile instead.',
            v_live_versions, v_live_jobs, v_unresolved_intents;
    END IF;
END $$;

-- 1. Close the refused V4 version as a definite terminal failure. Only rows without a provider
--    task and not already completed are touched, so V2 completed/selected is preserved.
UPDATE video_render.scene_video_versions v
   SET status = 'failed',
       error_code = COALESCE(v.error_code, 'SCENE_VIDEO_SUBMIT_GUARD_REJECTED'),
       error_message = COALESCE(
           v.error_message,
           'Láº§n gá»­i video nÃ y bá»‹ cháº·n trÆ°á»›c khi gá»i nhÃ  cung cáº¥p; khÃ´ng cÃ³ yÃªu cáº§u nÃ o Ä‘Æ°á»£c gá»­i Ä‘i.'),
       updated_at = now()
 WHERE v.scene_id = :scene_id
   AND v.status = 'pending_reconciliation'
   AND v.is_selected = false
   AND NULLIF(btrim(v.provider_task_id), '') IS NULL
   AND NULLIF(btrim(v.provider_video_id_base), '') IS NULL;

-- 2. Close the refused render job terminally and clear the manual-review hold for this scene.
UPDATE render.render_jobs j
   SET status = 'failed',
       error_code = COALESCE(j.error_code, 'SCENE_VIDEO_SUBMIT_GUARD_REJECTED'),
       error_message = COALESCE(
           j.error_message,
           'Láº§n gá»­i video nÃ y bá»‹ cháº·n trÆ°á»›c khi gá»i nhÃ  cung cáº¥p; khÃ´ng cÃ³ yÃªu cáº§u nÃ o Ä‘Æ°á»£c gá»­i Ä‘i.'),
       input_json = COALESCE(j.input_json, '{}'::jsonb) - 'sceneVideoManualReviewAt',
       updated_at = now(),
       completed_at = COALESCE(j.completed_at, now())
 WHERE j.job_type = 'render_scene_video'
   AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = :scene_id::text
   AND j.status NOT IN ('completed', 'failed')
   AND NULLIF(btrim(j.provider_task_id), '') IS NULL
   AND NULLIF(btrim(j.provider_video_id_base), '') IS NULL;

-- 3. Clear the scene-level hold so a new attempt is not refused by the guard.
UPDATE video_render.video_project_scenes s
   SET status = CASE WHEN s.status = 'video_rendering' THEN 'failed' ELSE s.status END,
       updated_at = now()
 WHERE s.id = :scene_id
   AND s.status = 'video_rendering';

-- 4. Record that the refusal never reached the provider on the durable intent (if present).
UPDATE render.scene_video_submit_intents i
   SET submit_state = 'CONFIRMED_NO_TASK',
       reconciled_at = COALESCE(i.reconciled_at, now()),
       updated_at = now()
 WHERE i.scene_id = :scene_id
   AND i.submit_state IN ('SUBMIT_INTENT_PERSISTED', 'SUBMIT_IN_FLIGHT')
   AND NULLIF(btrim(i.provider_task_id), '') IS NULL;

-- 5. Release any reservation that never reached the provider, so points return to the wallet.
UPDATE billing.ai_image_billing_records b
   SET status = 'released',
       error_message = COALESCE(
           b.error_message,
           'Láº§n gá»­i video nÃ y bá»‹ cháº·n trÆ°á»›c khi gá»i nhÃ  cung cáº¥p; khÃ´ng cÃ³ yÃªu cáº§u nÃ o Ä‘Æ°á»£c gá»­i Ä‘i.'),
       failed_at = COALESCE(b.failed_at, now()),
       updated_at = now()
 WHERE b.logical_request_id IN (
       SELECT v.logical_request_id
         FROM video_render.scene_video_versions v
        WHERE v.scene_id = :scene_id
 )
   AND b.status IN ('reserved', 'pending_reconciliation')
   AND NULLIF(btrim(b.provider_task_id), '') IS NULL;

COMMIT;
