\set ON_ERROR_STOP on

-- Conservative cleanup: no rows are deleted.
BEGIN;

WITH orphan_image AS (
    UPDATE video_render.scene_image_versions
       SET status='failed', error_code='orphan_queued_version',
           error_message='Queued image version had no render job attached and was marked by manual cleanup SQL.',
           updated_at=now()
     WHERE status='queued' AND render_job_id IS NULL
       AND created_at < now() - interval '30 minutes'
     RETURNING id
), orphan_scene_video AS (
    UPDATE video_render.scene_video_versions
       SET status='failed', error_code='orphan_queued_version',
           error_message='Queued scene video version had no render job attached and was marked by manual cleanup SQL.',
           updated_at=now()
     WHERE status='queued' AND render_job_id IS NULL
       AND created_at < now() - interval '30 minutes'
     RETURNING id
), orphan_final AS (
    UPDATE video_render.final_video_versions
       SET status='failed', error_code='orphan_queued_version',
           error_message='Queued final video version had no render job attached and was marked by manual cleanup SQL.',
           updated_at=now()
     WHERE status='queued' AND render_job_id IS NULL
       AND created_at < now() - interval '30 minutes'
     RETURNING id
)
SELECT 'ORPHAN_IMAGE_MARKED' section, count(*) marked_count FROM orphan_image
UNION ALL SELECT 'ORPHAN_SCENE_VIDEO_MARKED', count(*) FROM orphan_scene_video
UNION ALL SELECT 'ORPHAN_FINAL_VIDEO_MARKED', count(*) FROM orphan_final;

COMMIT;
