\set ON_ERROR_STOP on

-- Scene video versioning columns required by the runtime already exist in
-- database/scene-media-versioning/01_add_scene_media_versioning.sql.
-- This file is intentionally idempotent and only verifies key columns.

SELECT 'SCENE_VIDEO_VERSION_COLUMNS' section, column_name
FROM information_schema.columns
WHERE table_schema='video_render'
  AND table_name='scene_video_versions'
  AND column_name IN (
      'logical_request_id','provider_task_id','billing_logical_request_id',
      'source_image_version_id','estimated_usd','actual_usd','charged_points',
      'refunded_points','aspect_ratio','status','is_selected'
  )
ORDER BY column_name;
