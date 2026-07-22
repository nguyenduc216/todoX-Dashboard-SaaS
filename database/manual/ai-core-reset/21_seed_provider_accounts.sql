BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

INSERT INTO public.todox_ai_provider_account
    (provider_id, provider_code, account_code, account_name, environment, enabled, is_default,
     priority, weight, max_concurrency, rate_limit_count, rate_limit_requests, rate_limit_window_seconds,
     balance_unit, minimum_balance, minimum_balance_threshold, health_status, config_json)
SELECT p.id, p.provider_code, 'kie-default', 'KIE Default', 'production', true, true,
       100, 100, 1, 20, 20, 10, 'credits', NULL, NULL, 'unknown',
       '{"credential_config_name":"KIE_API_KEY","source":"preserved_metadata"}'::jsonb
FROM public.todox_ai_provider p
WHERE p.provider_code = 'kie'
ON CONFLICT ON CONSTRAINT todox_ai_provider_account_code_uk DO UPDATE
SET enabled = EXCLUDED.enabled,
    is_default = EXCLUDED.is_default,
    max_concurrency = EXCLUDED.max_concurrency,
    rate_limit_count = EXCLUDED.rate_limit_count,
    rate_limit_requests = EXCLUDED.rate_limit_requests,
    rate_limit_window_seconds = EXCLUDED.rate_limit_window_seconds,
    updated_at = now();

INSERT INTO public.todox_ai_provider_account_credential
    (provider_account_id, credential_key, credential_role, enabled, priority, metadata_json)
SELECT a.id, 'KIE_API_KEY', 'api_key', true, 100, '{"source":"configuration_reference"}'::jsonb
FROM public.todox_ai_provider_account a
WHERE a.provider_code = 'kie'
  AND a.account_code = 'kie-default'
ON CONFLICT (provider_account_id, credential_role, (COALESCE(credential_id::text, credential_key, credential_config_name)))
WHERE enabled
DO UPDATE SET updated_at = now();

COMMIT;
