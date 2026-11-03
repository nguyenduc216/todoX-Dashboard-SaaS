-- TodoX.EditVideoTool licensing (P1). Additive and idempotent; run manually on todo_saas.
-- Creates a new schema only. No existing table is altered.
--
-- licenses : one row per sold license key (key stored as SHA-256 hash only).
-- devices  : machines/phones bound to a license. Slots are counted per device_kind
--            (default 1 desktop + 1 mobile). Only status='active' rows hold a slot.
-- events   : audit trail incl. rejected activations used to flag over-limit sharing.

CREATE SCHEMA IF NOT EXISTS editvideo;

CREATE TABLE IF NOT EXISTS editvideo.licenses (
    id                   uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id            uuid        NOT NULL REFERENCES system.tenants(id),
    customer_id          uuid        NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    license_key_hash     text        NOT NULL,
    key_prefix           text        NOT NULL,
    plan_code            text        NOT NULL DEFAULT 'standard',
    max_desktop_devices  integer     NOT NULL DEFAULT 1,
    max_mobile_devices   integer     NOT NULL DEFAULT 1,
    status               text        NOT NULL DEFAULT 'active',   -- active | suspended | revoked
    expires_at           timestamptz NULL,
    note                 text        NULL,
    created_by           uuid        NULL,
    created_at           timestamptz NOT NULL DEFAULT now(),
    updated_at           timestamptz NOT NULL DEFAULT now(),
    revoked_at           timestamptz NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_licenses_key_hash
    ON editvideo.licenses(license_key_hash);

CREATE INDEX IF NOT EXISTS ix_editvideo_licenses_tenant_customer
    ON editvideo.licenses(tenant_id, customer_id);

CREATE TABLE IF NOT EXISTS editvideo.devices (
    id                       uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id                uuid        NOT NULL REFERENCES system.tenants(id),
    license_id               uuid        NOT NULL REFERENCES editvideo.licenses(id) ON DELETE CASCADE,
    device_hash              text        NOT NULL,
    device_kind              text        NOT NULL,                -- desktop | mobile
    device_name              text        NULL,
    os                       text        NULL,
    app_version              text        NULL,
    status                   text        NOT NULL DEFAULT 'active', -- active | released | revoked
    access_token_hash        text        NULL,
    access_token_expires_at  timestamptz NULL,
    activated_at             timestamptz NOT NULL DEFAULT now(),
    last_seen_at             timestamptz NOT NULL DEFAULT now(),
    last_ip                  text        NULL,
    released_at              timestamptz NULL,
    released_reason          text        NULL
);

-- A machine can hold at most one active slot per license.
CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_devices_active_hash
    ON editvideo.devices(license_id, device_hash)
    WHERE status = 'active';

CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_devices_token_hash
    ON editvideo.devices(access_token_hash)
    WHERE access_token_hash IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_editvideo_devices_license
    ON editvideo.devices(license_id, status);

CREATE TABLE IF NOT EXISTS editvideo.license_events (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id    uuid        NOT NULL REFERENCES system.tenants(id),
    license_id   uuid        NULL REFERENCES editvideo.licenses(id) ON DELETE CASCADE,
    device_id    uuid        NULL REFERENCES editvideo.devices(id) ON DELETE SET NULL,
    event_type   text        NOT NULL,  -- issued | activated | reactivated | released | revoked_device
                                        -- | limit_exceeded | device_mismatch | status_changed | limits_changed
    ip           text        NULL,
    detail       jsonb       NULL,
    actor_user_id uuid       NULL,
    created_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_editvideo_license_events_license_time
    ON editvideo.license_events(license_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_editvideo_license_events_type_time
    ON editvideo.license_events(event_type, created_at DESC);
