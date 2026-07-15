-- YEScale image provider seed for TodoX.
-- Queried from YEScale MCP on 2026-07-15T16:40:14.893Z UTC:
--   yescale_list_models, yescale_get_model_doc
--
-- Safety:
-- - This is a standalone manual SQL file, not a migration.
-- - It does not store YEScale credentials.
-- - Provider and capabilities are disabled by default.
-- - Review unit_cost_points, enable the provider/capabilities, then set a default manually.
--
-- Rollback without deleting history:
--   UPDATE public.todox_ai_provider_capability
--      SET enabled = false, is_default = false, updated_at = now()
--    WHERE provider_code = 'yescale_task_image';
--   UPDATE public.todox_ai_provider
--      SET enabled = false, updated_at = now()
--    WHERE provider_code = 'yescale_task_image';

BEGIN;

INSERT INTO public.todox_ai_provider
    (provider_code, provider_name, provider_type, base_url, api_key_config_name,
     enabled, is_system, priority, description, config_json, created_by, updated_by, created_at, updated_at)
SELECT
    'yescale_task_image',
    'YEScale Task Image',
    'external_api',
    'https://api.yescale.io',
    'AiProviders__YEScale__AccessKey',
    false,
    true,
    40,
    'YEScale async task image generation provider. Enable only after configuring AiProviders__YEScale__AccessKey.',
    '{
       "protocol": "yescale_task",
       "submit_endpoint": "/task/submit",
       "poll_endpoint_template": "/task/{task_id}",
       "poll_terminal_statuses": ["SUCCESS", "FAILURE"],
       "poll_interval_seconds": 5,
       "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
     }'::jsonb,
    'manual_sql',
    'manual_sql',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_image'
);

UPDATE public.todox_ai_provider
   SET provider_name = 'YEScale Task Image',
       provider_type = 'external_api',
       base_url = 'https://api.yescale.io',
       api_key_config_name = 'AiProviders__YEScale__AccessKey',
       priority = 40,
       description = 'YEScale async task image generation provider. Enable only after configuring AiProviders__YEScale__AccessKey.',
       config_json = '{
         "protocol": "yescale_task",
         "submit_endpoint": "/task/submit",
         "poll_endpoint_template": "/task/{task_id}",
         "poll_terminal_statuses": ["SUCCESS", "FAILURE"],
         "poll_interval_seconds": 5,
         "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
       }'::jsonb,
       updated_by = 'manual_sql',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image';

WITH provider AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_image'
),
desired AS (
    SELECT
        p.id AS provider_id,
        'yescale_task_image'::text AS provider_code,
        'image_generation'::text AS capability_code,
        'YEScale Image Default - nano-banana-2'::text AS display_name,
        'nano-banana-2'::text AS model_name,
        '/task/submit'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        false::boolean AS is_default,
        false::boolean AS enabled,
        true::boolean AS allow_user_select,
        '{
          "routing_role": "default",
          "adapter_profile": "nano_banana_2",
          "size": "1K",
          "google_search": "disable",
          "thinking": "minimal",
          "fallback_models": ["seedream-5"],
          "model_profiles": {
            "seedream-5": "seedream_5"
          },
          "model_sizes": {
            "seedream-5": "2K"
          },
          "verified_input_modalities": ["text", "image"],
          "verified_output_modalities": ["image"],
          "verified_endpoint": "/task/submit",
          "verified_async": true,
          "verified_price_usd_per_request": "0.06-0.20",
          "verified_throughput": 105,
          "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
        }'::jsonb AS config_json
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_image',
        'image_generation',
        'YEScale Image Cheap - gpt-image',
        'gpt-image',
        '/task/submit',
        'request',
        0::numeric,
        false,
        false,
        true,
        '{
          "routing_role": "cheap",
          "adapter_profile": "gpt_image",
          "size": "1024x1024",
          "quality": "low",
          "background": "transparent",
          "verified_input_modalities": ["text", "image"],
          "verified_output_modalities": ["image"],
          "verified_endpoint": "/task/submit",
          "verified_async": true,
          "verified_price_usd_per_request": "0.024-0.30",
          "verified_throughput": 95,
          "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
        }'::jsonb
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_image',
        'image_generation',
        'YEScale Image Backup - seedream-5',
        'seedream-5',
        '/task/submit',
        'request',
        0::numeric,
        false,
        false,
        true,
        '{
          "routing_role": "backup",
          "adapter_profile": "seedream_5",
          "size": "2K",
          "verified_input_modalities": ["text", "image"],
          "verified_output_modalities": ["image"],
          "verified_endpoint": "/task/submit",
          "verified_async": true,
          "verified_price_usd_per_request": "0.065",
          "verified_throughput": 90,
          "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
        }'::jsonb
    FROM provider p
)
UPDATE public.todox_ai_provider_capability c
   SET display_name = d.display_name,
       endpoint_path = d.endpoint_path,
       unit_type = d.unit_type,
       unit_cost_points = d.unit_cost_points,
       allow_user_select = d.allow_user_select,
       config_json = d.config_json,
       updated_by = 'manual_sql',
       updated_at = now()
  FROM desired d
 WHERE c.provider_id = d.provider_id
   AND c.capability_code = d.capability_code
   AND c.model_name = d.model_name;

