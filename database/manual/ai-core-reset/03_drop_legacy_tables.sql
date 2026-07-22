BEGIN;
SET LOCAL statement_timeout = '5min';
SET LOCAL lock_timeout = '10s';
SELECT pg_advisory_xact_lock(hashtext('ai-core-reset'));

DROP TABLE IF EXISTS public.todox_ai_operation_billing_transactions CASCADE;
DROP TABLE IF EXISTS public.todox_ai_operation_assets CASCADE;
DROP TABLE IF EXISTS dance_sell.dance_sell_provider_operations CASCADE;
DROP TABLE IF EXISTS public.todox_ai_feature_provider_route CASCADE;
DROP TABLE IF EXISTS billing.ai_image_provider_attempts CASCADE;
DROP TABLE IF EXISTS billing.ai_image_billing_records CASCADE;

COMMIT;
