\set ON_ERROR_STOP on

DO $$
BEGIN
    IF to_regclass('public.todox_ai_provider') IS NULL THEN
        RAISE EXCEPTION 'Missing table public.todox_ai_provider.';
    END IF;

    IF to_regclass('public.todox_ai_provider_capability') IS NULL THEN
        RAISE EXCEPTION 'Missing table public.todox_ai_provider_capability.';
    END IF;
END $$;

SELECT 'PRECHECK_PROVIDER' section, provider_code, provider_name, enabled, api_key_config_name
FROM public.todox_ai_provider
WHERE provider_code = 'yescale_task_video';

SELECT 'PRECHECK_CAPABILITIES' section, provider_code, capability_code, model_name, enabled, is_default, allow_user_select
FROM public.todox_ai_provider_capability
WHERE provider_code = 'yescale_task_video'
ORDER BY capability_code, model_name;
