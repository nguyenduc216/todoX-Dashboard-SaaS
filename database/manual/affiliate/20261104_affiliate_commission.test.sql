-- Verifies 20261104_affiliate_commission.sql: schema, guards and the menu item exist,
-- and a full agent -> referral -> purchase -> commission -> paid flow works.
-- The flow runs in a sub-transaction that is always rolled back, so no test data is kept.
DO $$
DECLARE
    missing text;
    cust1 uuid; cust2 uuid; tenant uuid;
    agent_id uuid; pkg_id uuid; purchase_id uuid; commission_id uuid;
    amount numeric;
BEGIN
    SELECT string_agg(t, ', ') INTO missing
      FROM unnest(ARRAY['agents', 'customer_referrals', 'packages', 'package_purchases', 'commissions']) AS t
     WHERE to_regclass('affiliate.' || t) IS NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'affiliate test: missing tables: %', missing;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM system.navigation_menu_items
                    WHERE code = 'affiliates' AND href = '/affiliates' AND is_active
                      AND visibility_policy = 'any_module' AND module_keys @> ARRAY['customers']::text[]) THEN
        RAISE EXCEPTION 'affiliate test: menu item affiliates is missing or not admin-scoped';
    END IF;

    SELECT id, tenant_id INTO cust1, tenant FROM crm.customers ORDER BY created_at LIMIT 1;
    SELECT id INTO cust2 FROM crm.customers WHERE tenant_id = tenant AND id <> cust1 ORDER BY created_at LIMIT 1;
    IF cust2 IS NULL THEN
        RAISE NOTICE 'affiliate test: OK (schema only; fewer than 2 customers for the flow check)';
        RETURN;
    END IF;

    BEGIN
        INSERT INTO affiliate.agents (tenant_id, customer_id, agent_code)
        VALUES (tenant, cust1, 'DLTEST99')
        ON CONFLICT (customer_id) DO UPDATE SET is_active = affiliate.agents.is_active
        RETURNING id INTO agent_id;

        INSERT INTO affiliate.customer_referrals (tenant_id, customer_id, agent_id, referral_code, source)
        VALUES (tenant, cust2, agent_id, 'DLTEST99', 'admin')
        ON CONFLICT (customer_id) DO UPDATE SET agent_id = EXCLUDED.agent_id;

        INSERT INTO affiliate.packages (tenant_id, package_code, package_name, price_amount, cycle_months, commission_percent)
        VALUES (tenant, 'ZZTEST', 'Affiliate test package', 1000000, 1, 15)
        RETURNING id INTO pkg_id;

        INSERT INTO affiliate.package_purchases (tenant_id, customer_id, package_id, package_code, package_name, amount, cycle_start, cycle_end)
        VALUES (tenant, cust2, pkg_id, 'ZZTEST', 'Affiliate test package', 1000000, current_date, current_date + 30)
        RETURNING id INTO purchase_id;

        INSERT INTO affiliate.commissions (tenant_id, purchase_id, agent_id, customer_id, package_id, base_amount, commission_percent, commission_amount)
        VALUES (tenant, purchase_id, agent_id, cust2, pkg_id, 1000000, 15, 150000)
        RETURNING id INTO commission_id;

        UPDATE affiliate.commissions SET status = 'paid', paid_at = now(), bank_reference = 'TEST' WHERE id = commission_id;

        SELECT commission_amount INTO amount FROM affiliate.commissions WHERE id = commission_id AND status = 'paid';
        IF amount IS DISTINCT FROM 150000 THEN
            RAISE EXCEPTION 'affiliate test: commission flow returned %', amount;
        END IF;

        -- Guards: one commission per purchase, percent capped at 100.
        BEGIN
            INSERT INTO affiliate.commissions (tenant_id, purchase_id, agent_id, customer_id, package_id, base_amount, commission_percent, commission_amount)
            VALUES (tenant, purchase_id, agent_id, cust2, pkg_id, 1, 1, 1);
            RAISE EXCEPTION 'affiliate test: duplicate commission per purchase was accepted';
        EXCEPTION WHEN unique_violation THEN NULL;
        END;
        BEGIN
            UPDATE affiliate.packages SET commission_percent = 150 WHERE id = pkg_id;
            RAISE EXCEPTION 'affiliate test: commission_percent 150 was accepted';
        EXCEPTION WHEN check_violation THEN NULL;
        END;

        RAISE EXCEPTION USING ERRCODE = 'P0001', MESSAGE = 'affiliate_test_rollback';
    EXCEPTION WHEN raise_exception THEN
        IF SQLERRM <> 'affiliate_test_rollback' THEN
            RAISE;
        END IF;
    END;

    IF EXISTS (SELECT 1 FROM affiliate.packages WHERE package_code = 'ZZTEST') THEN
        RAISE EXCEPTION 'affiliate test: test data was not rolled back';
    END IF;

    RAISE NOTICE 'affiliate test: OK (schema, menu, flow and guards)';
END $$;
