-- RVIDEO 79AI video capability seed.
-- Additive and idempotent. Review and execute manually against todo_saas.
-- This script never creates a provider and never removes or disables routes.

BEGIN;

DO $$
BEGIN
    IF to_regclass('public.todox_ai_provider') IS NULL
       OR to_regclass('public.todox_ai_provider_capability') IS NULL THEN
        RAISE EXCEPTION 'RVIDEO_79AI_VIDEO_CAPABILITY_SEED_FAILED missing provider tables.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM public.todox_ai_provider
         WHERE id = 18
           AND lower(btrim(provider_code)) = '79ai'
    ) THEN
        RAISE EXCEPTION 'RVIDEO_79AI_VIDEO_CAPABILITY_SEED_FAILED provider id=18/provider_code=79ai was not found.';
    END IF;
END $$;

WITH desired AS (
    SELECT
        18::bigint AS provider_id,
        '79ai'::text AS provider_code,
        'rvideo_scene_video_generation'::text AS capability_code,
        '79AI RVIDEO scene video'::text AS display_name,
        'seedance_20_pro'::text AS model_name,
        '/create-video'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        true AS is_default,
        true AS enabled,
        false AS allow_user_select,
        jsonb_build_object(
            'domain', '79ai.net',
            'image_upload_path', '/image-upload',
            'submit_path', '/create-video',
            'poll_path', '/video',
            'runtime_owner', 'rvideo',
            'models', jsonb_build_array(
                jsonb_build_object('model', 'seedance_20_pro', 'modes', jsonb_build_array('fast', 'fast_2', 'professional')),
                jsonb_build_object('model', 'seedance_25_omni', 'modes', jsonb_build_array('business_professional'))
            )
        ) AS config_json
)
UPDATE public.todox_ai_provider_capability c
   SET display_name = d.display_name,
       endpoint_path = d.endpoint_path,
       unit_type = d.unit_type,
       unit_cost_points = d.unit_cost_points,
       is_default = d.is_default,
       enabled = d.enabled,
       allow_user_select = d.allow_user_select,
       config_json = COALESCE(c.config_json, '{}'::jsonb) || d.config_json,
       updated_by = 'manual_sql',
       updated_at = now()
  FROM desired d
 WHERE c.provider_id = d.provider_id
   AND lower(btrim(c.provider_code)) = d.provider_code
   AND c.capability_code = d.capability_code;

WITH desired AS (
    SELECT
        18::bigint AS provider_id,
        '79ai'::text AS provider_code,
        'rvideo_scene_video_generation'::text AS capability_code,
        '79AI RVIDEO scene video'::text AS display_name,
        'seedance_20_pro'::text AS model_name,
        '/create-video'::text AS endpoint_path,
        'request'::text AS unit_type,
        0::numeric AS unit_cost_points,
        true AS is_default,
        true AS enabled,
        false AS allow_user_select,
        jsonb_build_object(
            'domain', '79ai.net',
            'image_upload_path', '/image-upload',
            'submit_path', '/create-video',
            'poll_path', '/video',
            'runtime_owner', 'rvideo',
            'models', jsonb_build_array(
                jsonb_build_object('model', 'seedance_20_pro', 'modes', jsonb_build_array('fast', 'fast_2', 'professional')),
                jsonb_build_object('model', 'seedance_25_omni', 'modes', jsonb_build_array('business_professional'))
            )
        ) AS config_json
)
INSERT INTO public.todox_ai_provider_capability
    (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
     unit_type, unit_cost_points, is_default, enabled, allow_user_select, config_json,
     created_by, updated_by, created_at, updated_at)
SELECT d.provider_id, d.provider_code, d.capability_code, d.display_name, d.model_name, d.endpoint_path,
       d.unit_type, d.unit_cost_points, d.is_default, d.enabled, d.allow_user_select, d.config_json,
       'manual_sql', 'manual_sql', now(), now()
  FROM desired d
 WHERE NOT EXISTS (
       SELECT 1
         FROM public.todox_ai_provider_capability c
        WHERE c.provider_id = d.provider_id
          AND lower(btrim(c.provider_code)) = d.provider_code
          AND c.capability_code = d.capability_code
 );

SELECT
    c.id,
    c.provider_id,
    c.provider_code,
    c.capability_code,
    c.model_name,
    c.endpoint_path,
    c.enabled,
    c.is_default,
    c.config_json
FROM public.todox_ai_provider_capability c
WHERE c.provider_id = 18
  AND lower(btrim(c.provider_code)) = '79ai'
  AND c.capability_code = 'rvideo_scene_video_generation';

COMMIT;
