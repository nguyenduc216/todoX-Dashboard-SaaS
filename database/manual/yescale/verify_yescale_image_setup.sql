-- Fail-fast verification for YEScale image provider, billing, wallet, and reconciliation setup.

DO $$
DECLARE
    missing_provider int;
    duplicate_count int;
    bad_enabled_price int;
    bad_defaults int;
    bad_default_model int;
    missing_billing int;
    missing_columns int;
    missing_indexes int;
    missing_system_wallet int;
    bad_backup_selectable int;
    poster_enabled int;
    stale_pending int;
    bad_scale int;
BEGIN
    SELECT count(*) INTO missing_provider
      FROM public.todox_ai_provider
     WHERE provider_code = 'yescale_task_image';

    IF missing_provider <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one yescale_task_image provider, found %.', missing_provider;
    END IF;

    SELECT count(*) INTO duplicate_count
      FROM (
        SELECT provider_id, capability_code, model_name, count(*) AS total
          FROM public.todox_ai_provider_capability
         WHERE provider_code = 'yescale_task_image'
         GROUP BY provider_id, capability_code, model_name
        HAVING count(*) > 1
      ) d;

    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'Duplicate YEScale provider/capability/model rows found. Groups=%', duplicate_count;
    END IF;

    SELECT count(*) INTO bad_enabled_price
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND enabled = true
       AND unit_cost_points <= 0;

    IF bad_enabled_price > 0 THEN
        RAISE EXCEPTION 'Enabled YEScale rows have unit_cost_points <= 0. Rows=%', bad_enabled_price;
    END IF;

    SELECT count(*) INTO bad_defaults
      FROM (
        SELECT capability_code, count(*) FILTER (WHERE is_default) AS defaults
          FROM public.todox_ai_provider_capability
         WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','thumbnail_generation')
         GROUP BY capability_code
        HAVING count(*) FILTER (WHERE is_default) > 1
      ) x;

    IF bad_defaults > 0 THEN
        RAISE EXCEPTION 'More than one default exists for an image capability. Bad groups=%', bad_defaults;
    END IF;

    SELECT count(*) INTO bad_default_model
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','thumbnail_generation')
       AND enabled = true
       AND is_default = true
       AND model_name <> 'nano-banana-2';

    IF bad_default_model > 0 THEN
        RAISE EXCEPTION 'YEScale enabled defaults must be nano-banana-2. Rows=%', bad_default_model;
    END IF;

    SELECT count(*) INTO bad_backup_selectable
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND model_name = 'seedream-5'
       AND allow_user_select = true;

    IF bad_backup_selectable > 0 THEN
        RAISE EXCEPTION 'seedream-5 backup must not be user-selectable. Rows=%', bad_backup_selectable;
    END IF;

    SELECT count(*) INTO poster_enabled
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code = 'poster_generation'
       AND enabled = true;

    IF poster_enabled > 0 THEN
        RAISE EXCEPTION 'poster_generation must remain disabled for YEScale until composite support is routed. Rows=%', poster_enabled;
    END IF;

    SELECT count(*) INTO missing_billing
      FROM (
        VALUES
          ('billing.ai_image_billing_records'),
          ('billing.ai_image_provider_attempts')
      ) required(table_name)
      WHERE to_regclass(required.table_name) IS NULL;

    IF missing_billing > 0 THEN
        RAISE EXCEPTION 'YEScale image billing support tables are missing. Count=%', missing_billing;
    END IF;

    SELECT count(*) INTO missing_columns
      FROM (
        VALUES
          ('token_wallets','wallet_scope'),
          ('token_wallets','wallet_code'),
          ('token_wallets','overdraft_limit'),
          ('ai_image_billing_records','payer_type'),
          ('ai_image_billing_records','payer_customer_id'),
          ('ai_image_billing_records','payer_wallet_id'),
          ('ai_image_billing_records','system_charged_points'),
          ('ai_image_billing_records','reserved_until'),
          ('ai_image_billing_records','pending_reconciliation_at'),
          ('ai_image_provider_attempts','provider_estimated_cost_usd'),
          ('ai_image_provider_attempts','provider_actual_cost_usd'),
          ('ai_image_provider_attempts','cost_source'),
          ('ai_image_provider_attempts','error_code')
      ) required(table_name, column_name)
      WHERE NOT EXISTS (
          SELECT 1
            FROM information_schema.columns c
           WHERE c.table_schema = 'billing'
             AND c.table_name = required.table_name
             AND c.column_name = required.column_name
      );

    IF missing_columns > 0 THEN
        RAISE EXCEPTION 'YEScale billing required columns are missing. Count=%', missing_columns;
    END IF;

    SELECT count(*) INTO missing_indexes
      FROM (
        VALUES
          ('token_wallets_customer_scope_uk'),
          ('token_wallets_system_code_uk'),
          ('ai_image_provider_attempts_record_attempt_uk'),
          ('ai_image_billing_records_reconciliation_ix')
      ) required(index_name)
      WHERE NOT EXISTS (
          SELECT 1 FROM pg_indexes
           WHERE schemaname = 'billing'
             AND indexname = required.index_name
      );

    IF missing_indexes > 0 THEN
        RAISE EXCEPTION 'YEScale billing required indexes are missing. Count=%', missing_indexes;
    END IF;

    SELECT count(*) INTO missing_system_wallet
      FROM billing.token_wallets
     WHERE wallet_scope = 'system'
       AND wallet_code = 'TODOX_AI_IMAGE_SYSTEM';

    IF missing_system_wallet <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one TODOX_AI_IMAGE_SYSTEM wallet, found %.', missing_system_wallet;
    END IF;

    SELECT count(*) INTO bad_scale
      FROM information_schema.columns
     WHERE table_schema = 'billing'
       AND table_name IN ('ai_image_billing_records','ai_image_provider_attempts','token_wallets')
       AND column_name IN ('customer_charged_points','system_charged_points','provider_cost_points','provider_estimated_cost_usd','provider_actual_cost_usd','balance','locked_balance')
       AND numeric_scale < 4;

    IF bad_scale > 0 THEN
        RAISE EXCEPTION 'Billing numeric scale must support 0.0192 points. Bad columns=%', bad_scale;
    END IF;

    SELECT count(*) INTO stale_pending
      FROM billing.ai_image_billing_records
     WHERE (status = 'reserved' AND reserved_until < now())
        OR (status = 'pending_reconciliation' AND pending_reconciliation_at < now() - interval '2 hours');

    IF stale_pending > 0 THEN
        RAISE EXCEPTION 'Stale image billing reservations/reconciliation rows require manual handling before production enable. Rows=%', stale_pending;
    END IF;
