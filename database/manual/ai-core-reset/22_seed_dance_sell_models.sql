BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

UPDATE public.todox_ai_provider
SET enabled = true,
    updated_by = 'ai_core_reset',
    updated_at = now()
WHERE provider_code = 'kie';

WITH provider_row AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'kie'
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, model_display_name,
     unit_type, unit_cost_points, minimum_points, is_default, allow_user_select, enabled, priority,
     endpoint_path, operation_type, feature_codes, runtime_config_json, pricing_unit, config_json, pricing_json, created_by, updated_by)
SELECT id, 'kie', 'dance_sell_reference_image', 'Dance Sell Reference Image', 'gpt-image-2-image-to-image',
       'GPT Image 2 Image To Image', 'credits', 0, 0, true, true, true, 100,
       '/api/v1/jobs/createTask',
       'reference_image',
       ARRAY['dance_sell']::text[],
       '{"operation_type":"reference_image","input_urls":2,"default_aspect_ratio":"1:1","poll_endpoint":"/api/v1/jobs/recordInfo"}'::jsonb,
       'credits',
       '{"operation_type":"reference_image","input_urls":2,"default_aspect_ratio":"1:1","poll_endpoint":"/api/v1/jobs/recordInfo"}'::jsonb,
       '{"pricing_status":"requires_verification","usage_unit":"credits"}'::jsonb,
       'ai_core_reset', 'ai_core_reset'
FROM provider_row
ON CONFLICT (provider_id, capability_code, model_name) DO UPDATE
SET enabled = EXCLUDED.enabled,
    allow_user_select = EXCLUDED.allow_user_select,
    endpoint_path = EXCLUDED.endpoint_path,
    config_json = EXCLUDED.config_json,
    operation_type = EXCLUDED.operation_type,
    feature_codes = EXCLUDED.feature_codes,
    runtime_config_json = EXCLUDED.runtime_config_json,
    pricing_unit = EXCLUDED.pricing_unit,
    pricing_json = EXCLUDED.pricing_json,
    updated_by = 'ai_core_reset',
    updated_at = now();

WITH provider_row AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'kie'
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, model_display_name,
     unit_type, unit_cost_points, minimum_points, is_default, allow_user_select, enabled, priority,
     endpoint_path, operation_type, feature_codes, runtime_config_json, pricing_unit, config_json, pricing_json, created_by, updated_by)
SELECT id, 'kie', 'dance_sell_motion_video', 'Dance Sell Motion Video', 'kling-2.6/motion-control',
       'Kling 2.6 Motion Control', 'credits', 0, 0, true, true, true, 100,
       '/api/v1/jobs/createTask',
       'motion_video',
       ARRAY['dance_sell']::text[],
       '{"operation_type":"motion_video","allowed_modes":["720p"],"allowed_character_orientations":["image"],"poll_endpoint":"/api/v1/jobs/recordInfo"}'::jsonb,
       'credits',
       '{"operation_type":"motion_video","allowed_modes":["720p"],"allowed_character_orientations":["image"],"poll_endpoint":"/api/v1/jobs/recordInfo"}'::jsonb,
       '{"pricing_status":"requires_verification","usage_unit":"credits"}'::jsonb,
       'ai_core_reset', 'ai_core_reset'
FROM provider_row
ON CONFLICT (provider_id, capability_code, model_name) DO UPDATE
SET enabled = EXCLUDED.enabled,
    allow_user_select = EXCLUDED.allow_user_select,
    endpoint_path = EXCLUDED.endpoint_path,
    config_json = EXCLUDED.config_json,
    operation_type = EXCLUDED.operation_type,
    feature_codes = EXCLUDED.feature_codes,
    runtime_config_json = EXCLUDED.runtime_config_json,
    pricing_unit = EXCLUDED.pricing_unit,
    pricing_json = EXCLUDED.pricing_json,
    updated_by = 'ai_core_reset',
    updated_at = now();

DELETE FROM public.todox_ai_provider_capability c
USING public.todox_ai_provider p
WHERE p.id = c.provider_id
  AND p.provider_code = 'kie'
  AND c.capability_code = 'motion_control_video'
  AND c.model_name = 'kling-2.6/motion-control';

COMMIT;
