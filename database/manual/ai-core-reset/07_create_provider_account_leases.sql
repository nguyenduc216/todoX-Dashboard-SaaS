BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_provider_account_lease (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE CASCADE,
    render_job_id uuid NOT NULL,
    provider_code text NOT NULL,
    capability_code text NULL,
    model_name text NULL,
    status text NOT NULL DEFAULT 'active',
    worker_key text NOT NULL,
    lease_expires_at timestamptz NOT NULL,
    heartbeat_at timestamptz NOT NULL DEFAULT now(),
    released_at timestamptz NULL,
    release_reason text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_account_lease_status_ck CHECK (status IN ('active','released','expired','cancelled','failed'))
);

COMMIT;
