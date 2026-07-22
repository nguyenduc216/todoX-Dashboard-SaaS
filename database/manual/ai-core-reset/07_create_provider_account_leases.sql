BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP TABLE IF EXISTS public.todox_ai_provider_account_lease CASCADE;

CREATE TABLE public.todox_ai_provider_account_lease (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE CASCADE,
    render_job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    worker_key text NOT NULL,
    lease_status text NOT NULL DEFAULT 'active',
    leased_at timestamptz NOT NULL DEFAULT now(),
    lease_until timestamptz NOT NULL,
    heartbeat_at timestamptz NULL,
    released_at timestamptz NULL,
    release_reason text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_account_lease_status_ck CHECK (lease_status IN ('active','released','expired','cancelled'))
);

CREATE UNIQUE INDEX todox_ai_provider_account_lease_render_active_uk
    ON public.todox_ai_provider_account_lease(render_job_id)
    WHERE lease_status = 'active';
CREATE INDEX todox_ai_provider_account_lease_account_active_ix
    ON public.todox_ai_provider_account_lease(provider_account_id, lease_status, lease_until);
CREATE INDEX todox_ai_provider_account_lease_expired_ix
    ON public.todox_ai_provider_account_lease(lease_status, lease_until)
    WHERE lease_status = 'active';
CREATE INDEX todox_ai_provider_account_lease_worker_ix
    ON public.todox_ai_provider_account_lease(worker_key, lease_status);
CREATE INDEX todox_ai_provider_account_lease_render_ix
    ON public.todox_ai_provider_account_lease(render_job_id);

COMMIT;
