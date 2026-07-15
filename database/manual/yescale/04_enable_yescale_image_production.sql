-- Production default switch for YEScale image capabilities.
-- Standalone SQL, not a migration. Run only after backup and staging smoke tests.

BEGIN;

DO $$
DECLARE
    bad_count int;
    default_count int;
BEGIN
    SELECT count(*) INTO bad_count
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation')
       AND unit_cost_points <= 0;

    IF bad_count > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: YEScale image capability has unit_cost_points <= 0. Rows=%', bad_count;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM public.todox_ai_provider WHERE provider_code='yescale_task_image') THEN
        RAISE EXCEPTION 'Refusing production enable: yescale_task_image provider is missing.';
    END IF;
END $$;

UPDATE public.todox_ai_provider
   SET enabled = true, updated_by = 'manual_sql_production', updated_at = now()
 WHERE provider_code = 'yescale_task_image';

WITH caps AS (
    SELECT unnest(ARRAY[
        'avatar_generation',
        'chibi_avatar_generation',
        'character_generation',
        'image_generation',
        'scene_image_generation',
        'poster_generation',
        'thumbnail_generation'
    ]) AS capability_code
)
UPDATE public.todox_ai_provider_capability c
   SET is_default = false, updated_by = 'manual_sql_production', updated_at = now()
  FROM caps
 WHERE c.capability_code = caps.capability_code
   AND c.is_default = true;

UPDATE public.todox_ai_provider_capability
   SET enabled = true,
       is_default = (model_name = 'nano-banana-2'),
       allow_user_select = (model_name <> 'seedream-5'),
       updated_by = 'manual_sql_production',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation');

DO $$
DECLARE
    bad_defaults int;
    bad_zero int;
BEGIN
    SELECT count(*) INTO bad_defaults
      FROM (
        SELECT capability_code, count(*) FILTER (WHERE is_default) AS defaults
          FROM public.todox_ai_provider_capability
         WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation')
         GROUP BY capability_code
        HAVING count(*) FILTER (WHERE is_default) <> 1
      ) x;

    IF bad_defaults > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: each image capability must have exactly one default. Bad groups=%', bad_defaults;
    END IF;

    SELECT count(*) INTO bad_zero
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND enabled = true
       AND unit_cost_points <= 0;

    IF bad_zero > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: enabled YEScale row has zero/negative points. Rows=%', bad_zero;
    END IF;
END $$;

COMMIT;
