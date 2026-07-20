-- Manual SQL, not an EF migration.
-- Seeds KIE provider and motion_control_video capability for Phase 1.

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

WITH provider_upsert AS (
    INSERT INTO public.todox_ai_provider
        (provider_code, provider_name, provider_type, base_url, api_key_config_name,
         enabled, is_system, priority, description, config_json, created_at, updated_at)
    VALUES
        ('kie', 'KIE', 'external_api', 'https://api.kie.ai', 'KIE_API_KEY',
         false, true, 80,
         'KIE provider for Kling motion-control video. Phase 1 disabled by default; API key must come from environment/secret store.',
         jsonb_build_object(
             'submit_endpoint', '/api/v1/jobs/createTask',
             'poll_endpoint', '/api/v1/jobs/recordInfo',
             'feature_code', 'dance_sell',
             'rate_limit_requests_per_10s', 20,
             'max_concurrent_tasks', 100,
             'phase', 'phase1_no_billing'
         ),
         now(), now())
    ON CONFLICT (provider_code) DO UPDATE
       SET provider_name = EXCLUDED.provider_name,
           provider_type = EXCLUDED.provider_type,
           base_url = EXCLUDED.base_url,
           api_key_config_name = EXCLUDED.api_key_config_name,
           description = EXCLUDED.description,
           config_json = EXCLUDED.config_json,
           updated_at = now()
    RETURNING id
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select, config_json, created_at, updated_at)
SELECT p.id, 'kie', 'motion_control_video', 'Kling 2.6 Motion Control', 'kling-2.6/motion-control',
       '/api/v1/jobs/createTask', 'request', 0, false, false, false,
       jsonb_build_object(
           'feature_code', 'dance_sell',
           'poll_endpoint', '/api/v1/jobs/recordInfo',
           'default_mode', '720p',
           'allowed_modes', jsonb_build_array('720p'),
           'allowed_character_orientations', jsonb_build_array('image'),
           'pricing_status', 'not_verified',
           'billing_enabled', false,
           'phase', 'phase1_no_billing'
       ),
       now(), now()
  FROM public.todox_ai_provider p
 WHERE p.provider_code = 'kie'
ON CONFLICT (provider_code, capability_code, model_name) DO UPDATE
   SET display_name = EXCLUDED.display_name,
       endpoint_path = EXCLUDED.endpoint_path,
       unit_type = EXCLUDED.unit_type,
       unit_cost_points = EXCLUDED.unit_cost_points,
       is_default = false,
       enabled = false,
       allow_user_select = false,
       config_json = EXCLUDED.config_json,
       updated_at = now();

COMMIT;
