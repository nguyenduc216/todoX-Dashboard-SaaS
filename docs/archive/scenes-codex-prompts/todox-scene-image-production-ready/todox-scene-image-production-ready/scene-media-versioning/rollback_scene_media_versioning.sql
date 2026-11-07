-- Safe rollback: disable feature settings only. Version history and media are retained.
BEGIN;
UPDATE system.app_settings SET setting_value='false',is_active=true
WHERE setting_key IN ('features.scene_render_versioning','features.scene_video_versioning','features.final_video_versioning');
COMMIT;

SELECT setting_key,setting_value FROM system.app_settings
WHERE setting_key IN ('features.scene_render_versioning','features.scene_video_versioning','features.final_video_versioning')
ORDER BY setting_key;

