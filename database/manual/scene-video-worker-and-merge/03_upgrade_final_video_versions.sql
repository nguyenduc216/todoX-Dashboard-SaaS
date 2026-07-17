\set ON_ERROR_STOP on

SELECT 'FINAL_VIDEO_VERSION_COLUMNS' section, column_name
FROM information_schema.columns
WHERE table_schema='video_render'
  AND table_name='final_video_versions'
  AND column_name IN (
      'logical_request_id','status','is_selected','source_file_path','public_url','duration_seconds'
  )
ORDER BY column_name;
