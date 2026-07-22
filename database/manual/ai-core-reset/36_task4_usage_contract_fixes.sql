BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-task4-usage'));

ALTER TABLE public.todox_ai_provider_usage_log
    ADD COLUMN IF NOT EXISTS idempotency_key text NULL,
    ADD COLUMN IF NOT EXISTS render_job_id uuid NULL,
    ADD COLUMN IF NOT EXISTS provider_account_id uuid NULL,
    ADD COLUMN IF NOT EXISTS provider_task_id text NULL,
    ADD COLUMN IF NOT EXISTS attempt_no integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS logical_request_id text NULL,
    ADD COLUMN IF NOT EXISTS provider_cost_currency text NULL,
    ADD COLUMN IF NOT EXISTS usage_source text NULL,
    ADD COLUMN IF NOT EXISTS provider_usage_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS request_json jsonb NULL,
    ADD COLUMN IF NOT EXISTS response_json jsonb NULL,
    ADD COLUMN IF NOT EXISTS finalized_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS user_id uuid NULL,
    ADD COLUMN IF NOT EXISTS operation_type text NULL,
    ADD COLUMN IF NOT EXISTS error_code text NULL;

DO $$
BEGIN
    ALTER TABLE public.todox_ai_provider_usage_log DROP CONSTRAINT IF EXISTS todox_ai_provider_usage_log_unit_ck;
    ALTER TABLE public.todox_ai_provider_usage_log
        ADD CONSTRAINT todox_ai_provider_usage_log_unit_ck CHECK (
            unit_type IN ('credits','tokens','token_1000','request','requests','image','images','second','seconds','video_second','video_seconds','minute','minutes','fixed','usd')
        );

    ALTER TABLE public.todox_ai_provider_usage_log DROP CONSTRAINT IF EXISTS todox_ai_provider_usage_log_status_ck;
    ALTER TABLE public.todox_ai_provider_usage_log
        ADD CONSTRAINT todox_ai_provider_usage_log_status_ck CHECK (
            status IN ('pending','success','failed','cancelled','refunded')
        );

    IF NOT EXISTS (
        SELECT 1
          FROM pg_indexes
         WHERE schemaname = 'public'
           AND indexname = 'todox_ai_provider_usage_log_idempotency_uk'
    ) THEN
        EXECUTE 'CREATE UNIQUE INDEX todox_ai_provider_usage_log_idempotency_uk ON public.todox_ai_provider_usage_log (idempotency_key);';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_indexes
         WHERE schemaname = 'public'
           AND indexname = 'todox_ai_provider_usage_log_render_ix'
    ) THEN
        EXECUTE 'CREATE INDEX todox_ai_provider_usage_log_render_ix ON public.todox_ai_provider_usage_log (render_job_id, created_at DESC);';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_indexes
         WHERE schemaname = 'public'
           AND indexname = 'todox_ai_provider_usage_log_provider_task_ix'
    ) THEN
        EXECUTE 'CREATE INDEX todox_ai_provider_usage_log_provider_task_ix ON public.todox_ai_provider_usage_log (provider_task_id, created_at DESC);';
    END IF;
END $$;

UPDATE public.todox_ai_provider_usage_log
   SET idempotency_key = COALESCE(
           idempotency_key,
           lower(concat_ws(':', logical_request_id, provider_code, provider_task_id, attempt_no, status, id::text))
       ),
       finalized_at = CASE
           WHEN finalized_at IS NULL AND status IN ('success','failed','cancelled','refunded') THEN created_at
           ELSE finalized_at
       END;

COMMIT;
