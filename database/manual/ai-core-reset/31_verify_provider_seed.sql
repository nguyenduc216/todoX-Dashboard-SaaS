SELECT p.provider_code, p.enabled, COUNT(c.id) AS capability_count
FROM public.todox_ai_provider p
LEFT JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
GROUP BY p.provider_code, p.enabled
ORDER BY p.provider_code;

SELECT p.provider_code, c.capability_code, c.model_name, c.enabled, c.allow_user_select
FROM public.todox_ai_provider_capability c
JOIN public.todox_ai_provider p ON p.id = c.provider_id
WHERE p.provider_code = 'kie'
  AND c.model_name IN ('gpt-image-2-image-to-image','kling-2.6/motion-control')
ORDER BY c.capability_code, c.model_name;

SELECT provider_code, account_code, enabled, is_default, max_concurrency, rate_limit_requests, rate_limit_window_seconds
FROM public.todox_ai_provider_account
WHERE provider_code = 'kie'
ORDER BY account_code;
