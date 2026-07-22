BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP TABLE IF EXISTS public.todox_ai_provider_account_credential CASCADE;

CREATE TABLE public.todox_ai_provider_account_credential (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE CASCADE,
    credential_id uuid NULL REFERENCES system.provider_credentials(id) ON DELETE SET NULL,
    credential_key text NULL,
    credential_config_name text NULL,
    credential_role text NOT NULL DEFAULT 'api_key',
    enabled boolean NOT NULL DEFAULT true,
    priority integer NOT NULL DEFAULT 100,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_account_credential_ref_ck CHECK (
        credential_id IS NOT NULL OR credential_key IS NOT NULL OR credential_config_name IS NOT NULL
    ),
    CONSTRAINT todox_ai_provider_account_credential_role_ck CHECK (
        credential_role IN ('api_key','access_token','refresh_token','callback_secret','client_id','client_secret','secondary_key','custom')
    ),
    CONSTRAINT todox_ai_provider_account_credential_priority_ck CHECK (priority >= 0)
);

CREATE UNIQUE INDEX todox_ai_provider_account_credential_active_uk
    ON public.todox_ai_provider_account_credential (
        provider_account_id,
        credential_role,
        COALESCE(credential_id::text, credential_key, credential_config_name)
    )
    WHERE enabled;

COMMIT;
