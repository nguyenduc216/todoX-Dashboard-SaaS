-- Standalone preflight SQL. Do not run automatically.
-- Purpose: verify the existing AI provider tables can represent ImageAICreativeRender
-- as a scene_image_generation provider capability row.

DO $$
DECLARE
    duplicate_groups int;
BEGIN
    IF to_regclass('public.todox_ai_provider') IS NULL THEN
        RAISE EXCEPTION 'Missing table public.todox_ai_provider.';
    END IF;

    IF to_regclass('public.todox_ai_provider_capability') IS NULL THEN
        RAISE EXCEPTION 'Missing table public.todox_ai_provider_capability.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM information_schema.columns
         WHERE table_schema = 'public'
           AND table_name = 'todox_ai_provider_capability'
           AND column_name IN ('provider_id', 'provider_code', 'capability_code', 'model_name', 'is_default', 'enabled')
         GROUP BY table_schema, table_name
        HAVING count(*) = 6
    ) THEN
        RAISE EXCEPTION 'public.todox_ai_provider_capability does not expose the expected provider capability columns.';
    END IF;

    SELECT count(*) INTO duplicate_groups
      FROM (
        SELECT provider_id, capability_code, COALESCE(model_name, ''), count(*) AS total
          FROM public.todox_ai_provider_capability
         GROUP BY provider_id, capability_code, COALESCE(model_name, '')
        HAVING count(*) > 1
      ) d;

    IF duplicate_groups > 0 THEN
        RAISE EXCEPTION 'Duplicate provider/capability/model groups already exist: %.', duplicate_groups;
    END IF;
END $$;

SELECT p.id,
       p.provider_code,
       p.provider_name,
       p.enabled,
       c.id AS capability_id,
       c.capability_code,
       c.model_name,
       c.unit_type,
       c.unit_cost_points,
       c.enabled AS capability_enabled,
       c.is_default
  FROM public.todox_ai_provider p
  LEFT JOIN public.todox_ai_provider_capability c
    ON c.provider_id = p.id
   AND c.capability_code = 'scene_image_generation'
 WHERE p.provider_code IN ('image_ai_creative_render', 'todox_image')
 ORDER BY p.provider_code, c.model_name;
