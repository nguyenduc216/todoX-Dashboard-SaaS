-- Read-only verification for the RVIDEO 79AI video path.
-- No data is modified here.

WITH provider AS (
    SELECT id, provider_code, provider_name, enabled
      FROM public.todox_ai_provider
     WHERE lower(btrim(provider_code)) = '79ai'
     LIMIT 1
),
capability AS (
    SELECT id, provider_id, provider_code, capability_code, display_name, model_name, enabled, is_default
      FROM public.todox_ai_provider_capability
     WHERE lower(btrim(provider_code)) = '79ai'
       AND lower(btrim(capability_code)) = 'rvideo_scene_video_generation'
     ORDER BY is_default DESC, id
     LIMIT 1
),
models AS (
    SELECT count(*) AS model_count
      FROM public.todox_ai_provider_model
     WHERE lower(btrim(provider_code)) = '79ai'
       AND lower(btrim(provider_model_code)) IN ('seedance_20_pro', 'seedance_25_omni')
)
SELECT
    p.id AS provider_id,
    p.provider_code,
    p.provider_name,
    p.enabled AS provider_enabled,
    c.id AS capability_id,
    c.capability_code,
    c.display_name,
    c.model_name AS capability_model_name,
    c.enabled AS capability_enabled,
    c.is_default AS capability_is_default,
    m.model_count,
    CASE
        WHEN p.id IS NOT NULL
         AND c.id IS NOT NULL
         AND c.provider_id = p.id
         AND c.enabled = TRUE
         AND p.enabled = TRUE
        THEN 'pass'
        ELSE 'fail'
    END AS runtime_status
  FROM provider p
  LEFT JOIN capability c ON c.provider_id = p.id
 CROSS JOIN models m;

