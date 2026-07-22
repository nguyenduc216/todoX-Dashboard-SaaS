BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-task5-provider-diagnostics'));

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='public'
           AND indexname='todox_ai_provider_account_health_ix'
    ) THEN
        EXECUTE 'CREATE INDEX todox_ai_provider_account_health_ix ON public.todox_ai_provider_account (provider_code, enabled, health_status, cooldown_until);';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='public'
           AND indexname='todox_ai_provider_account_lease_account_status_ix'
    ) THEN
        EXECUTE 'CREATE INDEX todox_ai_provider_account_lease_account_status_ix ON public.todox_ai_provider_account_lease (provider_account_id, lease_status, lease_until);';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='public'
           AND indexname='todox_ai_provider_balance_ledger_idempotency_uk'
    ) THEN
        EXECUTE 'CREATE UNIQUE INDEX todox_ai_provider_balance_ledger_idempotency_uk ON public.todox_ai_provider_balance_ledger (idempotency_key);';
    END IF;
END $$;

COMMIT;
