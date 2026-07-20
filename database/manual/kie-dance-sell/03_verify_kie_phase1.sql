-- Manual verification for KIE Dance Sell Phase 1.

DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(name, ', ') INTO missing
      FROM (
        VALUES
          ('public.todox_ai_provider'),
          ('public.todox_ai_provider_capability'),
          ('public.todox_ai_provider_usage_log'),
          ('render.render_jobs'),
          ('render.render_job_events'),
          ('dance_sell.dance_sell_jobs')
      ) AS required(name)
     WHERE to_regclass(required.name) IS NULL;

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Missing required table(s): %', missing;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM public.todox_ai_provider WHERE provider_code='kie'
    ) THEN
        RAISE EXCEPTION 'Missing KIE provider row.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM public.todox_ai_provider_capability
         WHERE provider_code='kie'
           AND capability_code='motion_control_video'
           AND model_name='kling-2.6/motion-control'
    ) THEN
        RAISE EXCEPTION 'Missing KIE motion_control_video capability.';
    END IF;
END $$;

SELECT p.provider_code, p.provider_name, p.enabled, p.base_url, p.api_key_config_name,
       c.capability_code, c.model_name, c.enabled AS capability_enabled,
       c.allow_user_select, c.unit_type, c.unit_cost_points
  FROM public.todox_ai_provider p
  JOIN public.todox_ai_provider_capability c ON c.provider_id = p.id
 WHERE p.provider_code = 'kie'
 ORDER BY c.capability_code, c.model_name;

SELECT conname, pg_get_constraintdef(oid) AS definition
  FROM pg_constraint
 WHERE conrelid = 'dance_sell.dance_sell_jobs'::regclass
 ORDER BY conname;

SELECT indexname, indexdef
  FROM pg_indexes
 WHERE schemaname = 'dance_sell'
   AND tablename = 'dance_sell_jobs'
 ORDER BY indexname;

SELECT conname, pg_get_constraintdef(oid) AS definition
  FROM pg_constraint
 WHERE conrelid = 'render.render_jobs'::regclass
   AND conname ILIKE '%job_type%';
