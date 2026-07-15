-- Production default switch for YEScale image capabilities.
-- Standalone SQL, not a migration. Run only after backup and staging smoke tests.
-- poster_generation is intentionally excluded because fixed composite still bypasses the shared router.

BEGIN;

CREATE TABLE IF NOT EXISTS billing.yescale_image_default_snapshot (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    snapshot_key text NOT NULL,
    capability_code text NOT NULL,
    provider_capability_id bigint NULL,
    provider_code text NULL,
    model_name text NULL,
    was_enabled boolean NULL,
    was_default boolean NULL,
    captured_at timestamptz NOT NULL DEFAULT now(),
    captured_by text NOT NULL DEFAULT 'manual_sql_production',
    CONSTRAINT yescale_image_default_snapshot_uk UNIQUE (snapshot_key, capability_code)
);

DO $$
DECLARE
    cap text;
    row_count int;
    default_count int;
    bad_count int;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM public.todox_ai_provider WHERE provider_code='yescale_task_image') THEN
        RAISE EXCEPTION 'Refusing production enable: yescale_task_image provider is missing.';
    END IF;
    IF to_regclass('billing.ai_image_billing_records') IS NULL THEN
        RAISE EXCEPTION 'Run 02_add_yescale_billing_support.sql before production enable.';
    END IF;
    IF to_regclass('billing.ai_image_provider_attempts') IS NULL THEN
        RAISE EXCEPTION 'Run 02_add_yescale_billing_support.sql before production enable.';
    END IF;

    FOREACH cap IN ARRAY ARRAY['avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation']
    LOOP
        SELECT count(*) INTO row_count
          FROM public.todox_ai_provider_capability
         WHERE provider_code = 'yescale_task_image'
           AND capability_code = cap
           AND model_name IN ('nano-banana-2','gpt-image','seedream-5');

        IF row_count <> 3 THEN
            RAISE EXCEPTION 'Refusing production enable: capability % must have nano-banana-2, gpt-image, seedream-5. Found %.', cap, row_count;
        END IF;
    END LOOP;

    SELECT count(*) INTO bad_count
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
       AND unit_cost_points <= 0;

    IF bad_count > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: YEScale image capability has unit_cost_points <= 0. Rows=%', bad_count;
    END IF;

    SELECT count(*) INTO default_count
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
       AND model_name = 'nano-banana-2';

    IF default_count <> 5 THEN
        RAISE EXCEPTION 'Refusing production enable: expected 5 nano-banana-2 billed routed default candidate rows, found %.', default_count;
    END IF;
END $$;

INSERT INTO billing.yescale_image_default_snapshot
    (snapshot_key, capability_code, provider_capability_id, provider_code, model_name, was_enabled, was_default, captured_by)
SELECT 'yescale_image_production_before',
       caps.capability_code,
       c.id,
       c.provider_code,
       c.model_name,
       c.enabled,
       c.is_default,
       'manual_sql_production'
  FROM (SELECT unnest(ARRAY['avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation']) AS capability_code) caps
  LEFT JOIN LATERAL (
      SELECT id, provider_code, model_name, enabled, is_default
        FROM public.todox_ai_provider_capability
       WHERE capability_code = caps.capability_code
         AND is_default = true
       ORDER BY updated_at DESC NULLS LAST, id DESC
       LIMIT 1
  ) c ON true
ON CONFLICT (snapshot_key, capability_code) DO NOTHING;

UPDATE public.todox_ai_provider
   SET enabled = true, updated_by = 'manual_sql_production', updated_at = now()
 WHERE provider_code = 'yescale_task_image';

WITH caps AS (
    SELECT unnest(ARRAY['avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation']) AS capability_code
)
UPDATE public.todox_ai_provider_capability c
   SET is_default = false, updated_by = 'manual_sql_production', updated_at = now()
  FROM caps
 WHERE c.capability_code = caps.capability_code
   AND c.is_default = true;

UPDATE public.todox_ai_provider_capability
   SET enabled = true,
       is_default = (model_name = 'nano-banana-2'),
       allow_user_select = (model_name <> 'seedream-5'),
       updated_by = 'manual_sql_production',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation');

UPDATE public.todox_ai_provider_capability
   SET enabled = false,
       is_default = false,
       updated_by = 'manual_sql_production',
       updated_at = now()
 WHERE provider_code = 'yescale_task_image'
   AND capability_code IN ('character_generation','poster_generation');

DO $$
DECLARE
    bad_defaults int;
    bad_default_model int;
    bad_backup_selectable int;
BEGIN
    SELECT count(*) INTO bad_defaults
      FROM (
        SELECT capability_code, count(*) FILTER (WHERE is_default) AS defaults
          FROM public.todox_ai_provider_capability
         WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
         GROUP BY capability_code
        HAVING count(*) FILTER (WHERE is_default) <> 1
      ) x;

    IF bad_defaults > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: each image capability must have exactly one default. Bad groups=%', bad_defaults;
    END IF;

    SELECT count(*) INTO bad_default_model
      FROM public.todox_ai_provider_capability
     WHERE capability_code IN ('avatar_generation','chibi_avatar_generation','image_generation','scene_image_generation','thumbnail_generation')
       AND is_default = true
       AND NOT (provider_code = 'yescale_task_image' AND model_name = 'nano-banana-2');

    IF bad_default_model > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: default model must be YEScale nano-banana-2. Rows=%', bad_default_model;
    END IF;

    SELECT count(*) INTO bad_backup_selectable
      FROM public.todox_ai_provider_capability
     WHERE provider_code = 'yescale_task_image'
       AND model_name = 'seedream-5'
       AND allow_user_select = true;

    IF bad_backup_selectable > 0 THEN
        RAISE EXCEPTION 'Refusing production enable: seedream-5 backup must not be user-selectable. Rows=%', bad_backup_selectable;
    END IF;
END $$;

COMMIT;
