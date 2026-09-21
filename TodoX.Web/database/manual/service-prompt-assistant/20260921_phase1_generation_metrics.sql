-- Review and execute manually. No render or billing schema is changed.
ALTER TABLE settings.service_prompt_generations
    ALTER COLUMN training_version_id DROP NOT NULL;

ALTER TABLE settings.service_prompt_generations
    ADD COLUMN IF NOT EXISTS runtime_provider text,
    ADD COLUMN IF NOT EXISTS credit numeric,
    ADD COLUMN IF NOT EXISTS first_event_ms integer,
    ADD COLUMN IF NOT EXISTS first_content_ms integer,
    ADD COLUMN IF NOT EXISTS total_duration_ms integer,
    ADD COLUMN IF NOT EXISTS streaming_duration_ms integer;

SELECT column_name, is_nullable
  FROM information_schema.columns
 WHERE table_schema='settings'
   AND table_name='service_prompt_generations'
   AND column_name IN ('training_version_id','runtime_provider','credit','first_event_ms','first_content_ms','total_duration_ms','streaming_duration_ms')
 ORDER BY column_name;

DO $$
DECLARE
    missing_or_invalid text;
BEGIN
    WITH expected(column_name, data_type, is_nullable) AS
    (
        VALUES
            ('training_version_id', 'uuid', 'YES'),
            ('runtime_provider', 'text', 'YES'),
            ('credit', 'numeric', 'YES'),
            ('first_event_ms', 'integer', 'YES'),
            ('first_content_ms', 'integer', 'YES'),
            ('total_duration_ms', 'integer', 'YES'),
            ('streaming_duration_ms', 'integer', 'YES')
    )
    SELECT string_agg(
               e.column_name || ' expected ' || e.data_type || '/' || e.is_nullable
               || ' but found ' || COALESCE(c.data_type, 'missing') || '/' || COALESCE(c.is_nullable, 'missing'),
               ', ' ORDER BY e.column_name)
      INTO missing_or_invalid
      FROM expected e
      LEFT JOIN information_schema.columns c
        ON c.table_schema = 'settings'
       AND c.table_name = 'service_prompt_generations'
       AND c.column_name = e.column_name
     WHERE c.column_name IS NULL
        OR c.data_type <> e.data_type
        OR c.is_nullable <> e.is_nullable;

    IF missing_or_invalid IS NOT NULL THEN
        RAISE EXCEPTION 'Prompt Assistant metrics verification failed: %', missing_or_invalid;
    END IF;
END $$;
