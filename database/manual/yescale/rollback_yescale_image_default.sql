-- Rollback YEScale image defaults without deleting usage/wallet history.

BEGIN;

UPDATE public.todox_ai_provider_capability
   SET enabled = false,
       is_default = false,
       updated_by = 'manual_sql_rollback',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image';

UPDATE public.todox_ai_provider
   SET enabled = false,
       updated_by = 'manual_sql_rollback',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image';

COMMIT;
