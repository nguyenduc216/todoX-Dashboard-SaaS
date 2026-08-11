-- Phase 2A: allow persisted Timelapse setup jobs without making them claimable by workers.
-- Existing queue statuses are preserved; only the additive draft state is introduced.

ALTER TABLE render.render_jobs
    DROP CONSTRAINT IF EXISTS ck_render_jobs_status;

ALTER TABLE render.render_jobs
    ADD CONSTRAINT ck_render_jobs_status CHECK (
        status IN (
            'draft',
            'queued',
            'preparing',
            'rendering',
            'post_processing',
            'pending_reconciliation',
            'completed',
            'failed',
            'cancelled'
        )
    );

CREATE INDEX IF NOT EXISTS idx_render_jobs_customer_type_created
    ON render.render_jobs (tenant_id, customer_id, user_id, job_type, created_at DESC);

COMMENT ON COLUMN render.render_jobs.status IS
    'draft, queued, preparing, rendering, post_processing, pending_reconciliation, completed, failed, cancelled';
