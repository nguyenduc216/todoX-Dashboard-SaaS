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

-- auth.permissions.code is GENERATED ALWAYS as module || '.' || action.
-- The current database has no UNIQUE constraint on code, so do NOT use ON CONFLICT.
-- First update existing permission rows.
UPDATE auth.permissions
SET name = 'Xem giải pháp ngành nghề Landing',
    is_active = true
WHERE module = 'landing.industries' AND action = 'view';

UPDATE auth.permissions
SET name = 'Tạo giải pháp ngành nghề Landing',
    is_active = true
WHERE module = 'landing.industries' AND action = 'create';

UPDATE auth.permissions
SET name = 'Cập nhật giải pháp ngành nghề Landing',
    is_active = true
WHERE module = 'landing.industries' AND action = 'update';

UPDATE auth.permissions
SET name = 'Xóa mềm/khôi phục giải pháp ngành nghề Landing',
    is_active = true
WHERE module = 'landing.industries' AND action = 'delete';

-- Then insert only rows that do not exist. code is generated automatically.
INSERT INTO auth.permissions (module, action, name, is_active)
SELECT 'landing.industries', 'view', 'Xem giải pháp ngành nghề Landing', true
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
    WHERE module = 'landing.industries' AND action = 'view'
);

INSERT INTO auth.permissions (module, action, name, is_active)
SELECT 'landing.industries', 'create', 'Tạo giải pháp ngành nghề Landing', true
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
    WHERE module = 'landing.industries' AND action = 'create'
);

INSERT INTO auth.permissions (module, action, name, is_active)
SELECT 'landing.industries', 'update', 'Cập nhật giải pháp ngành nghề Landing', true
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
    WHERE module = 'landing.industries' AND action = 'update'
);

INSERT INTO auth.permissions (module, action, name, is_active)
SELECT 'landing.industries', 'delete', 'Xóa mềm/khôi phục giải pháp ngành nghề Landing', true
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
    WHERE module = 'landing.industries' AND action = 'delete'
);

-- Canonical Landing Page group: both Landing features MUST live under this one group.
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

-- Put the existing contact item into the canonical Landing Page group.
WITH g AS
(
    SELECT id FROM system.navigation_menu_groups WHERE code = 'landing_page'
)
UPDATE system.navigation_menu_items i
SET group_id = g.id,
    sort_order = 10,
    is_active = true,
    updated_at = now()
FROM g
WHERE i.code = 'landing_contacts'
   OR i.href = '/landing/contacts';

-- Ensure Contact item exists in the same group.
WITH g AS
(
    SELECT id FROM system.navigation_menu_groups WHERE code = 'landing_page'
)
INSERT INTO system.navigation_menu_items
(group_id, code, title, href, icon_key, module_keys, visibility_policy, match_all, sort_order, is_active, description)
SELECT g.id, 'landing_contacts', 'Liên hệ tư vấn', '/landing/contacts', 'ContactMail',
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

-- Ensure Industry item exists in the SAME group.
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

-- Clean up duplicated Landing Page groups created by older scripts.
-- First move any Landing-related child items to the canonical group.
WITH canonical AS (
    SELECT id FROM system.navigation_menu_groups WHERE code = 'landing_page'
), duplicate_groups AS (
    SELECT id
    FROM system.navigation_menu_groups
    WHERE id <> (SELECT id FROM canonical)
      AND (
        lower(trim(title)) = 'landing page'
        OR code IN ('landing', 'landing_industries', 'landing_contacts')
      )
)
UPDATE system.navigation_menu_items i
SET group_id = (SELECT id FROM canonical),
    updated_at = now()
WHERE i.group_id IN (SELECT id FROM duplicate_groups)
  AND (
      i.code IN ('landing_contacts', 'landing_industries')
      OR i.href IN ('/landing/contacts', '/landing/industries')
  );

-- Deactivate empty duplicate groups instead of hard-deleting, to avoid FK/history issues.
WITH canonical AS (
    SELECT id FROM system.navigation_menu_groups WHERE code = 'landing_page'
)
UPDATE system.navigation_menu_groups g
SET is_active = false,
    updated_at = now()
WHERE g.id <> (SELECT id FROM canonical)
  AND (
      lower(trim(g.title)) = 'landing page'
      OR g.code IN ('landing', 'landing_industries', 'landing_contacts')
  )
  AND NOT EXISTS (
      SELECT 1
      FROM system.navigation_menu_items i
      WHERE i.group_id = g.id
        AND i.is_active = true
  );

COMMIT;
