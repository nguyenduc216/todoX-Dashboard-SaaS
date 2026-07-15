-- Fail-fast verification for YEScale image provider setup.

DO $$
DECLARE
    missing_provider int;
    duplicate_count int;
    bad_enabled_price int;
    bad_defaults int;
    bad_default_model int;
    missing_billing int;
    bad_backup_selectable int;
    poster_enabled int;
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
         WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
         GROUP BY capability_code
        HAVING count(*) FILTER (WHERE is_default) > 1
      ) x;

    IF bad_defaults > 0 THEN
        RAISE EXCEPTION 'More than one default exists for an image capability. Bad groups=%', bad_defaults;
    END IF;

    SELECT count(*) INTO bad_default_model
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
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
       AND capability_code IN ('character_generation','poster_generation')
       AND enabled = true;

    IF poster_enabled > 0 THEN
        RAISE EXCEPTION 'character_generation/poster_generation must remain disabled for YEScale until routed billing/composite support is complete. Rows=%', poster_enabled;
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

SELECT table_schema, table_name
  FROM information_schema.tables
 WHERE table_schema = 'billing'
   AND table_name IN ('ai_image_billing_records','ai_image_provider_attempts','yescale_image_default_snapshot')
 ORDER BY table_name;
