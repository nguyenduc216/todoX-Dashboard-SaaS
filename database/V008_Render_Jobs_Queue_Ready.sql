-- ============================================================
-- V008_Render_Jobs_Queue_Ready.sql
-- Queue-ready render job store for TodoX Dashboard.
-- Additive and idempotent: prepares image/video render workflows
-- without changing the current direct Avatar Studio render flow.
-- ============================================================

CREATE SCHEMA IF NOT EXISTS render;

CREATE TABLE IF NOT EXISTS render.render_jobs (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            uuid REFERENCES system.tenants(id),
    user_id              uuid REFERENCES auth.app_users(id) ON DELETE SET NULL,
    customer_id          uuid REFERENCES crm.customers(id) ON DELETE SET NULL,
    job_type             varchar(80) NOT NULL,
    status               varchar(40) NOT NULL DEFAULT 'queued',
    priority             integer NOT NULL DEFAULT 100,
    worker_key           varchar(120),
    lock_owner           varchar(120),
    lock_until           timestamptz,
    input_json           jsonb NOT NULL DEFAULT '{}'::jsonb,
    prompt_json          jsonb NOT NULL DEFAULT '{}'::jsonb,
    reference_json       jsonb NOT NULL DEFAULT '[]'::jsonb,
    output_json          jsonb NOT NULL DEFAULT '[]'::jsonb,
    log_code             varchar(20),
    error_code           varchar(80),
    error_message        text,
    cancel_reason        text,
    retry_of_job_id      uuid REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    attempt_count        integer NOT NULL DEFAULT 0,
    max_attempts         integer NOT NULL DEFAULT 1,
    retry_after          timestamptz,
    point_cost_estimate  numeric NOT NULL DEFAULT 0,
    point_cost_charged   numeric NOT NULL DEFAULT 0,
    point_status         varchar(40) NOT NULL DEFAULT 'not_required',
    provider_code        varchar(80),
    model_code           varchar(120),
    queued_at            timestamptz NOT NULL DEFAULT now(),
    started_at           timestamptz,
    completed_at         timestamptz,
    cancelled_at         timestamptz,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz,
    CONSTRAINT ck_render_jobs_status CHECK (
        status IN ('queued', 'preparing', 'rendering', 'post_processing', 'completed', 'failed', 'cancelled')
    ),
    CONSTRAINT ck_render_jobs_point_status CHECK (
        point_status IN ('not_required', 'pending', 'charged', 'insufficient', 'refunded', 'cancelled')
    ),
    CONSTRAINT ck_render_jobs_job_type CHECK (length(trim(job_type)) > 0),
    CONSTRAINT ck_render_jobs_attempts CHECK (attempt_count >= 0 AND max_attempts >= 1)
);

CREATE TABLE IF NOT EXISTS render.render_job_events (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id         uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    tenant_id      uuid REFERENCES system.tenants(id),
    event_type     varchar(80) NOT NULL,
    level          varchar(20) NOT NULL DEFAULT 'info',
    message        text,
    data_json      jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_code  varchar(80),
    model_code     varchar(120),
    created_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_render_job_events_level CHECK (level IN ('debug', 'info', 'warning', 'error'))
);

-- Repair path for environments where an older V008 draft created the table
-- before all queue-ready columns existed. CREATE TABLE IF NOT EXISTS does not
-- add missing columns, so keep these ALTERs idempotent.
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS tenant_id uuid REFERENCES system.tenants(id);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS user_id uuid REFERENCES auth.app_users(id) ON DELETE SET NULL;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS customer_id uuid REFERENCES crm.customers(id) ON DELETE SET NULL;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS job_type varchar(80) NOT NULL DEFAULT 'unknown';
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS status varchar(40) NOT NULL DEFAULT 'queued';
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS priority integer NOT NULL DEFAULT 100;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS worker_key varchar(120);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS lock_owner varchar(120);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS lock_until timestamptz;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS input_json jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS prompt_json jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS reference_json jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS output_json jsonb NOT NULL DEFAULT '[]'::jsonb;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS log_code varchar(20);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS error_code varchar(80);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS error_message text;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS cancel_reason text;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS retry_of_job_id uuid REFERENCES render.render_jobs(id) ON DELETE SET NULL;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS attempt_count integer NOT NULL DEFAULT 0;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS max_attempts integer NOT NULL DEFAULT 1;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS retry_after timestamptz;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS point_cost_estimate numeric NOT NULL DEFAULT 0;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS point_cost_charged numeric NOT NULL DEFAULT 0;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS point_status varchar(40) NOT NULL DEFAULT 'not_required';
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS provider_code varchar(80);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS model_code varchar(120);
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS queued_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS started_at timestamptz;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS completed_at timestamptz;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS cancelled_at timestamptz;
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
ALTER TABLE render.render_jobs ADD COLUMN IF NOT EXISTS updated_at timestamptz;

ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS job_id uuid REFERENCES render.render_jobs(id) ON DELETE CASCADE;
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS tenant_id uuid REFERENCES system.tenants(id);
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS event_type varchar(80) NOT NULL DEFAULT 'UNKNOWN';
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS level varchar(20) NOT NULL DEFAULT 'info';
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS message text;
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS data_json jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS provider_code varchar(80);
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS model_code varchar(120);
ALTER TABLE render.render_job_events ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();

CREATE INDEX IF NOT EXISTS idx_render_jobs_tenant_status
    ON render.render_jobs(tenant_id, status, priority, queued_at);

CREATE INDEX IF NOT EXISTS idx_render_jobs_claim
    ON render.render_jobs(status, retry_after, priority, queued_at)
    WHERE status IN ('queued', 'failed');

CREATE INDEX IF NOT EXISTS idx_render_jobs_locked
    ON render.render_jobs(lock_until)
    WHERE lock_until IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_render_jobs_user_created
    ON render.render_jobs(user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_render_jobs_customer_created
    ON render.render_jobs(customer_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_render_jobs_log_code
    ON render.render_jobs(log_code)
    WHERE log_code IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_render_jobs_type_status
    ON render.render_jobs(job_type, status, priority, queued_at);

CREATE INDEX IF NOT EXISTS idx_render_job_events_job_created
    ON render.render_job_events(job_id, created_at);

CREATE INDEX IF NOT EXISTS idx_render_job_events_type
    ON render.render_job_events(event_type, created_at DESC);

COMMENT ON TABLE render.render_jobs IS
    'Queue-ready render job store for avatar/image/video/background worker workflows.';

COMMENT ON COLUMN render.render_jobs.status IS
    'queued, preparing, rendering, post_processing, completed, failed, cancelled';

COMMENT ON COLUMN render.render_jobs.point_status IS
    'Point billing state for user-facing TodoX points; provider/API tokens remain separate.';

COMMENT ON TABLE render.render_job_events IS
    'Append-only timeline for render retry/cancel/debug events.';

INSERT INTO system.foundation_versions (version_code, script_name, status, message)
SELECT 'V008', 'V008_Render_Jobs_Queue_Ready.sql', 'success', 'Queue-ready render job store + events'
WHERE NOT EXISTS (SELECT 1 FROM system.foundation_versions WHERE version_code = 'V008');
