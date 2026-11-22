-- Verifies 20261122_editvideo_skill_geo_location.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_missing integer;
BEGIN
    SELECT count(*) INTO v_missing
      FROM system.tenants t
     WHERE NOT EXISTS (SELECT 1 FROM editvideo.skills k
                        JOIN editvideo.skill_versions v ON v.skill_id = k.id
                       WHERE k.tenant_id = t.id AND k.slug = 'todox-bds-vi-tri' AND k.is_active);
    IF v_missing > 0 THEN
        RAISE EXCEPTION 'editvideo geo skill test: todox-bds-vi-tri has no published version in % tenant(s)', v_missing;
    END IF;

    RAISE NOTICE 'editvideo geo skill test: OK';
END $$;
