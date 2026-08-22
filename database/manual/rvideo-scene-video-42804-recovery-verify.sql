WITH target_core_job AS (
    SELECT '3454d911-4d57-4ef4-9b99-7178282a6e5f'::uuid AS core_job_id
),
target_project AS (
    SELECT p.id AS project_id
      FROM video_render.video_projects p
      JOIN target_core_job t ON p.core_job_id = t.core_job_id
     LIMIT 1
),
candidate_jobs AS (
    SELECT j.id,
           j.status,
           j.error_code,
           j.error_message,
           j.input_json,
           v.id AS scene_video_version_id,
           v.scene_id,
           v.version_number,
           v.status AS scene_video_status,
           v.provider_task_id
      FROM render.render_jobs j
      JOIN target_project p ON (j.input_json->>'projectId') = p.project_id::text
      JOIN video_render.scene_video_versions v ON v.render_job_id = j.id
     WHERE j.job_type = 'render_scene_video'
       AND j.status = 'failed'
       AND (
            j.error_code = '42804'
            OR j.error_message ILIKE '%42804%'
            OR j.error_message ILIKE '%created_by%'
       )
       AND v.provider_task_id IS NOT NULL
       AND btrim(v.provider_task_id) <> ''
       AND v.status <> 'completed'
)
SELECT *
  FROM candidate_jobs
 ORDER BY scene_id, version_number, id;
