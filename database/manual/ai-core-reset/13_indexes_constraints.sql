BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_account_code_uk
    ON public.todox_ai_provider_account(provider_code, account_code, environment);
CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_account_default_uk
    ON public.todox_ai_provider_account(provider_code, environment)
    WHERE is_default;
CREATE INDEX IF NOT EXISTS todox_ai_provider_account_select_ix
    ON public.todox_ai_provider_account(provider_code, enabled, priority, health_status, cooldown_until);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_account_lease_render_active_uk
    ON public.todox_ai_provider_account_lease(render_job_id)
    WHERE status = 'active';
CREATE INDEX IF NOT EXISTS todox_ai_provider_account_lease_claim_ix
    ON public.todox_ai_provider_account_lease(provider_account_id, status, lease_expires_at);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_usage_log_idem_uk
    ON public.todox_ai_provider_usage_log(idempotency_key)
    WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_usage_log_task_attempt_uk
    ON public.todox_ai_provider_usage_log(provider_code, provider_task_id, attempt_no)
    WHERE provider_task_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ai_billing_records_idem_uk
    ON billing.ai_billing_records(idempotency_key)
    WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ai_provider_attempts_render_attempt_uk
    ON billing.ai_provider_attempts(render_job_id, attempt_no);

CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_reference_versions_job_version_uk
    ON dance_sell.dance_sell_reference_versions(dance_sell_job_id, version_no);
CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_reference_versions_one_selected_uk
    ON dance_sell.dance_sell_reference_versions(dance_sell_job_id)
    WHERE is_selected;

COMMIT;
