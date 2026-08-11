-- TodoX Landing Page - Contact Leads & Dashboard Menu
-- Target database: todo_saas (PostgreSQL)
-- Run manually. Application MUST NOT auto-run migrations.
-- Idempotent: safe to execute more than once.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS landing;
CREATE SCHEMA IF NOT EXISTS system;

CREATE TABLE IF NOT EXISTS landing.contact_leads
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    lead_code varchar(40) NOT NULL UNIQUE,
    full_name varchar(200) NOT NULL,
    phone varchar(50) NOT NULL,
    email varchar(320) NULL,
    company_name varchar(250) NULL,
    industry_code varchar(100) NULL,
    interested_product varchar(100) NULL,
    message text NULL,
    status varchar(30) NOT NULL DEFAULT 'new',
    priority varchar(20) NOT NULL DEFAULT 'normal',
    assigned_user_id uuid NULL,
    source_url text NULL,
    referrer_url text NULL,
    landing_page_code varchar(100) NOT NULL DEFAULT 'todox-home',
    utm_source varchar(200) NULL,
    utm_medium varchar(200) NULL,
    utm_campaign varchar(200) NULL,
    utm_content varchar(200) NULL,
    utm_term varchar(200) NULL,
    ip_address inet NULL,
    user_agent text NULL,
    request_id varchar(100) NULL,
    consent_accepted boolean NOT NULL DEFAULT false,
    consent_at timestamptz NULL,
    first_contacted_at timestamptz NULL,
    next_follow_up_at timestamptz NULL,
    converted_at timestamptz NULL,
    closed_at timestamptz NULL,
    internal_note text NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_by uuid NULL,
    is_deleted boolean NOT NULL DEFAULT false,
    deleted_at timestamptz NULL,
    deleted_by uuid NULL,
    CONSTRAINT ck_landing_contact_leads_status CHECK
    (status IN ('new','contacted','consulting','quotation_sent','qualified','converted','not_suitable','closed')),
    CONSTRAINT ck_landing_contact_leads_priority CHECK
    (priority IN ('low','normal','high','urgent')),
    CONSTRAINT ck_landing_contact_leads_name_not_blank CHECK (length(btrim(full_name)) > 0),
    CONSTRAINT ck_landing_contact_leads_phone_not_blank CHECK (length(btrim(phone)) > 0)
);

COMMENT ON TABLE landing.contact_leads IS
'Thông tin khách hàng đăng ký tư vấn từ landing page TodoX. Dùng chung database với Dashboard.';

CREATE TABLE IF NOT EXISTS landing.contact_lead_activities
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    lead_id uuid NOT NULL REFERENCES landing.contact_leads(id) ON DELETE CASCADE,
    activity_type varchar(50) NOT NULL,
    title varchar(250) NULL,
    content text NULL,
    old_status varchar(30) NULL,
    new_status varchar(30) NULL,
    old_assigned_user_id uuid NULL,
    new_assigned_user_id uuid NULL,
    activity_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    metadata_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT ck_landing_lead_activity_type CHECK
    (activity_type IN ('created','note','status_changed','assigned','phone_call','email','meeting','quotation','follow_up','converted','closed','restored','deleted'))
);

CREATE SEQUENCE IF NOT EXISTS landing.contact_lead_code_seq
    AS bigint START WITH 1 INCREMENT BY 1 MINVALUE 1 CACHE 20;

CREATE OR REPLACE FUNCTION landing.generate_contact_lead_code()
RETURNS varchar
LANGUAGE plpgsql
AS $$
DECLARE next_no bigint;
BEGIN
    next_no := nextval('landing.contact_lead_code_seq');
    RETURN 'LD-' || to_char(current_date, 'YYYYMMDD') || '-' || lpad(next_no::text, 6, '0');
END;
$$;

ALTER TABLE landing.contact_leads
    ALTER COLUMN lead_code SET DEFAULT landing.generate_contact_lead_code();

CREATE OR REPLACE FUNCTION landing.set_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_contact_leads_set_updated_at ON landing.contact_leads;
CREATE TRIGGER trg_contact_leads_set_updated_at
BEFORE UPDATE ON landing.contact_leads
FOR EACH ROW EXECUTE FUNCTION landing.set_updated_at();

