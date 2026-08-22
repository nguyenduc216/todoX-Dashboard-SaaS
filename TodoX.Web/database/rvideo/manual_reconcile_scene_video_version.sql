-- Manual recovery script for a single already-completed scene video version.
-- No schema changes. Run only after verifying the provider task has already completed
-- and you have the final media URL/path from the provider or storage layer.
--
-- Required inputs (replace placeholders before execution):
--   :tenant_id
--   :version_id
--   :scene_id
--   :project_id
--   :provider_code
--   :model_name
--   :provider_capability_id
--   :provider_task_id
--   :public_url
--   :source_file_path
--   :storage_key
--   :duration_seconds
--   :aspect_ratio
--   :result_media_id
--   :billing_logical_request_id
--   :charged_points
--   :error_code
--   :error_message

BEGIN;
SET TRANSACTION READ WRITE;

UPDATE video_render.scene_video_versions
   SET status = 'completed',
       provider_code = COALESCE(:provider_code, provider_code),
       requested_model = COALESCE(requested_model, :model_name),
       actual_model = COALESCE(:model_name, actual_model),
       provider_capability_id = COALESCE(:provider_capability_id, provider_capability_id),
       provider_task_id = COALESCE(:provider_task_id, provider_task_id),
       public_url = COALESCE(:public_url, public_url),
       source_file_path = COALESCE(:source_file_path, source_file_path),
       storage_key = COALESCE(:storage_key, storage_key),
       duration_seconds = COALESCE(:duration_seconds, duration_seconds),
       aspect_ratio = COALESCE(:aspect_ratio, aspect_ratio),
       result_media_id = COALESCE(:result_media_id, result_media_id),
       billing_logical_request_id = COALESCE(:billing_logical_request_id, billing_logical_request_id),
       charged_points = COALESCE(:charged_points, charged_points),
       error_code = NULL,
       error_message = NULL,
       completed_at = COALESCE(completed_at, now()),
       updated_at = now()
 WHERE id = :version_id
   AND tenant_id = :tenant_id;

UPDATE video_render.video_project_scenes
   SET selected_video_version_id = :version_id,
       scene_video_url = COALESCE(:public_url, scene_video_url),
       scene_video_path = COALESCE(:source_file_path, scene_video_path),
       status = 'video_ready',
       error_message = NULL,
       updated_at = now()
 WHERE id = :scene_id
   AND project_id = :project_id
   AND tenant_id = :tenant_id;

COMMIT;
