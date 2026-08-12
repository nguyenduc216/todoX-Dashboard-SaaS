CREATE SCHEMA IF NOT EXISTS system;

CREATE TABLE IF NOT EXISTS system.ai_provider_credential_master_key(
    key_version integer PRIMARY KEY,
    key_material bytea NOT NULL,
    algorithm text NOT NULL DEFAULT 'AES-256-GCM',
    status text NOT NULL DEFAULT 'active',
    created_at timestamptz NOT NULL DEFAULT now(),
    rotated_at timestamptz NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb
);

CREATE UNIQUE INDEX IF NOT EXISTS ai_provider_credential_master_key_one_active_idx
    ON system.ai_provider_credential_master_key(status)
    WHERE status = 'active';
