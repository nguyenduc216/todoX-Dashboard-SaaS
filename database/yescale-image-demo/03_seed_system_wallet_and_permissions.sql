-- Standalone SQL, not a migration.
-- Ensures demo system wallet permissions and billing dashboard permissions exist.

BEGIN;

DO $$
BEGIN
    IF to_regclass('auth.permissions') IS NULL THEN
        RAISE EXCEPTION 'Missing table auth.permissions.';
    END IF;
    IF to_regclass('auth.roles') IS NULL THEN
        RAISE EXCEPTION 'Missing table auth.roles.';
    END IF;
    IF to_regclass('auth.role_permissions') IS NULL THEN
        RAISE EXCEPTION 'Missing table auth.role_permissions.';
    END IF;
    IF to_regclass('billing.token_wallets') IS NULL THEN
        RAISE EXCEPTION 'Missing table billing.token_wallets.';
    END IF;
END $$;

INSERT INTO auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
SELECT gen_random_uuid(), 'ai.image', 'system_wallet.use',
       'Use AI image system wallet',
       'Allows an authenticated admin/root user to charge YEScale image renders to the system image wallet.',
       'AI Providers', true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
     WHERE module = 'ai.image'
       AND action = 'system_wallet.use'
);

INSERT INTO auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
SELECT gen_random_uuid(), 'ai.billing', 'dashboard.view',
       'View AI billing dashboard',
       'Allows an authenticated admin/root user to view AI billing usage and reconciliation summaries.',
       'AI Providers', true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
     WHERE module = 'ai.billing'
       AND action = 'dashboard.view'
);

INSERT INTO auth.permissions (id, module, action, name, description, permission_group, is_active, created_at)
SELECT gen_random_uuid(), 'ai.billing', 'reconciliation.manage',
       'Manage AI billing reconciliation',
       'Allows an authenticated admin/root user to manage AI image billing reconciliation records.',
       'AI Providers', true, now()
WHERE NOT EXISTS (
    SELECT 1 FROM auth.permissions
     WHERE module = 'ai.billing'
       AND action = 'reconciliation.manage'
);

INSERT INTO auth.role_permissions (role_id, permission_id)
SELECT r.id, p.id
  FROM auth.roles r
  JOIN auth.permissions p
    ON (p.module = 'ai.image' AND p.action = 'system_wallet.use')
    OR (p.module = 'ai.billing' AND p.action IN ('dashboard.view','reconciliation.manage'))
 WHERE r.code IN ('administrator_root', 'root', 'admin', 'administrator')
   AND NOT EXISTS (
       SELECT 1 FROM auth.role_permissions rp
        WHERE rp.role_id = r.id AND rp.permission_id = p.id
   );

DO $$
DECLARE
    system_wallet_count int;
    missing_permissions int;
BEGIN
    SELECT count(*) INTO system_wallet_count
      FROM billing.token_wallets
     WHERE wallet_scope = 'system'
       AND wallet_code = 'TODOX_AI_IMAGE_SYSTEM';

    IF system_wallet_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one TODOX_AI_IMAGE_SYSTEM wallet, found %.', system_wallet_count;
    END IF;

    SELECT count(*) INTO missing_permissions
      FROM (
        VALUES
          ('ai.image','system_wallet.use'),
          ('ai.billing','dashboard.view'),
          ('ai.billing','reconciliation.manage')
      ) required(module, action)
      WHERE NOT EXISTS (
          SELECT 1 FROM auth.permissions p
           WHERE p.module = required.module
             AND p.action = required.action
             AND p.is_active = true
      );

    IF missing_permissions > 0 THEN
        RAISE EXCEPTION 'Missing active AI billing permissions. Count=%', missing_permissions;
    END IF;
END $$;

COMMIT;
