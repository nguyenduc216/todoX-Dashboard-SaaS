BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-task5-render-event-contract'));

ALTER TABLE render.render_job_events
    ADD COLUMN IF NOT EXISTS render_job_id uuid NULL,
    ADD COLUMN IF NOT EXISTS provider_account_id uuid NULL,
    ADD COLUMN IF NOT EXISTS provider_task_id text NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='render'
           AND indexname='render_job_events_job_created_ix'
    ) THEN
        EXECUTE 'CREATE INDEX render_job_events_job_created_ix ON render.render_job_events (job_id, created_at, id);';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='render'
           AND indexname='render_job_events_provider_task_ix'
    ) THEN
        EXECUTE 'CREATE INDEX render_job_events_provider_task_ix ON render.render_job_events (provider_task_id, created_at DESC);';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='render'
           AND indexname='render_job_steps_job_step_attempt_uk'
    ) THEN
        EXECUTE 'CREATE UNIQUE INDEX render_job_steps_job_step_attempt_uk ON render.render_job_steps (render_job_id, step_key, attempt);';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_indexes
         WHERE schemaname='render'
           AND indexname='render_artifacts_job_type_url_uk'
    ) THEN
        EXECUTE 'CREATE UNIQUE INDEX render_artifacts_job_type_url_uk ON render.render_artifacts (render_job_id, artifact_type, COALESCE(public_url, provider_url, object_key, id::text));';
    END IF;
END $$;

COMMIT;
