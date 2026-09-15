-- Verify rDance reference submit-failure compensation after running
-- database/migrations/20260902_rdance_reference_submit_refund.sql

-- 1. Trigger/function installed.
SELECT n.nspname AS schema_name,
       p.proname AS function_name
  FROM pg_proc p
  JOIN pg_namespace n ON n.oid = p.pronamespace
 WHERE n.nspname = 'dance_sell'
   AND p.proname = 'refund_reference_submit_failure';

SELECT trigger_name, event_manipulation, action_timing
  FROM information_schema.triggers
 WHERE event_object_schema = 'dance_sell'
   AND event_object_table = 'dance_sell_provider_operations'
   AND trigger_name = 'trg_refund_reference_submit_failure';

-- 2. Reference-image failures that were charged but have no provider task id.
-- After the migration, newly failed rows in this state should be refunded by the trigger.
SELECT o.id,
       o.dance_sell_job_id,
       o.status,
       o.provider_task_id,
       o.billing_status,
       o.refund_status,
       o.todox_points_charged,
       o.todox_points_refunded,
       o.balance_before,
       o.balance_after,
       o.failed_at,
       o.refunded_at
  FROM dance_sell.dance_sell_provider_operations o
 WHERE o.operation_type = 'reference_image'
   AND o.status = 'failed'
   AND o.provider_task_id IS NULL
 ORDER BY o.updated_at DESC
 LIMIT 50;

-- 3. Compensation ledger rows. There must be at most one row per operation id.
SELECT t.reference_id AS operation_id,
       count(*) AS refund_count,
       sum(t.amount) AS refunded_points,
       min(t.created_at) AS first_refund_at,
       max(t.created_at) AS last_refund_at
  FROM billing.token_transactions t
 WHERE t.transaction_type = 'refund'
   AND t.reference_type = 'dance_sell_reference_submit_refund'
 GROUP BY t.reference_id
 ORDER BY max(t.created_at) DESC;

-- 4. Any duplicate compensation is an error; expected result: zero rows.
SELECT t.reference_id AS operation_id,
       count(*) AS refund_count
  FROM billing.token_transactions t
 WHERE t.transaction_type = 'refund'
   AND t.reference_type = 'dance_sell_reference_submit_refund'
 GROUP BY t.reference_id
HAVING count(*) > 1;

-- 5. Provider-accepted failures must not receive this automatic compensation.
-- Expected result: zero rows.
SELECT o.id,
       o.provider_task_id,
       t.id AS refund_transaction_id,
       t.amount
  FROM dance_sell.dance_sell_provider_operations o
  JOIN billing.token_transactions t
    ON t.reference_id = o.id
   AND t.reference_type = 'dance_sell_reference_submit_refund'
   AND t.transaction_type = 'refund'
 WHERE o.operation_type = 'reference_image'
   AND o.provider_task_id IS NOT NULL;
