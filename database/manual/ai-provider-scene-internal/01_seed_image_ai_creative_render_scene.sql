-- Standalone data SQL. Do not run automatically.
-- Seeds/updates the internal ImageAICreativeRender provider capability for scene images.
-- This does not change the current default. Admin should choose it through AI Providers quick defaults.

BEGIN;

DO $$
BEGIN
    IF to_regclass('public.todox_ai_provider') IS NULL THEN
        RAISE EXCEPTION 'Missing table public.todox_ai_provider.';
    END IF;

    IF to_regclass('public.todox_ai_provider_capability') IS NULL THEN
        RAISE EXCEPTION 'Missing table public.todox_ai_provider_capability.';
    END IF;
END $$;

WITH upsert_provider AS (
    UPDATE public.todox_ai_provider
       SET provider_name = 'ImageAICreativeRender',
           provider_type = 'internal_api',
           base_url = NULL,
           api_key_config_name = NULL,
           enabled = true,
           is_system = true,
           priority = LEAST(priority, 30),
           description = 'Internal TodoX ImageAICreativeRender provider. No external API key is stored here.',
           config_json = COALESCE(config_json, '{}'::jsonb) || '{
             "factory_key": "todox_image",
             "internal_engine": "ImageAICreativeRender",
             "managed_by": "manual_sql"
           }'::jsonb,
           updated_by = 'manual_sql',
           updated_at = now()
     WHERE provider_code IN ('image_ai_creative_render', 'todox_image')
 RETURNING id, provider_code
),
insert_provider AS (
    INSERT INTO public.todox_ai_provider
        (provider_code, provider_name, provider_type, base_url, api_key_config_name,
         enabled, is_system, priority, description, config_json, created_by, updated_by, created_at, updated_at)
    SELECT 'image_ai_creative_render',
           'ImageAICreativeRender',
           'internal_api',
           NULL,
           NULL,
           true,
           true,
           30,
           'Internal TodoX ImageAICreativeRender provider. No external API key is stored here.',
           '{
             "factory_key": "todox_image",
             "internal_engine": "ImageAICreativeRender",
             "managed_by": "manual_sql"
           }'::jsonb,
           'manual_sql',
           'manual_sql',
           now(),
           now()
     WHERE NOT EXISTS (
           SELECT 1
             FROM public.todox_ai_provider
            WHERE provider_code IN ('image_ai_creative_render', 'todox_image')
     )
 RETURNING id, provider_code
),
provider AS (
    SELECT id, provider_code FROM upsert_provider
    UNION ALL
    SELECT id, provider_code FROM insert_provider
    ORDER BY CASE provider_code WHEN 'image_ai_creative_render' THEN 0 ELSE 1 END
    LIMIT 1
),
desired AS (
    SELECT p.id AS provider_id,
           p.provider_code,
           'scene_image_generation'::text AS capability_code,
           'ImageAICreativeRender Scene Image Generation'::text AS display_name,
           'internal_default'::text AS model_name,
           NULL::text AS endpoint_path,
           'image'::text AS unit_type,
           3::numeric AS unit_cost_points,
           false::boolean AS is_default,
           true::boolean AS enabled,
           true::boolean AS allow_user_select,
           '{
             "factory_key": "todox_image",
             "internal_engine": "ImageAICreativeRender",
             "scenario": "video_scene",
             "cost_source": "configured_internal_tariff",
             "tariff_note": "Matches existing internal ImageAICreativeRender image tariff verified from current TodoX configuration.",
             "managed_by": "manual_sql"
           }'::jsonb AS config_json
      FROM provider p
)
UPDATE public.todox_ai_provider_capability c
   SET display_name = d.display_name,
       endpoint_path = d.endpoint_path,
       unit_type = d.unit_type,
       unit_cost_points = d.unit_cost_points,
       enabled = d.enabled,
       allow_user_select = d.allow_user_select,
       config_json = COALESCE(c.config_json, '{}'::jsonb) || d.config_json,
       updated_by = 'manual_sql',
       updated_at = now()
  FROM desired d
 WHERE c.provider_id = d.provider_id
   AND c.capability_code = d.capability_code
   AND c.model_name = d.model_name;

WITH provider AS (
    SELECT id, provider_code
      FROM public.todox_ai_provider
     WHERE provider_code IN ('image_ai_creative_render', 'todox_image')
     ORDER BY CASE provider_code WHEN 'image_ai_creative_render' THEN 0 ELSE 1 END
     LIMIT 1
),
desired AS (
    SELECT p.id AS provider_id,
           p.provider_code,
           'scene_image_generation'::text AS capability_code,
           'ImageAICreativeRender Scene Image Generation'::text AS display_name,
           'internal_default'::text AS model_name,
           NULL::text AS endpoint_path,
           'image'::text AS unit_type,
           3::numeric AS unit_cost_points,
           false::boolean AS is_default,
           true::boolean AS enabled,
           true::boolean AS allow_user_select,
           '{
             "factory_key": "todox_image",
             "internal_engine": "ImageAICreativeRender",
             "scenario": "video_scene",
             "cost_source": "configured_internal_tariff",
             "tariff_note": "Matches existing internal ImageAICreativeRender image tariff verified from current TodoX configuration.",
             "managed_by": "manual_sql"
           }'::jsonb AS config_json
      FROM provider p
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select,
     config_json, created_by, updated_by, created_at, updated_at)
SELECT d.provider_id,
       d.provider_code,
       d.capability_code,
       d.display_name,
       d.model_name,
       d.endpoint_path,
       d.unit_type,
       d.unit_cost_points,
       d.is_default,
       d.enabled,
       d.allow_user_select,
       d.config_json,
       'manual_sql',
       'manual_sql',
       now(),
       now()
  FROM desired d
 WHERE NOT EXISTS (
       SELECT 1
         FROM public.todox_ai_provider_capability c
        WHERE c.provider_id = d.provider_id
          AND c.capability_code = d.capability_code
          AND c.model_name = d.model_name
 );

DO $$
DECLARE
    default_count int;
BEGIN
    SELECT count(*) INTO default_count
      FROM public.todox_ai_provider_capability
     WHERE capability_code = 'scene_image_generation'
       AND is_default = true;

    IF default_count > 1 THEN
        RAISE EXCEPTION 'More than one scene_image_generation default exists after seed: %.', default_count;
    END IF;
END $$;

COMMIT;
