DO $$
BEGIN
    IF to_regclass('affiliate.settings') IS NULL THEN RAISE EXCEPTION 'affiliate.settings is missing'; END IF;
    IF to_regclass('affiliate.payouts') IS NULL THEN RAISE EXCEPTION 'affiliate.payouts is missing'; END IF;
    IF to_regclass('affiliate.topup_commissions') IS NULL THEN RAISE EXCEPTION 'affiliate.topup_commissions is missing'; END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'affiliate'
                   AND table_name = 'agents' AND column_name = 'topup_commission_percent') THEN
        RAISE EXCEPTION 'affiliate.agents.topup_commission_percent is missing';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'affiliate'
                   AND table_name = 'commissions' AND column_name = 'payout_id') THEN
        RAISE EXCEPTION 'affiliate.commissions.payout_id is missing';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = 'affiliate'
                   AND indexname = 'ux_affiliate_topup_commissions_transaction') THEN
        RAISE EXCEPTION 'one commission per top-up index is missing';
    END IF;
    -- Existing agents keep the default rate and existing commissions are not attached to a payout.
    IF EXISTS (SELECT 1 FROM affiliate.agents WHERE topup_commission_percent IS NOT NULL) THEN
        RAISE EXCEPTION 'existing agents must not get a custom rate';
    END IF;
    IF EXISTS (SELECT 1 FROM affiliate.commissions WHERE payout_id IS NOT NULL) THEN
        RAISE EXCEPTION 'existing commissions must not be attached to a payout';
    END IF;
END $$;
