BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP TABLE IF EXISTS public.todox_ai_provider_usage_log CASCADE;

CREATE TABLE public.todox_ai_provider_usage_log (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES system.tenants(id),
    customer_id uuid NULL,
    user_id uuid NULL,
    render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    provider_id bigint NULL REFERENCES public.todox_ai_provider(id) ON DELETE SET NULL,
    provider_capability_id bigint NULL REFERENCES public.todox_ai_provider_capability(id) ON DELETE SET NULL,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    provider_code text NOT NULL,
    capability_code text NULL,
    feature_code text NULL,
    operation_type text NULL,
    model_name text NULL,
    provider_task_id text NULL,
    attempt_no integer NOT NULL DEFAULT 1,
    logical_request_id text NULL,
    quantity numeric(20,8) NOT NULL DEFAULT 0,
    unit_type text NOT NULL DEFAULT 'request',
    unit_cost_points numeric(20,8) NOT NULL DEFAULT 0,
    total_points numeric(20,8) NOT NULL DEFAULT 0,
    provider_raw_cost numeric(20,8) NULL,
    provider_cost_currency text NULL,
    usage_source text NULL,
    provider_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    balance_before numeric(20,8) NULL,
    balance_after numeric(20,8) NULL,
    status text NOT NULL DEFAULT 'pending',
    error_code text NULL,
    error_message text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    request_json jsonb NULL,
    response_json jsonb NULL,
    idempotency_key text NULL,
    finalized_at timestamptz NULL,
    created_by text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_usage_log_attempt_ck CHECK (attempt_no > 0),
    CONSTRAINT todox_ai_provider_usage_log_numbers_ck CHECK (quantity >= 0 AND unit_cost_points >= 0 AND total_points >= 0),
    CONSTRAINT todox_ai_provider_usage_log_status_ck CHECK (status IN ('pending','success','failed','cancelled','refunded')),
    CONSTRAINT todox_ai_provider_usage_log_unit_ck CHECK (
        unit_type IN ('credits','tokens','token_1000','request','requests','image','images','second','seconds','video_second','video_seconds','minute','minutes','fixed','usd')
    )
);

COMMIT;
