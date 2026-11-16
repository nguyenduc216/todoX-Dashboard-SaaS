-- Verifies 20261116_editvideo_starter_skills.sql; raises (and so rolls back) on any problem.
DO $$
DECLARE
    v_slug text;
    v_missing integer;
    v_steps integer;
BEGIN
    FOREACH v_slug IN ARRAY ARRAY['todox-ban-hang-tiktok', 'todox-bds-gioi-thieu', 'todox-review-san-pham',
                                  'todox-gioi-thieu-doanh-nghiep', 'todox-highlight-su-kien', 'todox-video-ai-quang-cao']
    LOOP
        SELECT count(*) INTO v_missing
          FROM system.tenants t
         WHERE NOT EXISTS (SELECT 1 FROM editvideo.skills k
                            JOIN editvideo.skill_versions v ON v.skill_id = k.id
                           WHERE k.tenant_id = t.id AND k.slug = v_slug AND k.is_active);
        IF v_missing > 0 THEN
            RAISE EXCEPTION 'editvideo starter skills test: % has no published version in % tenant(s)', v_slug, v_missing;
        END IF;

        SELECT min(array_length(regexp_split_to_array(k.content, E'\n## '), 1)) INTO v_steps
          FROM editvideo.skills k WHERE k.slug = v_slug;
        IF v_steps < 3 THEN
            RAISE EXCEPTION 'editvideo starter skills test: % has fewer than 4 steps', v_slug;
        END IF;
    END LOOP;

    IF EXISTS (SELECT 1 FROM pg_class WHERE relname = 'tmp_starter_skills' AND relpersistence = 't') THEN
        RAISE EXCEPTION 'editvideo starter skills test: temp table left behind';
    END IF;

    RAISE NOTICE 'editvideo starter skills test: OK (% skills active)', (SELECT count(*) FROM editvideo.skills WHERE is_active);
END $$;
