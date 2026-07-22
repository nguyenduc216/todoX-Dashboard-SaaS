BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-phase5-1-billing'));

ALTER TABLE billing.ai_billing_records
    ADD COLUMN IF NOT EXISTS reservation_transaction_id uuid NULL,
    ADD COLUMN IF NOT EXISTS charge_transaction_id uuid NULL,
    ADD COLUMN IF NOT EXISTS refund_transaction_id uuid NULL,
    ADD COLUMN IF NOT EXISTS wallet_transaction_id uuid NULL,
    ADD COLUMN IF NOT EXISTS refunded_points numeric(20,4) NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS pending_reconciliation_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS reconciliation_lock_owner text NULL,
    ADD COLUMN IF NOT EXISTS reconciliation_lock_until timestamptz NULL;

ALTER TABLE billing.ai_billing_records DROP CONSTRAINT IF EXISTS ai_billing_records_status_ck;
ALTER TABLE billing.ai_billing_records
    ADD CONSTRAINT ai_billing_records_status_ck CHECK (
        status IN ('estimated','reserved','pending_provider','pending_reconciliation','completed','released','failed','manual_review','cancelled','insufficient','missing_payer','missing_customer','invalid')
    );

ALTER TABLE billing.ai_billing_records DROP CONSTRAINT IF EXISTS ai_billing_records_billing_status_ck;
ALTER TABLE billing.ai_billing_records
    ADD CONSTRAINT ai_billing_records_billing_status_ck CHECK (
        billing_status IN ('estimated','reserved','pending_provider','pending_reconciliation','completed','charged','released','failed','manual_review','cancelled','not_required')
    );

ALTER TABLE billing.ai_billing_records DROP CONSTRAINT IF EXISTS ai_billing_records_refund_status_ck;
ALTER TABLE billing.ai_billing_records
    ADD CONSTRAINT ai_billing_records_refund_status_ck CHECK (
        refund_status IN ('none','pending','completed','failed','manual_review','not_required','not_charged','partial','refunded')
    );

DO $$
DECLARE
    constraint_name text;
BEGIN
    FOR constraint_name IN
        SELECT c.conname
          FROM pg_constraint c
          JOIN pg_class t ON t.oid = c.conrelid
          JOIN pg_namespace n ON n.oid = t.relnamespace
         WHERE n.nspname = 'billing'
           AND t.relname = 'token_transactions'
           AND c.contype = 'c'
           AND pg_get_constraintdef(c.oid) ILIKE '%transaction_type%'
    LOOP
        EXECUTE format('ALTER TABLE billing.token_transactions DROP CONSTRAINT %I', constraint_name);
    END LOOP;

    ALTER TABLE billing.token_transactions
        ADD CONSTRAINT token_transactions_transaction_type_phase5_1_ck CHECK (
            transaction_type IN ('debit','credit','reserve','charge','release','refund','retry_charge','retry_refund','adjustment','top_up')
        );
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ai_billing_records_logical_request_uk
    ON billing.ai_billing_records (logical_request_id);

CREATE INDEX IF NOT EXISTS ai_billing_records_reconciliation_phase5_1_ix
    ON billing.ai_billing_records (status, billing_status, pending_reconciliation_at, reconciliation_lock_until);

CREATE UNIQUE INDEX IF NOT EXISTS token_transactions_ai_reserve_once_uk
    ON billing.token_transactions (reference_id)
    WHERE reference_type = 'ai_billing_reservation';

CREATE UNIQUE INDEX IF NOT EXISTS token_transactions_ai_charge_once_uk
    ON billing.token_transactions (reference_id)
    WHERE reference_type = 'ai_billing_charge';

CREATE UNIQUE INDEX IF NOT EXISTS token_transactions_ai_refund_once_uk
    ON billing.token_transactions (reference_id)
    WHERE reference_type = 'ai_billing_refund';

COMMIT;
