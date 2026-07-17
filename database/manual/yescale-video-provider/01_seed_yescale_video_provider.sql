\set ON_ERROR_STOP on

BEGIN;

UPDATE public.todox_ai_provider
   SET provider_name = 'YEScale Task Video',
       provider_type = 'external_api',
       base_url = 'https://api.yescale.io',
       api_key_config_name = 'AiProviders__YEScale__AccessKey',
       priority = 45,
       description = 'YEScale async task video provider. Runtime credential must come from environment or secret store.',
       config_json = '{
          "protocol": "yescale_task",
          "submit_endpoint": "/task/submit",
          "poll_endpoint_template": "/task/{task_id}",
          "poll_terminal_statuses": ["SUCCESS", "FAILURE"],
          "mcp_verified_at_utc": "2026-07-17T18:12:49.501Z"
        }'::jsonb,
       updated_by = 'manual_sql',
       updated_at = now()
 WHERE provider_code = 'yescale_task_video';

INSERT INTO public.todox_ai_provider
    (provider_code, provider_name, provider_type, base_url, api_key_config_name,
     enabled, is_system, priority, description, config_json, created_by, updated_by, created_at, updated_at)
SELECT
    'yescale_task_video', 'YEScale Task Video', 'external_api', 'https://api.yescale.io',
    'AiProviders__YEScale__AccessKey', true, true, 45,
    'YEScale async task video provider. Runtime credential must come from environment or secret store.',
    '{
       "protocol": "yescale_task",
       "submit_endpoint": "/task/submit",
       "poll_endpoint_template": "/task/{task_id}",
       "poll_terminal_statuses": ["SUCCESS", "FAILURE"],
       "mcp_verified_at_utc": "2026-07-17T18:12:49.501Z"
     }'::jsonb,
    'manual_sql', 'manual_sql', now(), now()
WHERE NOT EXISTS (
    SELECT 1 FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_video'
);

WITH provider AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_video'
),
desired AS (
    SELECT
        p.id AS provider_id,
        'yescale_task_video'::text AS provider_code,
        'image_to_video'::text AS capability_code,
        'YEScale image_to_video / grok-video'::text AS display_name,
        'grok-video'::text AS model_name,
        '/task/submit'::text AS endpoint_path,
        'request'::text AS unit_type,
        0.14 * 0.8::numeric AS unit_cost_points,
        false::boolean AS is_default,
        true::boolean AS enabled,
        true::boolean AS allow_user_select,
        '{
           "provider_estimated_cost_usd": 0.14,
           "aspect_ratios": ["2:3","3:2","16:9","9:16","1:1"],
           "durations": [4,6,8,10,12,15],
           "sizes": ["720P","1080P"],
           "cost_source": "configured_tariff",
           "mcp_verified_at_utc": "2026-07-17T18:12:48.769Z"
         }'::jsonb AS config_json
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_video',
        'image_to_video',
        'YEScale image_to_video / grok-video-1.5',
        'grok-video-1.5',
        '/task/submit',
        'request',
        0.20 * 0.8::numeric,
        false,
        true,
        true,
        '{
           "provider_estimated_cost_usd": 0.20,
           "aspect_ratios": ["16:9","9:16"],
           "durations": [4,6,8,10,12,15],
           "sizes": ["720P"],
           "cost_source": "configured_tariff",
           "mcp_verified_at_utc": "2026-07-17T18:12:48.769Z"
         }'::jsonb
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_video',
        'image_to_video',
        'YEScale image_to_video / omni-flash',
        'omni-flash',
        '/task/submit',
        'request',
        0.37 * 0.8::numeric,
        false,
        true,
        true,
        '{
           "provider_estimated_cost_usd": 0.37,
           "aspect_ratios": ["16:9","9:16"],
           "mode": "i2v(img_ref)",
           "supported_modes": ["t2v","i2v(img_ref)","i2v(first_last_frame)","v2v"],
           "cost_source": "configured_tariff",
           "mcp_verified_at_utc": "2026-07-17T18:12:49.501Z"
         }'::jsonb
    FROM provider p
)
UPDATE public.todox_ai_provider_capability c
   SET display_name = d.display_name,
       endpoint_path = d.endpoint_path,
       unit_type = d.unit_type,
       unit_cost_points = d.unit_cost_points,
       enabled = d.enabled,
       allow_user_select = d.allow_user_select,
       config_json = d.config_json,
       updated_by = 'manual_sql',
       updated_at = now()
  FROM desired d
 WHERE c.provider_id = d.provider_id
   AND c.capability_code = d.capability_code
   AND c.model_name = d.model_name;

WITH provider AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_video'
),
desired AS (
    SELECT
        p.id AS provider_id,
        'yescale_task_video'::text AS provider_code,
        'image_to_video'::text AS capability_code,
        'YEScale image_to_video / grok-video'::text AS display_name,
        'grok-video'::text AS model_name,
        '/task/submit'::text AS endpoint_path,
        'request'::text AS unit_type,
        0.14 * 0.8::numeric AS unit_cost_points,
        false::boolean AS is_default,
        true::boolean AS enabled,
        true::boolean AS allow_user_select,
        '{
           "provider_estimated_cost_usd": 0.14,
           "aspect_ratios": ["2:3","3:2","16:9","9:16","1:1"],
           "durations": [4,6,8,10,12,15],
           "sizes": ["720P","1080P"],
           "cost_source": "configured_tariff",
           "mcp_verified_at_utc": "2026-07-17T18:12:48.769Z"
         }'::jsonb AS config_json
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_video',
        'image_to_video',
        'YEScale image_to_video / grok-video-1.5',
        'grok-video-1.5',
        '/task/submit',
        'request',
        0.20 * 0.8::numeric,
        false,
        true,
        true,
        '{
           "provider_estimated_cost_usd": 0.20,
           "aspect_ratios": ["16:9","9:16"],
           "durations": [4,6,8,10,12,15],
           "sizes": ["720P"],
           "cost_source": "configured_tariff",
           "mcp_verified_at_utc": "2026-07-17T18:12:48.769Z"
         }'::jsonb
    FROM provider p
    UNION ALL
    SELECT
        p.id,
        'yescale_task_video',
        'image_to_video',
        'YEScale image_to_video / omni-flash',
        'omni-flash',
        '/task/submit',
        'request',
        0.37 * 0.8::numeric,
        false,
        true,
        true,
        '{
           "provider_estimated_cost_usd": 0.37,
           "aspect_ratios": ["16:9","9:16"],
           "mode": "i2v(img_ref)",
           "supported_modes": ["t2v","i2v(img_ref)","i2v(first_last_frame)","v2v"],
           "cost_source": "configured_tariff",
           "mcp_verified_at_utc": "2026-07-17T18:12:49.501Z"
         }'::jsonb
    FROM provider p
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select,
     config_json, created_by, updated_by, created_at, updated_at)
SELECT d.provider_id, d.provider_code, d.capability_code, d.display_name, d.model_name, d.endpoint_path,
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

COMMIT;
