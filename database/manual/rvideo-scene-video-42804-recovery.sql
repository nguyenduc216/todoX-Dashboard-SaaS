BEGIN;

WITH target_parent AS (
    SELECT '3454d911-4d57-4ef4-9b99-7178282a6e5f'::uuid AS job_id
),
reopen_jobs AS (
    UPDATE render.render_jobs j
       SET status = 'pending_reconciliation',
           error_code = NULL,
           error_message = NULL,
           retry_after = now(),
           lock_owner = NULL,
           lock_until = NULL,
           updated_at = now()
      FROM target_parent p
     WHERE j.job_type = 'render_scene_video'
       AND j.status = 'failed'
       AND j.input_json->>'parentJobId' = p.job_id::text
       AND (
            j.error_code = '42804'
            OR j.error_message ILIKE '%42804%'
            OR j.error_message ILIKE '%created_by%'
       )
    RETURNING j.id
),
reopen_versions AS (
    UPDATE video_render.scene_video_versions v
       SET status = 'pending_reconciliation',
           error_code = NULL,
           error_message = NULL,
           updated_at = now()
      WHERE v.render_job_id IN (SELECT id FROM reopen_jobs)
        AND v.provider_task_id IS NOT NULL
        AND btrim(v.provider_task_id) <> ''
        AND v.status <> 'completed'
    RETURNING v.id, v.scene_id
),
reopen_scenes AS (
    UPDATE video_render.video_project_scenes s
       SET status = 'video_rendering',
           error_message = NULL,
           updated_at = now()
      WHERE s.id IN (SELECT scene_id FROM reopen_versions)
        AND s.status IN ('failed', 'video_rendering')
    RETURNING s.id
)
SELECT
    (SELECT count(*) FROM reopen_jobs) AS reopened_jobs,
    (SELECT count(*) FROM reopen_versions) AS reopened_versions,
    (SELECT count(*) FROM reopen_scenes) AS reopened_scenes;

COMMIT;
