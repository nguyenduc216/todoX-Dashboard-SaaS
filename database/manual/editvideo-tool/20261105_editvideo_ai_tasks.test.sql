-- Verifies 20261105_editvideo_ai_tasks.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_license uuid;
    v_tenant uuid;
    v_customer uuid;
    v_task uuid;
BEGIN
    IF to_regclass('editvideo.ai_tasks') IS NULL THEN
        RAISE EXCEPTION 'editvideo ai test: missing table editvideo.ai_tasks';
    END IF;

    SELECT l.id, l.tenant_id, l.customer_id INTO v_license, v_tenant, v_customer FROM editvideo.licenses l LIMIT 1;
    IF v_license IS NULL THEN
        RAISE NOTICE 'editvideo ai test: no license rows, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.ai_tasks (tenant_id, license_id, customer_id, kind, client_key, model, credits, points)
    VALUES (v_tenant, v_license, v_customer, 'image', 'selftest-key', 'selftest', 500, 0.1) RETURNING id INTO v_task;

    BEGIN
        INSERT INTO editvideo.ai_tasks (tenant_id, license_id, customer_id, kind, client_key, model, credits, points)
        VALUES (v_tenant, v_license, v_customer, 'image', 'selftest-key', 'selftest', 500, 0.1);
        RAISE EXCEPTION 'editvideo ai test: duplicate client_key was accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO editvideo.ai_tasks (tenant_id, license_id, customer_id, kind, model, credits, points)
        VALUES (v_tenant, v_license, v_customer, 'audio', 'selftest', 1, 1);
        RAISE EXCEPTION 'editvideo ai test: unknown kind was accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    DELETE FROM editvideo.ai_tasks WHERE id = v_task;
    RAISE NOTICE 'editvideo ai test: OK';
END $$;
