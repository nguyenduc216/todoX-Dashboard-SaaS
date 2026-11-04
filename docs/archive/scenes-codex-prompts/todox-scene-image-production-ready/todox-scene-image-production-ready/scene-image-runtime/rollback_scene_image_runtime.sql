\set ON_ERROR_STOP on

-- Safe application-level rollback. No history, billing, job or media rows are deleted.
BEGIN;

UPDATE system.app_settings
   SET setting_value='false'
 WHERE setting_key IN (
       'features.scene_render_versioning',
       'features.scene_video_versioning',
       'features.final_video_versioning');

COMMIT;

SELECT setting_key, setting_value, is_active
  FROM system.app_settings
 WHERE setting_key IN (
       'features.scene_render_versioning',
       'features.scene_video_versioning',
       'features.final_video_versioning')
 ORDER BY setting_key;

