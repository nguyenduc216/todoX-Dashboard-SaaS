DO $$
DECLARE
    invalid_count integer;
    missing_count integer;
BEGIN
    IF current_database() <> 'todo_saas' THEN
        RAISE EXCEPTION 'Safety stop: connected database is %, expected todo_saas.', current_database();
    END IF;

    SELECT count(*) INTO missing_count
      FROM (VALUES
          ('billing','ai_billing_records_logical_request_uk'),
          ('billing','token_transactions_ai_reserve_once_uk'),
          ('billing','token_transactions_ai_charge_once_uk'),
          ('billing','token_transactions_ai_refund_once_uk'),
          ('render','render_artifacts_job_type_url_phase5_1_uk'),
          ('public','todox_ai_provider_usage_log_idempotency_phase5_1_uk'),
          ('public','provider_account_lease_active_phase5_1_ix')
      ) AS required(schemaname, indexname)
      WHERE NOT EXISTS (
          SELECT 1
            FROM pg_indexes i
           WHERE i.schemaname = required.schemaname
             AND i.indexname = required.indexname
      );
    IF missing_count <> 0 THEN
        RAISE EXCEPTION 'Phase 5.1 required indexes missing: %', missing_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM billing.ai_billing_records
     WHERE refunded_points > charged_points;
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Refunded points exceed charged points: %', invalid_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM billing.ai_billing_records
     WHERE status IN ('completed','released','failed','cancelled')
       AND completed_at IS NULL
       AND status = 'completed';
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Completed billing records missing completed_at: %', invalid_count;
    END IF;

    SELECT count(*) INTO invalid_count
     FROM public.todox_ai_provider_account_lease
     WHERE lease_status = 'active'
       AND lease_until < now();
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Expired active provider leases need watchdog cleanup: %', invalid_count;
    END IF;

    SELECT count(*) INTO invalid_count
      FROM public.todox_ai_provider_account_credential
     WHERE COALESCE(credential_key, credential_config_name, '') ~ '(?i)(sk-[a-z0-9]|AIza|Bearer\s+\S+|-----BEGIN)';
    IF invalid_count <> 0 THEN
        RAISE EXCEPTION 'Provider credential table contains direct secret material: %', invalid_count;
    END IF;

    RAISE NOTICE 'Phase 5.1 production-readiness SQL verification passed.';
END $$;
