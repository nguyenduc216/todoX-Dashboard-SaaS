-- =====================================================================
-- OPERATOR ONLY. Run rvideo-scene-814-submit-recovery-preflight.sql first.
--
-- Purpose: close the Scene 814 V3 and V4 attempts that were refused BEFORE any provider
-- request, so the operator can press Render and create V5.
--
-- WHAT WAS WRONG WITH THE PREVIOUS VERSION OF THIS SCRIPT (all fixed here)
--   1. It matched v.status = 'pending_reconciliation', but the real V3/V4 rows are
--      version.status = 'queued'. The UPDATE matched ZERO rows and the script silently did nothing.
--   2. It updated every non-terminal row of the scene instead of the exact V3/V4 UUIDs.
--   3. It set billing status to 'released' without returning the locked points. The application
--      refund path (AiImageBillingService.ReleaseReservationAsync) ALSO does
--      balance = balance + reserved AND locked_balance = locked_balance - reserved.
--      A bare status flip left the customer points PERMANENTLY LOCKED. Step 5 now performs
--      the real settlement, mirroring the application refund exactly.
--   4. Its guard assertions did not match ClaimSubmitAsync: a CONFIRMED_NO_TASK intent only
--      unblocks when it belongs to the NEW job (i.render_job_id = the incoming job).
--
-- HARD SAFETY MODEL
--   This script does NOT trust the attempt status. It refuses the whole transaction unless the
--   database contains positive written proof that the refused attempts never reached the
--   provider: event_type = 'RVIDEO_VIDEO_NOT_SUBMITTED' with providerRequestSent = false,
--   written by SceneVideoWorkerHandler.RecordPreHttpSubmitRefusalAsync for exactly the V3/V4
--   logical requests. If that proof is absent the script RAISEs and rolls back, and the
--   operator must reconcile instead of failing the attempts.
--
-- FROZEN / NOT TOUCHED
--   V2 (completed + selected), its media URL/path, all provider identifiers and provider
--   payload contracts, migrations, IIS, and the provider itself.
--
-- =====================================================================
-- FILL IN BEFORE RUNNING (preflight prints every value)
--   v_tenant_id      : tenant uuid of project 183                  (preflight STEP 0)
--   v_v3_version_id  : exact V3 scene_video_versions.id            (preflight STEP 8)
--   v_v4_version_id  : exact V4 scene_video_versions.id            (preflight STEP 8)
--   v_v3_job_id      : V3 render.render_jobs.id                     (preflight STEP 2)
--   v_v4_job_id      : V4 render.render_jobs.id                     (preflight STEP 2)
--   v_v3_logical_id  : V3 logical_request_id                        (preflight STEP 1)
--   v_v4_logical_id  : V4 logical_request_id                        (preflight STEP 1)
-- =====================================================================
BEGIN;

-- ---------------------------------------------------------------------
-- Identity + expectations. Replace the seven literals below.
-- These are session-local so no object is created in the database.
-- ---------------------------------------------------------------------
-- is_local = false is REQUIRED: the identity must survive into the UPDATE statements.
-- With is_local = true the values would be discarded as soon as this statement finishes.
SELECT set_config('todox.v_tenant_id',     'REPLACE_WITH_TENANT_UUID',        false),
       set_config('todox.v_v3_version_id', 'REPLACE_WITH_V3_VERSION_UUID',    false),
       set_config('todox.v_v4_version_id', 'REPLACE_WITH_V4_VERSION_UUID',    false),
       set_config('todox.v_v3_job_id',     'REPLACE_WITH_V3_JOB_UUID',        false),
       set_config('todox.v_v4_job_id',     'REPLACE_WITH_V4_JOB_UUID',        false),
       set_config('todox.v_v3_logical_id', 'REPLACE_WITH_V3_LOGICAL_REQUEST', false),
       set_config('todox.v_v4_logical_id', 'REPLACE_WITH_V4_LOGICAL_REQUEST', false),
       set_config('todox.v_scene_id',      '814',                             false),
       set_config('todox.v_project_id',    '183',                             false);

