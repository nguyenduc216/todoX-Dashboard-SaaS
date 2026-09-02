-- BEFORE / AFTER verification for point module

SELECT tenant_id, resource_type, quality_tier, rate, unit, is_active
  FROM billing.point_rate_config
 ORDER BY tenant_id, resource_type, quality_tier;

SELECT tenant_id, service_id, resource_type, quality_tier, rate, unit, is_active
  FROM billing.service_point_rate_override
 ORDER BY tenant_id, service_id, resource_type, quality_tier;

SELECT o.tenant_id,
       o.service_id,
       o.resource_type,
       o.quality_tier,
       o.rate AS override_rate,
       g.rate AS global_rate,
       COALESCE(o.rate, g.rate) AS effective_rate,
       COALESCE(o.unit, g.unit) AS unit,
       CASE WHEN o.id IS NOT NULL THEN 'service_override' ELSE 'global' END AS source
  FROM billing.point_rate_config g
  LEFT JOIN billing.service_point_rate_override o
    ON o.tenant_id = g.tenant_id
   AND lower(o.resource_type) = lower(g.resource_type)
   AND lower(o.quality_tier) = lower(g.quality_tier)
 WHERE g.is_active = true
 ORDER BY g.tenant_id, g.resource_type, g.quality_tier, o.service_id;

SELECT w.customer_id,
       w.balance AS usable_balance,
       w.locked_balance AS locked_balance,
       w.balance - COALESCE(w.locked_balance, 0) AS available_balance
  FROM billing.token_wallets w
 ORDER BY w.created_at DESC
 LIMIT 50;

SELECT transaction_type,
       amount,
       balance_before,
       balance_after,
       reference_type,
       reference_id,
       created_at
  FROM billing.token_transactions
 ORDER BY created_at DESC
 LIMIT 25;

SELECT *
  FROM billing.token_usage_logs
 ORDER BY created_at DESC
 LIMIT 25;

SELECT *
  FROM billing.point_vouchers
 ORDER BY created_at DESC;

SELECT *
  FROM billing.point_voucher_redemptions
 ORDER BY redeemed_at DESC;

SELECT id, point_cost_estimate, point_cost_charged, point_status, input_json, created_at
  FROM render.render_jobs
 WHERE point_cost_estimate > 0
 ORDER BY created_at DESC
 LIMIT 25;

SELECT id AS rvideo_parent_job_id,
       point_cost_estimate,
       point_cost_charged,
       point_status,
       input_json->'usagePlan' AS usage_plan,
       (input_json->'usagePlan'->>'videoSeconds')::int AS total_video_seconds
  FROM render.render_jobs
 WHERE job_type IN ('render_video_batch', 'rvideo')
 ORDER BY created_at DESC
 LIMIT 25;

SELECT job_id, clip_index, start_progress_percent, end_progress_percent, duration_seconds
  FROM timelapse.timelapse_video_clips
 ORDER BY job_id, clip_index;

SELECT job_id, SUM(duration_seconds) AS timelapse_total_video_seconds
  FROM timelapse.timelapse_video_clips
 GROUP BY job_id
 ORDER BY job_id;

SELECT id AS rdance_job_id, point_cost_estimate, point_cost_charged, point_status,
       input_json->'usagePlan' AS usage_plan
  FROM render.render_jobs
 WHERE job_type = 'dance_sell'
 ORDER BY created_at DESC
 LIMIT 25;

SELECT id, error_code, point_status, point_cost_estimate
  FROM render.render_jobs
 WHERE point_status = 'insufficient'
 ORDER BY updated_at DESC
 LIMIT 25;

SELECT reference_type, reference_id, COUNT(*) AS charge_count
  FROM billing.token_transactions
 WHERE transaction_type IN ('debit', 'charge')
 GROUP BY reference_type, reference_id
 HAVING COUNT(*) > 1;

SELECT reference_type, reference_id, COUNT(*) AS rerender_charge_count
  FROM billing.token_transactions
 WHERE transaction_type IN ('debit', 'charge')
   AND reference_type ILIKE '%rerender%'
 GROUP BY reference_type, reference_id
 HAVING COUNT(*) > 1;

SELECT reference_type, reference_id, COUNT(*) AS system_retry_charge_count
  FROM billing.token_transactions
 WHERE transaction_type IN ('debit', 'charge')
   AND reference_type ILIKE '%retry%'
 GROUP BY reference_type, reference_id
 HAVING COUNT(*) > 1;

SELECT w.customer_id, w.balance, w.locked_balance, t.id AS transaction_id,
       t.transaction_type, t.amount, t.created_at
  FROM billing.token_wallets w
  LEFT JOIN billing.token_transactions t ON t.wallet_id = w.id
 ORDER BY w.customer_id, t.created_at DESC
 LIMIT 100;

SELECT w.customer_id,
       SUM(w.balance) AS total_balance,
       SUM(w.locked_balance) AS total_locked,
       SUM(t.amount) FILTER (WHERE t.transaction_type IN ('debit', 'charge')) AS debits,
       SUM(t.amount) FILTER (WHERE t.transaction_type IN ('credit', 'topup', 'refund', 'voucher', 'adjust_plus')) AS credits
  FROM billing.token_wallets w
  LEFT JOIN billing.token_transactions t ON t.wallet_id = w.id
 GROUP BY w.customer_id
 ORDER BY w.customer_id;

SELECT COUNT(*) AS duplicate_charges
  FROM (
      SELECT reference_type, reference_id, COUNT(*) AS cnt
        FROM billing.token_transactions
       WHERE transaction_type IN ('debit', 'charge')
       GROUP BY reference_type, reference_id
      HAVING COUNT(*) > 1
  ) x;

SELECT COUNT(*) AS duplicate_voucher_redemptions
  FROM (
      SELECT voucher_id, customer_id, COUNT(*) AS cnt
        FROM billing.point_voucher_redemptions
       GROUP BY voucher_id, customer_id
      HAVING COUNT(*) > 1
  ) x;

SELECT COUNT(*) AS duplicate_refunds
  FROM (
      SELECT reference_type, reference_id, COUNT(*) AS cnt
        FROM billing.token_transactions
       WHERE transaction_type = 'refund'
       GROUP BY reference_type, reference_id
      HAVING COUNT(*) > 1
  ) x;

SELECT routine_schema, routine_name
  FROM information_schema.routines
 WHERE routine_schema IN ('billing', 'render', 'catalog')
   AND routine_name ILIKE '%legacy%'
 ORDER BY routine_schema, routine_name;
