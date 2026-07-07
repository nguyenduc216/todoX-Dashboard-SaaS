-- TodoX Dashboard Two-Level Menu
-- Target DB: todo_saas
-- Idempotent metadata for dashboard navigation. Does not change auth.permissions.

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS system;

CREATE TABLE IF NOT EXISTS system.navigation_menu_groups (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code varchar(100) NOT NULL UNIQUE,
    title varchar(200) NOT NULL,
    icon_key varchar(100) NULL,
    sort_order int NOT NULL DEFAULT 0,
    default_expanded boolean NOT NULL DEFAULT true,
    is_active boolean NOT NULL DEFAULT true,
    description text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS system.navigation_menu_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    group_id uuid NOT NULL REFERENCES system.navigation_menu_groups(id) ON DELETE CASCADE,
    code varchar(120) NOT NULL UNIQUE,
    title varchar(200) NOT NULL,
    href text NOT NULL,
    icon_key varchar(100) NULL,
    module_keys text[] NOT NULL DEFAULT ARRAY[]::text[],
    visibility_policy varchar(50) NOT NULL DEFAULT 'any_module',
    match_all boolean NOT NULL DEFAULT false,
    sort_order int NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    description text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT navigation_menu_items_visibility_policy_check CHECK (
        visibility_policy IN ('always', 'any_module', 'admin_avatar_manager')
    )
);

CREATE INDEX IF NOT EXISTS ix_navigation_menu_groups_active_order
ON system.navigation_menu_groups(is_active, sort_order, title);

CREATE INDEX IF NOT EXISTS ix_navigation_menu_items_group_active_order
ON system.navigation_menu_items(group_id, is_active, sort_order, title);

CREATE INDEX IF NOT EXISTS ix_navigation_menu_items_module_keys_gin
ON system.navigation_menu_items USING gin(module_keys);

CREATE OR REPLACE FUNCTION system.set_navigation_menu_updated_at()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_navigation_menu_groups_set_updated_at ON system.navigation_menu_groups;
CREATE TRIGGER trg_navigation_menu_groups_set_updated_at
BEFORE UPDATE ON system.navigation_menu_groups
FOR EACH ROW
EXECUTE FUNCTION system.set_navigation_menu_updated_at();

DROP TRIGGER IF EXISTS trg_navigation_menu_items_set_updated_at ON system.navigation_menu_items;
CREATE TRIGGER trg_navigation_menu_items_set_updated_at
BEFORE UPDATE ON system.navigation_menu_items
FOR EACH ROW
EXECUTE FUNCTION system.set_navigation_menu_updated_at();

INSERT INTO system.navigation_menu_groups (code, title, icon_key, sort_order, default_expanded, description)
VALUES
('overview', 'Tổng quan', 'Dashboard', 10, true, 'Tổng quan hệ thống'),
('administration', 'Quản trị hệ thống', 'AdminPanelSettings', 20, true, 'Người dùng, khách hàng, phân quyền và cấu hình hệ thống'),
('billing_services', 'Dịch vụ & điểm', 'AccountBalanceWallet', 30, true, 'Điểm sử dụng và danh mục dịch vụ'),
('ai_studio', 'AI Studio', 'AutoFixHigh', 40, true, 'Avatar, render job và prompt AI'),
('social_marketing', 'Kênh mạng xã hội', 'Public', 50, true, 'Page, link video tham chiếu và chiến dịch reup'),
('api_integration', 'API & tích hợp', 'Api', 60, false, 'Cấu hình API provider và endpoint')
ON CONFLICT (code) DO UPDATE
SET title = EXCLUDED.title,
    icon_key = EXCLUDED.icon_key,
    sort_order = EXCLUDED.sort_order,
    default_expanded = EXCLUDED.default_expanded,
    description = EXCLUDED.description,
    is_active = true,
    updated_at = now();

