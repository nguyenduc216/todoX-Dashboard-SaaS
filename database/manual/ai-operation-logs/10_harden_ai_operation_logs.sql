CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_operation_billing_transactions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    operation_id uuid NOT NULL REFERENCES dance_sell.dance_sell_provider_operations(id),
    transaction_type text NOT NULL,
    idempotency_key text NOT NULL,
    points numeric(20,4) NOT NULL DEFAULT 0,
    wallet_transaction_id uuid NULL,
    status text NOT NULL DEFAULT 'pending',
    reason text,
    error_message text,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_operation_billing_transactions_idem_idx
    ON public.todox_ai_operation_billing_transactions(operation_id, transaction_type, idempotency_key);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'dance_sell_provider_operations_type_ck'
           AND conrelid = 'dance_sell.dance_sell_provider_operations'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_provider_operations
            ADD CONSTRAINT dance_sell_provider_operations_type_ck
            CHECK (operation_type IN ('reference_image', 'motion_video', 'output_stage'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'dance_sell_provider_operations_status_ck'
           AND conrelid = 'dance_sell.dance_sell_provider_operations'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_provider_operations
            ADD CONSTRAINT dance_sell_provider_operations_status_ck
            CHECK (status IN ('draft','queued','submitted','generating','completed','failed','timeout','cancelled'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'dance_sell_provider_operations_billing_status_ck'
           AND conrelid = 'dance_sell.dance_sell_provider_operations'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_provider_operations
            ADD CONSTRAINT dance_sell_provider_operations_billing_status_ck
            CHECK (billing_status IN ('not_required','estimated','reserved','charged','charge_failed','reconciliation','partially_refunded','refunded'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'dance_sell_provider_operations_refund_status_ck'
           AND conrelid = 'dance_sell.dance_sell_provider_operations'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_provider_operations
            ADD CONSTRAINT dance_sell_provider_operations_refund_status_ck
            CHECK (refund_status IN ('not_required','not_charged','pending','partially_refunded','refunded','refund_failed','manual_review'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'dance_sell_provider_operations_usage_unit_ck'
           AND conrelid = 'dance_sell.dance_sell_provider_operations'::regclass
    ) THEN
        ALTER TABLE dance_sell.dance_sell_provider_operations
            ADD CONSTRAINT dance_sell_provider_operations_usage_unit_ck
            CHECK (usage_unit IS NULL OR usage_unit IN ('credits','tokens','seconds','video_seconds','requests','images','usd','fixed','request','image','video_second','second'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'todox_ai_provider_balance_ledger_type_ck'
           AND conrelid = 'public.todox_ai_provider_balance_ledger'::regclass
    ) THEN
        ALTER TABLE public.todox_ai_provider_balance_ledger
            ADD CONSTRAINT todox_ai_provider_balance_ledger_type_ck
            CHECK (transaction_type IN ('opening_balance','top_up','usage_charge','refund','manual_adjustment','provider_sync'));
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'todox_ai_operation_billing_transactions_type_ck'
           AND conrelid = 'public.todox_ai_operation_billing_transactions'::regclass
    ) THEN
        ALTER TABLE public.todox_ai_operation_billing_transactions
            ADD CONSTRAINT todox_ai_operation_billing_transactions_type_ck
            CHECK (transaction_type IN ('reserve','charge','release','refund','retry_charge','retry_refund'));
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS todox_ai_operation_billing_transactions_operation_idx
    ON public.todox_ai_operation_billing_transactions(operation_id, created_at);

CREATE INDEX IF NOT EXISTS todox_ai_operation_billing_transactions_status_idx
    ON public.todox_ai_operation_billing_transactions(status, created_at);
