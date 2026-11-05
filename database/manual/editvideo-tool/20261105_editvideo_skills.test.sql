-- Verifies 20261105_editvideo_skills.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_tenant uuid;
    v_skill uuid;
BEGIN
    IF to_regclass('editvideo.skills') IS NULL THEN
        RAISE EXCEPTION 'editvideo skills test: table editvideo.skills missing';
    END IF;

    SELECT id INTO v_tenant FROM system.tenants LIMIT 1;
    IF v_tenant IS NULL THEN
        RAISE NOTICE 'editvideo skills test: no tenant, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.skills (tenant_id, slug, title, description, content)
    VALUES (v_tenant, 'todox-selftest-skill', 'Self test', 'test', 'body') RETURNING id INTO v_skill;

    BEGIN
        INSERT INTO editvideo.skills (tenant_id, slug, title, description, content)
        VALUES (v_tenant, 'Bad Slug', 'x', 'x', 'x');
        RAISE EXCEPTION 'editvideo skills test: slug check did not reject a bad slug';
    EXCEPTION WHEN check_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO editvideo.skills (tenant_id, slug, title, description, content)
        VALUES (v_tenant, 'todox-selftest-skill', 'dup', 'x', 'x');
        RAISE EXCEPTION 'editvideo skills test: duplicate slug accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    DELETE FROM editvideo.skills WHERE id = v_skill;
    RAISE NOTICE 'editvideo skills test: OK';
END $$;
