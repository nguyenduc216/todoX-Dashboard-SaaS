-- Feature settings are seeded OFF. Application code must honor these flags.
BEGIN;
INSERT INTO system.app_settings(id,setting_key,setting_group,setting_type,setting_value,description,is_active,created_at)
SELECT gen_random_uuid(),x.key,'render','boolean','false',x.description,true,now()
FROM (VALUES
 ('features.scene_render_versioning','Enable immutable scene image history and selection.'),
 ('features.scene_video_versioning','Enable image-to-video version history and selection.'),
 ('features.final_video_versioning','Enable final composition version history and selection.')
) x(key,description)
WHERE NOT EXISTS(SELECT 1 FROM system.app_settings s WHERE s.setting_key=x.key);
COMMIT;

