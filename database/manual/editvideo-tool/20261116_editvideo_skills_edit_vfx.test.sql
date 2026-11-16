-- Verifies 20261116_editvideo_skills_edit_vfx.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_slug text;
    v_missing integer;
BEGIN
    FOREACH v_slug IN ARRAY ARRAY['todox-dung-video', 'todox-vfx']
    LOOP
        SELECT count(*) INTO v_missing
          FROM system.tenants t
         WHERE NOT EXISTS (SELECT 1 FROM editvideo.skills k
                            JOIN editvideo.skill_versions v ON v.skill_id = k.id
                           WHERE k.tenant_id = t.id AND k.slug = v_slug AND k.is_active);
        IF v_missing > 0 THEN
            RAISE EXCEPTION 'editvideo edit/vfx skills test: % has no published version in % tenant(s)', v_slug, v_missing;
        END IF;
    END LOOP;

    RAISE NOTICE 'editvideo edit/vfx skills test: OK (% skills active)', (SELECT count(*) FROM editvideo.skills WHERE is_active);
END $$;
