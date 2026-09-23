-- Read-only diagnostic for RVIDEO Prompt Workspace projects 104-108.
-- Run manually against the target database. This script does not UPDATE or DELETE.

SELECT
    p.id,
    p.core_job_id,
    p.tenant_id AS project_tenant_id,
    p.customer_id AS project_customer_id,
    p.user_id AS project_user_id,
    p.status AS project_status,
    p.created_at AS project_created_at,
    p.original_prompt,
    j.id AS job_id,
    j.tenant_id AS job_tenant_id,
    j.customer_id AS job_customer_id,
    j.user_id AS job_user_id,
    j.operation_type,
    j.job_type,
    j.status AS job_status,
    j.logical_request_id,
    j.input_json,
    j.created_at AS job_created_at
FROM video_render.video_projects p
LEFT JOIN render.render_jobs j
    ON j.id = p.core_job_id
WHERE p.id IN (104, 105, 106, 107, 108)
ORDER BY p.id;

SELECT
    p.id AS project_id,
    COUNT(s.id) AS scene_count,
    COUNT(*) FILTER (
        WHERE s.static_image_url IS NOT NULL
          AND btrim(s.static_image_url) <> ''
    ) AS image_ready_count,
    COUNT(*) FILTER (
        WHERE s.scene_video_url IS NOT NULL
          AND btrim(s.scene_video_url) <> ''
    ) AS video_ready_count
FROM video_render.video_projects p
LEFT JOIN video_render.video_project_scenes s
    ON s.project_id = p.id
   AND s.tenant_id = p.tenant_id
WHERE p.id IN (104, 105, 106, 107, 108)
GROUP BY p.id
ORDER BY p.id;

-- Candidate jobs are listed for manual review only. Matching requires exact
-- tenant/customer/user/operation/type and project timing/context verification.
SELECT
    p.id AS project_id,
    j.id AS candidate_job_id,
    p.tenant_id AS project_tenant_id,
    j.tenant_id AS job_tenant_id,
    p.customer_id AS project_customer_id,
    j.customer_id AS job_customer_id,
    p.user_id AS project_user_id,
    j.user_id AS job_user_id,
    j.operation_type,
    j.job_type,
    j.status,
    p.created_at AS project_created_at,
    j.created_at AS job_created_at,
    j.input_json
FROM video_render.video_projects p
JOIN render.render_jobs j
  ON j.tenant_id = p.tenant_id
 AND j.customer_id = p.customer_id
 AND j.user_id IS NOT DISTINCT FROM p.user_id
 AND j.operation_type = 'RVIDEO'
 AND j.job_type = 'core_service'
 AND j.created_at BETWEEN p.created_at - interval '10 minutes' AND p.created_at + interval '10 minutes'
WHERE p.id IN (104, 105, 106, 107, 108)
  AND p.core_job_id IS NULL
ORDER BY p.id, j.created_at;