WITH g AS (
    SELECT code, id FROM system.navigation_menu_groups
)
INSERT INTO system.navigation_menu_items
(group_id, code, title, href, icon_key, module_keys, visibility_policy, match_all, sort_order, description)
SELECT g.id, x.code, x.title, x.href, x.icon_key, x.module_keys, x.visibility_policy, x.match_all, x.sort_order, x.description
FROM (
    VALUES
    ('overview', 'dashboard', 'Dashboard', '/', 'Dashboard', ARRAY[]::text[], 'always', true, 10, 'Trang tổng quan'),
    ('administration', 'admin_users', 'Quản trị viên', '/admin-users', 'AdminPanelSettings', ARRAY['admin_users']::text[], 'any_module', false, 10, 'Quản lý tài khoản quản trị'),
    ('administration', 'customers', 'Quản lý khách hàng', '/customers', 'Groups', ARRAY['customers']::text[], 'any_module', false, 20, 'Quản lý khách hàng'),
    ('administration', 'customer_accounts', 'Quản lý tài khoản', '/customer-accounts', 'ManageAccounts', ARRAY['customer_accounts']::text[], 'any_module', false, 30, 'Quản lý tài khoản khách hàng'),
    ('administration', 'permissions', 'Phân quyền', '/permissions', 'Security', ARRAY['roles','permissions']::text[], 'any_module', false, 40, 'Vai trò và quyền'),
    ('administration', 'settings', 'Cài đặt hệ thống', '/settings', 'Settings', ARRAY['settings','app_settings']::text[], 'any_module', false, 50, 'Cài đặt chung'),
    ('administration', 'activity_logs', 'Nhật ký hoạt động', '/activity-logs', 'Article', ARRAY['audit_logs']::text[], 'any_module', false, 60, 'Audit/activity logs'),
    ('billing_services', 'wallets', 'Quản lý điểm', '/wallets', 'AccountBalanceWallet', ARRAY['token_wallets']::text[], 'any_module', false, 10, 'Điểm/token wallet'),
    ('billing_services', 'services', 'Quản lý dịch vụ', '/services', 'Category', ARRAY['services']::text[], 'any_module', false, 20, 'Danh mục dịch vụ'),
    ('ai_studio', 'admin_avatar_manager', 'Quản lý avatar mẫu', '/admin/avatar-manager', 'FaceRetouchingNatural', ARRAY['settings','services']::text[], 'admin_avatar_manager', false, 10, 'Avatar mẫu và public avatar prompt'),
    ('ai_studio', 'render_jobs', 'Render Jobs', '/render-jobs', 'Movie', ARRAY['render_jobs']::text[], 'any_module', false, 20, 'Render jobs'),
    ('ai_studio', 'prompt_templates', 'Quản lý prompt', '/settings/prompt-templates', 'AutoFixHigh', ARRAY['prompt_templates','settings','app_settings']::text[], 'any_module', false, 30, 'Prompt templates'),
    ('social_marketing', 'customer_pages', 'Trang mạng xã hội', '/my-pages', 'Public', ARRAY['customer_pages']::text[], 'any_module', false, 10, 'Facebook/social pages'),
    ('social_marketing', 'reference_videos', 'Link video tham chiếu', '/reference-videos', 'VideoLibrary', ARRAY['reference_videos']::text[], 'any_module', false, 20, 'Reference videos'),
    ('social_marketing', 'reup_campaigns', 'Chiến dịch Reup', '/reup-campaigns', 'Campaign', ARRAY['reup_campaigns']::text[], 'any_module', false, 30, 'Reup campaigns'),
    ('api_integration', 'api_settings', 'Cài đặt API (todox)', '/api-settings', 'Api', ARRAY['api_providers']::text[], 'any_module', false, 10, 'API settings'),
    ('api_integration', 'api_providers', 'Nhà cung cấp API', '/settings/api-providers', 'Dns', ARRAY['api_providers']::text[], 'any_module', false, 20, 'API providers'),
    ('api_integration', 'api_endpoints', 'API Endpoints', '/settings/api-endpoints', 'SettingsEthernet', ARRAY['api_providers']::text[], 'any_module', false, 30, 'API endpoints')
) AS x(group_code, code, title, href, icon_key, module_keys, visibility_policy, match_all, sort_order, description)
JOIN g ON g.code = x.group_code
ON CONFLICT (code) DO UPDATE
SET group_id = EXCLUDED.group_id,
    title = EXCLUDED.title,
    href = EXCLUDED.href,
    icon_key = EXCLUDED.icon_key,
    module_keys = EXCLUDED.module_keys,
    visibility_policy = EXCLUDED.visibility_policy,
    match_all = EXCLUDED.match_all,
    sort_order = EXCLUDED.sort_order,
    description = EXCLUDED.description,
    is_active = true,
    updated_at = now();
