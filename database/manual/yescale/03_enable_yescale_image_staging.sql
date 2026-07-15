-- Enable YEScale image capabilities in staging only.
-- Standalone SQL, not a migration. Review before running.

BEGIN;

DO $$
DECLARE
    bad_count int;
BEGIN
    SELECT count(*) INTO bad_count
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation')
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
       updated_by = 'manual_sql_staging',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation');

COMMIT;
