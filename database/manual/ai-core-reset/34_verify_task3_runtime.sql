WITH kie_account AS (
    SELECT provider_code, account_code, max_concurrency, rate_limit_requests,
           rate_limit_window_seconds, config_json
      FROM public.todox_ai_provider_account
     WHERE provider_code = 'kie'
       AND account_code = 'kie-default'
),
legacy_tables(object_name, removed) AS (
    VALUES
    ('public.todox_ai_feature_provider_route', to_regclass('public.todox_ai_feature_provider_route') IS NULL),
    ('dance_sell.dance_sell_provider_operations', to_regclass('dance_sell.dance_sell_provider_operations') IS NULL),
    ('public.todox_ai_operation_assets', to_regclass('public.todox_ai_operation_assets') IS NULL),
    ('public.todox_ai_operation_billing_transactions', to_regclass('public.todox_ai_operation_billing_transactions') IS NULL),
    ('billing.ai_image_billing_records', to_regclass('billing.ai_image_billing_records') IS NULL),
    ('billing.ai_image_provider_attempts', to_regclass('billing.ai_image_provider_attempts') IS NULL)
),
orphan_checks(check_name, orphan_count) AS (
    SELECT 'lease_orphan_account', count(*)
      FROM public.todox_ai_provider_account_lease l
      LEFT JOIN public.todox_ai_provider_account a ON a.id = l.provider_account_id
     WHERE a.id IS NULL
    UNION ALL
    SELECT 'lease_orphan_render_job', count(*)
      FROM public.todox_ai_provider_account_lease l
      LEFT JOIN render.render_jobs j ON j.id = l.render_job_id
     WHERE j.id IS NULL
    UNION ALL
    SELECT 'usage_orphan_render_job', count(*)
      FROM public.todox_ai_provider_usage_log u
      LEFT JOIN render.render_jobs j ON j.id = u.render_job_id
     WHERE u.render_job_id IS NOT NULL AND j.id IS NULL
    UNION ALL
    SELECT 'billing_orphan_render_job', count(*)
      FROM billing.ai_billing_records b
      LEFT JOIN render.render_jobs j ON j.id = b.render_job_id
     WHERE b.render_job_id IS NOT NULL AND j.id IS NULL
    UNION ALL
    SELECT 'dance_reference_orphan_render_job', count(*)
      FROM dance_sell.dance_sell_reference_versions v
      LEFT JOIN render.render_jobs j ON j.id = v.render_job_id
     WHERE v.render_job_id IS NOT NULL AND j.id IS NULL
)
SELECT 'kie_rate_limit_provisional' AS check_name,
       (SELECT count(*) = 1
          FROM kie_account
         WHERE max_concurrency = 1
           AND rate_limit_requests IS NULL
           AND rate_limit_window_seconds IS NULL
           AND config_json @> '{"rateLimitVerified": false}'::jsonb) AS passed
UNION ALL
SELECT 'kie_credential_reference',
       EXISTS (
           SELECT 1
             FROM public.todox_ai_provider_account a
             JOIN public.todox_ai_provider_account_credential c ON c.provider_account_id = a.id
            WHERE a.provider_code = 'kie'
              AND a.account_code = 'kie-default'
              AND c.credential_role = 'api_key'
              AND c.credential_key = 'KIE_API_KEY'
              AND c.enabled
       )
UNION ALL
SELECT 'legacy_tables_removed', bool_and(removed) FROM legacy_tables
UNION ALL
SELECT 'claim_index_exists',
       EXISTS (
           SELECT 1 FROM pg_indexes
            WHERE schemaname = 'public'
              AND indexname IN ('todox_ai_provider_account_lease_render_active_uk','todox_ai_provider_account_lease_account_active_ix')
            GROUP BY schemaname
           HAVING count(*) = 2
       )
UNION ALL
SELECT 'provider_task_index_exists',
       EXISTS (
           SELECT 1 FROM pg_indexes
            WHERE schemaname = 'render'
              AND indexname = 'render_jobs_provider_task_ix'
       )
UNION ALL
SELECT 'generic_billing_tables_exist',
       to_regclass('billing.ai_billing_records') IS NOT NULL
       AND to_regclass('billing.ai_provider_attempts') IS NOT NULL
UNION ALL
SELECT check_name, orphan_count = 0 FROM orphan_checks
ORDER BY check_name;

SELECT provider_code, count(*) AS capability_count
  FROM public.todox_ai_provider_capability
 GROUP BY provider_code
 ORDER BY provider_code;

SELECT count(*) AS total_capability_count
  FROM public.todox_ai_provider_capability;
