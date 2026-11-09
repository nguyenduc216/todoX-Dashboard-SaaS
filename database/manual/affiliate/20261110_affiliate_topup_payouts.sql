-- TodoX Dashboard - Đại lý: hoa hồng theo lần nạp điểm của khách được giới thiệu + lịch sử thanh toán hoa hồng.
-- Target database: todo_saas. Additive and idempotent: only the affiliate schema (created by
-- 20261104_affiliate_commission.sql) is extended; billing.* and crm.* are only read / referenced.

-- 1) Cấu hình chung: giá trị 1 điểm (VND) để quy đổi lần nạp điểm ra tiền, và % hoa hồng mặc định.
CREATE TABLE IF NOT EXISTS affiliate.settings
(
    tenant_id uuid PRIMARY KEY,
    vnd_per_point numeric(18,2) NOT NULL DEFAULT 0,
    default_topup_commission_percent numeric(5,2) NOT NULL DEFAULT 0,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid NULL,
    CONSTRAINT ck_affiliate_settings_vnd CHECK (vnd_per_point >= 0),
    CONSTRAINT ck_affiliate_settings_percent CHECK (default_topup_commission_percent >= 0 AND default_topup_commission_percent <= 100)
);

-- 2) % hoa hồng nạp điểm riêng của từng đại lý (null = dùng mặc định).
ALTER TABLE affiliate.agents ADD COLUMN IF NOT EXISTS topup_commission_percent numeric(5,2) NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_affiliate_agents_topup_percent'
                   AND conrelid = 'affiliate.agents'::regclass) THEN
        ALTER TABLE affiliate.agents ADD CONSTRAINT ck_affiliate_agents_topup_percent
            CHECK (topup_commission_percent IS NULL OR (topup_commission_percent >= 0 AND topup_commission_percent <= 100));
    END IF;
END $$;

-- 3) Lịch sử thanh toán hoa hồng TodoX chuyển cho đại lý (thông tin ngân hàng được chụp lại lúc thanh toán).
CREATE TABLE IF NOT EXISTS affiliate.payouts
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    agent_id uuid NOT NULL REFERENCES affiliate.agents(id),
    amount numeric(18,2) NOT NULL,
    currency varchar(10) NOT NULL DEFAULT 'VND',
    item_count integer NOT NULL DEFAULT 0,
    paid_at timestamptz NOT NULL DEFAULT now(),
    bank_name varchar(200) NULL,
    bank_account_no varchar(100) NULL,
    bank_account_name varchar(200) NULL,
    bank_reference varchar(200) NULL,
    note text NULL,
    status varchar(20) NOT NULL DEFAULT 'paid',
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    cancelled_at timestamptz NULL,
    cancelled_by uuid NULL,
    CONSTRAINT ck_affiliate_payouts_amount CHECK (amount >= 0),
    CONSTRAINT ck_affiliate_payouts_status CHECK (status IN ('paid', 'cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_affiliate_payouts_agent
    ON affiliate.payouts (agent_id, paid_at DESC);

ALTER TABLE affiliate.commissions ADD COLUMN IF NOT EXISTS payout_id uuid NULL REFERENCES affiliate.payouts(id);

-- 4) Hoa hồng phát sinh từ mỗi lần nạp điểm (billing.token_transactions loại 'topup') của khách được giới thiệu.
--    Giá trị điểm và % được chụp lại khi ghi nhận; mỗi giao dịch nạp chỉ sinh một dòng.
CREATE TABLE IF NOT EXISTS affiliate.topup_commissions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    agent_id uuid NOT NULL REFERENCES affiliate.agents(id),
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    transaction_id uuid NOT NULL,
    topup_at timestamptz NOT NULL,
    points numeric(18,2) NOT NULL,
    vnd_per_point numeric(18,2) NOT NULL,
    base_amount numeric(18,2) NOT NULL,
    commission_percent numeric(5,2) NOT NULL,
    commission_amount numeric(18,2) NOT NULL,
    currency varchar(10) NOT NULL DEFAULT 'VND',
    status varchar(20) NOT NULL DEFAULT 'pending',
    payout_id uuid NULL REFERENCES affiliate.payouts(id),
    paid_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_affiliate_topup_commissions_status CHECK (status IN ('pending', 'paid', 'cancelled')),
    CONSTRAINT ck_affiliate_topup_commissions_amount CHECK (commission_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_affiliate_topup_commissions_transaction
    ON affiliate.topup_commissions (transaction_id);
CREATE INDEX IF NOT EXISTS ix_affiliate_topup_commissions_agent
    ON affiliate.topup_commissions (agent_id, status, topup_at DESC);
