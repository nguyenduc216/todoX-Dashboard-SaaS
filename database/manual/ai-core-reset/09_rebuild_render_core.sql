BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS render;

DROP TABLE IF EXISTS render.render_artifacts CASCADE;
DROP TABLE IF EXISTS render.render_job_inputs CASCADE;
DROP TABLE IF EXISTS render.render_job_events CASCADE;
DROP TABLE IF EXISTS render.render_job_steps CASCADE;
DROP TABLE IF EXISTS render.render_job_snapshots CASCADE;
DROP TABLE IF EXISTS render.render_events CASCADE;
DROP TABLE IF EXISTS render.render_scenes CASCADE;
DROP TABLE IF EXISTS render.render_jobs CASCADE;

CREATE TABLE render.render_jobs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES system.tenants(id),
    customer_id uuid NULL,
    user_id uuid NULL,
    customer_user_id uuid NULL,
    service_id uuid NULL,
    pricing_tier_id uuid NULL,
    job_code text NULL UNIQUE,
    logical_request_id text NULL,
    job_type text NOT NULL,
    operation_type text NULL,
    business_entity_type text NULL,
    business_entity_id uuid NULL,
    parent_job_id uuid NULL,
    parent_render_job_id uuid NULL,
    retry_of_job_id uuid NULL,
    title text NULL,
    prompt text NULL,
    source_type text NULL,
    source_url text NULL,
    target_duration_sec integer NULL,
    scene_count integer NULL,
    status text NOT NULL DEFAULT 'queued',
    current_step text NULL,
    progress_percent integer NOT NULL DEFAULT 0,
    priority integer NOT NULL DEFAULT 100,
    worker_key text NULL,
    lock_owner text NULL,
    lock_until timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    max_attempts integer NOT NULL DEFAULT 1,
    retry_after timestamptz NULL,
    provider_id bigint NULL REFERENCES public.todox_ai_provider(id) ON DELETE SET NULL,
    provider_capability_id bigint NULL REFERENCES public.todox_ai_provider_capability(id) ON DELETE SET NULL,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    provider_code text NULL,
    model_code text NULL,
    provider_task_id text NULL,
    provider_status text NULL,
    input_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    prompt_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    reference_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    output_json jsonb NOT NULL DEFAULT '[]'::jsonb,
    result_summary_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    last_provider_response_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_response_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    usage_quantity numeric(20,8) NULL,
    usage_unit text NULL,
    provider_raw_cost numeric(20,8) NULL,
    provider_cost numeric(20,8) NULL,
    provider_cost_currency text NULL,
    provider_currency text NULL,
    provider_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    point_cost_estimate numeric(20,4) NOT NULL DEFAULT 0,
    point_cost_charged numeric(20,4) NOT NULL DEFAULT 0,
    point_status text NOT NULL DEFAULT 'not_required',
    billing_record_id uuid NULL,
    token_cost numeric(20,4) NOT NULL DEFAULT 0,
    token_transaction_id uuid NULL,
    log_code text NULL,
    error_code text NULL,
    error_message text NULL,
    cancel_reason text NULL,
    minio_prefix text NULL,
    options jsonb NOT NULL DEFAULT '{}'::jsonb,
    queued_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    cancelled_at timestamptz NULL,
    last_heartbeat_at timestamptz NULL,
    heartbeat_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NULL,
    CONSTRAINT render_jobs_attempts_ck CHECK (attempt_count >= 0 AND max_attempts > 0),
    CONSTRAINT render_jobs_status_ck CHECK (status IN ('draft','queued','preparing','rendering','post_processing','completed','failed','cancelled','timeout'))
);

ALTER TABLE render.render_jobs
    ADD CONSTRAINT render_jobs_parent_job_fkey FOREIGN KEY (parent_job_id) REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    ADD CONSTRAINT render_jobs_parent_render_job_fkey FOREIGN KEY (parent_render_job_id) REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    ADD CONSTRAINT render_jobs_retry_of_job_fkey FOREIGN KEY (retry_of_job_id) REFERENCES render.render_jobs(id) ON DELETE SET NULL;

CREATE TABLE render.render_job_steps (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    render_job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    step_key text NOT NULL,
    step_name text NULL,
    step_order integer NOT NULL DEFAULT 0,
    status text NOT NULL DEFAULT 'pending',
    attempt integer NOT NULL DEFAULT 0,
    input_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    output_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    error_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    started_at timestamptz NULL,
    finished_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT render_job_steps_status_ck CHECK (status IN ('pending','running','completed','failed','skipped'))
);

CREATE TABLE render.render_job_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    render_job_id uuid GENERATED ALWAYS AS (job_id) STORED,
    tenant_id uuid NULL REFERENCES system.tenants(id),
    event_type text NOT NULL,
    level text NOT NULL DEFAULT 'info',
    message text NULL,
    data_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_code text NULL,
    model_code text NULL,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    provider_task_id text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT render_job_events_level_ck CHECK (level IN ('debug','info','warning','error'))
);

CREATE TABLE render.render_job_inputs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    render_job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    input_type text NOT NULL,
    input_role text NULL,
    media_id uuid NULL,
    object_key text NULL,
    public_url text NULL,
    provider_url text NULL,
    mime_type text NULL,
    input_text text NULL,
    input_url text NULL,
    input_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT render_job_inputs_type_ck CHECK (input_type IN ('character_image','product_image','direct_reference','motion_video','reference_image','prompt','other'))
);

CREATE TABLE render.render_artifacts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    render_job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    artifact_type text NOT NULL,
    media_id uuid NULL,
    object_key text NULL,
    storage_provider text NOT NULL DEFAULT 'minio',
    bucket text NULL,
    public_url text NULL,
    provider_url text NULL,
    console_url text NULL,
    mime_type text NULL,
    format text NULL,
    size_bytes bigint NULL,
    checksum text NULL,
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT render_artifacts_type_ck CHECK (artifact_type IN ('reference_image','final_video','thumbnail','provider_raw_output','preview','other'))
);

COMMIT;
