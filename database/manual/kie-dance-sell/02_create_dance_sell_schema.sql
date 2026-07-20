-- Manual SQL, not an EF migration.
-- Creates Phase 1 business table for KIE Dance Sell jobs.

BEGIN;

CREATE SCHEMA IF NOT EXISTS dance_sell;

CREATE TABLE IF NOT EXISTS dance_sell.dance_sell_jobs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid REFERENCES system.tenants(id),
    customer_id uuid REFERENCES crm.customers(id) ON DELETE SET NULL,
    user_id uuid REFERENCES auth.app_users(id) ON DELETE SET NULL,
    render_job_id uuid REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    logical_request_id text NOT NULL,
    status text NOT NULL DEFAULT 'queued',
    prompt text NOT NULL,
    character_image_url text NOT NULL,
    motion_video_url text NOT NULL,
    mode text NOT NULL DEFAULT '720p',
    character_orientation text NOT NULL DEFAULT 'image',
    provider_code text NOT NULL DEFAULT 'kie',
    provider_model text NOT NULL DEFAULT 'kling-2.6/motion-control',
    provider_task_id text NULL,
    provider_status text NULL,
    request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    submit_response_json jsonb NULL,
    poll_response_json jsonb NULL,
    callback_json jsonb NULL,
    error_json jsonb NULL,
    result_video_url text NULL,
    poll_count integer NOT NULL DEFAULT 0,
    next_poll_at timestamptz NULL,
    submitted_at timestamptz NULL,
    last_polled_at timestamptz NULL,
    completed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    error_code text NULL,
    error_message text NULL,
    CONSTRAINT dance_sell_jobs_logical_request_uk UNIQUE (logical_request_id),
    CONSTRAINT dance_sell_jobs_status_ck CHECK (status IN ('queued','submitted','rendering','completed','failed','timeout')),
    CONSTRAINT dance_sell_jobs_mode_ck CHECK (mode IN ('720p','1080p')),
    CONSTRAINT dance_sell_jobs_orientation_ck CHECK (character_orientation IN ('image','video')),
    CONSTRAINT dance_sell_jobs_poll_count_ck CHECK (poll_count >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_jobs_provider_task_uk
    ON dance_sell.dance_sell_jobs(provider_task_id)
 WHERE provider_task_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dance_sell_jobs_status_next_poll_ix
    ON dance_sell.dance_sell_jobs(status, next_poll_at)
 WHERE status IN ('queued','submitted','rendering');

CREATE INDEX IF NOT EXISTS dance_sell_jobs_customer_created_ix
    ON dance_sell.dance_sell_jobs(customer_id, created_at DESC);

CREATE INDEX IF NOT EXISTS dance_sell_jobs_render_job_ix
    ON dance_sell.dance_sell_jobs(render_job_id);

COMMIT;
