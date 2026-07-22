BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

INSERT INTO public.todox_ai_provider_account
    (provider_id, provider_code, account_code, account_name, environment, enabled, is_default,
     priority, weight, max_concurrency, rate_limit_requests, rate_limit_window_seconds,
     balance_unit, minimum_balance_threshold, health_status, config_json)
SELECT p.id, p.provider_code, 'kie-default', 'KIE Default', 'production', true, true,
       100, 1, 1, 20, 10, 'credits', NULL, 'unknown',
       '{"credential_config_name":"KIE_API_KEY","source":"preserved_metadata"}'::jsonb
FROM public.todox_ai_provider p
WHERE p.provider_code = 'kie'
ON CONFLICT (provider_code, account_code, environment) DO UPDATE
SET enabled = EXCLUDED.enabled,
    is_default = EXCLUDED.is_default,
    max_concurrency = EXCLUDED.max_concurrency,
    rate_limit_requests = EXCLUDED.rate_limit_requests,
    rate_limit_window_seconds = EXCLUDED.rate_limit_window_seconds,
    updated_at = now();

COMMIT;
