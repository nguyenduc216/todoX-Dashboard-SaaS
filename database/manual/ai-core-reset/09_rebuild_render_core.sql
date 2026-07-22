BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS render;

ALTER TABLE render.render_jobs
    ADD COLUMN IF NOT EXISTS parent_render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS business_entity_type text NULL,
    ADD COLUMN IF NOT EXISTS business_entity_id uuid NULL,
    ADD COLUMN IF NOT EXISTS operation_type text NULL,
    ADD COLUMN IF NOT EXISTS provider_capability_id bigint NULL REFERENCES public.todox_ai_provider_capability(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS provider_task_id text NULL,
    ADD COLUMN IF NOT EXISTS provider_status text NULL,
    ADD COLUMN IF NOT EXISTS provider_request_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS provider_response_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS provider_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS provider_cost numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS provider_currency text NULL,
    ADD COLUMN IF NOT EXISTS usage_quantity numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS usage_unit text NULL,
    ADD COLUMN IF NOT EXISTS billing_record_id uuid NULL,
    ADD COLUMN IF NOT EXISTS result_summary_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS heartbeat_at timestamptz NULL;

CREATE TABLE IF NOT EXISTS render.render_job_inputs (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    render_job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    input_role text NOT NULL,
    media_id uuid NULL,
    object_key text NULL,
    public_url text NULL,
    provider_url text NULL,
    mime_type text NULL,
    input_text text NULL,
    input_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

COMMIT;
