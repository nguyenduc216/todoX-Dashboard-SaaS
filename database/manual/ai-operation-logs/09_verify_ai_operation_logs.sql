SELECT 'provider_accounts' AS check_name, COUNT(*) AS row_count FROM public.todox_ai_provider_account
UNION ALL
SELECT 'feature_provider_routes', COUNT(*) FROM public.todox_ai_feature_provider_route
UNION ALL
SELECT 'dance_sell_provider_operations', COUNT(*) FROM dance_sell.dance_sell_provider_operations
UNION ALL
SELECT 'operation_assets', COUNT(*) FROM public.todox_ai_operation_assets
UNION ALL
SELECT 'provider_balance_ledger', COUNT(*) FROM public.todox_ai_provider_balance_ledger;

SELECT feature_code, operation_type, provider_code, model_name, enabled, is_default, allow_user_select
  FROM public.todox_ai_feature_provider_route
 WHERE feature_code='dance_sell'
 ORDER BY operation_type, is_default DESC, priority;
