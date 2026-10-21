-- Operator only. READ-ONLY. Run AFTER the repair, and again AFTER pressing Render for V5.
-- Purpose: prove V2 survived untouched, V3/V4 are terminal, the guard is open, and the
-- new V5 attempt submitted exactly ONCE with no duplicate provider send.
--
-- Contract: TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs (ClaimSubmitAsync guard).
-- Set the scene id (814) and project id (183) below before running.

-- =====================================================================
-- CHECK 1. V2 must still be completed and selected, with media identity intact.
-- EXPECTED: exactly one row: status=completed, is_selected=true, provider_task_id non-empty.
-- =====================================================================
SELECT v.id                     AS selected_version_id,
       v.version_number,
       v.status,
       v.is_selected,
       v.provider_task_id,
       v.provider_video_id_base,
       v.public_url,
       v.selected_at
  FROM video_render.scene_video_versions v
  JOIN video_render.video_project_scenes s
    ON s.selected_video_version_id = v.id AND s.id = v.scene_id AND s.tenant_id = v.tenant_id
 WHERE v.scene_id = 814
   AND v.status = 'completed'
   AND v.is_selected = true;

-- =====================================================================
-- CHECK 2. No false pending_reconciliation may remain without a provider task.
-- EXPECTED: ZERO rows. Any row here means an attempt is stuck again.
-- =====================================================================
SELECT v.id, v.version_number, v.status, v.error_code, v.provider_task_id, v.error_message
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = 814
   AND v.status = 'pending_reconciliation'
   AND NULLIF(btrim(v.provider_task_id), '') IS NULL;

-- =====================================================================
-- CHECK 3. Full version history with provider identity. V3/V4 must be failed and
-- must carry NO provider task, so no provider work is orphaned.
-- EXPECTED: V2 completed+selected, V3 failed, V4 failed, V5 submitted (after Render).
-- =====================================================================
SELECT v.version_number,
       v.status,
       v.error_code,
       v.is_selected,
       v.logical_request_id,
       v.provider_task_id,
       v.provider_video_id_base,
       v.submitted_at,
       v.completed_at,
       v.created_at
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = 814
 ORDER BY v.version_number;

