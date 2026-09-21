-- Review and execute manually in the intended environment.
-- This script is idempotent and does not alter render or billing tables.

ALTER TABLE settings.service_prompt_assistants
    ADD COLUMN IF NOT EXISTS gommo_agent_id integer;

ALTER TABLE settings.service_prompt_assistants
    ADD COLUMN IF NOT EXISTS gommo_agent_id_base text NOT NULL DEFAULT '';

DO $$
BEGIN
    IF NOT EXISTS
    (
        SELECT 1 FROM pg_constraint
         WHERE conname = 'ck_service_prompt_assistants_gommo_agent_id'
           AND conrelid = 'settings.service_prompt_assistants'::regclass
    ) THEN
        ALTER TABLE settings.service_prompt_assistants
            ADD CONSTRAINT ck_service_prompt_assistants_gommo_agent_id
            CHECK (gommo_agent_id IS NULL OR gommo_agent_id > 0);
    END IF;
END $$;

SELECT id, service_id, provider_code, gommo_agent_id, gommo_agent_id_base
  FROM settings.service_prompt_assistants
 ORDER BY updated_at DESC;
