-- Operator only. READ-ONLY. Run and record output BEFORE any Scene 814 repair.
-- Purpose: prove the exact state of Scene 814 attempts V2/V3/V4 and decide whether V3/V4
-- attempts are safely closable, so the operator can press Render to create V5.
--
-- Contract: TodoX.Web/Services/VideoRender/SceneVideoSubmitCoordinator.cs (ClaimSubmitAsync guard).
-- Contract: TodoX.Web/Services/VideoRender/VideoRenderRepository.cs (EscalateUnidentifiedSceneVideoJobsAsync).
-- Frozen: no migration, no direct DB write, no provider call, no IIS restart.
--
-- HOW TO RUN (DBeaver / psql): fill in the three identity values below before executing.
--   v_tenant_id  : tenant of project 183
--   v_project_id : 183
--   v_scene_id   : 814
--
-- Every result set below is read-only; nothing is modified by this file.

-- =====================================================================
-- STEP 0. Identity resolution. Prove project/scene/tenant before anything else.
-- =====================================================================
SELECT s.id                AS scene_id,
       s.project_id,
       s.tenant_id,
       s.scene_index,
       s.status            AS scene_status,
       s.selected_video_version_id,
       s.scene_video_url,
       s.scene_video_path,
       s.error_message     AS scene_error_message,
       p.core_job_id,
       p.status            AS project_status
  FROM video_render.video_project_scenes s
  JOIN video_render.video_projects p ON p.id = s.project_id AND p.tenant_id = s.tenant_id
 WHERE s.id = 814 AND s.project_id = 183;

-- =====================================================================
-- STEP 1. Every scene-video version. Confirms which row is V2 (must remain
-- completed/selected) and the exact status/UUID of V3 and V4.
-- EXPECTED (from production report): V2 completed+selected; V3 queued; V4 queued.
-- =====================================================================
SELECT v.id                     AS version_id,
       v.version_number,
       v.status,
       v.error_code,
       v.error_message,
       v.is_selected,
       v.logical_request_id,
       v.render_job_id,
       v.provider_code,
       v.actual_model,
       v.provider_task_id,
       v.provider_video_id_base,
       v.charged_points,
       v.refunded_points,
       v.submitted_at,
       v.completed_at,
       v.public_url,
       v.created_at,
       v.updated_at
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = 814
 ORDER BY v.version_number;