CREATE OR REPLACE FUNCTION landing.log_contact_lead_created()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO landing.contact_lead_activities
    (lead_id, activity_type, title, content, new_status, activity_at, created_by, metadata_json)
    VALUES
    (NEW.id, 'created', 'Tiếp nhận đăng ký tư vấn', 'Thông tin được gửi từ landing page.',
     NEW.status, NEW.created_at, NEW.created_by,
     jsonb_build_object('source_url', NEW.source_url, 'landing_page_code', NEW.landing_page_code, 'request_id', NEW.request_id));
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_contact_leads_log_created ON landing.contact_leads;
CREATE TRIGGER trg_contact_leads_log_created
AFTER INSERT ON landing.contact_leads
FOR EACH ROW EXECUTE FUNCTION landing.log_contact_lead_created();

CREATE INDEX IF NOT EXISTS ix_contact_leads_created_at
ON landing.contact_leads(created_at DESC) WHERE is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_status_created_at
ON landing.contact_leads(status, created_at DESC) WHERE is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_assigned_status
ON landing.contact_leads(assigned_user_id, status, next_follow_up_at) WHERE is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_phone
ON landing.contact_leads(phone) WHERE is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_email_lower
ON landing.contact_leads(lower(email)) WHERE email IS NOT NULL AND is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_next_follow_up
ON landing.contact_leads(next_follow_up_at)
WHERE next_follow_up_at IS NOT NULL
  AND status NOT IN ('converted','not_suitable','closed')
  AND is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_utm_campaign
ON landing.contact_leads(utm_campaign, created_at DESC)
WHERE utm_campaign IS NOT NULL AND is_deleted = false;
CREATE INDEX IF NOT EXISTS ix_contact_leads_metadata_gin
ON landing.contact_leads USING gin(metadata_json);
CREATE INDEX IF NOT EXISTS ix_contact_lead_activities_lead_time
ON landing.contact_lead_activities(lead_id, activity_at DESC);

-- Dashboard navigation metadata.
INSERT INTO system.navigation_menu_groups
(code, title, icon_key, sort_order, default_expanded, is_active, description)
VALUES
('landing_page', 'Landing Page', 'Language', 55, true, true,
 'Quản lý nội dung và khách hàng đăng ký từ website todox.vn')
ON CONFLICT (code) DO UPDATE
SET title = EXCLUDED.title,
    icon_key = EXCLUDED.icon_key,
    sort_order = EXCLUDED.sort_order,
    default_expanded = EXCLUDED.default_expanded,
    is_active = true,
    description = EXCLUDED.description,
    updated_at = now();

WITH g AS
(
    SELECT id FROM system.navigation_menu_groups WHERE code = 'landing_page'
)
INSERT INTO system.navigation_menu_items
(group_id, code, title, href, icon_key, module_keys, visibility_policy, match_all, sort_order, is_active, description)
SELECT g.id, 'landing_contacts', 'Liên hệ tư vấn', '/landing/contacts', 'ContactPhone',
       ARRAY[]::text[], 'always', false, 10, true,
       'Quản lý khách hàng đăng ký tư vấn từ todox.vn'
FROM g
ON CONFLICT (code) DO UPDATE
SET group_id = EXCLUDED.group_id,
    title = EXCLUDED.title,
    href = EXCLUDED.href,
    icon_key = EXCLUDED.icon_key,
    module_keys = EXCLUDED.module_keys,
    visibility_policy = EXCLUDED.visibility_policy,
    match_all = EXCLUDED.match_all,
    sort_order = EXCLUDED.sort_order,
    is_active = true,
    description = EXCLUDED.description,
    updated_at = now();

-- Optional dashboard permissions for server-side authorization.
INSERT INTO auth.permissions
(module, action, code, name, is_active)
VALUES
('landing_contacts', 'view', 'landing_contacts.view', 'Xem lead Landing Page', true),
('landing_contacts', 'update', 'landing_contacts.update', 'Cập nhật lead Landing Page', true),
('landing_contacts', 'assign', 'landing_contacts.assign', 'Phân công lead Landing Page', true),
('landing_contacts', 'delete', 'landing_contacts.delete', 'Xóa mềm/khôi phục lead Landing Page', true),
('landing_contacts', 'export', 'landing_contacts.export', 'Xuất CSV lead Landing Page', true)
ON CONFLICT (code) DO UPDATE
SET module = EXCLUDED.module,
    action = EXCLUDED.action,
    name = EXCLUDED.name,
    is_active = true;

COMMIT;

-- Verification
SELECT g.code AS group_code, g.title AS group_title,
       i.code AS item_code, i.title AS item_title, i.href, i.is_active
FROM system.navigation_menu_groups g
LEFT JOIN system.navigation_menu_items i ON i.group_id = g.id
WHERE g.code = 'landing_page'
ORDER BY i.sort_order;

SELECT status, count(*) AS total
FROM landing.contact_leads
WHERE is_deleted = false
GROUP BY status
ORDER BY status;
