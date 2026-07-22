ALTER TABLE dance_sell.dance_sell_provider_operations
    ADD COLUMN IF NOT EXISTS billing_idempotency_key text,
    ADD COLUMN IF NOT EXISTS refund_idempotency_key text,
    ADD COLUMN IF NOT EXISTS wallet_transaction_id uuid,
    ADD COLUMN IF NOT EXISTS refund_transaction_id uuid,
    ADD COLUMN IF NOT EXISTS billing_error text,
    ADD COLUMN IF NOT EXISTS refund_error text;

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_provider_operations_billing_idem_idx
    ON dance_sell.dance_sell_provider_operations(billing_idempotency_key)
    WHERE billing_idempotency_key IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_provider_operations_refund_idem_idx
    ON dance_sell.dance_sell_provider_operations(refund_idempotency_key)
    WHERE refund_idempotency_key IS NOT NULL;
