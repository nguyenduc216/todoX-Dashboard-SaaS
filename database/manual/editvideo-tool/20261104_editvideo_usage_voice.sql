-- TodoX.EditVideoTool: per-use session logs, voice (TTS) jobs and admin alerts.
-- Additive and idempotent; run through tools/prod-sql/Invoke-TodoXSql.ps1. No existing table is altered.

-- One row per time a customer's tool starts a piece of work (health check, edit, voice ...).
CREATE TABLE IF NOT EXISTS editvideo.usage_sessions (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id    uuid        NOT NULL REFERENCES system.tenants(id),
    license_id   uuid        NOT NULL REFERENCES editvideo.licenses(id) ON DELETE CASCADE,
    device_id    uuid        NULL REFERENCES editvideo.devices(id) ON DELETE SET NULL,
    customer_id  uuid        NOT NULL,
    tool         text        NOT NULL,              -- health | auto_edit | voice | probe ...
    app_version  text        NULL,
    status       text        NOT NULL DEFAULT 'running', -- running | ok | failed
    summary      jsonb       NULL,
    started_at   timestamptz NOT NULL DEFAULT now(),
    ended_at     timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_editvideo_usage_sessions_license_time
    ON editvideo.usage_sessions(license_id, started_at DESC);

CREATE INDEX IF NOT EXISTS ix_editvideo_usage_sessions_customer_time
    ON editvideo.usage_sessions(tenant_id, customer_id, started_at DESC);

-- Technical log lines for a session. Never video content or narration text.
CREATE TABLE IF NOT EXISTS editvideo.usage_logs (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id   uuid        NOT NULL REFERENCES system.tenants(id),
    session_id  uuid        NOT NULL REFERENCES editvideo.usage_sessions(id) ON DELETE CASCADE,
    level       text        NOT NULL,              -- info | warn | error
    code        text        NOT NULL,
    message     text        NULL,
    data        jsonb       NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_editvideo_usage_logs_session
    ON editvideo.usage_logs(session_id, created_at);

CREATE INDEX IF NOT EXISTS ix_editvideo_usage_logs_level_time
    ON editvideo.usage_logs(level, created_at DESC);

-- Text-to-speech requests made through TodoX's Vbee account; one charge per succeeded job.
CREATE TABLE IF NOT EXISTS editvideo.voice_jobs (
    id                   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            uuid        NOT NULL REFERENCES system.tenants(id),
    license_id           uuid        NOT NULL REFERENCES editvideo.licenses(id) ON DELETE CASCADE,
    device_id            uuid        NULL REFERENCES editvideo.devices(id) ON DELETE SET NULL,
    customer_id          uuid        NOT NULL,
    session_id           uuid        NULL REFERENCES editvideo.usage_sessions(id) ON DELETE SET NULL,
    voice_code           text        NOT NULL,
    quality_tier         text        NOT NULL,
    char_count           integer     NOT NULL,
    points               numeric(18,2) NOT NULL,
    status               text        NOT NULL DEFAULT 'submitted', -- submitted | succeeded | failed
    provider_request_id  text        NULL,
    audio_url            text        NULL,
    error_code           text        NULL,
    error_message        text        NULL,
    charged              boolean     NOT NULL DEFAULT false,
    created_at           timestamptz NOT NULL DEFAULT now(),
    completed_at         timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_editvideo_voice_jobs_provider_request
    ON editvideo.voice_jobs(provider_request_id) WHERE provider_request_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_editvideo_voice_jobs_license_time
    ON editvideo.voice_jobs(license_id, created_at DESC);

-- Things a TodoX admin must act on (e.g. the Vbee account ran out of credit).
CREATE TABLE IF NOT EXISTS editvideo.admin_alerts (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id    uuid        NOT NULL REFERENCES system.tenants(id),
    code         text        NOT NULL,
    message      text        NOT NULL,
    data         jsonb       NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    resolved_at  timestamptz NULL
);

CREATE INDEX IF NOT EXISTS ix_editvideo_admin_alerts_open
    ON editvideo.admin_alerts(tenant_id, created_at DESC) WHERE resolved_at IS NULL;
