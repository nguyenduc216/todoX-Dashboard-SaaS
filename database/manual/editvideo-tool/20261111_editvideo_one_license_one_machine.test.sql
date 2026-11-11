-- Verifies 20261111_editvideo_one_license_one_machine.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_tenant uuid;
    v_customer uuid;
    v_blocked boolean := false;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'editvideo' AND indexname = 'ux_editvideo_licenses_one_open_per_customer') THEN
        RAISE EXCEPTION 'one-license test: index missing';
    END IF;

    IF EXISTS (SELECT 1 FROM editvideo.licenses l
                WHERE l.max_mobile_devices <> 0 AND l.max_desktop_devices > 0
                  AND NOT EXISTS (SELECT 1 FROM editvideo.devices d WHERE d.license_id = l.id AND d.status = 'active' AND d.device_kind = 'mobile')) THEN
        RAISE EXCEPTION 'one-license test: a license still offers an unused phone slot';
    END IF;

    SELECT c.tenant_id, c.id INTO v_tenant, v_customer FROM crm.customers c
     WHERE NOT EXISTS (SELECT 1 FROM editvideo.licenses l WHERE l.customer_id = c.id)
     LIMIT 1;
    IF v_customer IS NULL THEN
        RAISE NOTICE 'one-license test: no customer without license, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.licenses (tenant_id, customer_id, license_key_hash, key_prefix)
    VALUES (v_tenant, v_customer, 'selftest-one-license-a', 'SELFTEST');
    BEGIN
        INSERT INTO editvideo.licenses (tenant_id, customer_id, license_key_hash, key_prefix)
        VALUES (v_tenant, v_customer, 'selftest-one-license-b', 'SELFTEST');
    EXCEPTION WHEN unique_violation THEN
        v_blocked := true;
    END;
    IF NOT v_blocked THEN
        RAISE EXCEPTION 'one-license test: second open license was not blocked';
    END IF;

    -- After revoking the first, a new one is allowed.
    UPDATE editvideo.licenses SET status = 'revoked' WHERE license_key_hash = 'selftest-one-license-a';
    INSERT INTO editvideo.licenses (tenant_id, customer_id, license_key_hash, key_prefix)
    VALUES (v_tenant, v_customer, 'selftest-one-license-b', 'SELFTEST');

    DELETE FROM editvideo.licenses WHERE license_key_hash IN ('selftest-one-license-a', 'selftest-one-license-b');
    RAISE NOTICE 'one-license test: OK';
END $$;