-- =====================================================================
-- STEP 2. Every scene-video render job for this scene.
-- EXPECTED: V3 job failed (42P01) and V4 job failed with
-- error_code=SCENE_VIDEO_MANUAL_REVIEW_REQUIRED plus input_json
-- ? 'sceneVideoManualReviewAt'. That marker is what blocks a new submit.
-- =====================================================================
SELECT j.id                        AS render_job_id,
       j.status                    AS job_status,
       j.error_code,
       j.error_message,
       j.attempt_count,
       j.max_attempts,
       j.provider_code,
       j.model_code,
       COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id')       AS input_scene_id,
       COALESCE(j.input_json->>'projectId', j.input_json->>'project_id')   AS input_project_id,
       j.input_json->>'logicalRequestId'                                   AS input_logical_request_id,
       (COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt')  AS has_manual_review_marker,
       j.input_json->>'sceneVideoManualReviewAt'                           AS manual_review_at,
       j.provider_task_id,
       j.provider_video_id_base,
       j.completed_at,
       j.created_at,
       j.updated_at
  FROM render.render_jobs j
 WHERE j.job_type = 'render_scene_video'
   AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = '814'
 ORDER BY j.created_at;

-- =====================================================================
-- STEP 3. Durable submit intents for this scene.
-- EXPECTED for Scene 814: ZERO rows (no intent was persisted).
-- If rows exist, their submit_state decides whether a submit reached the provider.
-- =====================================================================
SELECT i.logical_request_id,
       i.render_job_id,
       i.owner_token,
       i.submit_state,
       i.provider_task_id,
       i.provider_video_id_base,
       i.created_at,
       i.reconciled_at
  FROM render.scene_video_submit_intents i
 WHERE i.scene_id = 814
 ORDER BY i.created_at;

-- =====================================================================
-- STEP 4. Billing records for every attempt of this scene.
-- EXPECTED: V3 and V4 logical requests are reservations that never reached the
-- provider, i.e. status='reserved' (or parked in 'pending_reconciliation').
-- Record payer_wallet_id / reserved_points: the repair must return locked points.
-- =====================================================================
SELECT b.id                        AS billing_record_id,
       b.logical_request_id,
       b.status,
       b.payer_type,
       b.payer_customer_id,
       b.payer_wallet_id,
       b.provider_code,
       b.capability_code,
       b.reserved_points,
       b.system_charged_points,
       b.charged_points,
       b.refunded_points,
       b.provider_task_id,
       b.reserved_until,
       b.pending_reconciliation_at,
       b.reconciliation_lock_owner,
       b.failed_at,
       b.error_message,
       b.created_at
  FROM billing.ai_image_billing_records b
 WHERE b.logical_request_id IN (
         SELECT v.logical_request_id
           FROM video_render.scene_video_versions v
          WHERE v.scene_id = 814
       )
 ORDER BY b.created_at;

-- =====================================================================
-- STEP 4b. Wallet state for the payers of those reservations. Needed so the
-- operator can confirm locked_balance actually decreases after the repair.
-- =====================================================================
SELECT w.id AS wallet_id,
       w.tenant_id,
       w.customer_id,
       w.wallet_scope,
       w.wallet_code,
       w.balance,
       w.locked_balance,
       w.updated_at
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
-- STEP 5. THE DECISIVE EVIDENCE: the pre-HTTP refusal audit trail.
-- TodoX.Web/Services/VideoRender/SceneVideoWorkerHandler.cs writes
-- event_type='RVIDEO_VIDEO_NOT_SUBMITTED' with providerRequestSent=false when
-- (and only when) a guarded attempt was refused before any provider request.
--
--   * 2 rows for V3+V4  => both attempts are PROVEN pre-HTTP; repair is allowed.
--   * fewer rows        => the newest attempt is NOT proven; STOP and reconcile.
--
-- This event is the ONLY proof available in the database. Application logs for a
-- refused submit are not stored in these tables.
-- =====================================================================
SELECT e.id,
       e.project_id,
       e.event_type,
       e.level,
       e.created_at,
       e.data_json->>'sceneVideoVersionId' AS scene_video_version_id,
       e.data_json->>'logicalRequestId'    AS logical_request_id,
       e.data_json->>'errorCode'           AS error_code,
       e.data_json->>'blockReason'         AS block_reason,
       e.data_json->>'providerRequestSent' AS provider_request_sent,
       e.data_json->>'intentAlreadyHadProviderTask' AS intent_already_had_provider_task
  FROM video_render.video_project_events e
 WHERE e.project_id = 183
   AND e.event_type = 'RVIDEO_VIDEO_NOT_SUBMITTED'
 ORDER BY e.created_at;

-- =====================================================================
-- STEP 6. Negative proof: is there ANY provider task identity attached to a
-- non-terminal V3/V4 attempt? A non-empty value here means the attempt MAY have
-- reached the provider, so the repair MUST NOT run.
-- EXPECTED: zero rows. This is the hard safety gate for requirement 2.
-- =====================================================================
SELECT 'version' AS source, v.id::text AS entity_id, v.version_number,
       v.status, v.provider_task_id, v.provider_video_id_base
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = 814
   AND v.provider_task_id IS NOT NULL AND btrim(v.provider_task_id) <> ''
UNION ALL
SELECT 'job' AS source, j.id::text AS entity_id, NULL::int AS version_number,
       j.status, j.provider_task_id, j.provider_video_id_base
  FROM render.render_jobs j
 WHERE j.job_type = 'render_scene_video'
   AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = '814'
   AND (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
        OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL);

-- =====================================================================
-- STEP 7. Guard evaluation EXACTLY as SceneVideoSubmitCoordinator.ClaimSubmitAsync
-- evaluates it (block reason precedence 1 -> 2 -> 3). All three flags must be
-- FALSE after the repair for a new V5 submit to be permitted.
--
-- Flag 1 reproduces the intent clause, including the corrected rule that a
-- CONFIRMED_NO_TASK intent only stops blocking when it belongs to the NEW job
-- (i.render_job_id = the incoming job), which the previous script version omitted.
-- =====================================================================
SELECT
  EXISTS (SELECT 1 FROM render.scene_video_submit_intents i
            JOIN render.render_jobs prior ON prior.id = i.render_job_id AND prior.tenant_id = i.tenant_id
           WHERE i.scene_id = 814
             AND NOT ((i.submit_state = 'CONFIRMED_PROVIDER_TASK' AND prior.status = 'completed')
                      OR i.submit_state = 'CONFIRMED_NO_TASK'))      AS flag_unresolved_intent,
  EXISTS (SELECT 1 FROM render.render_jobs j
           WHERE j.job_type = 'render_scene_video'
             AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = '814'
             AND (j.status = 'pending_reconciliation'
               OR j.error_code = 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
               OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
               OR (j.status NOT IN ('completed', 'failed')
                   AND (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
                        OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL))))
                                                                     AS flag_job_in_flight_or_manual_review,
  EXISTS (SELECT 1 FROM video_render.scene_video_versions v
           WHERE v.scene_id = 814
             AND ((v.status NOT IN ('completed', 'failed')
                   AND (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
                        OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL))
               OR v.status = 'pending_reconciliation'))               AS flag_version_in_flight_or_reconciliation;

-- =====================================================================
-- STEP 8. Capture the exact V3/V4 UUIDs to paste into the repair script.
-- Only the two newest non-terminal, non-selected attempts are returned.
-- =====================================================================
SELECT v.id AS version_id,
       v.version_number,
       v.status,
       v.logical_request_id,
       v.render_job_id,
       v.is_selected
  FROM video_render.scene_video_versions v
 WHERE v.scene_id = 814
   AND v.id <> COALESCE((SELECT s.selected_video_version_id
                           FROM video_render.video_project_scenes s
                          WHERE s.id = 814), '00000000-0000-0000-0000-000000000000'::uuid)
   AND v.status NOT IN ('completed', 'failed', 'cancelled')
 ORDER BY v.version_number DESC;

-- =====================================================================
-- DECISION TABLE (record the outcome in the operator log)
-- =====================================================================
-- Step 5 returns 2 pre-HTTP refusal events for V3+V4, AND
-- Step 6 returns zero rows, AND
-- Step 7 flag_version_* / flag_job_* are only true because of V3/V4 rows,
--   => the attempts are PROVEN pre-HTTP. Proceed to the repair script.
--
-- Step 5 returns fewer pre-HTTP events than attempts, OR
-- Step 6 returns ANY row, OR
-- Step 3 returns an intent in SUBMIT_IN_FLIGHT / AMBIGUOUS_SUBMIT,
--   => STOP. The newest attempt is ambiguous. Do NOT run the repair script.
--      Reconcile the provider task against the stored identifier instead, using
--      rvideo-scene-video-42804-recovery-verify.sql as the read-only entry point.