CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_provider_balance_ledger (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id),
    transaction_type text NOT NULL,
    amount numeric(20,8) NOT NULL,
    currency text,
    balance_before numeric(20,8),
    balance_after numeric(20,8),
    source text NOT NULL,
    reference_type text,
    reference_id uuid,
    provider_transaction_id text,
    idempotency_key text,
    note text,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_balance_ledger_idem_idx
    ON public.todox_ai_provider_balance_ledger(provider_account_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS todox_ai_provider_balance_ledger_account_idx ON public.todox_ai_provider_balance_ledger(provider_account_id, created_at);
CREATE INDEX IF NOT EXISTS todox_ai_provider_balance_ledger_reference_idx ON public.todox_ai_provider_balance_ledger(reference_type, reference_id);
