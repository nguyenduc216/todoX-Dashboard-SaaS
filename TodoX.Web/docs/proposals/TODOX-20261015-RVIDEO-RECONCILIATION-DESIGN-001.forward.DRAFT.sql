-- DRAFT ONLY; NOT FOR EXECUTION. FEATURE_ID: RVID-UI-V2-SCENE-RERENDER-HISTORY
-- CHANGE_ID: TODOX-20261015-RVIDEO-RECONCILIATION-DESIGN-001
-- Purpose: additive typed lease, handoff and durable manual-verification metadata.
-- Assumes repository scene_image_versions tenant_id uuid, id uuid, status/error_code text;
-- deployed schema MUST be checked independently. DBA preflight: catalog column types,
-- constraints, pg_index validity, table size, long transactions, UNKNOWN row counts and
-- duplicate tenant/job input imageVersionId. STOP on drift. No IF NOT EXISTS: it can
-- silently accept incompatible or invalid definitions. DBA configures session lock_timeout
-- and statement_timeout; after timeout inspect catalogs before rerun. No backfill here.
-- PHASE 1: separate short transaction. ALTER TABLE holds ACCESS EXCLUSIVE until COMMIT.
-- Rerun only after comparing each column/constraint definition; repair partial deployments
-- through a separately reviewed script, not by blindly replaying this phase.
BEGIN;
ALTER TABLE video_render.scene_image_versions
  ADD COLUMN enqueue_reconcile_after timestamptz NULL,
  ADD COLUMN enqueue_reconcile_deadline timestamptz NULL,
  ADD COLUMN enqueue_reconcile_claim_token uuid NULL,
  ADD COLUMN enqueue_reconcile_lease_until timestamptz NULL,
  ADD COLUMN enqueue_reconcile_attempts integer NULL,
  ADD COLUMN enqueue_reconcile_last_checked_at timestamptz NULL,
  ADD COLUMN enqueue_reconcile_manual_at timestamptz NULL,
  ADD COLUMN enqueue_reconcile_handed_off_at timestamptz NULL,
  ADD COLUMN enqueue_reconcile_alert_type text NULL,
  ADD COLUMN enqueue_reconcile_alert_next_at timestamptz NULL,
  ADD COLUMN enqueue_reconcile_alert_attempts integer NULL,
  ADD COLUMN enqueue_reconcile_alert_sent_at timestamptz NULL,
  ADD COLUMN enqueue_reconcile_alert_ack_at timestamptz NULL;
ALTER TABLE video_render.scene_image_versions
  ADD CONSTRAINT ck_scene_image_reconcile_attempts CHECK (enqueue_reconcile_attempts IS NULL OR enqueue_reconcile_attempts >= 0) NOT VALID,
  ADD CONSTRAINT ck_scene_image_reconcile_alert_attempts CHECK (enqueue_reconcile_alert_attempts IS NULL OR enqueue_reconcile_alert_attempts >= 0) NOT VALID,
  ADD CONSTRAINT ck_scene_image_reconcile_window CHECK (enqueue_reconcile_after IS NULL OR enqueue_reconcile_deadline IS NULL OR enqueue_reconcile_after <= enqueue_reconcile_deadline) NOT VALID;
COMMIT;
-- Phase 1 postcheck: 13 columns/three constraints, no changed statuses or job rows.
-- DBA schedules constraint VALIDATE separately after reviewing scan/lock impact.

-- PHASE 2: AUTOCOMMIT statements, NOT inside any transaction-wrapping migration runner.
-- CREATE INDEX CONCURRENTLY permits writes, but may wait for old snapshots; failed
-- creation may leave an INVALID index. Inspect pg_index.indisvalid AND indisready and
-- pg_get_indexdef; DBA reviews DROP INDEX CONCURRENTLY outside transaction for invalid
-- index, then reruns ONLY missing phase 2 statements. Never use IF NOT EXISTS here.
CREATE INDEX CONCURRENTLY ix_scene_image_reconcile_due
  ON video_render.scene_image_versions (tenant_id, enqueue_reconcile_after, id)
  WHERE status = 'pending_reconciliation' AND error_code = 'RENDER_JOB_ENQUEUE_UNKNOWN'
    AND enqueue_reconcile_manual_at IS NULL AND enqueue_reconcile_handed_off_at IS NULL;
CREATE INDEX CONCURRENTLY ix_scene_image_reconcile_manual
  ON video_render.scene_image_versions (tenant_id, enqueue_reconcile_manual_at, id)
  WHERE enqueue_reconcile_manual_at IS NOT NULL AND enqueue_reconcile_alert_ack_at IS NULL;
-- Phase 2 postcheck: both index definitions valid/ready and predicates correct.
-- Deploy DDL -> postcheck -> approved separate UNKNOWN-only deadline backfill ->
-- disabled worker -> PostgreSQL tests/tenant & identity gates -> canary enable.