-- Standalone additive seed for RVIDEO 79AI image routing.
-- Not executed by the application or this coding task.
-- Credentials remain in the provider secure-credential store; this script contains no secret.

BEGIN;

DO $$
BEGIN
    IF to_regclass('public.todox_ai_provider') IS NULL
       OR to_regclass('public.todox_ai_provider_capability') IS NULL THEN
        RAISE EXCEPTION 'Missing TodoX AI provider tables.';
    END IF;
END $$;

INSERT INTO public.todox_ai_provider
    (provider_code, provider_name, provider_type, base_url, api_key_config_name,
     enabled, is_system, priority, description, config_json,
     created_by, updated_by, created_at, updated_at)
SELECT
    '79ai',
    '79AI / Gommo',
    'external_api',
    'https://api.gommo.net/ai',
    NULL,
    true,
    true,
    35,
    '79AI asynchronous image provider. Credentials are resolved from the secure provider credential store.',
    '{"protocol":"79ai_task","domain":"79ai.net","submit_path":"/generateImage","poll_path":"/image","list_path":"/images","poll_interval_seconds":10,"poll_max_attempts":18}'::jsonb,
    'manual_sql',
    'manual_sql',
    now(),
    now()
WHERE NOT EXISTS (
    SELECT 1 FROM public.todox_ai_provider WHERE lower(provider_code) = '79ai'
);

WITH provider AS (
    SELECT id
      FROM public.todox_ai_provider
     WHERE lower(provider_code) = '79ai'
     ORDER BY id
     LIMIT 1
),
desired AS (
    SELECT
        p.id AS provider_id,
        '79ai'::text AS provider_code,
        'rvideo_scene_image_generation'::text AS capability_code,
        '79AI RVIDEO scene image'::text AS display_name,
        'google_image_gen_banana_2'::text AS model_name,
        '/generateImage'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        true AS is_default,
        true AS enabled,
        false AS allow_user_select,
        jsonb_build_object(
            'domain', '79ai.net',
            'project_id', 'default',
            'action_type', 'create',
            'submit_path', '/generateImage',
            'poll_path', '/image',
            'list_path', '/images',
            'poll_interval_seconds', 10,
            'poll_max_attempts', 18,
            'models', jsonb_build_array(
                jsonb_build_object('model', 'google_image_gen_banana_2', 'mode', 'vip', 'resolution', '1k'),
                jsonb_build_object('model', 'imagegen_2_0', 'mode', 'low_basic', 'resolution', '1k'),
                jsonb_build_object('model', 'seedream_4_5', 'mode', 'vip', 'resolution', '2k')
            )
        ) AS config_json
    FROM provider p
)
UPDATE public.todox_ai_provider_capability c
   SET display_name = d.display_name,
       endpoint_path = d.endpoint_path,
       unit_type = d.unit_type,
       unit_cost_points = d.unit_cost_points,
       is_default = d.is_default,
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
    SELECT id
      FROM public.todox_ai_provider
     WHERE lower(provider_code) = '79ai'
     ORDER BY id
     LIMIT 1
),
desired AS (
    SELECT
        p.id AS provider_id,
        '79ai'::text AS provider_code,
        'rvideo_scene_image_generation'::text AS capability_code,
        '79AI RVIDEO scene image'::text AS display_name,
        'google_image_gen_banana_2'::text AS model_name,
        '/generateImage'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        true AS is_default,
        true AS enabled,
        false AS allow_user_select,
        jsonb_build_object(
            'domain', '79ai.net',
            'project_id', 'default',
            'action_type', 'create',
            'submit_path', '/generateImage',
            'poll_path', '/image',
            'list_path', '/images',
            'poll_interval_seconds', 10,
            'poll_max_attempts', 18,
            'models', jsonb_build_array(
                jsonb_build_object('model', 'google_image_gen_banana_2', 'mode', 'vip', 'resolution', '1k'),
                jsonb_build_object('model', 'imagegen_2_0', 'mode', 'low_basic', 'resolution', '1k'),
                jsonb_build_object('model', 'seedream_4_5', 'mode', 'vip', 'resolution', '2k')
            )
        ) AS config_json
    FROM provider p
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select, config_json,
     created_by, updated_by, created_at, updated_at)
SELECT provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
       unit_type, unit_cost_points, is_default, enabled, allow_user_select, config_json,
       'manual_sql', 'manual_sql', now(), now()
  FROM desired d
 WHERE NOT EXISTS (
    SELECT 1
      FROM public.todox_ai_provider_capability c
     WHERE c.provider_id = d.provider_id
       AND c.capability_code = d.capability_code
       AND c.model_name = d.model_name
);

COMMIT;
