-- Billing support preflight for YEScale image provider.
-- Standalone SQL, not a migration. This file does not modify schema when existing columns are present.

DO $$
DECLARE
    missing text;
BEGIN
    SELECT string_agg(required.column_name, ', ') INTO missing
      FROM (
        VALUES
            ('todox_ai_provider_usage_log', 'customer_id'),
            ('todox_ai_provider_usage_log', 'provider_id'),
            ('todox_ai_provider_usage_log', 'provider_capability_id'),
            ('todox_ai_provider_usage_log', 'provider_code'),
            ('todox_ai_provider_usage_log', 'capability_code'),
            ('todox_ai_provider_usage_log', 'feature_code'),
            ('todox_ai_provider_usage_log', 'model_name'),
            ('todox_ai_provider_usage_log', 'request_id'),
            ('todox_ai_provider_usage_log', 'job_id'),
            ('todox_ai_provider_usage_log', 'unit_cost_points'),
            ('todox_ai_provider_usage_log', 'total_points'),
            ('todox_ai_provider_usage_log', 'provider_raw_cost'),
            ('todox_ai_provider_usage_log', 'metadata_json')
      ) AS required(table_name, column_name)
      WHERE NOT EXISTS (
          SELECT 1
            FROM information_schema.columns c
           WHERE c.table_schema = 'public'
             AND c.table_name = required.table_name
             AND c.column_name = required.column_name
      );

    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Billing preflight failed. Missing columns: %', missing;
    END IF;
END $$;

SELECT column_name, data_type, numeric_precision, numeric_scale
  FROM information_schema.columns
 WHERE table_schema = 'public'
   AND table_name = 'todox_ai_provider_usage_log'
   AND column_name IN ('unit_cost_points','total_points','provider_raw_cost')
 ORDER BY column_name;
