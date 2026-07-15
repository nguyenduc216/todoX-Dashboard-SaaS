-- Standalone SQL, not a migration.
-- Seeds YEScale image provider/capabilities disabled by default.
-- MCP verified at UTC: 2026-07-15T18:53:03.6516197Z

BEGIN;

INSERT INTO public.todox_ai_provider
    (provider_code, provider_name, provider_type, base_url, api_key_config_name,
     enabled, is_system, priority, description, config_json, created_by, updated_by, created_at, updated_at)
VALUES
    ('yescale_task_image', 'YEScale Task Image', 'external_api', 'https://api.yescale.io',
     'AiProviders__YEScale__AccessKey', false, true, 40,
     'YEScale async task image provider. Credentials must come from secret store/environment.',
     '{
        "protocol": "yescale_task",
        "submit_endpoint": "/task/submit",
        "poll_endpoint_template": "/task/{task_id}",
        "poll_terminal_statuses": ["SUCCESS", "FAILURE"],
        "mcp_verified_at_utc": "2026-07-15T18:53:03.6516197Z"
      }'::jsonb,
     'manual_sql', 'manual_sql', now(), now())
ON CONFLICT (provider_code) DO UPDATE SET
    provider_name = EXCLUDED.provider_name,
    provider_type = EXCLUDED.provider_type,
    base_url = EXCLUDED.base_url,
    api_key_config_name = EXCLUDED.api_key_config_name,
    priority = EXCLUDED.priority,
    description = EXCLUDED.description,
    config_json = EXCLUDED.config_json,
    updated_by = EXCLUDED.updated_by,
    updated_at = now();

WITH provider AS (
    SELECT id FROM public.todox_ai_provider WHERE provider_code = 'yescale_task_image'
),
capabilities AS (
    SELECT unnest(ARRAY[
        'avatar_generation',
        'chibi_avatar_generation',
        'character_generation',
        'image_generation',
        'scene_image_generation',
        'poster_generation',
        'thumbnail_generation'
    ]) AS capability_code
),
models AS (
    SELECT 'default'::text AS routing_role, 'nano-banana-2'::text AS model_name, 'nano_banana_2'::text AS adapter_profile,
           '1K'::text AS size, NULL::text AS quality, 0.064::numeric AS unit_cost_points,
           '{"google_search":"disable","thinking":"minimal","fallback_models":["seedream-5"],"transient_terminal_error_codes":["rate_limit","temporarily_unavailable"],"model_profiles":{"seedream-5":"seedream_5"},"model_sizes":{"seedream-5":"2K"}}'::jsonb AS extra_config
    UNION ALL
    SELECT 'cheap', 'gpt-image', 'gpt_image', '1024x1024', 'low', 0.0192::numeric,
           '{"background":"transparent"}'::jsonb
    UNION ALL
    SELECT 'backup', 'seedream-5', 'seedream_5', '2K', NULL, 0.052::numeric,
           '{}'::jsonb
),
desired AS (
    SELECT
        p.id AS provider_id,
        'yescale_task_image'::text AS provider_code,
        c.capability_code,
        ('YEScale ' || c.capability_code || ' / ' || m.routing_role || ' / ' || m.model_name)::text AS display_name,
        m.model_name,
        '/task/submit'::text AS endpoint_path,
        'request'::text AS unit_type,
        m.unit_cost_points,
        false::boolean AS is_default,
        false::boolean AS enabled,
        (m.routing_role <> 'backup')::boolean AS allow_user_select,
        jsonb_build_object(
            'routing_role', m.routing_role,
            'adapter_profile', m.adapter_profile,
            'size', m.size,
            'quality', m.quality,
            'provider_estimated_cost_usd',
                CASE m.model_name
                    WHEN 'nano-banana-2' THEN 0.08
                    WHEN 'gpt-image' THEN 0.024
                    WHEN 'seedream-5' THEN 0.065
                END,
            'cost_source', 'configured_tariff',
            'point_formula', 'usd * 8000 / 10000',
            'verified_endpoint', '/task/submit',
            'verified_async', true,
            'mcp_verified_at_utc', '2026-07-15T18:53:03.6516197Z'
        ) || m.extra_config AS config_json
    FROM provider p
    CROSS JOIN capabilities c
    CROSS JOIN models m
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select,
     config_json, created_by, updated_by, created_at, updated_at)
SELECT provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
       unit_type, unit_cost_points, is_default, enabled, allow_user_select,
       config_json, 'manual_sql', 'manual_sql', now(), now()
FROM desired
ON CONFLICT (provider_id, capability_code, model_name) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    endpoint_path = EXCLUDED.endpoint_path,
    unit_type = EXCLUDED.unit_type,
    unit_cost_points = EXCLUDED.unit_cost_points,
    allow_user_select = EXCLUDED.allow_user_select,
    config_json = EXCLUDED.config_json,
    updated_by = EXCLUDED.updated_by,
    updated_at = now();

COMMIT;
