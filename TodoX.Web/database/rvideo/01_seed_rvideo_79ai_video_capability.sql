-- RVIDEO 79AI video capability seed.
-- Additive and idempotent. Review and execute manually against todo_saas.
-- This script never creates a provider and never removes or disables routes.
-- Pricing is copied from active 79AI catalog price rows; it never seeds zero.

BEGIN;

DO $$
DECLARE
    resolved_provider_id bigint;
    resolved_provider_code text;
    fallback_points numeric;
    pricing_rules jsonb;
    desired_config jsonb;
BEGIN
    IF to_regclass('public.todox_ai_provider') IS NULL
       OR to_regclass('public.todox_ai_provider_capability') IS NULL
       OR to_regclass('public.todox_ai_provider_model') IS NULL
       OR to_regclass('public.todox_ai_model_price') IS NULL THEN
        RAISE EXCEPTION 'RVIDEO_79AI_VIDEO_CAPABILITY_SEED_FAILED required provider/capability/catalog tables are missing.';
    END IF;

    SELECT p.id, lower(btrim(p.provider_code))
      INTO resolved_provider_id, resolved_provider_code
      FROM public.todox_ai_provider p
     WHERE lower(btrim(p.provider_code)) = '79ai'
       AND p.enabled = true
     ORDER BY p.priority, p.id
     LIMIT 1;

    IF resolved_provider_id IS NULL THEN
        RAISE EXCEPTION 'RVIDEO_79AI_VIDEO_CAPABILITY_SEED_FAILED enabled provider_code=79ai was not found.';
    END IF;

    SELECT
        jsonb_agg(
            jsonb_build_object(
                'match', jsonb_strip_nulls(jsonb_build_object(
                    'model', priced.model_code,
                    'duration', priced.duration_seconds
                )),
                'chargedPoints', priced.charged_points,
                'costSource', 'catalog_todox_ai_model_price_max_variant',
                'ruleKey', concat(priced.model_code, '|any|', COALESCE(priced.duration_seconds::text, 'any'))
            ) ORDER BY priced.model_code, priced.duration_seconds
        ),
        max(priced.charged_points)
      INTO pricing_rules, fallback_points
      FROM (
          SELECT m.provider_model_code AS model_code,
                 p.duration_seconds,
                 max(COALESCE(p.sell_points, p.internal_cost_points)) AS charged_points
            FROM public.todox_ai_provider_model m
            JOIN public.todox_ai_model_price p ON p.model_id = m.id
           WHERE m.provider_id = resolved_provider_id
             AND lower(btrim(m.provider_code)) = resolved_provider_code
             AND lower(btrim(m.provider_model_code)) IN ('seedance_20_pro', 'seedance_25_omni')
             AND m.enabled = true
             AND m.is_deprecated = false
             AND p.active = true
             AND COALESCE(p.sell_points, p.internal_cost_points, 0) > 0
           GROUP BY m.provider_model_code, p.duration_seconds
      ) priced;

    IF pricing_rules IS NULL OR fallback_points IS NULL OR fallback_points <= 0 THEN
        RAISE EXCEPTION 'RVIDEO_79AI_VIDEO_CAPABILITY_SEED_FAILED positive Seedance catalog pricing was not found.';
    END IF;

    desired_config := jsonb_build_object(
        'domain', '79ai.net',
        'image_upload_path', '/image-upload',
        'submit_path', '/create-video',
        'poll_path', '/video',
        'runtime_owner', 'rvideo',
        'pricing_source', 'todox_ai_model_price',
        'pricing', jsonb_build_object('rules', pricing_rules),
        'models', jsonb_build_array(
            jsonb_build_object('model', 'seedance_20_pro', 'modes', jsonb_build_array('fast', 'fast_2', 'professional')),
            jsonb_build_object('model', 'seedance_25_omni', 'modes', jsonb_build_array('business_professional'))
        )
    );

    UPDATE public.todox_ai_provider_capability c
       SET display_name = '79AI RVIDEO scene video',
           model_name = 'seedance_20_pro',
           endpoint_path = '/create-video',
           unit_type = 'request',
           unit_cost_points = fallback_points,
           is_default = true,
           enabled = true,
           allow_user_select = false,
           config_json = COALESCE(c.config_json, '{}'::jsonb) || desired_config,
           updated_by = 'manual_sql',
           updated_at = now()
     WHERE c.provider_id = resolved_provider_id
       AND lower(btrim(c.provider_code)) = resolved_provider_code
       AND c.capability_code = 'rvideo_scene_video_generation';

    INSERT INTO public.todox_ai_provider_capability
        (provider_id, provider_code, capability_code, display_name, model_name, endpoint_path,
         unit_type, unit_cost_points, is_default, enabled, allow_user_select, config_json,
         created_by, updated_by, created_at, updated_at)
    SELECT resolved_provider_id, resolved_provider_code, 'rvideo_scene_video_generation',
           '79AI RVIDEO scene video', 'seedance_20_pro', '/create-video', 'request',
           fallback_points, true, true, false, desired_config,
           'manual_sql', 'manual_sql', now(), now()
     WHERE NOT EXISTS (
         SELECT 1
           FROM public.todox_ai_provider_capability c
          WHERE c.provider_id = resolved_provider_id
            AND lower(btrim(c.provider_code)) = resolved_provider_code
            AND c.capability_code = 'rvideo_scene_video_generation'
     );
END $$;

SELECT
    c.id,
    c.provider_id,
    c.provider_code,
    c.capability_code,
    c.model_name,
    c.endpoint_path,
    c.unit_cost_points,
    c.enabled,
    c.is_default,
    c.config_json
FROM public.todox_ai_provider_capability c
JOIN public.todox_ai_provider p ON p.id = c.provider_id
WHERE lower(btrim(p.provider_code)) = '79ai'
  AND c.capability_code = 'rvideo_scene_video_generation';

COMMIT;
