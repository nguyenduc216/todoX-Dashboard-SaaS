BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

ALTER TABLE public.todox_ai_provider_capability
    DROP CONSTRAINT IF EXISTS ck_todox_ai_provider_capability_unit_type;
ALTER TABLE public.todox_ai_provider_capability
    ADD CONSTRAINT ck_todox_ai_provider_capability_unit_type
    CHECK (unit_type IN ('credits','tokens','token_1000','request','requests','image','images','second','seconds','video_second','video_seconds','minute','minutes','fixed','usd','scene','character_1000'));

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_account_default_uk
    ON public.todox_ai_provider_account(provider_code, environment)
    WHERE is_default;
CREATE INDEX IF NOT EXISTS todox_ai_provider_account_select_ix
    ON public.todox_ai_provider_account(provider_code, enabled, priority, health_status, cooldown_until);

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_capability_default_uk
    ON public.todox_ai_provider_capability(provider_id, capability_code)
    WHERE enabled AND is_default;
CREATE INDEX IF NOT EXISTS todox_ai_provider_capability_operation_ix
    ON public.todox_ai_provider_capability(provider_code, operation_type, enabled, priority);

CREATE INDEX IF NOT EXISTS render_jobs_claim_ix
    ON render.render_jobs(status, retry_after, priority, queued_at)
    WHERE status IN ('queued','preparing');
CREATE INDEX IF NOT EXISTS render_jobs_provider_task_ix
    ON render.render_jobs(provider_code, provider_task_id)
    WHERE provider_task_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS render_jobs_business_entity_ix
    ON render.render_jobs(business_entity_type, business_entity_id);
CREATE INDEX IF NOT EXISTS render_jobs_retry_ix
    ON render.render_jobs(retry_of_job_id);
CREATE INDEX IF NOT EXISTS render_jobs_parent_ix
    ON render.render_jobs(parent_job_id, parent_render_job_id);
CREATE INDEX IF NOT EXISTS render_jobs_account_status_ix
    ON render.render_jobs(provider_account_id, status, queued_at);
CREATE UNIQUE INDEX IF NOT EXISTS render_job_steps_job_step_uk
    ON render.render_job_steps(render_job_id, step_key);
CREATE INDEX IF NOT EXISTS render_job_events_job_created_ix
    ON render.render_job_events(job_id, created_at);
CREATE INDEX IF NOT EXISTS render_job_events_task_ix
    ON render.render_job_events(provider_task_id)
    WHERE provider_task_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS render_job_inputs_unique_ix
    ON render.render_job_inputs(render_job_id, input_type, COALESCE(media_id::text, object_key, public_url, provider_url, input_text, ''));
CREATE UNIQUE INDEX IF NOT EXISTS render_artifacts_unique_ix
    ON render.render_artifacts(render_job_id, artifact_type, COALESCE(media_id::text, object_key, public_url, provider_url, checksum, ''));

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_usage_log_idem_uk
    ON public.todox_ai_provider_usage_log(idempotency_key)
    WHERE idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_usage_log_task_attempt_uk
    ON public.todox_ai_provider_usage_log(provider_code, provider_task_id, attempt_no)
    WHERE provider_task_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS todox_ai_provider_usage_log_account_created_ix
    ON public.todox_ai_provider_usage_log(provider_account_id, created_at DESC);
CREATE INDEX IF NOT EXISTS todox_ai_provider_usage_log_render_ix
    ON public.todox_ai_provider_usage_log(render_job_id);

CREATE UNIQUE INDEX IF NOT EXISTS ai_billing_records_logical_request_uk
    ON billing.ai_billing_records(logical_request_id);
CREATE INDEX IF NOT EXISTS ai_billing_records_provider_task_ix
    ON billing.ai_billing_records(provider_task_id)
    WHERE provider_task_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ai_billing_records_reconciliation_ix
    ON billing.ai_billing_records(status, next_reconciliation_at, reconciliation_attempt_count);
CREATE UNIQUE INDEX IF NOT EXISTS ai_provider_attempts_render_attempt_uk
    ON billing.ai_provider_attempts(render_job_id, attempt_no);
CREATE INDEX IF NOT EXISTS ai_provider_attempts_task_ix
    ON billing.ai_provider_attempts(provider_task_id)
    WHERE provider_task_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS dance_sell_jobs_reference_render_ix
    ON dance_sell.dance_sell_jobs(reference_render_job_id);
CREATE INDEX IF NOT EXISTS dance_sell_jobs_motion_render_ix
    ON dance_sell.dance_sell_jobs(motion_render_job_id);
CREATE INDEX IF NOT EXISTS dance_sell_jobs_customer_created_ix
    ON dance_sell.dance_sell_jobs(customer_id, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_reference_versions_job_version_uk
    ON dance_sell.dance_sell_reference_versions(dance_sell_job_id, version_no);
CREATE UNIQUE INDEX IF NOT EXISTS dance_sell_reference_versions_one_selected_uk
    ON dance_sell.dance_sell_reference_versions(dance_sell_job_id)
    WHERE is_selected;

COMMIT;