-- ---------------------------------------------------------------------
-- PRECONDITION A. Identity must actually be populated (no placeholders left).
-- ---------------------------------------------------------------------
DO $$
BEGIN
    IF current_setting('todox.v_tenant_id')     LIKE 'REPLACE_%'
    OR current_setting('todox.v_v3_version_id') LIKE 'REPLACE_%'
    OR current_setting('todox.v_v4_version_id') LIKE 'REPLACE_%'
    OR current_setting('todox.v_v3_job_id')     LIKE 'REPLACE_%'
    OR current_setting('todox.v_v4_job_id')     LIKE 'REPLACE_%'
    OR current_setting('todox.v_v3_logical_id') LIKE 'REPLACE_%'
    OR current_setting('todox.v_v4_logical_id') LIKE 'REPLACE_%' THEN
        RAISE EXCEPTION 'Scene 814 repair refused: identity parameters still contain REPLACE_ placeholders.';
    END IF;
END $$;

-- ---------------------------------------------------------------------
-- PRECONDITION B. Tenant / project / scene guard. The scene must belong to the
-- declared tenant and to project 183, and V2 must still be completed + selected.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_tenant_id     uuid := current_setting('todox.v_tenant_id')::uuid;
    v_scene_id      bigint := current_setting('todox.v_scene_id')::bigint;
    v_project_id    bigint := current_setting('todox.v_project_id')::bigint;
    v_scene_rows    int;
    v_selected_ok   int;
BEGIN
    SELECT count(*) INTO v_scene_rows
      FROM video_render.video_project_scenes s
      JOIN video_render.video_projects p ON p.id = s.project_id AND p.tenant_id = s.tenant_id
     WHERE s.id = v_scene_id
       AND s.project_id = v_project_id
       AND s.tenant_id = v_tenant_id;

    IF v_scene_rows <> 1 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: scene % / project % / tenant % did not resolve to exactly one row (got %).',
            v_scene_id, v_project_id, v_tenant_id, v_scene_rows;
    END IF;

    -- V2 must exist, be completed and selected, and carry its media untouched.
    SELECT count(*) INTO v_selected_ok
      FROM video_render.scene_video_versions v
      JOIN video_render.video_project_scenes s
        ON s.selected_video_version_id = v.id AND s.id = v.scene_id AND s.tenant_id = v.tenant_id
     WHERE v.scene_id = v_scene_id
       AND v.tenant_id = v_tenant_id
       AND v.status = 'completed'
       AND v.is_selected = true;

    IF v_selected_ok <> 1 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: expected exactly one completed+selected version to preserve, found %. Aborting so V2 history cannot be damaged.',
            v_selected_ok;
    END IF;
END $$;
-- ---------------------------------------------------------------------
-- PRECONDITION C. POSITIVE EVIDENCE GATE (requirement: proof the attempt can no
-- longer be running at the provider).
--
-- An attempt is only closable when the application wrote the pre-HTTP refusal audit
-- event for that exact logical request with providerRequestSent=false. Core evidence:
-- the version is non-terminal, has no provider task identity, AND the audit event exists.
-- ---------------------------------------------------------------------
DO $$
DECLARE
    v_tenant_id      uuid := current_setting('todox.v_tenant_id')::uuid;
    v_scene_id       bigint := current_setting('todox.v_scene_id')::bigint;
    v_v3_version_id  uuid := current_setting('todox.v_v3_version_id')::uuid;
    v_v4_version_id  uuid := current_setting('todox.v_v4_version_id')::uuid;
    v_v3_job_id      uuid := current_setting('todox.v_v3_job_id')::uuid;
    v_v4_job_id      uuid := current_setting('todox.v_v4_job_id')::uuid;
    v_v3_logical_id  text := current_setting('todox.v_v3_logical_id');
    v_v4_logical_id  text := current_setting('todox.v_v4_logical_id');
    v_evidence_v3    int;
    v_evidence_v4    int;
    v_provider_id_v3 int;
    v_provider_id_v4 int;
    v_live_other     int;
    v_bad_intent     int;
