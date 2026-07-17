-- Standalone rollback SQL. Do not run automatically.
-- Removes only the manually seeded scene_image_generation capability row.
-- It leaves the provider row in place because it may be used by avatar/character paths.

BEGIN;

UPDATE public.todox_ai_provider_capability
   SET is_default = false,
       enabled = false,
       allow_user_select = false,
       updated_by = 'manual_sql_rollback',
       updated_at = now()
 WHERE capability_code = 'scene_image_generation'
   AND model_name = 'internal_default'
   AND provider_code IN ('image_ai_creative_render', 'todox_image')
   AND config_json ->> 'managed_by' = 'manual_sql';

COMMIT;
