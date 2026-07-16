\set ON_ERROR_STOP on
DO $$
DECLARE missing text; missing_columns text; bad_selected int; broken_lineage int; bad_scene_pointer int; bad_final_pointer int;
BEGIN
 SELECT string_agg(x.name,', ') INTO missing FROM (VALUES
  ('video_render.scene_image_versions'),('video_render.scene_video_versions'),
  ('video_render.final_video_versions'),('video_render.final_video_version_items')) x(name)
 WHERE to_regclass(x.name) IS NULL;
 IF missing IS NOT NULL THEN RAISE EXCEPTION 'Missing versioning tables: %',missing; END IF;

 SELECT string_agg(c.column_name, ', ') INTO missing_columns
 FROM (VALUES
  ('scene_image_versions','compiled_image_prompt_snapshot'),
  ('scene_image_versions','provider_usage_json'),
  ('scene_image_versions','billing_logical_request_id'),
  ('scene_image_versions','estimated_usd'),
  ('scene_image_versions','actual_usd'),
  ('scene_image_versions','charged_points'),
  ('scene_image_versions','refunded_points'),
  ('scene_image_versions','result_media_id'),
  ('scene_video_versions','source_image_version_id'),
  ('final_video_version_items','scene_video_version_id')) c(table_name,column_name)
 WHERE NOT EXISTS (
   SELECT 1 FROM information_schema.columns
   WHERE table_schema='video_render'
     AND table_name=c.table_name
     AND column_name=c.column_name
 );
 IF missing_columns IS NOT NULL THEN RAISE EXCEPTION 'Missing versioning columns: %', missing_columns; END IF;

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

 SELECT count(*) INTO bad_scene_pointer
 FROM video_render.video_project_scenes s
 LEFT JOIN video_render.scene_image_versions i ON i.id=s.selected_image_version_id
 LEFT JOIN video_render.scene_video_versions v ON v.id=s.selected_video_version_id
 WHERE (s.selected_image_version_id IS NOT NULL AND (i.id IS NULL OR i.scene_id<>s.id OR i.project_id<>s.project_id))
    OR (s.selected_video_version_id IS NOT NULL AND (v.id IS NULL OR v.scene_id<>s.id OR v.project_id<>s.project_id));
 IF bad_scene_pointer>0 THEN RAISE EXCEPTION 'Broken selected scene media pointers: %',bad_scene_pointer; END IF;

 SELECT count(*) INTO bad_final_pointer
 FROM video_render.video_projects p
 LEFT JOIN video_render.final_video_versions f ON f.id=p.selected_final_video_version_id
 WHERE p.selected_final_video_version_id IS NOT NULL
   AND (f.id IS NULL OR f.project_id<>p.id);
 IF bad_final_pointer>0 THEN RAISE EXCEPTION 'Broken selected final video pointers: %',bad_final_pointer; END IF;
END $$;

SELECT 'COUNTS' section,
 (SELECT count(*) FROM video_render.scene_image_versions) image_versions,
 (SELECT count(*) FROM video_render.scene_video_versions) scene_video_versions,
 (SELECT count(*) FROM video_render.final_video_versions) final_video_versions,
 (SELECT count(*) FROM video_render.final_video_version_items) final_items;

SELECT 'VERSION_STATUS_COUNTS' section, media_type, status, count(*) row_count
FROM (
 SELECT 'image' media_type,status FROM video_render.scene_image_versions
 UNION ALL SELECT 'scene_video',status FROM video_render.scene_video_versions
 UNION ALL SELECT 'final_video',status FROM video_render.final_video_versions
) s
GROUP BY media_type,status
ORDER BY media_type,status;

SELECT 'ORPHAN_QUEUED' section, media_type, count(*) row_count
FROM (
 SELECT 'image' media_type FROM video_render.scene_image_versions WHERE status='queued' AND render_job_id IS NULL
 UNION ALL SELECT 'scene_video' FROM video_render.scene_video_versions WHERE status='queued' AND render_job_id IS NULL
 UNION ALL SELECT 'final_video' FROM video_render.final_video_versions WHERE status='queued' AND render_job_id IS NULL
) q
GROUP BY media_type
ORDER BY media_type;

SELECT 'DUPLICATE_LOGICAL_REQUEST' section, media_type, logical_request_id, count(*) row_count
FROM (
 SELECT 'image' media_type,logical_request_id FROM video_render.scene_image_versions
 UNION ALL SELECT 'scene_video',logical_request_id FROM video_render.scene_video_versions
 UNION ALL SELECT 'final_video',logical_request_id FROM video_render.final_video_versions
) d
GROUP BY media_type,logical_request_id
HAVING count(*)>1
ORDER BY media_type,logical_request_id;

SELECT 'BILLING_GAPS' section, count(*) image_completed_missing_billing
FROM video_render.scene_image_versions
WHERE status='completed'
  AND (billing_logical_request_id IS NULL OR estimated_usd IS NULL OR charged_points IS NULL);

SELECT 'FINAL_ITEM_GAPS' section, count(*) invalid_final_items
FROM video_render.final_video_version_items item
LEFT JOIN video_render.scene_video_versions v ON v.id=item.scene_video_version_id
WHERE v.id IS NULL OR v.status<>'completed' OR v.scene_id<>item.scene_id;

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

SELECT 'FINAL_SELECTION' section,p.id project_id,p.selected_final_video_version_id,
 CASE
   WHEN f.id IS NULL THEN 'NO_SELECTED_FINAL'
   WHEN EXISTS (
     SELECT 1
     FROM video_render.final_video_version_items item
     JOIN video_render.video_project_scenes s ON s.id=item.scene_id
     WHERE item.final_video_version_id=f.id
       AND item.scene_video_version_id IS DISTINCT FROM s.selected_video_version_id
   ) THEN 'FINAL_OUTDATED'
   ELSE 'CURRENT'
 END final_state
FROM video_render.video_projects p
LEFT JOIN video_render.final_video_versions f ON f.id=p.selected_final_video_version_id
ORDER BY p.id;

SELECT 'VERIFY_RESULT' section,'PASS' status,'Keep flags false until code build/test/publish passes.' next_action;
