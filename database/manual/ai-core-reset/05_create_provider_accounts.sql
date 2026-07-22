BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP TABLE IF EXISTS public.todox_ai_provider_account CASCADE;

CREATE TABLE public.todox_ai_provider_account (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_id bigint NOT NULL REFERENCES public.todox_ai_provider(id) ON DELETE CASCADE,
    provider_code text NOT NULL,
    account_code text NOT NULL,
    account_name text NOT NULL,
    external_account_id text NULL,
    environment text NOT NULL DEFAULT 'production',
    enabled boolean NOT NULL DEFAULT true,
    is_default boolean NOT NULL DEFAULT false,
    priority integer NOT NULL DEFAULT 100,
    weight integer NOT NULL DEFAULT 100,
    max_concurrency integer NOT NULL DEFAULT 1,
    rate_limit_count integer NULL,
    rate_limit_requests integer NULL,
    rate_limit_window_seconds integer NULL,
    balance_unit text NULL,
    last_known_balance numeric(20,8) NULL,
    minimum_balance numeric(20,8) NULL,
    minimum_balance_threshold numeric(20,8) NULL,
    last_balance_source text NULL,
    last_balance_synced_at timestamptz NULL,
    health_status text NOT NULL DEFAULT 'unknown',
    consecutive_failures integer NOT NULL DEFAULT 0,
    cooldown_until timestamptz NULL,
    last_selected_at timestamptz NULL,
    last_success_at timestamptz NULL,
    last_failure_at timestamptz NULL,
    config_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by uuid NULL,
    updated_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_account_code_uk UNIQUE (provider_id, account_code, environment),
    CONSTRAINT todox_ai_provider_account_concurrency_ck CHECK (max_concurrency > 0),
    CONSTRAINT todox_ai_provider_account_weight_ck CHECK (weight > 0),
    CONSTRAINT todox_ai_provider_account_priority_ck CHECK (priority >= 0),
    CONSTRAINT todox_ai_provider_account_rate_limit_ck CHECK (
        (rate_limit_count IS NULL OR rate_limit_count >= 0)
        AND (rate_limit_requests IS NULL OR rate_limit_requests >= 0)
        AND (rate_limit_window_seconds IS NULL OR rate_limit_window_seconds >= 0)
    ),
    CONSTRAINT todox_ai_provider_account_balance_ck CHECK (
        (minimum_balance IS NULL OR minimum_balance >= 0)
        AND (minimum_balance_threshold IS NULL OR minimum_balance_threshold >= 0)
    ),
    CONSTRAINT todox_ai_provider_account_health_ck CHECK (
        health_status IN ('unknown','healthy','degraded','cooldown','disabled','exhausted')
    )
);

COMMIT;
