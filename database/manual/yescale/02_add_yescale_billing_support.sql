-- Standalone SQL, not a migration.
-- Adds system-funded image billing, payer tracking, reconciliation, and idempotency support.

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
    IF to_regclass('system.app_settings') IS NULL THEN
        RAISE EXCEPTION 'Missing table system.app_settings.';
    END IF;
END $$;

ALTER TABLE billing.token_wallets
    ADD COLUMN IF NOT EXISTS wallet_scope text NOT NULL DEFAULT 'customer',
    ADD COLUMN IF NOT EXISTS wallet_code text NULL,
    ADD COLUMN IF NOT EXISTS overdraft_limit numeric(18,6) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS low_balance_threshold numeric(18,6) NOT NULL DEFAULT 0;

UPDATE billing.token_wallets
   SET wallet_scope = 'customer'
 WHERE wallet_scope IS NULL OR wallet_scope = '';

CREATE UNIQUE INDEX IF NOT EXISTS token_wallets_customer_scope_uk
    ON billing.token_wallets (tenant_id, customer_id)
 WHERE wallet_scope = 'customer' AND customer_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS token_wallets_system_code_uk
    ON billing.token_wallets (tenant_id, wallet_code)
 WHERE wallet_scope = 'system' AND wallet_code IS NOT NULL;

INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
SELECT gen_random_uuid(), 'ai.image.system_wallet.initial_balance', 'billing', 'number', '1000',
       'Initial TodoX point budget for system-funded AI image renders. Review before production.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM system.app_settings WHERE setting_key = 'ai.image.system_wallet.initial_balance'
);

INSERT INTO system.app_settings (id, setting_key, setting_group, setting_type, setting_value, description, is_active, created_at)
SELECT gen_random_uuid(), 'ai.image.system_wallet.low_balance_threshold', 'billing', 'number', '100',
       'Low-balance warning threshold for system-funded AI image renders.',
       true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM system.app_settings WHERE setting_key = 'ai.image.system_wallet.low_balance_threshold'
);

WITH tenants AS (
    SELECT id AS tenant_id
      FROM system.tenants
),
settings AS (
    SELECT
        COALESCE((SELECT setting_value::numeric FROM system.app_settings WHERE setting_key='ai.image.system_wallet.initial_balance' AND is_active LIMIT 1), 0) AS initial_balance,
        COALESCE((SELECT setting_value::numeric FROM system.app_settings WHERE setting_key='ai.image.system_wallet.low_balance_threshold' AND is_active LIMIT 1), 0) AS threshold
)
INSERT INTO billing.token_wallets
    (id, tenant_id, customer_id, wallet_scope, wallet_code, balance, locked_balance, overdraft_limit, low_balance_threshold, status, created_at, updated_at)
SELECT gen_random_uuid(), tenants.tenant_id, NULL, 'system', 'TODOX_AI_IMAGE_SYSTEM',
       settings.initial_balance, 0, 0, settings.threshold, 'active', now(), now()
  FROM tenants, settings
ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS billing.ai_image_billing_records (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    logical_request_id text NOT NULL,
    render_job_id text NULL,
    customer_id uuid NULL,
    user_id uuid NULL,
    wallet_id uuid NULL,
    payer_type text NOT NULL DEFAULT 'customer',
    payer_customer_id uuid NULL,
    payer_wallet_id uuid NULL,
    provider_id bigint NOT NULL,
    provider_capability_id bigint NOT NULL,
    provider_code text NOT NULL,
    capability_code text NOT NULL,
    feature_code text NOT NULL,
    requested_model text NULL,
    actual_model text NULL,
    provider_task_id text NULL,
    provider_estimated_cost_usd numeric(18,6) NOT NULL DEFAULT 0,
    total_provider_estimated_cost_usd numeric(18,6) NOT NULL DEFAULT 0,
    provider_actual_cost_usd numeric(18,6) NULL,
    total_provider_actual_cost_usd numeric(18,6) NULL,
    provider_cost_source text NOT NULL DEFAULT 'configured_tariff',
    exchange_rate_vnd_per_usd numeric(18,6) NOT NULL DEFAULT 8000,
    todox_vnd_per_point numeric(18,6) NOT NULL DEFAULT 10000,
    provider_cost_points numeric(18,6) NOT NULL DEFAULT 0,
    customer_charged_points numeric(18,6) NOT NULL DEFAULT 0,
    system_charged_points numeric(18,6) NOT NULL DEFAULT 0,
    billing_exempt boolean NOT NULL DEFAULT false,
    exemption_reason text NULL,
    wallet_transaction_id uuid NULL,
    status text NOT NULL,
    error_message text NULL,
    metadata_json jsonb NULL,
    created_by text NULL,
    reserved_until timestamptz NULL,
    pending_reconciliation_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    failed_at timestamptz NULL,
    CONSTRAINT ai_image_billing_records_logical_request_uk UNIQUE (logical_request_id),
    CONSTRAINT ai_image_billing_records_status_ck CHECK (status IN ('reserved','completed','released','pending_reconciliation','insufficient','missing_payer','missing_customer','failed','invalid')),
    CONSTRAINT ai_image_billing_records_payer_ck CHECK (
        (payer_type = 'customer' AND payer_customer_id IS NOT NULL AND customer_charged_points >= 0 AND system_charged_points = 0)
        OR (payer_type = 'system' AND payer_customer_id IS NULL AND system_charged_points >= 0 AND customer_charged_points = 0)
    ),
    CONSTRAINT ai_image_billing_records_points_ck CHECK (provider_cost_points >= 0 AND customer_charged_points >= 0 AND system_charged_points >= 0),
    CONSTRAINT ai_image_billing_records_cost_ck CHECK (
        provider_estimated_cost_usd >= 0
        AND total_provider_estimated_cost_usd >= 0
        AND (provider_actual_cost_usd IS NULL OR provider_actual_cost_usd >= 0)
        AND (total_provider_actual_cost_usd IS NULL OR total_provider_actual_cost_usd >= 0)
    )
);

