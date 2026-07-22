BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset-task3'));

UPDATE public.todox_ai_provider_account
   SET rate_limit_count = NULL,
       rate_limit_requests = NULL,
       rate_limit_window_seconds = NULL,
       config_json = COALESCE(config_json, '{}'::jsonb)
                     || jsonb_build_object(
                            'rateLimitVerified', false,
                            'rateLimitStatus', 'provisional',
                            'rateLimitNote', 'KIE account rate limit must be verified from provider account documentation before production enforcement.'
                        ),
       updated_at = now()
 WHERE provider_code = 'kie'
   AND account_code = 'kie-default';

COMMIT;
