-- RVIDEO support diagnostic. Required input: :job_uuid (uuid)
-- The query starts from the public TodoX Core Job UUID; project_id stays internal.
WITH core_job AS (
    SELECT r.id, r.tenant_id, r.customer_id, r.user_id, r.service_id, r.job_type,
           r.status AS core_status, r.current_step, r.progress_percent,
           r.point_status, r.point_cost_estimate, r.point_cost_charged,
           r.input_json, r.output_json, r.error_code, r.error_message,
           r.created_at, r.updated_at, r.completed_at
      FROM render.render_jobs r
     WHERE r.id = :job_uuid
), project AS (
    SELECT p.*
      FROM video_render.video_projects p
      JOIN core_job c ON c.id = p.core_job_id AND c.tenant_id = p.tenant_id
)
SELECT 'core_job' AS record_type, c.id::text AS record_id, to_jsonb(c) AS payload
  FROM core_job c
UNION ALL
SELECT 'project', p.id::text, to_jsonb(p)
  FROM project p
UNION ALL
SELECT 'scene', s.id::text, to_jsonb(s)
  FROM video_render.video_project_scenes s JOIN project p ON p.id=s.project_id AND p.tenant_id=s.tenant_id
UNION ALL
SELECT 'scene_image_version', v.id::text, to_jsonb(v)
  FROM video_render.scene_image_versions v
  JOIN project p ON p.id=v.project_id AND p.tenant_id=v.tenant_id
UNION ALL
SELECT 'render_job', r.id::text, to_jsonb(r)
  FROM render.render_jobs r JOIN project p ON r.tenant_id=p.tenant_id
 WHERE r.input_json->>'projectId'=p.id::text
UNION ALL
SELECT 'provider_usage', u.id::text, to_jsonb(u)
  FROM public.todox_ai_provider_usage_log u
  JOIN core_job c ON (u.job_id = c.id::text OR u.request_id = c.id::text)
UNION ALL
SELECT 'project_event', e.id::text, to_jsonb(e)
  FROM video_render.video_project_events e JOIN project p ON p.id=e.project_id AND p.tenant_id=e.tenant_id
UNION ALL
SELECT 'core_event', e.id::text, to_jsonb(e)
  FROM render.render_job_events e JOIN core_job c ON c.id=e.job_id AND c.tenant_id=e.tenant_id
ORDER BY record_type, record_id;
