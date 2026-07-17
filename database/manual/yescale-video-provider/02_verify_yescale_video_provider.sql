\set ON_ERROR_STOP on

SELECT 'PROVIDER' section, provider_code, provider_name, enabled, api_key_config_name, priority
FROM public.todox_ai_provider
WHERE provider_code = 'yescale_task_video';

SELECT 'CAPABILITIES' section,
       provider_code, capability_code, model_name, enabled, allow_user_select, is_default, unit_type, unit_cost_points
FROM public.todox_ai_provider_capability
WHERE provider_code = 'yescale_task_video'
ORDER BY capability_code, model_name;

SELECT 'DUPLICATES' section, provider_code, capability_code, model_name, count(*) row_count
FROM public.todox_ai_provider_capability
WHERE provider_code = 'yescale_task_video'
GROUP BY provider_code, capability_code, model_name
HAVING count(*) > 1;

SELECT 'DEFAULT_COUNT' section, capability_code, count(*) default_count
FROM public.todox_ai_provider_capability
WHERE provider_code = 'yescale_task_video'
  AND is_default = true
GROUP BY capability_code;
