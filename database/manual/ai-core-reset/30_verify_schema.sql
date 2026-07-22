WITH required_tables(schema_name, table_name) AS (
    VALUES
    ('public','todox_ai_provider'),
    ('public','todox_ai_provider_capability'),
    ('public','todox_ai_provider_account'),
    ('public','todox_ai_provider_account_credential'),
    ('public','todox_ai_provider_account_lease'),
    ('public','todox_ai_provider_balance_ledger'),
    ('public','todox_ai_provider_usage_log'),
    ('render','render_jobs'),
    ('render','render_job_inputs'),
    ('render','render_job_events'),
    ('render','render_artifacts'),
    ('billing','ai_billing_records'),
    ('billing','ai_provider_attempts'),
    ('billing','token_wallets'),
    ('billing','token_transactions'),
    ('billing','token_usage_logs'),
    ('dance_sell','dance_sell_jobs'),
    ('dance_sell','dance_sell_reference_versions')
)
SELECT r.*, to_regclass(format('%I.%I', r.schema_name, r.table_name)) IS NOT NULL AS exists
FROM required_tables r
ORDER BY r.schema_name, r.table_name;
