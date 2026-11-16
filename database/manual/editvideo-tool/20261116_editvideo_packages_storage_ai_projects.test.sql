-- Verifies 20261116_editvideo_packages_storage_ai_projects.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_tenant uuid;
    v_customer uuid;
    v_file uuid;
    v_used bigint;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'affiliate' AND table_name = 'packages' AND column_name = 'points_granted')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'affiliate' AND table_name = 'packages' AND column_name = 'storage_mb')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'affiliate' AND table_name = 'package_purchases' AND column_name = 'points_granted')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'affiliate' AND table_name = 'package_purchases' AND column_name = 'storage_mb') THEN
        RAISE EXCEPTION 'editvideo packages test: package column missing';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'editvideo' AND table_name = 'ai_tasks' AND column_name = 'provider_project_id') THEN
        RAISE EXCEPTION 'editvideo packages test: ai_tasks.provider_project_id missing';
    END IF;

    IF to_regclass('editvideo.storage_files') IS NULL OR to_regclass('editvideo.ai_projects') IS NULL THEN
        RAISE EXCEPTION 'editvideo packages test: table missing';
    END IF;

    SELECT c.tenant_id, c.id INTO v_tenant, v_customer FROM crm.customers c LIMIT 1;
    IF v_customer IS NULL THEN
        RAISE NOTICE 'editvideo packages test: no customer, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.storage_files (tenant_id, customer_id, project_key, project_name, file_name, relative_path, size_bytes, public_token, expires_at)
    VALUES (v_tenant, v_customer, 'selftest', 'Self test', 'a.jpg', 'selftest/a.jpg', 1234, 'selftest-' || gen_random_uuid(), now() + interval '7 days')
    RETURNING id INTO v_file;
    SELECT coalesce(sum(size_bytes), 0) INTO v_used FROM editvideo.storage_files WHERE customer_id = v_customer AND deleted_at IS NULL AND id = v_file;
    IF v_used <> 1234 THEN
        RAISE EXCEPTION 'editvideo packages test: storage usage not counted';
    END IF;

    INSERT INTO editvideo.ai_projects (tenant_id, customer_id, project_key, project_name, provider_project_id, provider_project_name)
    VALUES (v_tenant, v_customer, 'selftest', 'Self test', 'p1', 'KH - Self test');
    BEGIN
        INSERT INTO editvideo.ai_projects (tenant_id, customer_id, project_key, project_name, provider_project_id, provider_project_name)
        VALUES (v_tenant, v_customer, 'selftest', 'Self test', 'p2', 'KH - Self test');
        RAISE EXCEPTION 'editvideo packages test: duplicate project allowed';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    DELETE FROM editvideo.storage_files WHERE id = v_file;
    DELETE FROM editvideo.ai_projects WHERE customer_id = v_customer AND project_key = 'selftest';
    RAISE NOTICE 'editvideo packages test: OK';
END $$;
