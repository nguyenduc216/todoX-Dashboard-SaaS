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
