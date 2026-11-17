-- Verifies 20261117_editvideo_projects_videos.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_tenant uuid;
    v_customer uuid;
    v_project uuid;
BEGIN
    IF to_regclass('editvideo.projects') IS NULL OR to_regclass('editvideo.project_videos') IS NULL THEN
        RAISE EXCEPTION 'editvideo projects test: table missing';
    END IF;

    SELECT c.tenant_id, c.id INTO v_tenant, v_customer FROM crm.customers c LIMIT 1;
    IF v_customer IS NULL THEN
        RAISE NOTICE 'editvideo projects test: no customer, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.projects (tenant_id, customer_id, project_key, project_name)
    VALUES (v_tenant, v_customer, '__test-project__', 'Test') RETURNING id INTO v_project;
    INSERT INTO editvideo.project_videos (tenant_id, customer_id, project_id, client_video_id, title, file_name)
    VALUES (v_tenant, v_customer, v_project, '__test-video__', 'Test', 'test.mp4');

    BEGIN
        INSERT INTO editvideo.project_videos (tenant_id, customer_id, project_id, client_video_id, title, file_name, sync_status)
        VALUES (v_tenant, v_customer, v_project, '__test-video-2__', 'Test', 'test.mp4', 'bad');
        RAISE EXCEPTION 'editvideo projects test: bad sync_status accepted';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    DELETE FROM editvideo.projects WHERE id = v_project;
    IF EXISTS (SELECT 1 FROM editvideo.project_videos WHERE client_video_id = '__test-video__' AND customer_id = v_customer) THEN
        RAISE EXCEPTION 'editvideo projects test: cascade delete failed';
    END IF;

    RAISE NOTICE 'editvideo projects test: OK';
END $$;
