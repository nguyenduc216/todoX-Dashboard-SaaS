DO $$
DECLARE
    missing_count integer;
    invalid_count integer;
BEGIN
    SELECT count(*) INTO missing_count
      FROM (VALUES
          ('render','render_job_events','provider_account_id'),
          ('render','render_job_events','provider_task_id'),
          ('render','render_job_events','render_job_id'),
          ('render','render_job_steps','step_key'),
          ('render','render_artifacts','artifact_type'),
          ('public','todox_ai_provider_balance_ledger','idempotency_key'),
          ('billing','ai_billing_records','refund_status'),
          ('billing','ai_billing_records','refunded_points')
      ) AS required(table_schema, table_name, column_name)
      WHERE NOT EXISTS (
          SELECT 1 FROM information_schema.columns c
           WHERE c.table_schema = required.table_schema
             AND c.table_name = required.table_name
             AND c.column_name = required.column_name
      );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Task 5 required columns missing: %', missing_count;
    END IF;

    SELECT count(*) INTO missing_count
      FROM (VALUES
          ('render','render_job_events_job_created_ix'),
          ('render','render_job_events_provider_task_ix'),
          ('render','render_job_steps_job_step_attempt_uk'),
          ('render','render_artifacts_job_type_url_uk'),
          ('public','todox_ai_provider_account_health_ix'),
          ('public','todox_ai_provider_account_lease_account_status_ix'),
          ('public','todox_ai_provider_balance_ledger_idempotency_uk')
      ) AS required(schemaname, indexname)
      WHERE NOT EXISTS (
          SELECT 1 FROM pg_indexes i
           WHERE i.schemaname = required.schemaname
             AND i.indexname = required.indexname
      );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Task 5 required indexes missing: %', missing_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM billing.ai_billing_records
     WHERE refunded_points > charged_points;
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Refunded points exceed charged points: %', invalid_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM public.todox_ai_provider_account_lease
     WHERE lease_status IN ('released','expired')
       AND released_at IS NULL;
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Released/expired leases missing released_at: %', invalid_count;
    END IF;

    IF to_regclass('public.todox_ai_feature_provider_route') IS NOT NULL
       OR to_regclass('dance_sell.dance_sell_provider_operations') IS NOT NULL
       OR to_regclass('public.todox_ai_operation_assets') IS NOT NULL
       OR to_regclass('public.todox_ai_operation_billing_transactions') IS NOT NULL
       OR to_regclass('billing.ai_image_billing_records') IS NOT NULL
       OR to_regclass('billing.ai_image_provider_attempts') IS NOT NULL THEN
        RAISE EXCEPTION 'Removed legacy AI runtime tables must not exist.';
    END IF;

    RAISE NOTICE 'Task 5 runtime verification passed.';
END $$;
