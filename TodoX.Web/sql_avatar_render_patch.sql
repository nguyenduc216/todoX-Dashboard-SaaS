-- Avatar render log safety patch
-- No migration schema change; run manually on PostgreSQL if the 22001 error persists.
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT table_schema, table_name, column_name, data_type, character_maximum_length
          FROM information_schema.columns
         WHERE table_schema IN ('auth', 'public', 'marketing')
           AND (
                (table_name = 'user_avatar_renders' AND column_name IN ('model', 'status', 'prompt_input', 'prompt_used', 'error_message'))
             OR (table_name = 'todox_ai_provider_usage_log' AND column_name IN ('provider_code', 'capability_code', 'feature_code', 'model_name', 'request_id', 'job_id', 'unit_type', 'status'))
             OR (table_name = 'avatar_templates' AND column_name IN ('name', 'slug', 'scenario', 'last_render_log_code', 'last_generated_prompt'))
           )
    LOOP
        RAISE NOTICE '%.%.% type=% length=%', r.table_schema, r.table_name, r.column_name, r.data_type, r.character_maximum_length;
    END LOOP;
END $$;

ALTER TABLE IF EXISTS auth.user_avatar_renders
    ALTER COLUMN model TYPE varchar(255),
    ALTER COLUMN status TYPE varchar(50),
    ALTER COLUMN prompt_input TYPE text,
    ALTER COLUMN prompt_used TYPE text,
    ALTER COLUMN error_message TYPE text;

ALTER TABLE IF EXISTS public.todox_ai_provider_usage_log
    ALTER COLUMN provider_code TYPE varchar(100),
    ALTER COLUMN capability_code TYPE varchar(100),
    ALTER COLUMN feature_code TYPE varchar(100),
    ALTER COLUMN model_name TYPE varchar(255),
    ALTER COLUMN request_id TYPE varchar(100),
    ALTER COLUMN job_id TYPE varchar(100),
    ALTER COLUMN unit_type TYPE varchar(50),
    ALTER COLUMN status TYPE varchar(50);

ALTER TABLE IF EXISTS marketing.avatar_templates
    ALTER COLUMN name TYPE text,
    ALTER COLUMN slug TYPE text,
    ALTER COLUMN scenario TYPE text,
    ALTER COLUMN last_render_log_code TYPE text,
    ALTER COLUMN last_generated_prompt TYPE text;
