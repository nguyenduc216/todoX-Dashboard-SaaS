BEGIN;
SET LOCAL statement_timeout = '10min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

DO $$
DECLARE
    table_name text;
    tables text[] := ARRAY[
        'dance_sell.dance_sell_reference_versions',
        'dance_sell.dance_sell_jobs',
        'render.render_job_inputs',
        'render.render_artifacts',
        'render.render_job_events',
        'render.render_job_steps',
        'render.render_job_snapshots',
        'render.render_events',
        'render.render_scenes',
        'render.render_jobs',
        'public.todox_ai_provider_usage_log',
        'billing.ai_image_provider_attempts',
        'billing.ai_image_billing_records',
        'public.todox_ai_provider_account_lease',
        'public.todox_ai_provider_balance_ledger'
    ];
BEGIN
    FOREACH table_name IN ARRAY tables LOOP
        IF to_regclass(table_name) IS NOT NULL THEN
            EXECUTE format('TRUNCATE TABLE %s RESTART IDENTITY CASCADE', table_name);
            RAISE NOTICE 'truncated %', table_name;
        ELSE
            RAISE NOTICE 'skip missing %', table_name;
        END IF;
    END LOOP;
END $$;

DELETE FROM billing.token_usage_logs
WHERE provider_code IS NOT NULL
   OR reference_type IN ('ai_image_render','dance_sell_generation','scene_image','scene_video');

DELETE FROM billing.token_transactions
WHERE reference_type IN ('ai_image_render','dance_sell_generation','scene_image','scene_video');

COMMIT;
