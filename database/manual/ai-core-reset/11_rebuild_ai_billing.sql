BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS billing;

CREATE TABLE IF NOT EXISTS billing.ai_billing_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES system.tenants(id),
    render_job_id uuid NULL,
    business_entity_type text NULL,
    business_entity_id uuid NULL,
    payer_type text NOT NULL DEFAULT 'customer',
    payer_customer_id uuid NULL,
    payer_wallet_id uuid NULL REFERENCES billing.token_wallets(id),
    status text NOT NULL DEFAULT 'estimated',
    billing_unit text NOT NULL DEFAULT 'points',
    estimated_points numeric(20,4) NOT NULL DEFAULT 0,
    reserved_points numeric(20,4) NOT NULL DEFAULT 0,
    charged_points numeric(20,4) NOT NULL DEFAULT 0,
    refunded_points numeric(20,4) NOT NULL DEFAULT 0,
    provider_cost numeric(20,8) NULL,
    provider_currency text NULL,
    tariff_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    idempotency_key text NULL,
    error_code text NULL,
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL
);

CREATE TABLE IF NOT EXISTS billing.ai_provider_attempts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    billing_record_id uuid NULL REFERENCES billing.ai_billing_records(id) ON DELETE SET NULL,
    render_job_id uuid NOT NULL,
    attempt_no integer NOT NULL,
    provider_code text NOT NULL,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id),
    provider_task_id text NULL,
    status text NOT NULL,
    usage_quantity numeric(20,8) NULL,
    usage_unit text NULL,
    provider_cost numeric(20,8) NULL,
    provider_currency text NULL,
    raw_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    CONSTRAINT ai_provider_attempts_attempt_ck CHECK (attempt_no > 0)
);

COMMIT;
