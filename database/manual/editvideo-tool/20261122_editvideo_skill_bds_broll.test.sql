-- Verifies 20261122_editvideo_skill_bds_broll.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_missing integer;
BEGIN
    SELECT count(*) INTO v_missing
      FROM system.tenants t
     WHERE NOT EXISTS (SELECT 1 FROM editvideo.skills k
                        JOIN editvideo.skill_versions v ON v.skill_id = k.id
                       WHERE k.tenant_id = t.id AND k.slug = 'todox-bds-chia-se-canh-chen' AND k.is_active
                         AND k.settings->>'captionStyle' = 'sparkle');
    IF v_missing > 0 THEN
        RAISE EXCEPTION 'editvideo broll skill test: todox-bds-chia-se-canh-chen has no published version in % tenant(s)', v_missing;
    END IF;

    RAISE NOTICE 'editvideo broll skill test: OK';
END $$;