END $$;

SELECT provider_code, provider_name, enabled, api_key_config_name
  FROM public.todox_ai_provider
 WHERE provider_code = 'yescale_task_image';

SELECT capability_code,
       count(*) FILTER (WHERE is_default) AS default_count,
       count(*) FILTER (WHERE enabled AND unit_cost_points <= 0) AS enabled_zero_price_count,
       string_agg(model_name || ':' || enabled || ':' || is_default || ':' || unit_cost_points, ', ' ORDER BY model_name) AS models
  FROM public.todox_ai_provider_capability
 WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation')
 GROUP BY capability_code
 ORDER BY capability_code;

SELECT wallet_scope, wallet_code, balance, locked_balance, overdraft_limit, low_balance_threshold, status
  FROM billing.token_wallets
 WHERE wallet_scope = 'system'
 ORDER BY wallet_code;

SELECT status, count(*) AS rows, sum(customer_charged_points) AS customer_points, sum(system_charged_points) AS system_points
  FROM billing.ai_image_billing_records
 GROUP BY status
 ORDER BY status;

SELECT table_schema, table_name
  FROM information_schema.tables
 WHERE table_schema = 'billing'
   AND table_name IN ('ai_image_billing_records','ai_image_provider_attempts','yescale_image_default_snapshot')
 ORDER BY table_name;