BEGIN
    -- C1. Each target version must belong to this tenant/scene and be non-terminal + unselected.
    IF (SELECT count(*) FROM video_render.scene_video_versions v
         WHERE v.id = v_v3_version_id AND v.scene_id = v_scene_id AND v.tenant_id = v_tenant_id
           AND v.status NOT IN ('completed', 'failed', 'cancelled') AND v.is_selected = false) <> 1 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: V3 version % is not a non-terminal unselected row of scene %.',
            v_v3_version_id, v_scene_id;
    END IF;
    IF (SELECT count(*) FROM video_render.scene_video_versions v
         WHERE v.id = v_v4_version_id AND v.scene_id = v_scene_id AND v.tenant_id = v_tenant_id
           AND v.status NOT IN ('completed', 'failed', 'cancelled') AND v.is_selected = false) <> 1 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: V4 version % is not a non-terminal unselected row of scene %.',
            v_v4_version_id, v_scene_id;
    END IF;

    -- C2. Positive pre-HTTP refusal evidence for each logical request.
    SELECT count(*) INTO v_evidence_v3
      FROM video_render.video_project_events e
     WHERE e.project_id = current_setting('todox.v_project_id')::bigint
       AND e.event_type = 'RVIDEO_VIDEO_NOT_SUBMITTED'
       AND e.data_json->>'logicalRequestId' = v_v3_logical_id
       AND e.data_json->>'providerRequestSent' = 'false';
    SELECT count(*) INTO v_evidence_v4
      FROM video_render.video_project_events e
     WHERE e.project_id = current_setting('todox.v_project_id')::bigint
       AND e.event_type = 'RVIDEO_VIDEO_NOT_SUBMITTED'
       AND e.data_json->>'logicalRequestId' = v_v4_logical_id
       AND e.data_json->>'providerRequestSent' = 'false';

    IF v_evidence_v3 < 1 OR v_evidence_v4 < 1 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: missing pre-HTTP refusal evidence (V3 evidence=%, V4 evidence=%). The attempt may still be running at the provider. Reconcile instead of failing the attempts.',
            v_evidence_v3, v_evidence_v4;
    END IF;

    -- C3. Negative proof: no provider identity may exist on either target version or job.
    SELECT count(*) INTO v_provider_id_v3
      FROM video_render.scene_video_versions v
     WHERE v.id = v_v3_version_id
       AND (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
            OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL);
    SELECT count(*) INTO v_provider_id_v4
      FROM video_render.scene_video_versions v
     WHERE v.id = v_v4_version_id
       AND (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
            OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL);

    IF v_provider_id_v3 > 0 OR v_provider_id_v4 > 0 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: a provider task identity exists on V3/V4 (V3=%, V4=%). The attempt reached the provider. Reconcile instead.',
            v_provider_id_v3, v_provider_id_v4;
    END IF;

    -- C4. No OTHER version/job of this scene may still be live with a provider identity.
    SELECT count(*) INTO v_live_other
      FROM video_render.scene_video_versions v
     WHERE v.scene_id = v_scene_id
       AND v.status NOT IN ('completed', 'failed', 'cancelled')
       AND v.id NOT IN (v_v3_version_id, v_v4_version_id)
       AND (NULLIF(btrim(v.provider_task_id), '') IS NOT NULL
            OR NULLIF(btrim(v.provider_video_id_base), '') IS NOT NULL);
    SELECT v_live_other + count(*) INTO v_live_other
      FROM render.render_jobs j
     WHERE j.job_type = 'render_scene_video'
       AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = v_scene_id::text
       AND j.status NOT IN ('completed', 'failed', 'cancelled')
       AND j.id NOT IN (v_v3_job_id, v_v4_job_id)
       AND (NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
            OR NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL);

    IF v_live_other > 0 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: % other live provider-bound attempt(s) exist for this scene. Resolve those first.', v_live_other;
    END IF;

    -- C5. No ambiguous intent may exist for the scene.
    SELECT count(*) INTO v_bad_intent
      FROM render.scene_video_submit_intents i
     WHERE i.scene_id = v_scene_id
       AND i.submit_state IN ('SUBMIT_IN_FLIGHT', 'AMBIGUOUS_SUBMIT', 'MANUAL_REVIEW_REQUIRED');

    IF v_bad_intent > 0 THEN
        RAISE EXCEPTION 'Scene 814 repair refused: % in-flight/ambiguous submit intent(s) exist for this scene. Reconcile instead.', v_bad_intent;
    END IF;
