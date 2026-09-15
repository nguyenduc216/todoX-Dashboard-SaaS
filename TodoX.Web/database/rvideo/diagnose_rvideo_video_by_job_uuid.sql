-- RVIDEO live-smoke diagnostic.
-- Read-only. The only input is the public render.render_jobs.id:
--   :job_uuid
--
-- Example:
--   psql ... -v job_uuid="'00000000-0000-0000-0000-000000000000'" \
--     -f database/rvideo/diagnose_rvideo_video_by_job_uuid.sql

BEGIN;
SET TRANSACTION READ ONLY;

-- Resolve the public job UUID once conceptually in every result set. No
-- project_id, scene_id, or child render_job_id input is required.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
)
SELECT
    j.id AS public_job_uuid,
    j.tenant_id,
    j.customer_id,
    j.user_id,
    j.job_type,
    j.status AS job_status,
    j.current_step,
    j.progress_percent,
    j.input_json,
    j.output_json,
    j.error_code,
    j.error_message,
    j.created_at,
    j.updated_at,
    p.id AS video_project_id,
    p.core_job_id,
    p.title AS project_title,
    p.status AS project_status,
    p.final_video_url,
    p.final_video_path,
    p.selected_final_video_version_id
FROM input i
JOIN render.render_jobs j ON j.id = i.job_uuid
LEFT JOIN video_render.video_projects p ON p.core_job_id = j.id
WHERE j.input_json->>'engine' = 'RVIDEO'
   OR p.id IS NOT NULL;

-- Video project and scene plan.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
)
SELECT
    p.*,
    p.core_job_id AS public_job_uuid
FROM root r
JOIN video_render.video_projects p ON p.id = r.project_id;

WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
)
SELECT
    s.*,
    p.title AS project_title
FROM root r
JOIN video_render.video_project_scenes s ON s.project_id = r.project_id
JOIN video_render.video_projects p ON p.id = s.project_id
ORDER BY s.scene_index, s.id;

-- Selected image versions and image-version history for every derived scene.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
)
SELECT
    s.scene_index,
    s.id AS scene_id,
    s.selected_image_version_id,
    v.id AS image_version_id,
    v.version_number,
    v.is_selected,
    v.status,
    v.logical_request_id,
    v.render_job_id,
    v.provider_code,
    v.requested_model,
    v.actual_model,
    v.provider_task_id,
    v.result_media_id,
    v.storage_key,
    v.public_url,
    v.mime_type,
    v.billing_logical_request_id,
    v.error_code,
    v.error_message,
    v.created_at,
    v.updated_at
FROM root r
JOIN video_render.video_project_scenes s ON s.project_id = r.project_id
LEFT JOIN video_render.scene_image_versions v
       ON v.scene_id = s.id
      AND v.project_id = s.project_id
ORDER BY s.scene_index, v.version_number, v.created_at;

-- Scene video versions, including selected/current media persistence fields.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
)
SELECT
    s.scene_index,
    s.id AS scene_id,
    s.selected_video_version_id,
    v.id AS scene_video_version_id,
    v.source_image_version_id,
    v.version_number,
    v.is_selected,
    v.status,
    v.logical_request_id,
    v.render_job_id AS scene_video_render_job_id,
    v.provider_code,
    v.requested_model,
    v.actual_model,
    v.provider_task_id,
    v.result_media_id,
    v.storage_key,
    v.public_url,
    v.poster_media_id,
    v.poster_url,
    v.duration_seconds,
    v.mime_type,
    v.billing_logical_request_id,
    v.submitted_at,
    v.completed_at,
    v.error_code,
    v.error_message,
    v.created_at,
    v.updated_at
FROM root r
JOIN video_render.video_project_scenes s ON s.project_id = r.project_id
LEFT JOIN video_render.scene_video_versions v
       ON v.scene_id = s.id
      AND v.project_id = s.project_id
ORDER BY s.scene_index, v.version_number, v.created_at;

