-- Verifies 20261116_editvideo_ai_music.sql; raises (and so rolls back) on any problem. Also prints the point rates
-- the EditVideo tool charges from (billing.point_rate_config) and its editvideo.* settings, for the admin.
DO $$
DECLARE
    r record;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                    WHERE conname = 'ck_editvideo_ai_tasks_kind' AND pg_get_constraintdef(oid) LIKE '%music%') THEN
        RAISE EXCEPTION 'editvideo ai music test: kind check does not accept music';
    END IF;

    FOR r IN SELECT resource_type, quality_tier, rate, unit, is_active FROM billing.point_rate_config ORDER BY 1, 2 LOOP
        RAISE NOTICE 'rate % % = % % (active %)', r.resource_type, r.quality_tier, r.rate, r.unit, r.is_active;
    END LOOP;

    FOR r IN SELECT setting_key, setting_value FROM system.app_settings WHERE setting_key LIKE 'editvideo.%' ORDER BY 1 LOOP
        RAISE NOTICE 'setting % = %', r.setting_key, r.setting_value;
    END LOOP;

    RAISE NOTICE 'editvideo ai music test: OK';
END $$;
