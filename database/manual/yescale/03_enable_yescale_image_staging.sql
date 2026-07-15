-- Enable YEScale image capabilities in staging only.
-- Standalone SQL, not a migration. Review before running.
-- poster_generation is intentionally excluded because fixed composite still bypasses the shared router.

BEGIN;

DO $$
DECLARE
    expected_models int;
    bad_count int;
BEGIN
    IF to_regclass('billing.ai_image_billing_records') IS NULL THEN
        RAISE EXCEPTION 'Run 02_add_yescale_billing_support.sql before enabling YEScale image staging.';
    END IF;

    SELECT count(*) INTO expected_models
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
       AND model_name IN ('nano-banana-2','gpt-image','seedream-5');

    IF expected_models <> 15 THEN
        RAISE EXCEPTION 'Expected 15 YEScale staging rows (5 billed routed capabilities x 3 models), found %.', expected_models;
    END IF;

    SELECT count(*) INTO bad_count
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
       AND unit_cost_points <= 0;

    IF bad_count > 0 THEN
        RAISE EXCEPTION 'Refusing to enable YEScale image capabilities with unit_cost_points <= 0. Rows=%', bad_count;
    END IF;
END $$;

UPDATE public.todox_ai_provider
   SET enabled = true, updated_by = 'manual_sql_staging', updated_at = now()
 WHERE provider_code = 'yescale_task_image';

UPDATE public.todox_ai_provider_capability
   SET enabled = true,
       is_default = false,
       allow_user_select = (model_name <> 'seedream-5'),
       updated_by = 'manual_sql_staging',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation');

UPDATE public.todox_ai_provider_capability
   SET enabled = false,
       is_default = false,
       updated_by = 'manual_sql_staging',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('character_generation','poster_generation');

COMMIT;
