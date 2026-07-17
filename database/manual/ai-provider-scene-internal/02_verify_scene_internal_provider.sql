-- Standalone verification SQL. Do not run automatically.

SELECT p.id AS provider_id,
       p.provider_code,
       p.provider_name,
       p.enabled AS provider_enabled,
       c.id AS provider_capability_id,
       c.capability_code,
       c.display_name,
       c.model_name,
       c.unit_type,
       c.unit_cost_points,
       c.enabled AS capability_enabled,
       c.allow_user_select,
       c.is_default,
       c.config_json
  FROM public.todox_ai_provider p
  JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
 WHERE p.provider_code IN ('image_ai_creative_render', 'todox_image')
   AND c.capability_code = 'scene_image_generation'
 ORDER BY p.provider_code, c.is_default DESC, c.model_name;

SELECT capability_code,
       count(*) FILTER (WHERE is_default) AS default_count,
       count(*) FILTER (WHERE enabled) AS enabled_count
  FROM public.todox_ai_provider_capability
 WHERE capability_code = 'scene_image_generation'
 GROUP BY capability_code;
