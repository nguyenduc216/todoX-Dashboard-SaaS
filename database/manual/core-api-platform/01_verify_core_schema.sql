-- READ-ONLY verification for todoX Core Platform Phase 1.
-- Safe to run against todo_saas. This script does not modify schema or data.

WITH required_render_columns(column_name) AS (
    VALUES
        ('id'),
        ('tenant_id'),
        ('customer_id'),
        ('user_id'),
        ('service_id'),
        ('logical_request_id'),
        ('job_type'),
        ('operation_type'),
        ('source_type'),
        ('status'),
        ('progress_percent'),
        ('input_json'),
        ('prompt_json'),
        ('reference_json'),
        ('output_json'),
        ('point_cost_estimate'),
        ('point_cost_charged'),
        ('point_status'),
        ('retry_of_job_id'),
        ('error_code'),
        ('error_message'),
        ('created_at'),
        ('updated_at')
), render_check AS (
    SELECT r.column_name,
           CASE WHEN c.column_name IS NULL THEN 'MISSING' ELSE 'OK' END AS status
      FROM required_render_columns r
      LEFT JOIN information_schema.columns c
        ON c.table_schema = 'render'
       AND c.table_name = 'render_jobs'
       AND c.column_name = r.column_name
), required_service_columns(column_name) AS (
    VALUES
        ('id'),
        ('category_id'),
        ('service_code'),
        ('service_name'),
        ('service_type'),
        ('default_options'),
        ('status'),
        ('sort_order'),
        ('workflow_code')
), service_check AS (
    SELECT r.column_name,
           CASE WHEN c.column_name IS NULL THEN 'MISSING' ELSE 'OK' END AS status
      FROM required_service_columns r
      LEFT JOIN information_schema.columns c
        ON c.table_schema = 'catalog'
       AND c.table_name = 'services'
       AND c.column_name = r.column_name
)
SELECT 'render.render_jobs' AS object_name, column_name, status
  FROM render_check
UNION ALL
SELECT 'catalog.services' AS object_name, column_name, status
  FROM service_check
ORDER BY object_name, column_name;

-- Summary of catalog-driven form readiness.
SELECT service_code,
       service_name,
       status,
       workflow_code,
       CASE
           WHEN default_options ? 'form_schema' THEN 'HAS_FORM_SCHEMA'
           ELSE 'NO_FORM_SCHEMA'
       END AS form_schema_status
  FROM catalog.services
 ORDER BY sort_order, service_name;

-- Existing job source/service coverage. This reveals how much historic/current data can already be
-- projected into the new canonical contract without migration.
SELECT COALESCE(source_type, '<null>') AS source_type,
       job_type,
       count(*) AS job_count,
       count(service_id) AS jobs_with_service_id,
       count(logical_request_id) AS jobs_with_logical_request_id
  FROM render.render_jobs
 GROUP BY COALESCE(source_type, '<null>'), job_type
 ORDER BY job_count DESC, source_type, job_type;
