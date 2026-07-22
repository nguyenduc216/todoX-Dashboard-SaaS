BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-task4-billing'));

ALTER TABLE billing.ai_billing_records
    ADD COLUMN IF NOT EXISTS payer_customer_id uuid NULL,
    ADD COLUMN IF NOT EXISTS payer_wallet_id uuid NULL,
    ADD COLUMN IF NOT EXISTS system_charged_points numeric(20,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS provider_code text NULL,
    ADD COLUMN IF NOT EXISTS requested_model text NULL,
    ADD COLUMN IF NOT EXISTS actual_model text NULL,
    ADD COLUMN IF NOT EXISTS provider_estimated_cost_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS provider_actual_cost_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS provider_cost_source text NULL,
    ADD COLUMN IF NOT EXISTS exchange_rate_vnd_per_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS todox_vnd_per_point numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS provider_cost_points numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS customer_charged_points numeric(20,4) NULL,
    ADD COLUMN IF NOT EXISTS total_provider_estimated_cost_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS total_provider_actual_cost_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS actual_cost_incomplete boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS tariff_snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS wallet_transaction_id uuid NULL,
    ADD COLUMN IF NOT EXISTS billing_exempt boolean NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS exemption_reason text NULL,
    ADD COLUMN IF NOT EXISTS error_code text NULL,
    ADD COLUMN IF NOT EXISTS created_by text NULL,
    ADD COLUMN IF NOT EXISTS failed_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS reserved_until timestamptz NULL,
    ADD COLUMN IF NOT EXISTS pending_reconciliation_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS reconciliation_lock_owner text NULL,
    ADD COLUMN IF NOT EXISTS reconciliation_lock_until timestamptz NULL;

ALTER TABLE billing.ai_provider_attempts
    ADD COLUMN IF NOT EXISTS attempt_number integer NULL,
    ADD COLUMN IF NOT EXISTS model_name text NULL,
    ADD COLUMN IF NOT EXISTS success boolean NULL,
    ADD COLUMN IF NOT EXISTS provider_estimated_cost_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS provider_actual_cost_usd numeric(20,8) NULL,
    ADD COLUMN IF NOT EXISTS cost_source text NULL,
    ADD COLUMN IF NOT EXISTS raw_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS started_at timestamptz NULL;

UPDATE billing.ai_provider_attempts
   SET attempt_number = COALESCE(attempt_number, attempt_no),
       success = COALESCE(success, status = 'completed');

DO $$
BEGIN
    ALTER TABLE billing.ai_billing_records DROP CONSTRAINT IF EXISTS ai_billing_records_status_ck;
    ALTER TABLE billing.ai_billing_records DROP CONSTRAINT IF EXISTS ai_billing_records_billing_status_ck;
    ALTER TABLE billing.ai_billing_records DROP CONSTRAINT IF EXISTS ai_billing_records_refund_status_ck;
    ALTER TABLE billing.ai_billing_records
        ADD CONSTRAINT ai_billing_records_status_ck CHECK (
            status IN ('estimated','reserved','pending_provider','pending_reconciliation','completed','released','failed','manual_review','cancelled','insufficient','missing_payer','missing_customer','invalid')
        );
    ALTER TABLE billing.ai_billing_records
        ADD CONSTRAINT ai_billing_records_billing_status_ck CHECK (
            billing_status IN ('estimated','reserved','pending_provider','pending_reconciliation','completed','charged','released','failed','manual_review','cancelled','not_required')
        );
    ALTER TABLE billing.ai_billing_records
        ADD CONSTRAINT ai_billing_records_refund_status_ck CHECK (
            refund_status IN ('none','pending','completed','failed','manual_review','not_required','not_charged','partial','refunded')
        );

    ALTER TABLE billing.ai_provider_attempts DROP CONSTRAINT IF EXISTS ai_provider_attempts_status_ck;
    ALTER TABLE billing.ai_provider_attempts
        ADD CONSTRAINT ai_provider_attempts_status_ck CHECK (
            status IN ('queued','submitted','running','completed','failed','cancelled','timeout','success')
        );

    IF NOT EXISTS (
        SELECT 1
          FROM pg_indexes
         WHERE schemaname = 'billing'
           AND indexname = 'ai_billing_records_logical_request_uk'
    ) THEN
        EXECUTE 'CREATE UNIQUE INDEX ai_billing_records_logical_request_uk ON billing.ai_billing_records (logical_request_id);';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_indexes
         WHERE schemaname = 'billing'
           AND indexname = 'ai_billing_records_provider_task_ix'
    ) THEN
        EXECUTE 'CREATE INDEX ai_billing_records_provider_task_ix ON billing.ai_billing_records (provider_task_id, created_at DESC);';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_indexes
         WHERE schemaname = 'billing'
           AND indexname = 'ai_billing_records_reconciliation_ix'
    ) THEN
        EXECUTE 'CREATE INDEX ai_billing_records_reconciliation_ix ON billing.ai_billing_records (status, billing_status, next_reconciliation_at, reconciliation_lock_until);';
    END IF;

    DROP INDEX IF EXISTS billing.ai_provider_attempts_record_attempt_uk;
    EXECUTE 'CREATE UNIQUE INDEX ai_provider_attempts_record_attempt_uk ON billing.ai_provider_attempts (billing_record_id, attempt_number);';
END $$;

UPDATE billing.ai_billing_records
   SET provider_estimated_cost_usd = COALESCE(provider_estimated_cost_usd, provider_estimated_cost),
       provider_actual_cost_usd = COALESCE(provider_actual_cost_usd, provider_actual_cost),
       total_provider_estimated_cost_usd = COALESCE(total_provider_estimated_cost_usd, provider_estimated_cost),
       total_provider_actual_cost_usd = COALESCE(total_provider_actual_cost_usd, provider_actual_cost),
       customer_charged_points = COALESCE(customer_charged_points, charged_points),
       system_charged_points = COALESCE(system_charged_points, CASE WHEN payer_type = 'system' THEN charged_points ELSE 0 END),
       payer_customer_id = COALESCE(payer_customer_id, customer_id),
       payer_wallet_id = COALESCE(payer_wallet_id, wallet_id),
       provider_cost_points = COALESCE(provider_cost_points, estimated_points),
       reserved_until = COALESCE(reserved_until, next_reconciliation_at),
       pending_reconciliation_at = COALESCE(pending_reconciliation_at, next_reconciliation_at),
       provider_cost_source = COALESCE(provider_cost_source, 'provider'),
       tariff_snapshot_json = COALESCE(tariff_snapshot_json, pricing_snapshot_json)
 WHERE provider_estimated_cost_usd IS NULL
    OR provider_actual_cost_usd IS NULL
    OR total_provider_estimated_cost_usd IS NULL
    OR total_provider_actual_cost_usd IS NULL
    OR customer_charged_points IS NULL
    OR provider_cost_points IS NULL
    OR provider_cost_source IS NULL;

UPDATE billing.ai_billing_records
   SET provider_code = COALESCE(provider_code, metadata_json->>'providerCode'),
       wallet_transaction_id = COALESCE(wallet_transaction_id, charge_transaction_id, reservation_transaction_id),
       refund_status = CASE WHEN refund_status = 'not_required' THEN 'none' ELSE refund_status END;

COMMIT;
