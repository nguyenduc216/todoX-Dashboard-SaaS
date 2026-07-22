BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_provider_account_credential (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE CASCADE,
    credential_id uuid NULL REFERENCES system.provider_credentials(id) ON DELETE SET NULL,
    credential_key text NULL,
    credential_config_name text NULL,
    role text NOT NULL DEFAULT 'primary',
    enabled boolean NOT NULL DEFAULT true,
    config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_account_credential_ref_ck CHECK (
        credential_id IS NOT NULL OR credential_key IS NOT NULL OR credential_config_name IS NOT NULL
    )
);

COMMIT;
