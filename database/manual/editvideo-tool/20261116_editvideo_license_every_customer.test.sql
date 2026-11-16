-- Verifies 20261116_editvideo_license_every_customer.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_missing integer;
    v_auto integer;
    v_total integer;
BEGIN
    SELECT count(*) INTO v_missing
      FROM crm.customers c
     WHERE NOT EXISTS (SELECT 1 FROM editvideo.licenses l
                        WHERE l.tenant_id = c.tenant_id AND l.customer_id = c.id AND l.status <> 'revoked');
    IF v_missing > 0 THEN
        RAISE EXCEPTION 'editvideo license every customer test: % customers still without a license', v_missing;
    END IF;

    SELECT count(*) INTO v_auto FROM editvideo.licenses WHERE key_prefix = 'tdxv_auto' AND note LIKE 'Tự cấp cho khách trên hệ thống%';
    IF EXISTS (SELECT 1 FROM editvideo.licenses l
                WHERE l.key_prefix = 'tdxv_auto'
                  AND NOT EXISTS (SELECT 1 FROM editvideo.license_events e WHERE e.license_id = l.id AND e.event_type = 'issued')) THEN
        RAISE EXCEPTION 'editvideo license every customer test: issued event missing';
    END IF;

    SELECT count(*) INTO v_total FROM crm.customers;
    RAISE NOTICE 'editvideo license every customer test: OK (% customers, % auto licenses)', v_total, v_auto;
END $$;
