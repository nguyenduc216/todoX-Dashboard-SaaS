-- Standalone SQL, not a migration.
-- Adds image billing reconciliation/idempotency support for YEScale and other routed image providers.

BEGIN;

DO $$
BEGIN
    IF to_regnamespace('billing') IS NULL THEN
        RAISE EXCEPTION 'Missing schema billing.';
    END IF;
    IF to_regclass('billing.token_wallets') IS NULL THEN
        RAISE EXCEPTION 'Missing table billing.token_wallets.';
    END IF;
    IF to_regclass('billing.token_transactions') IS NULL THEN
        RAISE EXCEPTION 'Missing table billing.token_transactions.';
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS billing.ai_image_billing_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    logical_request_id text NOT NULL,
    render_job_id text NULL,
    customer_id uuid NULL,
    user_id uuid NULL,
    wallet_id uuid NULL,
    provider_id bigint NOT NULL,
    provider_capability_id bigint NOT NULL,
    provider_code text NOT NULL,
    capability_code text NOT NULL,
    feature_code text NOT NULL,
    requested_model text NULL,
    actual_model text NULL,
    provider_task_id text NULL,
    provider_estimated_cost_usd numeric(18,6) NOT NULL DEFAULT 0,
    provider_actual_cost_usd numeric(18,6) NULL,
    provider_cost_source text NOT NULL DEFAULT 'configured_tariff',
    exchange_rate_vnd_per_usd numeric(18,6) NOT NULL DEFAULT 8000,
    todox_vnd_per_point numeric(18,6) NOT NULL DEFAULT 10000,
    provider_cost_points numeric(18,6) NOT NULL DEFAULT 0,
    customer_charged_points numeric(18,6) NOT NULL DEFAULT 0,
    billing_exempt boolean NOT NULL DEFAULT false,
    exemption_reason text NULL,
    wallet_transaction_id uuid NULL,
    status text NOT NULL,
    error_message text NULL,
    metadata_json jsonb NULL,
    created_by text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    failed_at timestamptz NULL,
    CONSTRAINT ai_image_billing_records_logical_request_uk UNIQUE (logical_request_id),
    CONSTRAINT ai_image_billing_records_status_ck CHECK (status IN ('reserved','reserved_exempt','completed','released','insufficient','missing_customer','failed','invalid')),
    CONSTRAINT ai_image_billing_records_points_ck CHECK (provider_cost_points >= 0 AND customer_charged_points >= 0),
    CONSTRAINT ai_image_billing_records_cost_ck CHECK (provider_estimated_cost_usd >= 0 AND (provider_actual_cost_usd IS NULL OR provider_actual_cost_usd >= 0))
);

CREATE TABLE IF NOT EXISTS billing.ai_image_provider_attempts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    billing_record_id uuid NOT NULL REFERENCES billing.ai_image_billing_records(id) ON DELETE RESTRICT,
    attempt_number integer NOT NULL,
    model_name text NULL,
    provider_task_id text NULL,
    success boolean NOT NULL DEFAULT false,
    provider_estimated_cost_usd numeric(18,6) NULL,
    provider_actual_cost_usd numeric(18,6) NULL,
    error_message text NULL,
    raw_usage_json jsonb NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ai_image_provider_attempts_attempt_ck CHECK (attempt_number > 0),
    CONSTRAINT ai_image_provider_attempts_cost_ck CHECK (
        (provider_estimated_cost_usd IS NULL OR provider_estimated_cost_usd >= 0)
        AND (provider_actual_cost_usd IS NULL OR provider_actual_cost_usd >= 0)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ai_image_provider_attempts_record_attempt_uk
    ON billing.ai_image_provider_attempts (billing_record_id, attempt_number);

CREATE INDEX IF NOT EXISTS ai_image_billing_records_customer_created_ix
    ON billing.ai_image_billing_records (tenant_id, customer_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ai_image_billing_records_provider_created_ix
    ON billing.ai_image_billing_records (provider_code, capability_code, created_at DESC);

DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(required.column_name, ', ') INTO missing
      FROM (
        VALUES
            ('ai_image_billing_records', 'logical_request_id'),
            ('ai_image_billing_records', 'customer_id'),
            ('ai_image_billing_records', 'wallet_transaction_id'),
            ('ai_image_billing_records', 'provider_estimated_cost_usd'),
            ('ai_image_billing_records', 'provider_actual_cost_usd'),
            ('ai_image_billing_records', 'billing_exempt'),
            ('ai_image_provider_attempts', 'attempt_number'),
            ('ai_image_provider_attempts', 'provider_task_id')
      ) AS required(table_name, column_name)
      WHERE NOT EXISTS (
          SELECT 1
            FROM information_schema.columns c
           WHERE c.table_schema = 'billing'
             AND c.table_name = required.table_name
             AND c.column_name = required.column_name
      );

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'YEScale billing support validation failed. Missing columns: %', missing;
    END IF;
END $$;

COMMIT;
