\set ON_ERROR_STOP on

-- Mark orphan queued versions that have no render job and are old enough to be considered abandoned.
-- This is intentionally conservative and non-destructive. Review the preview result before COMMIT in a manual session if needed.
BEGIN;

WITH orphan_image AS (
    UPDATE video_render.scene_image_versions v
       SET status='failed',
           error_code='orphan_queued_version',
           error_message='Queued image version had no render job attached and was marked by manual cleanup SQL.',
           updated_at=now()
     WHERE v.status='queued'
       AND v.render_job_id IS NULL
       AND v.created_at < now() - interval '30 minutes'
     RETURNING v.id, v.project_id, v.scene_id, v.logical_request_id
),
orphan_scene_video AS (
    UPDATE video_render.scene_video_versions v
       SET status='failed',
           error_code='orphan_queued_version',
           error_message='Queued scene video version had no render job attached and was marked by manual cleanup SQL.',
           updated_at=now()
     WHERE v.status='queued'
       AND v.render_job_id IS NULL
       AND v.created_at < now() - interval '30 minutes'
     RETURNING v.id, v.project_id, v.scene_id, v.logical_request_id
),
orphan_final AS (
    UPDATE video_render.final_video_versions v
       SET status='failed',
           error_code='orphan_queued_version',
           error_message='Queued final video version had no render job attached and was marked by manual cleanup SQL.',
           updated_at=now()
     WHERE v.status='queued'
       AND v.render_job_id IS NULL
       AND v.created_at < now() - interval '30 minutes'
     RETURNING v.id, v.project_id, NULL::bigint AS scene_id, v.logical_request_id
)
SELECT 'ORPHAN_IMAGE_MARKED' AS section, count(*) AS marked_count FROM orphan_image
UNION ALL
SELECT 'ORPHAN_SCENE_VIDEO_MARKED' AS section, count(*) AS marked_count FROM orphan_scene_video
UNION ALL
SELECT 'ORPHAN_FINAL_VIDEO_MARKED' AS section, count(*) AS marked_count FROM orphan_final;

COMMIT;
