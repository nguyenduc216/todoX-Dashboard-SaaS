-- BEFORE / AFTER verification for point module

WITH required(module, action) AS (
    VALUES
        ('point_config','view'), ('point_config','manage'), ('wallet','view_all'),
        ('wallet','topup'), ('wallet','adjust'), ('wallet','refund'),
        ('voucher','view'), ('voucher','manage'), ('service_point_override','manage')
)
SELECT r.module || '.' || r.action AS permission,
       (p.id IS NOT NULL) AS exists_in_auth_permissions,
       COALESCE(string_agg(DISTINCT ar.code, ', ' ORDER BY ar.code), '') AS assigned_roles,
       CASE WHEN r.module IN ('point_config','wallet','voucher','service_point_override')
                 AND r.action IN ('view','view_all','manage','topup','adjust','refund')
            THEN 'root bypass is code-level; explicit role assignment is still listed'
            ELSE '' END AS inherited_or_wildcard
  FROM required r
  LEFT JOIN auth.permissions p
    ON p.module = r.module AND p.action = r.action AND p.is_active
  LEFT JOIN auth.role_permissions rp ON rp.permission_id = p.id
  LEFT JOIN auth.roles ar ON ar.id = rp.role_id
 GROUP BY r.module, r.action, p.id
 ORDER BY permission;

SELECT r.code AS role_code,
       p.module || '.' || p.action AS permission
  FROM auth.roles r
 CROSS JOIN auth.permissions p
 WHERE lower(r.code) IN ('support', 'admin', 'administrator_root', 'root', 'administrator')
   AND p.module || '.' || p.action IN (
       'point_config.view', 'point_config.manage', 'wallet.view_all', 'wallet.topup',
       'wallet.adjust', 'wallet.refund', 'voucher.view', 'voucher.manage',
       'service_point_override.manage')
 ORDER BY r.code, permission;

SELECT r.code AS customer_role_code,
       p.module || '.' || p.action AS accidental_permission
  FROM auth.roles r
  JOIN auth.role_permissions rp ON rp.role_id = r.id
  JOIN auth.permissions p ON p.id = rp.permission_id
 WHERE lower(r.code) IN ('customer', 'customer_owner', 'customer_user')
   AND p.module || '.' || p.action IN (
       'point_config.view', 'point_config.manage', 'wallet.view_all', 'wallet.topup',
       'wallet.adjust', 'wallet.refund', 'voucher.view', 'voucher.manage',
       'service_point_override.manage')
 ORDER BY r.code, accidental_permission;

WITH required(code) AS (
    VALUES
        ('point_config.view'), ('point_config.manage'), ('wallet.view_all'),
        ('wallet.topup'), ('wallet.adjust'), ('wallet.refund'),
        ('voucher.view'), ('voucher.manage'), ('service_point_override.manage')
)
SELECT r.code AS role_code, required.code AS missing_permission
  FROM auth.roles r
 CROSS JOIN required
 WHERE lower(r.code) IN ('support', 'admin', 'administrator_root', 'root', 'administrator')
   AND NOT EXISTS (
       SELECT 1
         FROM auth.role_permissions rp
         JOIN auth.permissions p ON p.id = rp.permission_id
        WHERE rp.role_id = r.id
          AND p.is_active
          AND p.module || '.' || p.action = required.code
   )
 ORDER BY r.code, missing_permission;

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

SELECT reference_type, reference_id, COUNT(*) AS rdance_reference_charge_count,
       SUM(amount) AS charged_points
  FROM billing.token_transactions
 WHERE transaction_type IN ('debit','charge')
   AND reference_type = 'dance_sell_reference_image'
 GROUP BY reference_type, reference_id
 HAVING COUNT(*) > 1;

SELECT t.reference_type, t.reference_id, t.amount AS remaining_charge,
       j.input_json #>> '{usagePlan,totalPoints}' AS logical_total,
       j.input_json #>> '{usagePlan,imageCount}' AS logical_image_count
  FROM billing.token_transactions t
  JOIN render.render_jobs j ON j.id = t.reference_id
 WHERE t.reference_type = 'dance_sell_job'
 ORDER BY t.created_at DESC
 LIMIT 50;

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

-- rVideo parent image aggregation and persisted pricing snapshot.
SELECT id AS rvideo_parent_job_id,
       customer_id,
       point_cost_estimate,
       point_cost_charged,
       point_status,
       input_json #>> '{usagePlan,imageCount}' AS image_count,
       input_json #>> '{usagePlan,videoSeconds}' AS video_seconds,
       input_json #>> '{usagePlan,voiceCount}' AS voice_count,
       input_json #>> '{usagePlan,totalPoints}' AS total_points
  FROM render.render_jobs
 WHERE job_type IN ('render_video_batch', 'rvideo')
 ORDER BY created_at DESC
 LIMIT 50;

-- rDance service and image usage recorded in the render-job input.
SELECT id AS rdance_parent_job_id,
       point_cost_estimate,
       point_cost_charged,
       point_status,
       input_json #>> '{usagePlan,serviceId}' AS service_id,
       input_json #>> '{usagePlan,imageCount}' AS image_count,
       input_json #>> '{usagePlan,videoSeconds}' AS video_seconds,
       input_json #>> '{usagePlan,totalPoints}' AS total_points
  FROM render.render_jobs
 WHERE job_type = 'dance_sell'
 ORDER BY created_at DESC
 LIMIT 50;

-- Initial parent charges must not be duplicated by child image/video jobs.
SELECT reference_type,
       reference_id,
       COUNT(*) AS charge_count,
       SUM(amount) AS charged_points
  FROM billing.token_transactions
 WHERE transaction_type IN ('debit', 'charge')
   AND reference_type IN ('rvideo_parent_job', 'dance_sell_job', 'timelapse_job')
 GROUP BY reference_type, reference_id
 HAVING COUNT(*) > 1
 ORDER BY charge_count DESC;

-- Explicit USER_RERENDER charges and duplicate deterministic references.
SELECT id,
       amount,
       reference_type,
       reference_id,
       description,
       created_at
  FROM billing.token_transactions
 WHERE reference_type ILIKE '%user_rerender%'
 ORDER BY created_at DESC
 LIMIT 100;

SELECT reference_type,
       reference_id,
       COUNT(*) AS duplicate_count,
       SUM(amount) AS total_points
  FROM billing.token_transactions
 WHERE reference_type ILIKE '%user_rerender%'
   AND transaction_type IN ('debit', 'charge')
 GROUP BY reference_type, reference_id
 HAVING COUNT(*) > 1
 ORDER BY duplicate_count DESC;

-- SYSTEM_RETRY should not produce an additional customer debit.
SELECT t.reference_type,
       t.reference_id,
       COUNT(*) AS retry_debit_count,
       SUM(t.amount) AS retry_points
  FROM billing.token_transactions t
 WHERE t.reference_type ILIKE '%retry%'
   AND t.transaction_type IN ('debit', 'charge')
 GROUP BY t.reference_type, t.reference_id
 HAVING SUM(t.amount) <> 0 OR COUNT(*) > 0
 ORDER BY t.created_at DESC;
