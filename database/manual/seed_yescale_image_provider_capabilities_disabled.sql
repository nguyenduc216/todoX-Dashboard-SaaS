-- YEScale image provider capability seed for TodoX.
-- Standalone manual SQL, not a migration. Do not run against production without review.
-- MCP queried at UTC: 2026-07-15T17:58:32.0838982Z
-- Tools: yescale_list_models, yescale_get_model_doc, yescale_get_task_model_config
--
-- Safety:
-- - Does not store credentials.
-- - Keeps provider and capabilities disabled.
-- - Keeps is_default=false and unit_cost_points=0 until business pricing is approved.
-- - User must configure AiProviders__YEScale__AccessKey outside source control.

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
       "mcp_verified_at_utc": "2026-07-15T17:58:32.0838982Z"
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
         "mcp_verified_at_utc": "2026-07-15T17:58:32.0838982Z"
       }'::jsonb,
       updated_by = 'manual_sql',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image';

WITH provider AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_image'
),
capabilities AS (
    SELECT unnest(ARRAY[
        'avatar_generation',
        'character_generation',
        'image_generation',
        'poster_generation',
        'thumbnail_generation'
    ]) AS capability_code
),
models AS (
    SELECT
        'default'::text AS routing_role,
        'YEScale Image Default - nano-banana-2'::text AS display_name_suffix,
        'nano-banana-2'::text AS model_name,
        'nano_banana_2'::text AS adapter_profile,
        '1K'::text AS size,
        '{
          "google_search": "disable",
          "thinking": "minimal",
          "fallback_models": ["seedream-5"],
          "transient_terminal_error_codes": [],
          "model_profiles": {"seedream-5": "seedream_5"},
          "model_sizes": {"seedream-5": "2K"}
        }'::jsonb AS extra_config
    UNION ALL
    SELECT
        'cheap',
        'YEScale Image Cheap - gpt-image',
        'gpt-image',
        'gpt_image',
        '1024x1024',
        '{"quality": "low", "background": "transparent"}'::jsonb
    UNION ALL
    SELECT
        'backup',
        'YEScale Image Backup - seedream-5',
        'seedream-5',
        'seedream_5',
        '2K',
        '{}'::jsonb
),
desired AS (
    SELECT
        p.id AS provider_id,
        'yescale_task_image'::text AS provider_code,
        c.capability_code,
        m.display_name_suffix || ' / ' || c.capability_code AS display_name,
        m.model_name,
        '/task/submit'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        false::boolean AS is_default,
        false::boolean AS enabled,
        true::boolean AS allow_user_select,
        jsonb_build_object(
            'routing_role', m.routing_role,
            'adapter_profile', m.adapter_profile,
            'size', m.size,
            'verified_input_modalities', jsonb_build_array('text', 'image'),
            'verified_output_modalities', jsonb_build_array('image'),
            'verified_endpoint', '/task/submit',
            'verified_async', true,
            'mcp_verified_at_utc', '2026-07-15T17:58:32.0838982Z'
        ) || m.extra_config AS config_json
    FROM provider p
    CROSS JOIN capabilities c
    CROSS JOIN models m
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

WITH desired AS (
    SELECT
        c.id,
        d.display_name,
        d.endpoint_path,
        d.unit_type,
        d.unit_cost_points,
        d.allow_user_select,
        d.config_json
    FROM public.todox_ai_provider_capability c
    JOIN public.todox_ai_provider p ON p.id = c.provider_id
    JOIN (
        SELECT *
        FROM (
            SELECT unnest(ARRAY[
                'avatar_generation',
                'character_generation',
                'image_generation',
                'poster_generation',
                'thumbnail_generation'
            ]) AS capability_code
        ) caps
        CROSS JOIN (
            VALUES
                ('default', 'YEScale Image Default - nano-banana-2', 'nano-banana-2', 'nano_banana_2', '1K', '{"google_search":"disable","thinking":"minimal","fallback_models":["seedream-5"],"transient_terminal_error_codes":[],"model_profiles":{"seedream-5":"seedream_5"},"model_sizes":{"seedream-5":"2K"}}'::jsonb),
                ('cheap', 'YEScale Image Cheap - gpt-image', 'gpt-image', 'gpt_image', '1024x1024', '{"quality":"low","background":"transparent"}'::jsonb),
                ('backup', 'YEScale Image Backup - seedream-5', 'seedream-5', 'seedream_5', '2K', '{}'::jsonb)
        ) models(routing_role, display_name_suffix, model_name, adapter_profile, size, extra_config)
    ) seed ON seed.capability_code = c.capability_code AND seed.model_name = c.model_name
    CROSS JOIN LATERAL (
        SELECT
            seed.display_name_suffix || ' / ' || seed.capability_code AS display_name,
            '/task/submit'::text AS endpoint_path,
            'request'::text AS unit_type,
            0::numeric AS unit_cost_points,
            true::boolean AS allow_user_select,
            jsonb_build_object(
                'routing_role', seed.routing_role,
                'adapter_profile', seed.adapter_profile,
                'size', seed.size,
                'verified_input_modalities', jsonb_build_array('text', 'image'),
                'verified_output_modalities', jsonb_build_array('image'),
                'verified_endpoint', '/task/submit',
                'verified_async', true,
                'mcp_verified_at_utc', '2026-07-15T17:58:32.0838982Z'
            ) || seed.extra_config AS config_json
    ) d
    WHERE p.provider_code = 'yescale_task_image'
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
 WHERE c.id = d.id;

SELECT provider_code, provider_name, enabled, api_key_config_name
  FROM public.todox_ai_provider
 WHERE provider_code = 'yescale_task_image';

SELECT provider_code, capability_code, display_name, model_name, endpoint_path,
       unit_type, unit_cost_points, is_default, enabled, allow_user_select,
       config_json ->> 'routing_role' AS routing_role,
       config_json ->> 'adapter_profile' AS adapter_profile
  FROM public.todox_ai_provider_capability
 WHERE provider_code = 'yescale_task_image'
 ORDER BY capability_code, display_name;

COMMIT;
