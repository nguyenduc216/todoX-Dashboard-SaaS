BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS public.todox_ai_provider_balance_ledger (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    provider_account_id uuid NOT NULL REFERENCES public.todox_ai_provider_account(id) ON DELETE CASCADE,
    tx_type text NOT NULL,
    amount numeric(20,8) NOT NULL,
    balance_before numeric(20,8) NULL,
    balance_after numeric(20,8) NULL,
    unit text NOT NULL DEFAULT 'credits',
    source text NOT NULL DEFAULT 'manual',
    reference_type text NULL,
    reference_id uuid NULL,
    idempotency_key text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT todox_ai_provider_balance_ledger_type_ck CHECK (tx_type IN ('opening','topup','adjustment','provider_sync','usage','refund','correction'))
);

COMMIT;
