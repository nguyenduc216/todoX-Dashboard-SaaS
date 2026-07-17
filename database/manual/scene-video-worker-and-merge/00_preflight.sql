\set ON_ERROR_STOP on

SELECT to_regclass('video_render.scene_video_versions') AS scene_video_versions,
       to_regclass('video_render.final_video_versions') AS final_video_versions,
       to_regclass('render.render_jobs') AS render_jobs;

SELECT column_name
FROM information_schema.columns
WHERE table_schema='video_render'
  AND table_name='scene_video_versions'
  AND column_name IN ('logical_request_id','provider_task_id','billing_logical_request_id','source_image_version_id');
