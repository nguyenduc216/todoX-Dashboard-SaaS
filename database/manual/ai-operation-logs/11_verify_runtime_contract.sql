WITH required_tables(schema_name, table_name) AS (
    VALUES
        ('public','todox_ai_provider_account'),
        ('public','todox_ai_feature_provider_route'),
        ('dance_sell','dance_sell_provider_operations'),
        ('public','todox_ai_operation_assets'),
        ('public','todox_ai_provider_balance_ledger'),
        ('public','todox_ai_operation_billing_transactions'),
        ('dance_sell','dance_sell_jobs'),
        ('dance_sell','dance_sell_reference_versions')
)
SELECT r.schema_name, r.table_name,
       CASE WHEN t.table_name IS NULL THEN 'missing' ELSE 'ok' END AS status
  FROM required_tables r
  LEFT JOIN information_schema.tables t
    ON t.table_schema = r.schema_name
   AND t.table_name = r.table_name
 ORDER BY r.schema_name, r.table_name;

WITH required_columns(schema_name, table_name, column_name) AS (
    VALUES
        ('dance_sell','dance_sell_jobs','reference_mode'),
        ('dance_sell','dance_sell_jobs','direct_reference_url'),
        ('dance_sell','dance_sell_jobs','motion_provider_code'),
        ('dance_sell','dance_sell_jobs','current_stage'),
        ('dance_sell','dance_sell_provider_operations','attempt_no'),
        ('dance_sell','dance_sell_provider_operations','provider_task_id'),
        ('dance_sell','dance_sell_provider_operations','credits_consumed'),
        ('dance_sell','dance_sell_provider_operations','billing_status'),
        ('public','todox_ai_operation_assets','asset_role'),
        ('public','todox_ai_provider_account','credential_config_name'),
        ('public','todox_ai_feature_provider_route','config_json'),
        ('public','todox_ai_provider_balance_ledger','idempotency_key')
)
SELECT r.schema_name, r.table_name, r.column_name,
       CASE WHEN c.column_name IS NULL THEN 'missing' ELSE 'ok' END AS status
  FROM required_columns r
  LEFT JOIN information_schema.columns c
    ON c.table_schema = r.schema_name
   AND c.table_name = r.table_name
   AND c.column_name = r.column_name
 ORDER BY r.schema_name, r.table_name, r.column_name;

SELECT feature_code, operation_type, provider_code, model_name, enabled, is_default, allow_user_select, config_json
  FROM public.todox_ai_feature_provider_route
 WHERE feature_code = 'dance_sell'
 ORDER BY operation_type, is_default DESC, priority;

SELECT conrelid::regclass AS table_name, conname
  FROM pg_constraint
 WHERE conname IN (
    'dance_sell_provider_operations_type_ck',
    'dance_sell_provider_operations_status_ck',
    'dance_sell_provider_operations_billing_status_ck',
    'dance_sell_provider_operations_refund_status_ck',
    'dance_sell_provider_operations_usage_unit_ck',
    'todox_ai_provider_balance_ledger_type_ck',
    'todox_ai_operation_billing_transactions_type_ck'
 )
 ORDER BY conrelid::regclass::text, conname;
