BEGIN;

WITH target_core_job AS (
    SELECT '3454d911-4d57-4ef4-9b99-7178282a6e5f'::uuid AS core_job_id
),
target_projects AS (
    SELECT p.id AS project_id
      FROM video_render.video_projects p
      JOIN target_core_job t ON p.core_job_id = t.core_job_id
),
candidate_jobs AS (
    SELECT j.id
      FROM render.render_jobs j
      JOIN target_projects p ON (j.input_json->>'projectId') = p.project_id::text
     WHERE j.job_type = 'render_scene_video'
       AND j.status = 'failed'
       AND (
            j.error_code = '42804'
            OR j.error_message ILIKE '%42804%'
            OR j.error_message ILIKE '%created_by%'
       )
       AND EXISTS (
            SELECT 1
              FROM video_render.scene_video_versions v
             WHERE v.render_job_id = j.id
               AND v.provider_task_id IS NOT NULL
               AND btrim(v.provider_task_id) <> ''
               AND v.status <> 'completed'
       )
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
      WHERE j.id IN (SELECT id FROM candidate_jobs)
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
    RETURNING v.id, v.scene_id, v.provider_task_id
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
    (SELECT core_job_id FROM target_core_job) AS core_job_id,
    (SELECT array_agg(project_id ORDER BY project_id) FROM target_projects) AS project_ids,
    (SELECT count(*) FROM candidate_jobs) AS candidate_jobs,
    (SELECT count(*) FROM reopen_jobs) AS reopened_jobs,
    (SELECT count(*) FROM reopen_versions) AS reopened_versions,
    (SELECT count(*) FROM reopen_scenes) AS reopened_scenes;

COMMIT;