ALTER TABLE billing.ai_image_billing_records
    ADD COLUMN IF NOT EXISTS payer_type text NOT NULL DEFAULT 'customer',
    ADD COLUMN IF NOT EXISTS payer_customer_id uuid NULL,
    ADD COLUMN IF NOT EXISTS payer_wallet_id uuid NULL,
    ADD COLUMN IF NOT EXISTS system_charged_points numeric(18,6) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_provider_estimated_cost_usd numeric(18,6) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS total_provider_actual_cost_usd numeric(18,6) NULL,
    ADD COLUMN IF NOT EXISTS reserved_until timestamptz NULL,
    ADD COLUMN IF NOT EXISTS pending_reconciliation_at timestamptz NULL;

UPDATE billing.ai_image_billing_records
   SET payer_type = CASE WHEN customer_id IS NULL THEN 'system' ELSE 'customer' END,
       payer_customer_id = CASE WHEN customer_id IS NULL THEN NULL ELSE customer_id END,
       payer_wallet_id = COALESCE(payer_wallet_id, wallet_id),
       total_provider_estimated_cost_usd = COALESCE(NULLIF(total_provider_estimated_cost_usd, 0), provider_estimated_cost_usd),
       system_charged_points = CASE WHEN customer_id IS NULL THEN COALESCE(NULLIF(system_charged_points, 0), customer_charged_points, provider_cost_points, 0) ELSE system_charged_points END,
       customer_charged_points = CASE WHEN customer_id IS NULL THEN 0 ELSE customer_charged_points END,
       billing_exempt = false,
       status = CASE WHEN status = 'reserved_exempt' THEN 'pending_reconciliation' ELSE status END,
       pending_reconciliation_at = CASE WHEN status = 'reserved_exempt' THEN COALESCE(pending_reconciliation_at, now()) ELSE pending_reconciliation_at END
 WHERE payer_type IS NULL
    OR payer_wallet_id IS NULL
    OR billing_exempt = true
    OR status = 'reserved_exempt';

DO $$
BEGIN
    ALTER TABLE billing.ai_image_billing_records DROP CONSTRAINT IF EXISTS ai_image_billing_records_status_ck;
    ALTER TABLE billing.ai_image_billing_records ADD CONSTRAINT ai_image_billing_records_status_ck
        CHECK (status IN ('reserved','completed','released','pending_reconciliation','insufficient','missing_payer','missing_customer','failed','invalid'));

    ALTER TABLE billing.ai_image_billing_records DROP CONSTRAINT IF EXISTS ai_image_billing_records_payer_ck;
    ALTER TABLE billing.ai_image_billing_records ADD CONSTRAINT ai_image_billing_records_payer_ck
        CHECK (
            (payer_type = 'customer' AND payer_customer_id IS NOT NULL AND customer_charged_points >= 0 AND system_charged_points = 0)
            OR (payer_type = 'system' AND payer_customer_id IS NULL AND system_charged_points >= 0 AND customer_charged_points = 0)
        );

    ALTER TABLE billing.ai_image_billing_records DROP CONSTRAINT IF EXISTS ai_image_billing_records_points_ck;
    ALTER TABLE billing.ai_image_billing_records ADD CONSTRAINT ai_image_billing_records_points_ck
        CHECK (provider_cost_points >= 0 AND customer_charged_points >= 0 AND system_charged_points >= 0);
END $$;

