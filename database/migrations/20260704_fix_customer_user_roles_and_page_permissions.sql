BEGIN;

-- Backfill role customer for existing customer users that can log in
-- but were not linked through auth.user_roles.
INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id
FROM auth.app_users u
JOIN auth.roles r ON r.code = 'customer'
WHERE u.user_type = 'customer'
  AND NOT EXISTS (
      SELECT 1
      FROM auth.user_roles ur
      WHERE ur.user_id = u.id
        AND ur.role_id = r.id
  );

-- Ensure the customer role can access page management when these permissions exist.
INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM auth.roles r
JOIN auth.permissions p ON p.module = 'customer_pages'
WHERE r.code = 'customer'
  AND p.action IN ('view', 'create', 'update', 'delete', 'activate', 'deactivate')
  AND NOT EXISTS (
      SELECT 1
      FROM auth.role_permissions rp
      WHERE rp.role_id = r.id
        AND rp.permission_id = p.id
  );

INSERT INTO system.foundation_versions (version_code, script_name, status, message)
SELECT '20260704_PERMISSION_FIX', '20260704_fix_customer_user_roles_and_page_permissions.sql', 'success',
       'Backfill customer user role and customer_pages permissions'
WHERE NOT EXISTS (
    SELECT 1
    FROM system.foundation_versions
    WHERE version_code = '20260704_PERMISSION_FIX'
);

COMMIT;