WITH provider AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_image'
),
desired AS (
    SELECT
        p.id AS provider_id,
        'yescale_task_image'::text AS provider_code,
        'image_generation'::text AS capability_code,
        'YEScale Image Default - nano-banana-2'::text AS display_name,
        'nano-banana-2'::text AS model_name,
        '/task/submit'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        false::boolean AS is_default,
        false::boolean AS enabled,
        true::boolean AS allow_user_select,
        '{
          "routing_role": "default",
          "adapter_profile": "nano_banana_2",
          "size": "1K",
          "google_search": "disable",
          "thinking": "minimal",
          "fallback_models": ["seedream-5"],
          "model_profiles": {
            "seedream-5": "seedream_5"
          },
          "model_sizes": {
            "seedream-5": "2K"
          },
          "verified_input_modalities": ["text", "image"],
          "verified_output_modalities": ["image"],
          "verified_endpoint": "/task/submit",
          "verified_async": true,
          "verified_price_usd_per_request": "0.06-0.20",
          "verified_throughput": 105,
          "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
        }'::jsonb AS config_json
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_image',
        'image_generation',
        'YEScale Image Cheap - gpt-image',
        'gpt-image',
        '/task/submit',
        'request',
        0::numeric,
        false,
        false,
        true,
        '{
          "routing_role": "cheap",
          "adapter_profile": "gpt_image",
          "size": "1024x1024",
          "quality": "low",
          "background": "transparent",
          "verified_input_modalities": ["text", "image"],
          "verified_output_modalities": ["image"],
          "verified_endpoint": "/task/submit",
          "verified_async": true,
          "verified_price_usd_per_request": "0.024-0.30",
          "verified_throughput": 95,
          "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
        }'::jsonb
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_image',
        'image_generation',
        'YEScale Image Backup - seedream-5',
        'seedream-5',
        '/task/submit',
        'request',
        0::numeric,
        false,
        false,
        true,
        '{
          "routing_role": "backup",
          "adapter_profile": "seedream_5",
          "size": "2K",
          "verified_input_modalities": ["text", "image"],
          "verified_output_modalities": ["image"],
          "verified_endpoint": "/task/submit",
          "verified_async": true,
          "verified_price_usd_per_request": "0.065",
          "verified_throughput": 90,
          "mcp_verified_at_utc": "2026-07-15T16:40:14.893Z"
        }'::jsonb
    FROM provider p
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select,
     config_json, created_by, updated_by, created_at, updated_at)
SELECT
    d.provider_id, d.provider_code, d.capability_code, d.display_name, d.model_name, d.endpoint_path,
    d.unit_type, d.unit_cost_points, d.is_default, d.enabled, d.allow_user_select,
    d.config_json, 'manual_sql', 'manual_sql', now(), now()
FROM desired d
WHERE NOT EXISTS (
    SELECT 1
      FROM public.todox_ai_provider_capability c
     WHERE c.provider_id = d.provider_id
       AND c.capability_code = d.capability_code
       AND c.model_name = d.model_name
);

SELECT provider_code, provider_name, enabled, api_key_config_name
  FROM public.todox_ai_provider
 WHERE provider_code = 'yescale_task_image';

SELECT provider_code, capability_code, display_name, model_name, endpoint_path,
       unit_type, unit_cost_points, is_default, enabled, allow_user_select,
       config_json ->> 'routing_role' AS routing_role,
       config_json ->> 'adapter_profile' AS adapter_profile
  FROM public.todox_ai_provider_capability
 WHERE provider_code = 'yescale_task_image'
 ORDER BY display_name;

COMMIT;
