-- Verifies 20261116_editvideo_music_price_x2.sql; raises (and so rolls back) on any problem.
DO $$
BEGIN
    IF (SELECT count(*) FROM system.app_settings
         WHERE is_active AND (setting_key, setting_value) IN (('editvideo.ai.music_markup', '2'), ('editvideo.ai.vnd_per_point', '1000'),
                                                             ('editvideo.ai.vnd_per_credit', '1'), ('editvideo.ai.cost_floor_markup', '2'))) <> 4 THEN
        RAISE EXCEPTION 'editvideo music price x2 test: settings not stored';
    END IF;
    RAISE NOTICE 'editvideo music price x2 test: OK';
END $$;
