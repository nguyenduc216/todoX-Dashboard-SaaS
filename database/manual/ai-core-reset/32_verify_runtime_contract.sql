WITH removed_objects(object_name) AS (
    VALUES
    ('public.todox_ai_feature_provider_route'),
    ('dance_sell.dance_sell_provider_operations'),
    ('public.todox_ai_operation_assets'),
    ('public.todox_ai_operation_billing_transactions')
)
SELECT object_name, to_regclass(object_name) IS NULL AS removed
FROM removed_objects;

SELECT indexname
FROM pg_indexes
WHERE schemaname IN ('public','render','billing','dance_sell')
  AND indexname IN (
      'todox_ai_provider_account_code_uk',
      'todox_ai_provider_account_lease_render_active_uk',
      'todox_ai_provider_usage_log_task_attempt_uk',
      'ai_provider_attempts_render_attempt_uk',
      'dance_sell_reference_versions_job_version_uk'
  )
ORDER BY indexname;

SELECT conname
FROM pg_constraint
WHERE conname IN (
    'todox_ai_provider_account_concurrency_ck',
    'todox_ai_provider_account_lease_status_ck',
    'ai_provider_attempts_attempt_ck',
    'dance_sell_reference_versions_status_ck'
)
ORDER BY conname;
