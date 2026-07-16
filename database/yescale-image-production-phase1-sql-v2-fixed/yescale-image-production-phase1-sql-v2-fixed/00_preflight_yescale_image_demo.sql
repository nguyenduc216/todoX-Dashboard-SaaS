-- YEScale Image Demo production preflight for database todo_saas.
-- Read-only: this file does not create or update database objects.
-- Run before 01_add_or_update_billing_support.sql.

\set ON_ERROR_STOP on

DO $$
DECLARE
    missing_objects text;
    tenant_count integer;
BEGIN
    IF current_database() <> 'todo_saas' THEN
        RAISE EXCEPTION 'Safety stop: connected database is %, expected todo_saas.', current_database();
    END IF;

    SELECT string_agg(v.object_name, ', ' ORDER BY v.object_name)
      INTO missing_objects
      FROM (VALUES
          ('billing.token_wallets'),
          ('billing.token_transactions'),
          ('system.app_settings'),
          ('system.tenants'),
          ('auth.permissions'),
          ('auth.roles'),
          ('auth.role_permissions'),
          ('public.todox_ai_provider'),
          ('public.todox_ai_provider_capability')
      ) AS v(object_name)
     WHERE to_regclass(v.object_name) IS NULL;

    IF missing_objects IS NOT NULL THEN
        RAISE EXCEPTION 'Foundation objects are missing: %. Apply the official TodoX foundation scripts before continuing.', missing_objects;
    END IF;

    SELECT count(*) INTO tenant_count FROM system.tenants;
    IF tenant_count <> 1 THEN
        RAISE EXCEPTION 'Current demo SQL expects exactly one tenant; found %.', tenant_count;
    END IF;
END $$;

SELECT 'DATABASE' AS section,
       current_database() AS database_name,
       current_user AS database_user,
       now() AS checked_at,
       'PASS' AS status;

SELECT 'FOUNDATION_OBJECTS' AS section,
       v.object_name,
       CASE WHEN to_regclass(v.object_name) IS NULL THEN 'MISSING' ELSE 'PASS' END AS status
  FROM (VALUES
      ('billing.token_wallets'),
      ('billing.token_transactions'),
      ('system.app_settings'),
      ('system.tenants'),
      ('auth.permissions'),
      ('auth.roles'),
      ('auth.role_permissions'),
      ('public.todox_ai_provider'),
      ('public.todox_ai_provider_capability')
  ) AS v(object_name)
 ORDER BY v.object_name;

SELECT 'TENANTS' AS section, count(*) AS tenant_count,
       CASE WHEN count(*) = 1 THEN 'PASS' ELSE 'FAIL' END AS status
  FROM system.tenants;

SELECT 'CURRENT_PROVIDER' AS section,
       provider_code,
       provider_name,
       enabled,
       api_key_config_name
  FROM public.todox_ai_provider
 WHERE provider_code = 'yescale_task_image';

SELECT 'CURRENT_MODELS' AS section,
       capability_code,
       model_name,
       enabled,
       is_default,
       allow_user_select,
       unit_cost_points
  FROM public.todox_ai_provider_capability
 WHERE provider_code = 'yescale_task_image'
 ORDER BY capability_code, is_default DESC, model_name;

SELECT 'CURRENT_BILLING_OBJECTS' AS section,
       v.object_name,
       CASE WHEN to_regclass(v.object_name) IS NULL THEN 'TO_BE_CREATED' ELSE 'EXISTS' END AS status
  FROM (VALUES
      ('billing.ai_image_billing_records'),
      ('billing.ai_image_provider_attempts'),
      ('billing.yescale_image_default_snapshot')
  ) AS v(object_name)
 ORDER BY v.object_name;

SELECT 'TOKEN_WALLET_COMPATIBILITY' AS section,
       column_name,
       data_type,
       is_nullable,
       CASE
           WHEN column_name = 'customer_id' AND is_nullable = 'NO'
               THEN 'WILL_BE_UPDATED_BY_SQL_01'
           ELSE 'PASS'
       END AS status
  FROM information_schema.columns
 WHERE table_schema = 'billing'
   AND table_name = 'token_wallets'
   AND column_name IN ('customer_id','wallet_scope','wallet_code','overdraft_limit','low_balance_threshold')
 ORDER BY column_name;

SELECT 'CURRENT_PERMISSIONS' AS section,
       required.module,
       required.action,
       CASE WHEN p.id IS NULL THEN 'TO_BE_CREATED' ELSE 'EXISTS' END AS status
  FROM (VALUES
      ('ai.image', 'system_wallet.use'),
      ('ai.billing', 'dashboard.view'),
      ('ai.billing', 'reconciliation.manage')
  ) AS required(module, action)
  LEFT JOIN auth.permissions p
    ON p.module = required.module
   AND p.action = required.action
 ORDER BY required.module, required.action;

SELECT 'PREFLIGHT_RESULT' AS section,
       'PASS' AS status,
       'Create a fresh pg_dump backup, then run SQL files 01 through 05 in order.' AS next_action;
