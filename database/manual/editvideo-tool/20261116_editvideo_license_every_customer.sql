-- TodoX.EditVideoTool: every customer on the system gets the EditVideo license (one per customer, one machine).
-- Plan and expiry follow the customer's current package (its cycle end, Vietnam time); without a package: plan
-- 'standard', no expiry. The key itself is random and never known: customers sign in with their TodoX account, or
-- take a key on "EditVideo của tôi" (Lấy license key). Data only, idempotent: customers who already have an open
-- license are skipped. Run through tools/prod-sql/Invoke-TodoXSql.ps1.

WITH current_package AS (
    SELECT DISTINCT ON (p.customer_id) p.customer_id, p.package_code, p.cycle_end
      FROM affiliate.package_purchases p
     WHERE p.status = 'paid' AND (p.cycle_end IS NULL OR p.cycle_end >= current_date)
     ORDER BY p.customer_id, p.purchased_at DESC
), issued AS (
    INSERT INTO editvideo.licenses
        (tenant_id, customer_id, license_key_hash, key_prefix, plan_code, max_desktop_devices, max_mobile_devices, expires_at, note)
    SELECT c.tenant_id, c.id,
           encode(sha256(convert_to('auto:' || gen_random_uuid()::text || ':' || clock_timestamp()::text, 'UTF8')), 'hex'),
           'tdxv_auto',
           COALESCE(cp.package_code, 'standard'),
           1, 0,
           CASE WHEN cp.cycle_end IS NULL THEN NULL
                ELSE ((cp.cycle_end + 1)::timestamp AT TIME ZONE 'Asia/Ho_Chi_Minh') - interval '1 microsecond' END,
           'Tự cấp cho khách trên hệ thống (16/11/2026)'
      FROM crm.customers c
      LEFT JOIN current_package cp ON cp.customer_id = c.id
     WHERE NOT EXISTS (SELECT 1 FROM editvideo.licenses l
                        WHERE l.tenant_id = c.tenant_id AND l.customer_id = c.id AND l.status <> 'revoked')
    RETURNING id, tenant_id, plan_code
)
INSERT INTO editvideo.license_events (tenant_id, license_id, event_type, detail)
SELECT tenant_id, id, 'issued', jsonb_build_object('planCode', plan_code, 'by', 'bulk-20261116')
  FROM issued;
