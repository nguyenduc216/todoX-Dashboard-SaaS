-- TodoX Dashboard - Đại lý & hoa hồng (Affiliate commission)
-- Target database: todo_saas (PostgreSQL)
-- Run manually. Application MUST NOT auto-run migrations.
-- Idempotent: safe to execute more than once.
-- Additive only: creates schema "affiliate"; does NOT alter Foundation V2 objects
-- (crm.customers, auth.*, billing.* are only referenced by foreign keys).

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS affiliate;

-- 1) Đại lý: một khách hàng (crm.customers) được đánh dấu là đại lý, có mã đại lý riêng.
CREATE TABLE IF NOT EXISTS affiliate.agents
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    agent_code varchar(40) NOT NULL,
    is_active boolean NOT NULL DEFAULT true,
    bank_name varchar(200) NULL,
    bank_account_no varchar(100) NULL,
    bank_account_name varchar(200) NULL,
    note text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_by uuid NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_affiliate_agents_customer
    ON affiliate.agents (customer_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_affiliate_agents_tenant_code
    ON affiliate.agents (tenant_id, upper(agent_code));

-- 2) Khách hàng được giới thiệu bởi đại lý (mỗi khách hàng tối đa 1 đại lý).
CREATE TABLE IF NOT EXISTS affiliate.customer_referrals
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    agent_id uuid NOT NULL REFERENCES affiliate.agents(id) ON DELETE CASCADE,
    referral_code varchar(40) NOT NULL,
    source varchar(30) NOT NULL DEFAULT 'admin',
    assigned_by uuid NULL,
    assigned_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_affiliate_customer_referrals_source CHECK (source IN ('register', 'admin'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_affiliate_customer_referrals_customer
    ON affiliate.customer_referrals (customer_id);
CREATE INDEX IF NOT EXISTS ix_affiliate_customer_referrals_agent
    ON affiliate.customer_referrals (agent_id);

-- 3) Gói khách hàng (A/B/C...) và tỷ lệ hoa hồng đại lý trên từng gói.
CREATE TABLE IF NOT EXISTS affiliate.packages
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    package_code varchar(40) NOT NULL,
    package_name varchar(200) NOT NULL,
    price_amount numeric(18,2) NOT NULL DEFAULT 0,
    currency varchar(10) NOT NULL DEFAULT 'VND',
    cycle_months integer NOT NULL DEFAULT 1,
    commission_percent numeric(5,2) NOT NULL DEFAULT 0,
    description text NULL,
    is_active boolean NOT NULL DEFAULT true,
    sort_order integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_by uuid NULL,
    CONSTRAINT ck_affiliate_packages_price CHECK (price_amount >= 0),
    CONSTRAINT ck_affiliate_packages_cycle CHECK (cycle_months >= 0),
    CONSTRAINT ck_affiliate_packages_percent CHECK (commission_percent >= 0 AND commission_percent <= 100)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_affiliate_packages_tenant_code
    ON affiliate.packages (tenant_id, upper(package_code));

-- 4) Lịch sử mua / gia hạn gói của khách hàng (mỗi chu kỳ là một dòng).
CREATE TABLE IF NOT EXISTS affiliate.package_purchases
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    package_id uuid NOT NULL REFERENCES affiliate.packages(id),
    package_code varchar(40) NOT NULL,
    package_name varchar(200) NOT NULL,
    amount numeric(18,2) NOT NULL,
    currency varchar(10) NOT NULL DEFAULT 'VND',
    purchased_at timestamptz NOT NULL DEFAULT now(),
    cycle_start date NULL,
    cycle_end date NULL,
    status varchar(20) NOT NULL DEFAULT 'paid',
    note text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    CONSTRAINT ck_affiliate_package_purchases_amount CHECK (amount >= 0),
    CONSTRAINT ck_affiliate_package_purchases_status CHECK (status IN ('paid', 'cancelled'))
);

CREATE INDEX IF NOT EXISTS ix_affiliate_package_purchases_customer
    ON affiliate.package_purchases (customer_id, purchased_at DESC);

-- 5) Hoa hồng đại lý phát sinh từ mỗi lần mua gói + trạng thái chuyển khoản.
--    Tỷ lệ và số tiền được chụp (snapshot) tại thời điểm mua gói.
CREATE TABLE IF NOT EXISTS affiliate.commissions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    purchase_id uuid NOT NULL REFERENCES affiliate.package_purchases(id) ON DELETE CASCADE,
    agent_id uuid NOT NULL REFERENCES affiliate.agents(id),
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    package_id uuid NOT NULL REFERENCES affiliate.packages(id),
    base_amount numeric(18,2) NOT NULL,
    commission_percent numeric(5,2) NOT NULL,
    commission_amount numeric(18,2) NOT NULL,
    currency varchar(10) NOT NULL DEFAULT 'VND',
    status varchar(20) NOT NULL DEFAULT 'pending',
    paid_at timestamptz NULL,
    paid_by uuid NULL,
    bank_reference varchar(200) NULL,
    note text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_affiliate_commissions_status CHECK (status IN ('pending', 'paid', 'cancelled')),
    CONSTRAINT ck_affiliate_commissions_amount CHECK (commission_amount >= 0)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_affiliate_commissions_purchase
    ON affiliate.commissions (purchase_id);
CREATE INDEX IF NOT EXISTS ix_affiliate_commissions_agent_status
    ON affiliate.commissions (agent_id, status, created_at DESC);

-- 6) Menu dashboard (nhóm Quản trị hệ thống). Ứng dụng cũng tự bổ sung menu nếu chưa seed.
INSERT INTO system.navigation_menu_items
(group_id, code, title, href, icon_key, module_keys, visibility_policy, match_all, sort_order, is_active, description)
SELECT g.id, 'affiliates', 'Đại lý & hoa hồng', '/affiliates', 'Handshake',
       ARRAY[]::text[], 'admin_job_monitor', false, 25, true,
       'Quản lý đại lý, tỷ lệ hoa hồng theo gói, lịch sử mua gói và chi trả hoa hồng'
  FROM system.navigation_menu_groups g
 WHERE g.code = 'administration'
ON CONFLICT (code) DO UPDATE
SET group_id = EXCLUDED.group_id,
    title = EXCLUDED.title,
    href = EXCLUDED.href,
    icon_key = EXCLUDED.icon_key,
    module_keys = EXCLUDED.module_keys,
    visibility_policy = EXCLUDED.visibility_policy,
    sort_order = EXCLUDED.sort_order,
    is_active = true,
    description = EXCLUDED.description,
    updated_at = now();

COMMIT;

-- Verification
SELECT table_name
  FROM information_schema.tables
 WHERE table_schema = 'affiliate'
 ORDER BY table_name;
