\set ON_ERROR_STOP on

BEGIN;

DELETE FROM public.todox_ai_provider_capability
WHERE provider_code = 'yescale_task_video';

DELETE FROM public.todox_ai_provider
WHERE provider_code = 'yescale_task_video';

COMMIT;
