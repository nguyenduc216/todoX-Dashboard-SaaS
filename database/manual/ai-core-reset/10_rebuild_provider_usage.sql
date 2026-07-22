BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

ALTER TABLE public.todox_ai_provider_usage_log
    ADD COLUMN IF NOT EXISTS render_job_id uuid NULL,
    ADD COLUMN IF NOT EXISTS provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS provider_task_id text NULL,
    ADD COLUMN IF NOT EXISTS attempt_no integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS usage_quantity numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS usage_unit text NULL,
    ADD COLUMN IF NOT EXISTS provider_cost numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS provider_currency text NULL,
    ADD COLUMN IF NOT EXISTS cost_source text NULL,
    ADD COLUMN IF NOT EXISTS finalized_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS idempotency_key text NULL;

COMMIT;
