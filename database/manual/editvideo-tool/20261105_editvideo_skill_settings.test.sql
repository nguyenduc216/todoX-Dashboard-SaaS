-- Verifies 20261105_editvideo_skill_settings.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_tenant uuid;
    v_skill uuid;
    v_settings jsonb;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'editvideo' AND table_name = 'skills' AND column_name = 'settings')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'editvideo' AND table_name = 'skill_versions' AND column_name = 'settings') THEN
        RAISE EXCEPTION 'editvideo skill settings test: column missing';
    END IF;

    SELECT id INTO v_tenant FROM system.tenants LIMIT 1;
    IF v_tenant IS NULL THEN
        RAISE NOTICE 'editvideo skill settings test: no tenant, behavioural check skipped';
        RETURN;
    END IF;

    INSERT INTO editvideo.skills (tenant_id, slug, title, description, content, settings)
    VALUES (v_tenant, 'todox-selftest-settings', 'Self test', 'test', 'body', '{"aspect":"16:9"}') RETURNING id INTO v_skill;
    INSERT INTO editvideo.skill_versions (tenant_id, skill_id, version_no, version_name, title, description, content)
    VALUES (v_tenant, v_skill, 1, 'v1', 'Self test', 'test', 'body');

    SELECT settings INTO v_settings FROM editvideo.skill_versions WHERE skill_id = v_skill;
    IF v_settings IS DISTINCT FROM '{}'::jsonb THEN
        RAISE EXCEPTION 'editvideo skill settings test: version default is not an empty object';
    END IF;

    DELETE FROM editvideo.skills WHERE id = v_skill;
    RAISE NOTICE 'editvideo skill settings test: OK';
END $$;
