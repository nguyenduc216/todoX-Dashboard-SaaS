-- DRAFT ONLY; NOT FOR EXECUTION. FEATURE_ID: RVID-UI-V2-SCENE-RERENDER-HISTORY
-- CHANGE_ID: TODOX-20261015-RVIDEO-RECONCILIATION-DESIGN-001
-- Purpose: fail-closed reversal of additive metadata; assumes exact forward draft installed.
-- Preflight: disable worker, inspect live catalog/index validity and count all metadata.
-- Required: preflight -> archive all tenant/id/metadata -> verify counts/checksum and
-- archive readability/retention -> separate DBA approval -> separately reviewed evidence
-- clearing (NOT supplied here) -> rollback -> postcheck. Approval is external to SQL.
-- Guard fails even for backfilled attempts=0. Never alter status/render_job_id.
-- ALTER TABLE takes ACCESS EXCLUSIVE; DBA selects lock/statement timeouts. Verify both
-- index definitions exist before running. If partial installation: STOP, design separate
-- partial rollback. Plain DROP INDEX here can block writes; DBA may instead approve
-- autocommit DROP INDEX CONCURRENTLY before this transaction, omitting DROP lines below.
BEGIN;
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM video_render.scene_image_versions
             WHERE enqueue_reconcile_after IS NOT NULL
                OR enqueue_reconcile_deadline IS NOT NULL
                OR enqueue_reconcile_claim_token IS NOT NULL
                OR enqueue_reconcile_lease_until IS NOT NULL
                OR enqueue_reconcile_attempts IS NOT NULL
                OR enqueue_reconcile_last_checked_at IS NOT NULL
                OR enqueue_reconcile_manual_at IS NOT NULL
                OR enqueue_reconcile_handed_off_at IS NOT NULL
                OR enqueue_reconcile_alert_type IS NOT NULL
                OR enqueue_reconcile_alert_next_at IS NOT NULL
                OR enqueue_reconcile_alert_attempts IS NOT NULL
                OR enqueue_reconcile_alert_sent_at IS NOT NULL
                OR enqueue_reconcile_alert_ack_at IS NOT NULL) THEN
    RAISE EXCEPTION 'Reconciliation evidence exists: archive, verify, DBA approval and reviewed clearing required';
  END IF;
END $$;
DROP INDEX video_render.ix_scene_image_reconcile_due;
DROP INDEX video_render.ix_scene_image_reconcile_manual;
ALTER TABLE video_render.scene_image_versions
  DROP CONSTRAINT ck_scene_image_reconcile_attempts,
  DROP CONSTRAINT ck_scene_image_reconcile_alert_attempts,
  DROP CONSTRAINT ck_scene_image_reconcile_window,
  DROP COLUMN enqueue_reconcile_after,
  DROP COLUMN enqueue_reconcile_deadline,
  DROP COLUMN enqueue_reconcile_claim_token,
  DROP COLUMN enqueue_reconcile_lease_until,
  DROP COLUMN enqueue_reconcile_attempts,
  DROP COLUMN enqueue_reconcile_last_checked_at,
  DROP COLUMN enqueue_reconcile_manual_at,
  DROP COLUMN enqueue_reconcile_handed_off_at,
  DROP COLUMN enqueue_reconcile_alert_type,
  DROP COLUMN enqueue_reconcile_alert_next_at,
  DROP COLUMN enqueue_reconcile_alert_attempts,
  DROP COLUMN enqueue_reconcile_alert_sent_at,
  DROP COLUMN enqueue_reconcile_alert_ack_at;
COMMIT;
-- Postcheck: catalog absence of these columns/constraints/indexes, unchanged statuses
-- and job links; independently retained archive verification record.