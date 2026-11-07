\set ON_ERROR_STOP on
DO $$
DECLARE missing text; bad_selected int; broken_lineage int;
BEGIN
 SELECT string_agg(x.name,', ') INTO missing FROM (VALUES
  ('video_render.scene_image_versions'),('video_render.scene_video_versions'),
  ('video_render.final_video_versions'),('video_render.final_video_version_items')) x(name)
 WHERE to_regclass(x.name) IS NULL;
 IF missing IS NOT NULL THEN RAISE EXCEPTION 'Missing versioning tables: %',missing; END IF;

 SELECT count(*) INTO bad_selected FROM (
  SELECT scene_id FROM video_render.scene_image_versions WHERE is_selected GROUP BY scene_id HAVING count(*)>1
  UNION ALL SELECT scene_id FROM video_render.scene_video_versions WHERE is_selected GROUP BY scene_id HAVING count(*)>1
  UNION ALL SELECT project_id FROM video_render.final_video_versions WHERE is_selected GROUP BY project_id HAVING count(*)>1
 ) q;
 IF bad_selected>0 THEN RAISE EXCEPTION 'Multiple selected versions found: %',bad_selected; END IF;

 SELECT count(*) INTO broken_lineage
 FROM video_render.scene_video_versions v JOIN video_render.scene_image_versions i ON i.id=v.source_image_version_id
 WHERE i.scene_id<>v.scene_id OR i.project_id<>v.project_id;
 IF broken_lineage>0 THEN RAISE EXCEPTION 'Broken image-to-video lineage rows: %',broken_lineage; END IF;
END $$;

SELECT 'COUNTS' section,
 (SELECT count(*) FROM video_render.scene_image_versions) image_versions,
 (SELECT count(*) FROM video_render.scene_video_versions) scene_video_versions,
 (SELECT count(*) FROM video_render.final_video_versions) final_video_versions,
 (SELECT count(*) FROM video_render.final_video_version_items) final_items;

SELECT 'SCENE_SELECTION' section,s.project_id,s.id scene_id,s.scene_index,
 s.selected_image_version_id,s.selected_video_version_id,
 CASE WHEN v.source_image_version_id IS DISTINCT FROM s.selected_image_version_id THEN 'VIDEO_OUTDATED' ELSE 'CURRENT' END video_state
FROM video_render.video_project_scenes s
LEFT JOIN video_render.scene_video_versions v ON v.id=s.selected_video_version_id
ORDER BY s.project_id,s.scene_index;

SELECT 'SETTINGS' section,setting_key,setting_value,is_active
FROM system.app_settings WHERE setting_key IN (
 'features.scene_render_versioning','features.scene_video_versioning','features.final_video_versioning')
ORDER BY setting_key;

SELECT 'VERIFY_RESULT' section,'PASS' status,'Keep flags false until code build/test/publish passes.' next_action;

