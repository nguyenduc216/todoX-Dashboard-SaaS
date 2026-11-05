-- TodoX.EditVideoTool: AI image/video tasks proxied to Gommo (79AI) through TodoX's own account.
-- Additive and idempotent; run through tools/prod-sql/Invoke-TodoXSql.ps1. No existing table is altered.
-- Only ownership, price and status live here: the generated files stay on Gommo's CDN and go straight to the
-- customer's machine.

CREATE TABLE IF NOT EXISTS editvideo.ai_tasks (
    id              uuid          PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       uuid          NOT NULL REFERENCES system.tenants(id),
    license_id      uuid          NOT NULL REFERENCES editvideo.licenses(id) ON DELETE CASCADE,
    device_id       uuid          NULL REFERENCES editvideo.devices(id) ON DELETE SET NULL,
    customer_id     uuid          NOT NULL,
    kind            text          NOT NULL,                     -- image | video
    client_key      text          NULL,                         -- the tool's retry key: same key, same task
    model           text          NOT NULL,
    options         jsonb         NULL,                         -- ratio / resolution / mode / duration chosen
    credits         numeric(18,2) NOT NULL,                     -- Gommo price quoted at create
    points          numeric(18,2) NOT NULL,                     -- TodoX points charged once on success
    status          text          NOT NULL DEFAULT 'submitting', -- submitting | running | succeeded | failed
    gommo_id_base   text          NULL,
    outputs         jsonb         NULL,                         -- result URLs on Gommo's CDN
    error_code      text          NULL,
    error_message   text          NULL,
    charged         boolean       NOT NULL DEFAULT false,
    created_at      timestamptz   NOT NULL DEFAULT now(),
    completed_at    timestamptz   NULL,
    CONSTRAINT ck_editvideo_ai_tasks_kind CHECK (kind IN ('image', 'video'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_ai_tasks_client_key
    ON editvideo.ai_tasks(license_id, client_key) WHERE client_key IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_editvideo_ai_tasks_license_time
    ON editvideo.ai_tasks(license_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_editvideo_ai_tasks_gommo
    ON editvideo.ai_tasks(gommo_id_base) WHERE gommo_id_base IS NOT NULL;
