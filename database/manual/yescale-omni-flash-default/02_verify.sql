SELECT
    p.provider_code,
    p.enabled AS provider_enabled,
    c.capability_code,
    c.model_name,
    c.enabled AS capability_enabled,
    c.is_default,
    c.priority,
    c.unit_cost_points,
    c.config_json ->> 'mode' AS mode,
    c.config_json ->> 'max_prompt_characters' AS max_prompt_characters
FROM public.todox_ai_provider p
JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
WHERE c.capability_code = 'image_to_video'
ORDER BY c.is_default DESC, c.priority, c.model_name;

SELECT COUNT(*) AS enabled_default_count
FROM public.todox_ai_provider_capability
WHERE capability_code = 'image_to_video'
  AND enabled = true
  AND is_default = true;
