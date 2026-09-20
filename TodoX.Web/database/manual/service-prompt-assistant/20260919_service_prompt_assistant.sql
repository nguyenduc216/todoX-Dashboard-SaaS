-- Service Prompt Assistant is additive and must be reviewed/executed manually.
-- This script intentionally does not alter render, media, billing, or provider tables.

CREATE SCHEMA IF NOT EXISTS settings;

CREATE TABLE IF NOT EXISTS settings.service_prompt_assistants
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    service_id uuid NOT NULL REFERENCES catalog.services(id) ON DELETE CASCADE,
    enabled boolean NOT NULL DEFAULT false,
    provider_code text NOT NULL,
    model_code text NOT NULL DEFAULT '',
    temperature numeric(5, 4),
    max_tokens integer,
    max_repair_attempts integer NOT NULL DEFAULT 1,
    active_training_version_id uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_service_prompt_assistants_service UNIQUE (service_id),
    CONSTRAINT ck_service_prompt_assistants_repair_attempts CHECK (max_repair_attempts BETWEEN 0 AND 3),
    CONSTRAINT ck_service_prompt_assistants_temperature CHECK (temperature IS NULL OR (temperature >= 0 AND temperature <= 2)),
    CONSTRAINT ck_service_prompt_assistants_max_tokens CHECK (max_tokens IS NULL OR max_tokens > 0)
);

CREATE TABLE IF NOT EXISTS settings.service_prompt_training_versions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    service_prompt_assistant_id uuid NOT NULL REFERENCES settings.service_prompt_assistants(id) ON DELETE CASCADE,
    version_no integer NOT NULL,
    version_name text,
    status text NOT NULL DEFAULT 'DRAFT',
    template_file_name text NOT NULL,
    template_json jsonb NOT NULL,
    description_file_name text NOT NULL,
    description_content text NOT NULL,
    created_by uuid,
    created_at timestamptz NOT NULL DEFAULT now(),
    published_at timestamptz,
    CONSTRAINT uq_service_prompt_training_versions_number UNIQUE (service_prompt_assistant_id, version_no),
    CONSTRAINT uq_service_prompt_training_versions_assistant_id UNIQUE (service_prompt_assistant_id, id),
    CONSTRAINT ck_service_prompt_training_versions_status CHECK (status IN ('DRAFT', 'PUBLISHED', 'ARCHIVED')),
    CONSTRAINT ck_service_prompt_training_versions_template_root CHECK (jsonb_typeof(template_json) IN ('object', 'array'))
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_service_prompt_assistants_id_service
    ON settings.service_prompt_assistants(id, service_id);

CREATE UNIQUE INDEX IF NOT EXISTS uq_service_prompt_training_versions_assistant_id
    ON settings.service_prompt_training_versions(service_prompt_assistant_id, id);

DO $$
BEGIN
    IF EXISTS
    (
        SELECT 1
          FROM pg_constraint
         WHERE conname = 'fk_service_prompt_assistants_active_version'
           AND conrelid = 'settings.service_prompt_assistants'::regclass
    ) THEN
        ALTER TABLE settings.service_prompt_assistants
            DROP CONSTRAINT fk_service_prompt_assistants_active_version;
    END IF;

    IF NOT EXISTS
    (
        SELECT 1
          FROM pg_constraint
         WHERE conname = 'fk_service_prompt_assistants_active_version'
           AND conrelid = 'settings.service_prompt_assistants'::regclass
    ) THEN
        ALTER TABLE settings.service_prompt_assistants
            ADD CONSTRAINT fk_service_prompt_assistants_active_version
            FOREIGN KEY (id, active_training_version_id)
            REFERENCES settings.service_prompt_training_versions(service_prompt_assistant_id, id)
            DEFERRABLE INITIALLY DEFERRED;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_service_prompt_training_versions_published
    ON settings.service_prompt_training_versions(service_prompt_assistant_id)
    WHERE status = 'PUBLISHED';

CREATE INDEX IF NOT EXISTS ix_service_prompt_training_versions_status
    ON settings.service_prompt_training_versions(service_prompt_assistant_id, status, created_at DESC);

CREATE TABLE IF NOT EXISTS settings.service_prompt_generations
(
    id uuid PRIMARY KEY,
    service_id uuid NOT NULL REFERENCES catalog.services(id) ON DELETE CASCADE,
    service_prompt_assistant_id uuid NOT NULL REFERENCES settings.service_prompt_assistants(id) ON DELETE CASCADE,
    training_version_id uuid NOT NULL REFERENCES settings.service_prompt_training_versions(id),
    user_id uuid,
    customer_id uuid,
    provider_code text NOT NULL,
    model_code text NOT NULL,
    user_input text NOT NULL,
    request_snapshot_sanitized jsonb NOT NULL DEFAULT '{}'::jsonb,
    raw_response_sanitized text,
    generated_json jsonb,
    validation_status text NOT NULL,
    validation_errors_json jsonb,
    repair_attempt_count integer NOT NULL DEFAULT 0,
    prompt_tokens integer,
    completion_tokens integer,
    total_tokens integer,
    status text NOT NULL,
    error_code text,
    error_message text,
    created_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz,
    CONSTRAINT ck_service_prompt_generations_validation_status CHECK (validation_status IN ('PASS', 'FAIL')),
    CONSTRAINT ck_service_prompt_generations_repair_attempts CHECK (repair_attempt_count BETWEEN 0 AND 3)
);

ALTER TABLE settings.service_prompt_generations
    DROP CONSTRAINT IF EXISTS fk_service_prompt_generations_service_assistant;

ALTER TABLE settings.service_prompt_generations
    ADD CONSTRAINT fk_service_prompt_generations_service_assistant
    FOREIGN KEY (service_prompt_assistant_id, service_id)
    REFERENCES settings.service_prompt_assistants(id, service_id);

CREATE INDEX IF NOT EXISTS ix_service_prompt_generations_service_created
    ON settings.service_prompt_generations(service_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_service_prompt_generations_training_version
    ON settings.service_prompt_generations(training_version_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_service_prompt_generations_status
    ON settings.service_prompt_generations(status, created_at DESC);
