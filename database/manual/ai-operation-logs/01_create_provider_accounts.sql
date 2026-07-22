CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_provider_account (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_code text NOT NULL,
    account_name text NOT NULL,
    environment text NOT NULL DEFAULT 'production',
    credential_config_name text NOT NULL,
    currency text NOT NULL DEFAULT 'USD',
    balance_unit text NOT NULL,
    last_known_balance numeric(20,8),
    last_balance_source text,
    last_balance_synced_at timestamptz,
    minimum_balance_threshold numeric(20,8),
    enabled boolean NOT NULL DEFAULT false,
    is_default boolean NOT NULL DEFAULT false,
    config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_account_unique_name_idx
    ON public.todox_ai_provider_account(provider_code, account_name, environment);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_account_one_default_idx
    ON public.todox_ai_provider_account(provider_code, environment)
    WHERE is_default = true;

CREATE INDEX IF NOT EXISTS todox_ai_provider_account_enabled_idx
    ON public.todox_ai_provider_account(provider_code, enabled, is_default);