-- Child render_scene_video jobs derived from scene_video_versions.render_job_id,
-- plus any matching child whose JSON snapshot carries the same project/scene.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
)
SELECT DISTINCT
    j.id AS child_render_job_id,
    j.retry_of_job_id,
    j.job_type,
    j.status,
    j.attempt_count,
    j.max_attempts,
    j.provider_code,
    j.model_code,
    j.input_json,
    j.output_json,
    j.error_code,
    j.error_message,
    j.queued_at,
    j.started_at,
    j.completed_at,
    j.created_at,
    j.updated_at,
    v.id AS scene_video_version_id,
    v.scene_id,
    v.version_number AS scene_video_version_number,
    v.logical_request_id,
    v.provider_task_id
FROM root r
JOIN video_render.video_project_scenes s ON s.project_id = r.project_id
JOIN video_render.scene_video_versions v ON v.scene_id = s.id
JOIN render.render_jobs j
  ON j.id = v.render_job_id
  OR (
      j.job_type = 'render_scene_video'
      AND (j.input_json->>'projectId')::text = r.project_id::text
      AND (j.input_json->>'sceneId')::text = s.id::text
  )
WHERE j.job_type = 'render_scene_video'
ORDER BY j.created_at, j.id;

-- Billing records for all derived scene-video attempts.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
),
logical_ids AS (
    SELECT DISTINCT v.billing_logical_request_id AS logical_request_id
    FROM root r
    JOIN video_render.scene_video_versions v ON v.project_id = r.project_id
    WHERE v.billing_logical_request_id IS NOT NULL
    UNION
    SELECT DISTINCT v.logical_request_id
    FROM root r
    JOIN video_render.scene_video_versions v ON v.project_id = r.project_id
)
SELECT
    b.*,
    li.logical_request_id AS matched_logical_request_id
FROM logical_ids li
JOIN billing.ai_image_billing_records b
  ON b.logical_request_id = li.logical_request_id
ORDER BY b.created_at, b.id;

-- Provider attempts, including attempt number and task IDs used for submit/poll.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
),
billing_ids AS (
    SELECT DISTINCT b.id
    FROM root r
    JOIN video_render.scene_video_versions v ON v.project_id = r.project_id
    JOIN billing.ai_image_billing_records b
      ON b.logical_request_id = v.billing_logical_request_id
      OR b.logical_request_id = v.logical_request_id
)
SELECT
    a.*,
    b.logical_request_id,
    b.provider_code,
    b.capability_code,
    b.requested_model,
    b.actual_model,
    b.provider_task_id AS billing_provider_task_id,
    b.status AS billing_status
FROM billing_ids bi
JOIN billing.ai_image_billing_records b ON b.id = bi.id
JOIN billing.ai_image_provider_attempts a ON a.billing_record_id = b.id
ORDER BY b.created_at, a.attempt_number, a.created_at;

-- Project event timeline and the render-job event timeline for the same flow.
WITH input AS (
    SELECT CAST(:job_uuid AS uuid) AS job_uuid
),
root AS (
    SELECT p.id AS project_id
    FROM input i
    JOIN video_render.video_projects p ON p.core_job_id = i.job_uuid
)
SELECT
    'video_project_event' AS event_source,
    e.id,
    e.project_id,
    NULL::uuid AS job_id,
    e.event_type,
    e.level,
    e.message,
    e.data_json::text AS data_json,
    e.created_at
FROM root r
JOIN video_render.video_project_events e ON e.project_id = r.project_id
UNION ALL
SELECT
    'render_job_event' AS event_source,
    e.id,
    r.project_id,
    e.job_id,
    e.event_type,
    e.level,
    e.message,
    e.data_json::text AS data_json,
    e.created_at
FROM root r
JOIN render.render_jobs j
  ON j.id = (SELECT job_uuid FROM input)
  OR (
      j.job_type = 'render_scene_video'
      AND (j.input_json->>'projectId')::text = r.project_id::text
  )
JOIN render.render_job_events e ON e.job_id = j.id
ORDER BY created_at, event_source, id;

COMMIT;
