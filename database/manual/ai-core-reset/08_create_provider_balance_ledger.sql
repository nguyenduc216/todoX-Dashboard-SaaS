BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

DROP TABLE IF EXISTS public.todox_ai_provider_balance_ledger CASCADE;

CREATE TABLE public.todox_ai_provider_balance_ledger (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE CASCADE,
    transaction_type text NOT NULL,
    amount numeric(20,8) NOT NULL,
    balance_before numeric(20,8) NULL,
    balance_after numeric(20,8) NULL,
    unit text NOT NULL,
    source text NULL,
    reference_type text NULL,
    reference_id uuid NULL,
    provider_transaction_id text NULL,
    idempotency_key text NOT NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT todox_ai_provider_balance_ledger_type_ck CHECK (
        transaction_type IN ('opening_balance','top_up','usage_charge','refund','manual_adjustment','provider_sync')
    )
);

CREATE UNIQUE INDEX todox_ai_provider_balance_ledger_idem_uk
    ON public.todox_ai_provider_balance_ledger(provider_account_id, idempotency_key);
CREATE INDEX todox_ai_provider_balance_ledger_account_created_ix
    ON public.todox_ai_provider_balance_ledger(provider_account_id, created_at DESC);

COMMIT;