-- =====================================================================
-- CHECK 4. The manual-review blocker must be gone from every job of this scene.
-- EXPECTED: ZERO rows.
-- =====================================================================
SELECT j.id AS render_job_id, j.status, j.error_code,
       (COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt') AS still_has_manual_review_marker,
       j.input_json->>'sceneVideoManualReviewAt' AS manual_review_at,
       j.provider_task_id, j.provider_video_id_base
  FROM render.render_jobs j
 WHERE j.job_type = 'render_scene_video'
   AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = '814'
   AND (j.error_code = 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
        OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
        OR j.status = 'pending_reconciliation');

-- =====================================================================
-- CHECK 5. Duplicate-claim proof. Exactly ONE intent per logical request for V5
-- (and one is enough). EXPECTED: ZERO rows from the HAVING clause.
-- =====================================================================
SELECT i.logical_request_id, count(*) AS intent_rows, min(i.submit_state) AS state
  FROM render.scene_video_submit_intents i
 WHERE i.scene_id = 814
 GROUP BY i.logical_request_id
HAVING count(*) <> 1;

-- =====================================================================
-- CHECK 5b. Inspect the V5 intent directly: it must be CONFIRMED_PROVIDER_TASK with a
-- single stored provider task id, or CONFIRMED_NO_TASK when it was refused pre-HTTP.
-- =====================================================================
SELECT i.logical_request_id, i.render_job_id, i.submit_state,
       i.provider_task_id, i.provider_video_id_base, i.created_at, i.reconciled_at
  FROM render.scene_video_submit_intents i
 WHERE i.scene_id = 814
 ORDER BY i.created_at;

-- =====================================================================
-- CHECK 6. Billing after repair + V5. V3/V4 must be released with NO provider task;
-- the V5 reservation must be reserved or completed. EXPECTED: no locked 'reserved' row
-- without a provider task, and no lingering pending_reconciliation.
-- =====================================================================
SELECT b.logical_request_id, b.status, b.payer_wallet_id,
       b.reserved_points, b.charged_points, b.refunded_points,
       b.provider_task_id, b.pending_reconciliation_at, b.failed_at
  FROM billing.ai_image_billing_records b
 WHERE b.logical_request_id IN (
         SELECT v.logical_request_id
           FROM video_render.scene_video_versions v
          WHERE v.scene_id = 814
       )
 ORDER BY b.created_at;

-- =====================================================================
-- CHECK 7. Wallet proof that the refund actually returned the locked points.
-- locked_balance must have dropped by the released reservations.
-- =====================================================================
SELECT w.id AS wallet_id, w.wallet_scope, w.wallet_code,
       w.balance, w.locked_balance, w.updated_at
  FROM billing.token_wallets w
 WHERE w.id IN (
         SELECT b.payer_wallet_id
           FROM billing.ai_image_billing_records b
          WHERE b.logical_request_id IN (
                  SELECT v.logical_request_id
                    FROM video_render.scene_video_versions v
                   WHERE v.scene_id = 814
                )
            AND b.payer_wallet_id IS NOT NULL
       )
 ORDER BY w.id;

-- =====================================================================
-- CHECK 8. GUARD ACCEPTANCE. Reproduces ClaimSubmitAsync precedence exactly.
-- All three flags must be FALSE for a brand-new attempt to be permitted.
-- After a successful V5 submit the flags may be true for the V5 job itself, which is
-- expected; what must NOT remain is a flag caused by V3/V4.
-- =====================================================================
SELECT
  EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
            JOIN render.render_jobs prior ON prior.id = i.render_job_id AND prior.tenant_id = i.tenant_id
           WHERE i.scene_id = 814
             AND NOT ((i.submit_state = 'CONFIRMED_PROVIDER_TASK' AND prior.status = 'completed')
                      OR i.submit_state = 'CONFIRMED_NO_TASK'))    AS flag_unresolved_intent,
  EXISTS (SELECT 1 FROM render.render_jobs j
           WHERE j.job_type = 'render_scene_video'
             AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = '814'
             AND (j.status = 'pending_reconciliation'
               OR j.error_code = 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
               OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
               OR (j.status NOT IN ('completed', 'failed', 'cancelled')
                   AND (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
                        OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL))))
                                                                 AS flag_job_in_flight_or_manual_review,
  EXISTS (SELECT 1 FROM video_render.scene_video_versions v
           WHERE v.scene_id = 814
             AND v.status = 'pending_reconciliation'
             AND NULLIF(btrim(v.provider_task_id), '') IS NULL)    AS flag_false_reconciliation;

-- =====================================================================
-- CHECK 9. Audit trail: confirm the operator recovery event and the pre-HTTP
-- refusal events are both preserved. EXPECTED: at least one
-- SCENE_VIDEO_814_RECOVERY_AUDIT row and one RVIDEO_VIDEO_NOT_SUBMITTED row per closed attempt.
-- =====================================================================
SELECT e.created_at, e.event_type, e.level, e.message,
       e.data_json->>'sceneVideoVersionId' AS scene_video_version_id,
       e.data_json->>'logicalRequestId'    AS logical_request_id,
       e.data_json->>'providerRequestSent' AS provider_request_sent
  FROM video_render.video_project_events e
 WHERE e.project_id = 183
   AND e.event_type IN ('SCENE_VIDEO_814_RECOVERY_AUDIT', 'RVIDEO_VIDEO_NOT_SUBMITTED')
 ORDER BY e.created_at;

-- =====================================================================
-- ACCEPTANCE SUMMARY (fill in and keep with the operator log)
--   V2 still completed + selected ............ CHECK 1 -> 1 row
--   No false pending_reconciliation .......... CHECK 2 -> 0 rows
--   V3/V4 terminal, no provider task ......... CHECK 3
--   No manual-review blocker ................. CHECK 4 -> 0 rows
--   Exactly one intent per logical request ... CHECK 5 -> 0 rows
--   V5 intent is CONFIRMED_* ................. CHECK 5b
--   V3/V4 billing released, no lock leak ..... CHECK 6 + CHECK 7
--   Guard no longer blocked by V3/V4 ......... CHECK 8
--   Audit history preserved .................. CHECK 9
-- =====================================================================