END $$;
-- =====================================================================
-- STEP 1. Audit BEFORE state. Preserved permanently in video_project_events so the
-- error history is never lost when the attempt rows are moved to terminal failure.
-- =====================================================================
INSERT INTO video_render.video_project_events
    (project_id, tenant_id, event_type, level, message, data_json, created_at)
SELECT current_setting('todox.v_project_id')::bigint,
       current_setting('todox.v_tenant_id')::uuid,
       'SCENE_VIDEO_814_RECOVERY_AUDIT',
       'warning',
       'Scene 814 pre-HTTP refused attempts (V3/V4) were closed by an operator after written pre-HTTP refusal evidence was verified.',
       jsonb_build_object(
           'sceneId', current_setting('todox.v_scene_id')::bigint,
           'v3VersionId', v3.id, 'v3LogicalRequestId', v3.logical_request_id,
           'v3PreviousStatus', v3.status, 'v3PreviousErrorCode', v3.error_code,
           'v3PreviousErrorMessage', left(COALESCE(v3.error_message, ''), 1000),
           'v4VersionId', v4.id, 'v4LogicalRequestId', v4.logical_request_id,
           'v4PreviousStatus', v4.status, 'v4PreviousErrorCode', v4.error_code,
           'v4PreviousErrorMessage', left(COALESCE(v4.error_message, ''), 1000),
           'v3JobId', current_setting('todox.v_v3_job_id')::uuid,
           'v4JobId', current_setting('todox.v_v4_job_id')::uuid,
           'previousCharge', jsonb_build_object(
               'v3ChargedPoints', v3.charged_points, 'v3RefundedPoints', v3.refunded_points,
               'v4ChargedPoints', v4.charged_points, 'v4RefundedPoints', v4.refunded_points),
           'providerRequestSent', false,
           'operatorNote', 'No migration. No provider call. V2 preserved.'),
       now()
  FROM video_render.scene_video_versions v3
  JOIN video_render.scene_video_versions v4
    ON v4.id = current_setting('todox.v_v4_version_id')::uuid
   AND v4.tenant_id = v3.tenant_id
 WHERE v3.id = current_setting('todox.v_v3_version_id')::uuid
   AND v3.tenant_id = current_setting('todox.v_tenant_id')::uuid
   AND v3.scene_id = current_setting('todox.v_scene_id')::bigint;

-- =====================================================================
-- STEP 2. Move V3 to terminal failure. Exact UUID, exact expected row count.
-- error_code is COALESCEd so an existing, meaningful error code is never overwritten.
-- =====================================================================
WITH target AS (
    SELECT v.id
      FROM video_render.scene_video_versions v
     WHERE v.id = current_setting('todox.v_v3_version_id')::uuid
       AND v.scene_id = current_setting('todox.v_scene_id')::bigint
       AND v.tenant_id = current_setting('todox.v_tenant_id')::uuid
       AND v.status NOT IN ('completed', 'failed', 'cancelled')
       AND v.is_selected = false
       AND NULLIF(btrim(v.provider_task_id), '') IS NULL
       AND NULLIF(btrim(v.provider_video_id_base), '') IS NULL
)
UPDATE video_render.scene_video_versions v
   SET status        = 'failed',
       error_code    = COALESCE(NULLIF(btrim(v.error_code), ''), 'SCENE_VIDEO_SUBMIT_GUARD_REJECTED'),
       error_message = COALESCE(NULLIF(btrim(v.error_message), ''),
                          'Lan gui video bi chan truoc khi goi nha cung cap; khong co yeu cau nao duoc gui di.'),
       completed_at  = COALESCE(v.completed_at, now()),
       updated_at    = now()
  FROM target t
 WHERE v.id = t.id;

