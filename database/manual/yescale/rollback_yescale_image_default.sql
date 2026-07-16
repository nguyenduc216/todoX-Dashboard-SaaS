-- Rollback YEScale image defaults without deleting usage, wallet, or reconciliation history.
-- Requires snapshot written by 04_enable_yescale_image_production.sql.

BEGIN;

DO $$
DECLARE
    snapshot_count int;
    active_reconciliation int;
BEGIN
    IF to_regclass('billing.yescale_image_default_snapshot') IS NULL THEN
        RAISE EXCEPTION 'Missing billing.yescale_image_default_snapshot. Cannot restore exact previous defaults.';
    END IF;

    SELECT count(*) INTO snapshot_count
      FROM billing.yescale_image_default_snapshot
     WHERE snapshot_key = 'yescale_image_production_before'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','thumbnail_generation');

    IF snapshot_count <> 6 THEN
        RAISE EXCEPTION 'Expected 6 snapshot rows, found %. Cannot rollback safely.', snapshot_count;
    END IF;

    SELECT count(*) INTO active_reconciliation
      FROM billing.ai_image_billing_records
     WHERE status IN ('reserved','pending_reconciliation','manual_review');

    IF active_reconciliation > 0 THEN
        RAISE EXCEPTION 'Image billing reconciliation rows are still active/manual review. Resolve before rollback. Rows=%', active_reconciliation;
    END IF;
END $$;

WITH caps AS (
    SELECT capability_code
      FROM billing.yescale_image_default_snapshot
     WHERE snapshot_key = 'yescale_image_production_before'
)
UPDATE public.todox_ai_provider_capability c
   SET is_default = false,
       updated_by = 'manual_sql_rollback',
       updated_at = now()
  FROM caps
 WHERE c.capability_code = caps.capability_code
   AND c.is_default = true;

UPDATE public.todox_ai_provider_capability c
   SET enabled = COALESCE(s.was_enabled, c.enabled),
       is_default = true,
       updated_by = 'manual_sql_rollback',
       updated_at = now()
  FROM billing.yescale_image_default_snapshot s
 WHERE s.snapshot_key = 'yescale_image_production_before'
   AND s.provider_capability_id IS NOT NULL
   AND c.id = s.provider_capability_id;

UPDATE public.todox_ai_provider_capability
   SET enabled = false,
       is_default = false,
       updated_by = 'manual_sql_rollback',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','thumbnail_generation','poster_generation')
   AND id NOT IN (
       SELECT provider_capability_id
         FROM billing.yescale_image_default_snapshot
        WHERE snapshot_key = 'yescale_image_production_before'
          AND provider_capability_id IS NOT NULL
   );

UPDATE public.todox_ai_provider
   SET enabled = EXISTS (
           SELECT 1
             FROM public.todox_ai_provider_capability
            WHERE provider_code = 'yescale_task_image'
              AND enabled = true
       ),
       updated_by = 'manual_sql_rollback',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image';

DO $$
DECLARE
    bad_defaults int;
BEGIN
    SELECT count(*) INTO bad_defaults
      FROM (
        SELECT capability_code, count(*) FILTER (WHERE is_default) AS defaults
          FROM public.todox_ai_provider_capability
         WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','character_generation','image_generation','scene_image_generation','thumbnail_generation')
         GROUP BY capability_code
        HAVING count(*) FILTER (WHERE is_default) <> 1
      ) x;

    IF bad_defaults > 0 THEN
        RAISE EXCEPTION 'Rollback left missing/duplicate defaults. Bad groups=%', bad_defaults;
    END IF;
END $$;

COMMIT;
