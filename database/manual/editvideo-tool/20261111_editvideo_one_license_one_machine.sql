-- TodoX EditVideo: one account = one license key = one computer (2026-11-11).
-- Additive: one new partial unique index + a data update on editvideo.licenses. No column or table is altered.
--
-- 1. Licenses without an active phone stop offering a phone slot (no phone app exists; a phone slot could be
--    used to run a second computer).
-- 2. A customer can hold at most one license that is not revoked. Fails (and rolls back) if a customer already
--    has two; resolve those in the dashboard first.

UPDATE editvideo.licenses l
   SET max_mobile_devices = 0, updated_at = now()
 WHERE l.max_mobile_devices <> 0
   AND l.max_desktop_devices > 0
   AND NOT EXISTS (SELECT 1 FROM editvideo.devices d
                    WHERE d.license_id = l.id AND d.status = 'active' AND d.device_kind = 'mobile');

CREATE UNIQUE INDEX IF NOT EXISTS ux_editvideo_licenses_one_open_per_customer
    ON editvideo.licenses(tenant_id, customer_id)
    WHERE status <> 'revoked';
