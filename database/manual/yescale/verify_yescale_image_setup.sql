SELECT provider_code, provider_name, enabled, api_key_config_name
  FROM public.todox_ai_provider
 WHERE provider_code = 'yescale_task_image';

SELECT capability_code,
       count(*) FILTER (WHERE is_default) AS default_count,
       count(*) FILTER (WHERE enabled AND unit_cost_points <= 0) AS enabled_zero_price_count,
       string_agg(model_name || ':' || enabled || ':' || is_default || ':' || unit_cost_points, ', ' ORDER BY model_name) AS models
  FROM public.todox_ai_provider_capability
 WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','poster_generation','thumbnail_generation')
 GROUP BY capability_code
 ORDER BY capability_code;