-- =====================================================================
-- STEP 3. Move V4 to terminal failure (same guarded shape as V3).
-- =====================================================================
WITH target AS (
    SELECT v.id
      FROM video_render.scene_video_versions v
     WHERE v.id = current_setting('todox.v_v4_version_id')::uuid
       AND v.scene_id = current_setting('todox.v_scene_id')::bigint
       AND v.tenant_id = current_setting('todox.v_tenant_id')::uuid
       AND v.status NOT IN ('completed', 'failed', 'cancelled')
       AND v.is_selected = false
       AND NULLIF(btrim(v.provider_task_id), '') IS NULL
       AND NULLIF(btrim(v.provider_video_id_base), '') IS NULL
)
UPDATE video_render.scene_video_versions v
   SET status        = 'failed',
       error_code    = COALESCE(NULLIF(btrim(v.error_code), ''), 'SCENE_VIDEO_SUBMIT_GUARD_REJECTED'),
       error_message = COALESCE(NULLIF(btrim(v.error_message), ''),
                          'Lan gui video bi chan truoc khi goi nha cung cap; khong co yeu cau nao duoc gui di.'),
       completed_at  = COALESCE(v.completed_at, now()),
       updated_at    = now()
  FROM target t
 WHERE v.id = t.id;

-- =====================================================================
-- STEP 4. Close the V3/V4 jobs terminally AND clear the V4 manual-review blocker.
-- Removing input_json->>'sceneVideoManualReviewAt' and normalising error_code is what
-- makes ClaimSubmitAsync stop returning SceneJobInFlightOrManualReview.
-- Only the two exact job UUIDs are touched.
-- =====================================================================
UPDATE render.render_jobs j
   SET status        = 'failed',
       error_code    = CASE
                           WHEN j.error_code = 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                           THEN 'SCENE_VIDEO_SUBMIT_GUARD_REJECTED'
                           ELSE COALESCE(NULLIF(btrim(j.error_code), ''), 'SCENE_VIDEO_SUBMIT_GUARD_REJECTED')
                       END,
       error_message = COALESCE(NULLIF(btrim(j.error_message), ''),
                          'Lan gui video bi chan truoc khi goi nha cung cap; khong co yeu cau nao duoc gui di.'),
       input_json    = COALESCE(j.input_json, '{}'::jsonb) - 'sceneVideoManualReviewAt',
       completed_at  = COALESCE(j.completed_at, now()),
       updated_at    = now()
 WHERE j.id IN (current_setting('todox.v_v3_job_id')::uuid,
                current_setting('todox.v_v4_job_id')::uuid)
   AND j.job_type = 'render_scene_video'
   AND j.tenant_id = current_setting('todox.v_tenant_id')::uuid
   AND COALESCE(j.input_json->>'sceneId', j.input_json->>'scene_id') = current_setting('todox.v_scene_id')
   -- The V3/V4 jobs are ALREADY 'failed' in production, but a failed job can still hold the
   -- manual-review marker that keeps ClaimSubmitAsync blocked. Only an already-terminal job
   -- that carries NO blocker is skipped; anything still holding a blocker is cleaned up.
   AND NOT (j.status IN ('completed', 'cancelled')
            OR (j.status = 'failed'
                AND j.error_code <> 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
                AND NOT (COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt')))
   AND NULLIF(btrim(j.provider_task_id), '') IS NULL
   AND NULLIF(btrim(j.provider_video_id_base), '') IS NULL;

-- =====================================================================
-- STEP 5. REAL billing settlement (requirement 5). Mirrors
-- AiImageBillingService.ReleaseReservationAsync exactly:
--   wallet.balance        += min(locked_balance, reserved_points)
--   wallet.locked_balance -= min(locked_balance, reserved_points)
--   billing_record.status  = 'released', failed_at = now()
-- Only reservations whose provider_task_id is empty and whose logical request belongs
-- to closed V3/V4 are released. Nothing is charged and no ledger row is fabricated.
-- =====================================================================
-- 5a. Return the locked points to each payer wallet.
WITH released AS (
    SELECT b.id,
           b.payer_wallet_id,
           b.reserved_points,
           (SELECT min(x) FROM (VALUES (w.locked_balance), (b.reserved_points)) AS t(x)) AS release_points
      FROM billing.ai_image_billing_records b
      JOIN billing.token_wallets w ON w.id = b.payer_wallet_id
     WHERE b.logical_request_id IN (current_setting('todox.v_v3_logical_id'),
                                    current_setting('todox.v_v4_logical_id'))
       AND b.status IN ('reserved', 'pending_reconciliation', 'manual_review')
       AND NULLIF(btrim(b.provider_task_id), '') IS NULL
       AND COALESCE(b.reserved_points, 0) > 0
)
UPDATE billing.token_wallets w
   SET balance        = w.balance + r.release_points,
       locked_balance = w.locked_balance - r.release_points,
       updated_at     = now()
  FROM released r
 WHERE w.id = r.payer_wallet_id
   AND r.release_points > 0;

-- 5b. Flip the reservation records to released and stamp the failure.
UPDATE billing.ai_image_billing_records b
   SET status                 = 'released',
       error_message          = COALESCE(NULLIF(btrim(b.error_message), ''),
                                    'Lan gui video bi chan truoc khi goi nha cung cap; diem da duoc hoan ve vi.'),
       failed_at              = COALESCE(b.failed_at, now()),
       pending_reconciliation_at = NULL,
       reconciliation_lock_owner = NULL,
       reconciliation_lock_until = NULL,
       updated_at             = now()
 WHERE b.logical_request_id IN (current_setting('todox.v_v3_logical_id'),
                                current_setting('todox.v_v4_logical_id'))
   AND b.status IN ('reserved', 'pending_reconciliation', 'manual_review')
   AND NULLIF(btrim(b.provider_task_id), '') IS NULL;

-- =====================================================================
-- STEP 6. Release any durable intent that belongs to the closed attempts, using the
-- SAME rule as SceneVideoSubmitCoordinator.TryRecordPreHttpFailureAsync: only a
-- persisted, unsubmitted intent is closed, and it is closed as CONFIRMED_NO_TASK.
-- =====================================================================
UPDATE render.scene_video_submit_intents i
   SET submit_state  = 'CONFIRMED_NO_TASK',
       reconciled_at = COALESCE(i.reconciled_at, now()),
       updated_at    = now()
 WHERE i.scene_id = current_setting('todox.v_scene_id')::bigint
   AND i.tenant_id = current_setting('todox.v_tenant_id')::uuid
   AND i.logical_request_id IN (current_setting('todox.v_v3_logical_id'),
                                current_setting('todox.v_v4_logical_id'))
   AND i.submit_state = 'SUBMIT_INTENT_PERSISTED'
   AND NULLIF(btrim(i.provider_task_id), '') IS NULL;

-- =====================================================================
-- STEP 7. Synchronise the scene status so the UI does not stay stuck on
-- video_rendering. V2 media columns are deliberately NOT touched.
-- =====================================================================
UPDATE video_render.video_project_scenes s
   SET status     = CASE WHEN s.status = 'video_rendering' THEN 'failed' ELSE s.status END,
       updated_at = now()
 WHERE s.id = current_setting('todox.v_scene_id')::bigint
   AND s.project_id = current_setting('todox.v_project_id')::bigint
   AND s.tenant_id = current_setting('todox.v_tenant_id')::uuid
   AND s.status = 'video_rendering';
-- =====================================================================
-- POSTCONDITIONS. The transaction is rolled back in full if any assertion fails.
-- =====================================================================
DO $$
DECLARE
    v_tenant_id      uuid := current_setting('todox.v_tenant_id')::uuid;
    v_scene_id       bigint := current_setting('todox.v_scene_id')::bigint;
    v_v3_version_id  uuid := current_setting('todox.v_v3_version_id')::uuid;
    v_v4_version_id  uuid := current_setting('todox.v_v4_version_id')::uuid;
    v_v3_job_id      uuid := current_setting('todox.v_v3_job_id')::uuid;
    v_v4_job_id      uuid := current_setting('todox.v_v4_job_id')::uuid;
    v_bad            int;
    v_selected_ok    int;
    v_v2_media_ok    int;
BEGIN
    -- P1. V3 and V4 must now be failed.
    SELECT count(*) INTO v_bad
      FROM video_render.scene_video_versions v
     WHERE v.id IN (v_v3_version_id, v_v4_version_id) AND v.status <> 'failed';
    IF v_bad > 0 THEN
        RAISE EXCEPTION 'Scene 814 repair postcondition failed: % of the target versions are still not failed.', v_bad;
    END IF;

    -- P2. No manual-review marker or SCENE_VIDEO_MANUAL_REVIEW_REQUIRED may survive on the
    -- target jobs, because either one keeps the submit guard closed.
    SELECT count(*) INTO v_bad
      FROM render.render_jobs j
     WHERE j.id IN (v_v3_job_id, v_v4_job_id)
       AND (j.error_code = 'SCENE_VIDEO_MANUAL_REVIEW_REQUIRED'
            OR COALESCE(j.input_json, '{}'::jsonb) ? 'sceneVideoManualReviewAt'
            OR j.status NOT IN ('completed', 'failed', 'cancelled'));
    IF v_bad > 0 THEN
        RAISE EXCEPTION 'Scene 814 repair postcondition failed: % target job(s) still block the submit guard.', v_bad;
    END IF;

    -- P3. V2 must still be completed AND selected, with its media identity intact.
    SELECT count(*) INTO v_selected_ok
      FROM video_render.scene_video_versions v
      JOIN video_render.video_project_scenes s
        ON s.selected_video_version_id = v.id AND s.id = v.scene_id AND s.tenant_id = v.tenant_id
     WHERE v.scene_id = v_scene_id AND v.tenant_id = v_tenant_id
       AND v.status = 'completed' AND v.is_selected = true;

    IF v_selected_ok <> 1 THEN
        RAISE EXCEPTION 'Scene 814 repair postcondition failed: the completed+selected V2 version was not preserved (found %).',
            v_selected_ok;
    END IF;

    SELECT count(*) INTO v_v2_media_ok
      FROM video_render.video_project_scenes s
      JOIN video_render.scene_video_versions v ON v.id = s.selected_video_version_id
     WHERE s.id = v_scene_id
       AND v.provider_task_id IS NOT NULL AND btrim(v.provider_task_id) <> '';
    IF v_v2_media_ok <> 1 THEN
        RAISE EXCEPTION 'Scene 814 repair postcondition failed: the selected V2 provider/media identity was not preserved.';
    END IF;

    -- P4. No released reservation may still hold locked points for these requests.
    SELECT count(*) INTO v_bad
      FROM billing.ai_image_billing_records b
     WHERE b.logical_request_id IN (current_setting('todox.v_v3_logical_id'),
                                    current_setting('todox.v_v4_logical_id'))
       AND b.status IN ('reserved', 'pending_reconciliation', 'manual_review')
       AND NULLIF(btrim(b.provider_task_id), '') IS NULL;
    IF v_bad > 0 THEN
        RAISE EXCEPTION 'Scene 814 repair postcondition failed: % reservation(s) would keep customer points locked.', v_bad;
    END IF;
END $$;

COMMIT;

-- =====================================================================
-- EXPECTED RESULT SUMMARY (record in the operator log)
--   STEP 2 -> V3 version:  1 row updated
--   STEP 3 -> V4 version:  1 row updated
--   STEP 4 -> jobs:        up to 2 rows updated
--   STEP 5a -> wallets:    1-2 rows updated (0 is valid when reserved_points = 0)
--   STEP 5b -> billing:    1-2 rows updated
--   STEP 6 -> intents:     0 rows updated when no intent was ever persisted (Scene 814)
--   STEP 7 -> scene:       0 or 1 row updated
-- If STEP 2 or STEP 3 reported 0 rows, the identity literals are wrong. Roll back.
-- =====================================================================