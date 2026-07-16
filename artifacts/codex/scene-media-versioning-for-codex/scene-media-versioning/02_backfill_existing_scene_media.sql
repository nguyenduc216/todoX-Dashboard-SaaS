-- Backfill existing image/video/final paths as immutable version 1 records.
-- No provider call and no billing record is created.
BEGIN;

INSERT INTO video_render.scene_image_versions(
 id,project_id,scene_id,tenant_id,customer_id,created_by,version_number,logical_request_id,
 image_prompt_snapshot,video_prompt_snapshot,scene_snapshot_json,result_media_id,storage_key,
 source_file_path,public_url,status,is_selected,selected_at,completed_at,created_at,updated_at,cost_source)
SELECT gen_random_uuid(),s.project_id,s.id,s.tenant_id,p.customer_id,p.user_id,1,
       'backfill-scene-image-'||s.id,
       s.image_prompt,s.video_prompt,
       jsonb_build_object('source','backfilled_existing_scene','scenePrompt',s.scene_prompt,'title',s.title,'sceneIndex',s.scene_index),
       NULL,COALESCE(s.static_image_path,s.static_image_url),s.static_image_path,s.static_image_url,
       'completed',true,now(),COALESCE(s.updated_at,s.created_at,now()),COALESCE(s.created_at,now()),now(),'backfilled_existing_scene'
FROM video_render.video_project_scenes s
JOIN video_render.video_projects p ON p.id=s.project_id
WHERE COALESCE(s.static_image_path,s.static_image_url) IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM video_render.scene_image_versions v WHERE v.scene_id=s.id);

UPDATE video_render.video_project_scenes s
SET selected_image_version_id=v.id
FROM video_render.scene_image_versions v
WHERE v.scene_id=s.id AND v.is_selected AND s.selected_image_version_id IS NULL;

INSERT INTO video_render.scene_video_versions(
 id,project_id,scene_id,source_image_version_id,tenant_id,customer_id,created_by,version_number,
 logical_request_id,image_prompt_snapshot,video_prompt_snapshot,scene_snapshot_json,render_config_json,
 result_media_id,storage_key,source_file_path,public_url,duration_seconds,status,is_selected,
 selected_at,completed_at,created_at,updated_at,cost_source)
SELECT gen_random_uuid(),s.project_id,s.id,s.selected_image_version_id,s.tenant_id,p.customer_id,p.user_id,1,
       'backfill-scene-video-'||s.id,s.image_prompt,s.video_prompt,
       jsonb_build_object('source','backfilled_existing_scene_video','scenePrompt',s.scene_prompt,'title',s.title,'sceneIndex',s.scene_index),
       '{}'::jsonb,NULL,COALESCE(s.scene_video_path,s.scene_video_url),s.scene_video_path,s.scene_video_url,
       s.duration_seconds,'completed',true,now(),COALESCE(s.updated_at,s.created_at,now()),COALESCE(s.created_at,now()),now(),'backfilled_existing_scene_video'
FROM video_render.video_project_scenes s
JOIN video_render.video_projects p ON p.id=s.project_id
WHERE COALESCE(s.scene_video_path,s.scene_video_url) IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM video_render.scene_video_versions v WHERE v.scene_id=s.id);

UPDATE video_render.video_project_scenes s
SET selected_video_version_id=v.id
FROM video_render.scene_video_versions v
WHERE v.scene_id=s.id AND v.is_selected AND s.selected_video_version_id IS NULL;

INSERT INTO video_render.final_video_versions(
 id,project_id,tenant_id,customer_id,created_by,version_number,logical_request_id,
 composition_config_json,result_media_id,storage_key,source_file_path,public_url,status,is_selected,
 selected_at,completed_at,created_at,updated_at)
SELECT gen_random_uuid(),p.id,p.tenant_id,p.customer_id,p.user_id,1,'backfill-final-video-'||p.id,
       jsonb_build_object('source','backfilled_existing_final_video'),NULL,
       COALESCE(p.final_video_path,p.final_video_url),p.final_video_path,p.final_video_url,
       'completed',true,now(),COALESCE(p.updated_at,p.created_at,now()),COALESCE(p.created_at,now()),now()
FROM video_render.video_projects p
WHERE COALESCE(p.final_video_path,p.final_video_url) IS NOT NULL
  AND NOT EXISTS(SELECT 1 FROM video_render.final_video_versions v WHERE v.project_id=p.id);

INSERT INTO video_render.final_video_version_items(final_video_version_id,scene_id,scene_video_version_id,item_order,config_json)
SELECT f.id,s.id,s.selected_video_version_id,s.scene_index,
       jsonb_build_object('source','backfilled_existing_final_video')
FROM video_render.final_video_versions f
JOIN video_render.video_project_scenes s ON s.project_id=f.project_id
WHERE f.logical_request_id='backfill-final-video-'||f.project_id
  AND s.selected_video_version_id IS NOT NULL
ON CONFLICT(final_video_version_id,scene_id) DO NOTHING;

UPDATE video_render.video_projects p SET selected_final_video_version_id=v.id
FROM video_render.final_video_versions v
WHERE v.project_id=p.id AND v.is_selected AND p.selected_final_video_version_id IS NULL;

COMMIT;

