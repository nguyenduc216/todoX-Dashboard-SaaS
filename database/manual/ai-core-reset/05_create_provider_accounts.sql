BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_provider_account (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_id bigint NOT NULL REFERENCES public.todox_ai_provider(id) ON DELETE CASCADE,
    provider_code text NOT NULL,
    account_code text NOT NULL,
    account_name text NOT NULL,
    environment text NOT NULL DEFAULT 'production',
    enabled boolean NOT NULL DEFAULT false,
    is_default boolean NOT NULL DEFAULT false,
    priority integer NOT NULL DEFAULT 100,
    weight integer NOT NULL DEFAULT 1,
    max_concurrency integer NOT NULL DEFAULT 1,
    rate_limit_requests integer NULL,
    rate_limit_window_seconds integer NULL,
    balance_unit text NULL,
    last_known_balance numeric(20,8) NULL,
    minimum_balance_threshold numeric(20,8) NULL,
    health_status text NOT NULL DEFAULT 'unknown',
    cooldown_until timestamptz NULL,
    last_selected_at timestamptz NULL,
    last_success_at timestamptz NULL,
    last_failure_at timestamptz NULL,
    config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_account_concurrency_ck CHECK (max_concurrency > 0),
    CONSTRAINT todox_ai_provider_account_weight_ck CHECK (weight > 0)
);

COMMIT;
