BEGIN;

DO $$
BEGIN
    IF to_regclass('auth.permissions') IS NULL
       OR to_regclass('auth.roles') IS NULL
       OR to_regclass('auth.role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Missing auth permission tables.';
    END IF;
END $$;

INSERT INTO auth.permissions
    (id, module, action, name, description, permission_group, is_active, created_at)
SELECT gen_random_uuid(), v.module, v.action, v.name, v.description, 'Point Module', true, now()
FROM (VALUES
    ('point_config','view','View point configuration','Allows viewing point rates.'),
    ('point_config','manage','Manage point configuration','Allows changing point rates.'),
    ('wallet','view_all','View all wallets','Allows viewing customer wallets.'),
    ('wallet','topup','Top up wallets','Allows adding points to wallets.'),
    ('wallet','adjust','Adjust wallets','Allows administrative wallet adjustments.'),
    ('wallet','refund','Refund wallets','Allows administrative wallet refunds.'),
    ('voucher','view','View vouchers','Allows viewing point vouchers.'),
    ('voucher','manage','Manage vouchers','Allows creating and managing point vouchers.'),
    ('service_point_override','manage','Manage service point overrides','Allows managing service-specific point overrides.')
) AS v(module, action, name, description)
WHERE NOT EXISTS (
    SELECT 1
      FROM auth.permissions p
     WHERE p.module = v.module
       AND p.action = v.action
);

INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM auth.roles r
  JOIN auth.permissions p
    ON p.module || '.' || p.action IN (
        'point_config.view', 'point_config.manage', 'wallet.view_all', 'wallet.topup',
        'wallet.adjust', 'wallet.refund', 'voucher.view', 'voucher.manage',
        'service_point_override.manage')
 WHERE lower(r.code) IN ('support', 'admin', 'administrator_root', 'root', 'administrator')
   AND p.is_active
   AND NOT EXISTS (
       SELECT 1
         FROM auth.role_permissions rp
        WHERE rp.role_id = r.id
          AND rp.permission_id = p.id
   );

COMMIT;