CREATE TABLE IF NOT EXISTS billing.ai_image_provider_attempts (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    billing_record_id uuid NOT NULL REFERENCES billing.ai_image_billing_records(id) ON DELETE RESTRICT,
    attempt_number integer NOT NULL,
    model_name text NULL,
    provider_task_id text NULL,
    success boolean NOT NULL DEFAULT false,
    provider_estimated_cost_usd numeric(18,6) NULL,
    provider_actual_cost_usd numeric(18,6) NULL,
    cost_source text NULL,
    error_code text NULL,
    error_message text NULL,
    raw_usage_json jsonb NULL,
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ai_image_provider_attempts_attempt_ck CHECK (attempt_number > 0),
    CONSTRAINT ai_image_provider_attempts_cost_ck CHECK (
        (provider_estimated_cost_usd IS NULL OR provider_estimated_cost_usd >= 0)
        AND (provider_actual_cost_usd IS NULL OR provider_actual_cost_usd >= 0)
    )
);

ALTER TABLE billing.ai_image_provider_attempts
    ADD COLUMN IF NOT EXISTS provider_estimated_cost_usd numeric(18,6) NULL,
    ADD COLUMN IF NOT EXISTS cost_source text NULL,
    ADD COLUMN IF NOT EXISTS error_code text NULL,
    ADD COLUMN IF NOT EXISTS started_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS completed_at timestamptz NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ai_image_provider_attempts_record_attempt_uk
    ON billing.ai_image_provider_attempts (billing_record_id, attempt_number);

CREATE INDEX IF NOT EXISTS ai_image_billing_records_customer_created_ix
    ON billing.ai_image_billing_records (tenant_id, customer_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ai_image_billing_records_provider_created_ix
    ON billing.ai_image_billing_records (provider_code, capability_code, created_at DESC);

CREATE INDEX IF NOT EXISTS ai_image_billing_records_payer_created_ix
    ON billing.ai_image_billing_records (tenant_id, payer_type, payer_customer_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ai_image_billing_records_reconciliation_ix
    ON billing.ai_image_billing_records (status, reserved_until, pending_reconciliation_at)
 WHERE status IN ('reserved','pending_reconciliation');

DO $$
DECLARE
    missing text;
    system_wallet_count int;
    bad_scale int;
BEGIN
    SELECT string_agg(required.table_name || '.' || required.column_name, ', ') INTO missing
      FROM (
        VALUES
            ('token_wallets', 'wallet_scope'),
            ('token_wallets', 'wallet_code'),
            ('token_wallets', 'overdraft_limit'),
            ('ai_image_billing_records', 'logical_request_id'),
            ('ai_image_billing_records', 'payer_type'),
            ('ai_image_billing_records', 'payer_customer_id'),
            ('ai_image_billing_records', 'payer_wallet_id'),
            ('ai_image_billing_records', 'system_charged_points'),
            ('ai_image_billing_records', 'reserved_until'),
            ('ai_image_billing_records', 'pending_reconciliation_at'),
            ('ai_image_provider_attempts', 'provider_estimated_cost_usd'),
            ('ai_image_provider_attempts', 'provider_actual_cost_usd'),
            ('ai_image_provider_attempts', 'cost_source'),
            ('ai_image_provider_attempts', 'error_code')
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

    SELECT count(*) INTO system_wallet_count
      FROM billing.token_wallets
     WHERE wallet_scope = 'system'
       AND wallet_code = 'TODOX_AI_IMAGE_SYSTEM';

    IF system_wallet_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one TODOX_AI_IMAGE_SYSTEM wallet, found %.', system_wallet_count;
    END IF;

    SELECT count(*) INTO bad_scale
      FROM information_schema.columns
     WHERE table_schema = 'billing'
       AND table_name IN ('ai_image_billing_records','ai_image_provider_attempts','token_wallets')
       AND column_name IN ('customer_charged_points','system_charged_points','provider_cost_points','provider_estimated_cost_usd','provider_actual_cost_usd','balance','locked_balance')
       AND numeric_scale < 4;

    IF bad_scale > 0 THEN
        RAISE EXCEPTION 'Billing numeric scale must support 0.0192 points. Bad columns=%', bad_scale;
    END IF;
END $$;

-- Operational reports for manual reconciliation:
-- SELECT * FROM billing.ai_image_billing_records WHERE status='reserved' AND reserved_until < now();
-- SELECT * FROM billing.ai_image_billing_records WHERE status='pending_reconciliation' ORDER BY pending_reconciliation_at NULLS FIRST;
-- SELECT wallet_scope, wallet_code, balance, locked_balance, low_balance_threshold FROM billing.token_wallets WHERE wallet_scope='system';

COMMIT;
