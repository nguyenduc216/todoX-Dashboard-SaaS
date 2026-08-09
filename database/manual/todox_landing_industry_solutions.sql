-- TodoX Landing industry solutions
-- Run manually on todo_saas. Codex must not execute this file.

BEGIN;

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE SCHEMA IF NOT EXISTS landing;

CREATE TABLE IF NOT EXISTS landing.industry_solutions
(
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    slug varchar(120) NOT NULL UNIQUE,
    title varchar(160) NOT NULL,
    short_description varchar(500) NULL,
    description text NULL,
    thumbnail_url text NULL,
    video_url text NULL,
    aspect_ratio varchar(10) NOT NULL DEFAULT '9:16',
    format_note text NULL,
    goal_note text NULL,
    capability_note text NULL,
    display_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid NULL,
    deleted_at timestamptz NULL,
    deleted_by uuid NULL,
    CONSTRAINT ck_industry_solutions_title_not_blank CHECK (length(btrim(title)) > 0),
    CONSTRAINT ck_industry_solutions_slug_not_blank CHECK (length(btrim(slug)) > 0),
    CONSTRAINT ck_industry_solutions_aspect_ratio CHECK (aspect_ratio IN ('9:16','16:9')),
    CONSTRAINT ck_industry_solutions_display_order CHECK (display_order >= 0)
);

ALTER TABLE landing.industry_solutions
    ADD COLUMN IF NOT EXISTS format_note text NULL,
    ADD COLUMN IF NOT EXISTS goal_note text NULL,
    ADD COLUMN IF NOT EXISTS capability_note text NULL,
    ADD COLUMN IF NOT EXISTS created_by uuid NULL,
    ADD COLUMN IF NOT EXISTS updated_by uuid NULL,
    ADD COLUMN IF NOT EXISTS deleted_at timestamptz NULL,
    ADD COLUMN IF NOT EXISTS deleted_by uuid NULL;

CREATE INDEX IF NOT EXISTS ix_industry_solutions_public_order
ON landing.industry_solutions(is_active, deleted_at, display_order, title);

CREATE OR REPLACE FUNCTION landing.set_industry_solution_updated_at()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_industry_solutions_updated_at ON landing.industry_solutions;
CREATE TRIGGER trg_industry_solutions_updated_at
BEFORE UPDATE ON landing.industry_solutions
FOR EACH ROW EXECUTE FUNCTION landing.set_industry_solution_updated_at();

INSERT INTO auth.permissions
(module, action, code, name, is_active)
VALUES
('landing_industries', 'view', 'landing.industries.view', 'Xem giải pháp ngành nghề Landing', true),
('landing_industries', 'create', 'landing.industries.create', 'Tạo giải pháp ngành nghề Landing', true),
('landing_industries', 'update', 'landing.industries.update', 'Cập nhật giải pháp ngành nghề Landing', true),
('landing_industries', 'delete', 'landing.industries.delete', 'Xóa mềm/khôi phục giải pháp ngành nghề Landing', true)
ON CONFLICT (code) DO UPDATE
SET module = EXCLUDED.module,
    action = EXCLUDED.action,
    name = EXCLUDED.name,
    is_active = true;

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
SELECT g.id, 'landing_industries', 'Giải pháp ngành nghề', '/landing/industries', 'VideoLibrary',
       ARRAY[]::text[], 'always', false, 20, true,
       'Quản lý ngành nghề, mô tả, thumbnail và video clip hiển thị trên todox.vn'
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

COMMIT;
