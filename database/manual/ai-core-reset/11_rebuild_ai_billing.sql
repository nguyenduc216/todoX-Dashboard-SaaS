BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS billing;

DROP TABLE IF EXISTS billing.ai_provider_attempts CASCADE;
DROP TABLE IF EXISTS billing.ai_billing_records CASCADE;

CREATE TABLE billing.ai_billing_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NULL REFERENCES system.tenants(id),
    customer_id uuid NULL,
    user_id uuid NULL,
    render_job_id uuid NULL REFERENCES render.render_jobs(id) ON DELETE SET NULL,
    provider_id bigint NULL REFERENCES public.todox_ai_provider(id) ON DELETE SET NULL,
    provider_capability_id bigint NULL REFERENCES public.todox_ai_provider_capability(id) ON DELETE SET NULL,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    provider_task_id text NULL,
    feature_code text NULL,
    capability_code text NULL,
    operation_type text NULL,
    logical_request_id text NOT NULL,
    payer_type text NOT NULL DEFAULT 'customer',
    wallet_id uuid NULL REFERENCES billing.token_wallets(id) ON DELETE SET NULL,
    status text NOT NULL DEFAULT 'estimated',
    billing_status text NOT NULL DEFAULT 'estimated',
    refund_status text NOT NULL DEFAULT 'not_required',
    estimated_points numeric(20,4) NOT NULL DEFAULT 0,
    reserved_points numeric(20,4) NOT NULL DEFAULT 0,
    charged_points numeric(20,4) NOT NULL DEFAULT 0,
    refunded_points numeric(20,4) NOT NULL DEFAULT 0,
    provider_usage_quantity numeric(20,8) NULL,
    provider_usage_unit text NULL,
    provider_estimated_cost numeric(20,8) NULL,
    provider_actual_cost numeric(20,8) NULL,
    provider_currency text NULL,
    pricing_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    provider_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    error_message text NULL,
    reservation_transaction_id uuid NULL,
    charge_transaction_id uuid NULL,
    refund_transaction_id uuid NULL,
    reconciliation_attempt_count integer NOT NULL DEFAULT 0,
    next_reconciliation_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    CONSTRAINT ai_billing_records_points_ck CHECK (
        estimated_points >= 0 AND reserved_points >= 0 AND charged_points >= 0 AND refunded_points >= 0
    ),
    CONSTRAINT ai_billing_records_status_ck CHECK (status IN ('estimated','reserved','completed','released','refunded','failed','manual_review')),
    CONSTRAINT ai_billing_records_billing_status_ck CHECK (billing_status IN ('estimated','reserved','charged','released','failed','not_required')),
    CONSTRAINT ai_billing_records_refund_status_ck CHECK (refund_status IN ('not_required','not_charged','partial','refunded','failed'))
);

CREATE TABLE billing.ai_provider_attempts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    billing_record_id uuid NULL REFERENCES billing.ai_billing_records(id) ON DELETE SET NULL,
    render_job_id uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    provider_account_id uuid NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE SET NULL,
    provider_task_id text NULL,
    attempt_no integer NOT NULL,
    status text NOT NULL,
    usage_quantity numeric(20,8) NULL,
    usage_unit text NULL,
    provider_cost numeric(20,8) NULL,
    provider_currency text NULL,
    request_metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    response_metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    error_code text NULL,
    error_message text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    CONSTRAINT ai_provider_attempts_attempt_ck CHECK (attempt_no > 0),
    CONSTRAINT ai_provider_attempts_status_ck CHECK (status IN ('queued','submitted','running','completed','failed','cancelled','timeout'))
);

COMMIT;
