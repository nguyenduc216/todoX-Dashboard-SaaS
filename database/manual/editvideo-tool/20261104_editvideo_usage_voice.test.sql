-- Verifies 20261104_editvideo_usage_voice.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    missing text;
    v_license uuid;
    v_tenant uuid;
    v_customer uuid;
    v_session uuid;
BEGIN
    SELECT string_agg(t, ', ') INTO missing
      FROM unnest(ARRAY['editvideo.usage_sessions', 'editvideo.usage_logs', 'editvideo.voice_jobs', 'editvideo.admin_alerts']) AS t
     WHERE to_regclass(t) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'editvideo usage test: missing tables: %', missing;
    END IF;

    SELECT l.id, l.tenant_id, l.customer_id INTO v_license, v_tenant, v_customer FROM editvideo.licenses l LIMIT 1;
    IF v_license IS NULL THEN
        RAISE NOTICE 'editvideo usage test: no license rows, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.usage_sessions (tenant_id, license_id, customer_id, tool)
    VALUES (v_tenant, v_license, v_customer, 'selftest') RETURNING id INTO v_session;
    INSERT INTO editvideo.usage_logs (tenant_id, session_id, level, code) VALUES (v_tenant, v_session, 'info', 'selftest');
    DELETE FROM editvideo.usage_sessions WHERE id = v_session;
    IF EXISTS (SELECT 1 FROM editvideo.usage_logs WHERE session_id = v_session) THEN
        RAISE EXCEPTION 'editvideo usage test: logs did not cascade';
    END IF;

    RAISE NOTICE 'editvideo usage test: OK';
END $$;
