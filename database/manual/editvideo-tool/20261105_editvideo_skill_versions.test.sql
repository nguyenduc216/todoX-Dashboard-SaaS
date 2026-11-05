-- Verifies 20261105_editvideo_skill_versions.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_tenant uuid;
    v_skill uuid;
    v_version uuid;
BEGIN
    IF to_regclass('editvideo.skill_versions') IS NULL THEN
        RAISE EXCEPTION 'editvideo skill versions test: table missing';
    END IF;

    SELECT id INTO v_tenant FROM system.tenants LIMIT 1;
    IF v_tenant IS NULL THEN
        RAISE NOTICE 'editvideo skill versions test: no tenant, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.skills (tenant_id, slug, title, description, content)
    VALUES (v_tenant, 'todox-selftest-versions', 'Self test', 'test', 'body') RETURNING id INTO v_skill;
    INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content)
    VALUES (v_tenant, v_skill, 1, 'v1', 'Self test', 'test', 'body') RETURNING id INTO v_version;

    BEGIN
        UPDATE editvideo.skill_versions SET content = 'changed' WHERE id = v_version;
        RAISE EXCEPTION 'editvideo skill versions test: a version could be edited';
    EXCEPTION WHEN restrict_violation THEN
        NULL;
    END;

    BEGIN
        INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content)
        VALUES (v_tenant, v_skill, 1, 'dup', 'x', 'x', 'x');
        RAISE EXCEPTION 'editvideo skill versions test: duplicate version number accepted';
    EXCEPTION WHEN unique_violation THEN
        NULL;
    END;

    DELETE FROM editvideo.skills WHERE id = v_skill;
    IF EXISTS (SELECT 1 FROM editvideo.skill_versions WHERE skill_id = v_skill) THEN
        RAISE EXCEPTION 'editvideo skill versions test: versions did not cascade with the skill';
    END IF;

    RAISE NOTICE 'editvideo skill versions test: OK';
END $$;
