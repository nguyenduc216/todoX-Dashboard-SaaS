-- Verifies 20261117_editvideo_skill_help.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_missing integer;
BEGIN
    SELECT count(*) INTO v_missing
      FROM system.tenants t
     WHERE NOT EXISTS (SELECT 1 FROM editvideo.skills k
                        JOIN editvideo.skill_versions v ON v.skill_id = k.id
                       WHERE k.tenant_id = t.id AND k.slug = 'todox-tro-giup' AND k.is_active);
    IF v_missing > 0 THEN
        RAISE EXCEPTION 'editvideo help skill test: todox-tro-giup has no published version in % tenant(s)', v_missing;
    END IF;

    RAISE NOTICE 'editvideo help skill test: OK';
END $$;
