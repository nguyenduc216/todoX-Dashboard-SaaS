-- Verifies 20261103_editvideo_licensing.sql. Raises (and so rolls back) when anything is missing.
-- Also exercises the slot index with a throwaway row inside the same transaction.

DO $$
DECLARE
    missing text;
    v_tenant uuid;
    v_customer uuid;
    v_license uuid;
BEGIN
    SELECT string_agg(t, ', ') INTO missing
      FROM unnest(ARRAY['editvideo.licenses', 'editvideo.devices', 'editvideo.license_events']) AS t
     WHERE to_regclass(t) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'editvideo test: missing tables: %', missing;
    END IF;

    SELECT string_agg(i, ', ') INTO missing
      FROM unnest(ARRAY[
            'editvideo.ux_editvideo_licenses_key_hash',
            'editvideo.ix_editvideo_licenses_tenant_customer',
            'editvideo.ux_editvideo_devices_active_hash',
            'editvideo.ux_editvideo_devices_token_hash',
            'editvideo.ix_editvideo_devices_license',
            'editvideo.ix_editvideo_license_events_license_time',
            'editvideo.ix_editvideo_license_events_type_time']) AS i
     WHERE to_regclass(i) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'editvideo test: missing indexes: %', missing;
    END IF;

    -- Behavioural check against a real tenant/customer. The probe rows are deleted again below,
    -- so an applied run leaves no test data behind.
    SELECT c.tenant_id, c.id INTO v_tenant, v_customer FROM crm.customers c LIMIT 1;
    IF v_customer IS NULL THEN
        RAISE NOTICE 'editvideo test: no customer rows, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.licenses (tenant_id, customer_id, license_key_hash, key_prefix)
    VALUES (v_tenant, v_customer, 'selftest-' || gen_random_uuid(), 'TDXV-TEST0')
    RETURNING id INTO v_license;

    INSERT INTO editvideo.devices (tenant_id, license_id, device_hash, device_kind)
    VALUES (v_tenant, v_license, repeat('0', 64), 'desktop');

    BEGIN
        INSERT INTO editvideo.devices (tenant_id, license_id, device_hash, device_kind)
        VALUES (v_tenant, v_license, repeat('0', 64), 'desktop');
        RAISE EXCEPTION 'editvideo test: duplicate active device was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL; -- expected
    END;

    DELETE FROM editvideo.licenses WHERE id = v_license; -- cascades to devices
    IF EXISTS (SELECT 1 FROM editvideo.devices WHERE license_id = v_license) THEN
        RAISE EXCEPTION 'editvideo test: device rows did not cascade';
    END IF;

    RAISE NOTICE 'editvideo test: OK';
END $$;
