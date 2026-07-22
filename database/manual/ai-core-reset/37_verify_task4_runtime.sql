DO $$
DECLARE
    missing_count integer;
    invalid_count integer;
BEGIN
    SELECT count(*) INTO missing_count
      FROM (VALUES
          ('billing','ai_billing_records','payer_customer_id'),
          ('billing','ai_billing_records','payer_wallet_id'),
          ('billing','ai_billing_records','provider_code'),
          ('billing','ai_billing_records','requested_model'),
          ('billing','ai_billing_records','actual_model'),
          ('billing','ai_billing_records','provider_estimated_cost_usd'),
          ('billing','ai_billing_records','provider_actual_cost_usd'),
          ('billing','ai_billing_records','provider_cost_source'),
          ('billing','ai_billing_records','customer_charged_points'),
          ('billing','ai_billing_records','system_charged_points'),
          ('billing','ai_billing_records','tariff_snapshot_json'),
          ('billing','ai_billing_records','wallet_transaction_id'),
          ('billing','ai_billing_records','reserved_until'),
          ('billing','ai_billing_records','pending_reconciliation_at'),
          ('billing','ai_billing_records','reconciliation_lock_owner'),
          ('billing','ai_billing_records','reconciliation_lock_until'),
          ('billing','ai_provider_attempts','attempt_number'),
          ('billing','ai_provider_attempts','model_name'),
          ('billing','ai_provider_attempts','success'),
          ('billing','ai_provider_attempts','raw_usage_json'),
          ('public','todox_ai_provider_usage_log','idempotency_key'),
          ('public','todox_ai_provider_usage_log','render_job_id'),
          ('public','todox_ai_provider_usage_log','provider_task_id'),
          ('public','todox_ai_provider_usage_log','logical_request_id'),
          ('public','todox_ai_provider_usage_log','provider_usage_json')
      ) AS required(table_schema, table_name, column_name)
      WHERE NOT EXISTS (
          SELECT 1 FROM information_schema.columns c
           WHERE c.table_schema = required.table_schema
             AND c.table_name = required.table_name
             AND c.column_name = required.column_name
      );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Task 4 generic billing/usage columns missing: %', missing_count;
    END IF;

    SELECT count(*) INTO missing_count
      FROM (VALUES
          ('billing','ai_billing_records_logical_request_uk'),
          ('billing','ai_billing_records_provider_task_ix'),
          ('billing','ai_billing_records_reconciliation_ix'),
          ('billing','ai_provider_attempts_record_attempt_uk'),
          ('public','todox_ai_provider_usage_log_idempotency_uk'),
          ('public','todox_ai_provider_usage_log_render_ix'),
          ('public','todox_ai_provider_usage_log_provider_task_ix')
      ) AS required(schemaname, indexname)
      WHERE NOT EXISTS (
          SELECT 1 FROM pg_indexes i
           WHERE i.schemaname = required.schemaname
             AND i.indexname = required.indexname
      );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Task 4 required indexes missing: %', missing_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM public.todox_ai_provider_usage_log
     WHERE unit_type NOT IN ('credits','tokens','token_1000','request','requests','image','images','second','seconds','video_second','video_seconds','minute','minutes','fixed','usd');
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Invalid provider usage units: %', invalid_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM billing.ai_billing_records
     WHERE status NOT IN ('estimated','reserved','pending_provider','pending_reconciliation','completed','released','failed','manual_review','cancelled','insufficient','missing_payer','missing_customer','invalid');
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Invalid billing states: %', invalid_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM public.todox_ai_provider_usage_log
     WHERE provider_account_id IS NOT NULL
       AND NOT EXISTS (
           SELECT 1 FROM public.todox_ai_provider_account a
            WHERE a.id = todox_ai_provider_usage_log.provider_account_id
       );
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Orphan provider usage account links: %', invalid_count;
    END IF;

    IF to_regclass('public.todox_ai_feature_provider_route') IS NOT NULL
       OR to_regclass('dance_sell.dance_sell_provider_operations') IS NOT NULL
       OR to_regclass('public.todox_ai_operation_assets') IS NOT NULL
       OR to_regclass('public.todox_ai_operation_billing_transactions') IS NOT NULL
       OR to_regclass('billing.ai_image_billing_records') IS NOT NULL
       OR to_regclass('billing.ai_image_provider_attempts') IS NOT NULL THEN
        RAISE EXCEPTION 'Removed legacy AI runtime tables must not exist.';
    END IF;

    RAISE NOTICE 'Task 4 runtime verification passed.';
END $$;
