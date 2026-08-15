-- TodoX Core API Platform - read-only verification
-- Safe to run manually against todo_saas. This script performs no writes.

-- 1) Confirm active services and whether a dynamic form schema is currently configured.
SELECT
    s.id,
    s.service_code,
    s.service_name,
    s.service_type,
    s.workflow_code,
    s.status,
    jsonb_typeof(COALESCE(s.default_options->'form_schema', '{}'::jsonb)) AS form_schema_type,
    COALESCE(s.default_options->'form_schema', '{}'::jsonb) AS form_schema
FROM catalog.services s
ORDER BY s.sort_order, s.service_name;

-- 2) Core jobs should use the existing canonical render table rather than introducing api_jobs/zalo_jobs.
SELECT
    r.id,
    r.service_id,
    s.service_code,
    r.logical_request_id,
    r.job_type,
    r.operation_type,
    r.source_type,
    r.status,
    r.progress_percent,
    r.point_cost_estimate,
    r.point_cost_charged,
    r.point_status,
    r.created_at
FROM render.render_jobs r
LEFT JOIN catalog.services s ON s.id = r.service_id
WHERE r.job_type = 'core_service'
ORDER BY r.created_at DESC
LIMIT 100;

-- 3) Detect duplicate external logical requests. Expected result after the Core path is used: zero rows.
-- The application layer serializes creates with pg_advisory_xact_lock because there is currently
-- no unique constraint on render.render_jobs(logical_request_id).
SELECT
    r.customer_id,
    r.service_id,
    r.source_type,
    r.logical_request_id,
    count(*) AS duplicate_count,
    min(r.created_at) AS first_created_at,
    max(r.created_at) AS last_created_at
FROM render.render_jobs r
WHERE r.job_type = 'core_service'
  AND r.logical_request_id IS NOT NULL
GROUP BY r.customer_id, r.service_id, r.source_type, r.logical_request_id
HAVING count(*) > 1
ORDER BY duplicate_count DESC, last_created_at DESC;

-- 4) Check that the columns used by the Core contract exist in the deployed database.
SELECT
    c.column_name,
    c.data_type,
    c.is_nullable
FROM information_schema.columns c
WHERE c.table_schema = 'render'
  AND c.table_name = 'render_jobs'
  AND c.column_name IN (
      'id', 'tenant_id', 'customer_id', 'user_id', 'service_id',
      'logical_request_id', 'job_type', 'operation_type', 'source_type',
      'status', 'progress_percent', 'priority', 'input_json', 'prompt_json',
      'reference_json', 'output_json', 'options', 'point_cost_estimate',
      'point_cost_charged', 'point_status', 'max_attempts', 'queued_at',
      'created_at', 'updated_at', 'completed_at'
  )
ORDER BY c.column_name;
