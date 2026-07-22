BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-phase5-1-completion'));

CREATE UNIQUE INDEX IF NOT EXISTS render_artifacts_job_type_url_phase5_1_uk
    ON render.render_artifacts (render_job_id, artifact_type, COALESCE(public_url, provider_url, object_key, id::text));

CREATE UNIQUE INDEX IF NOT EXISTS render_job_steps_job_step_attempt_phase5_1_uk
    ON render.render_job_steps (render_job_id, step_key, attempt);

CREATE INDEX IF NOT EXISTS render_job_events_terminal_phase5_1_ix
    ON render.render_job_events (render_job_id, event_type, provider_task_id, created_at DESC)
    WHERE event_type IN ('job_completed','job_failed','job_cancelled','billing_completed','billing_released','usage_finalized','lease_released');

CREATE UNIQUE INDEX IF NOT EXISTS todox_ai_provider_usage_log_idempotency_phase5_1_uk
    ON public.todox_ai_provider_usage_log (idempotency_key)
    WHERE idempotency_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS provider_account_lease_active_phase5_1_ix
    ON public.todox_ai_provider_account_lease (provider_account_id, lease_status, lease_until)
    WHERE lease_status = 'active';

CREATE INDEX IF NOT EXISTS ai_provider_attempts_provider_task_phase5_1_ix
    ON billing.ai_provider_attempts (provider_task_id, completed_at DESC)
    WHERE provider_task_id IS NOT NULL;

COMMIT;
