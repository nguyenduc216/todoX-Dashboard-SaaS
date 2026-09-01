-- Manual, idempotent customer account favorite service setup.
-- Database: todo_saas. Do not execute automatically.

BEGIN;

CREATE TABLE IF NOT EXISTS crm.customer_service_favorites (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL,
    customer_id uuid NOT NULL,
    user_id uuid NOT NULL,
    service_id uuid NOT NULL,
    added_source text NOT NULL DEFAULT 'admin',
    created_by_user_id uuid NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT customer_service_favorites_added_source_chk CHECK (added_source IN ('admin', 'user')),
    CONSTRAINT customer_service_favorites_customer_fk FOREIGN KEY (customer_id) REFERENCES crm.customers(id) ON DELETE CASCADE,
    CONSTRAINT customer_service_favorites_user_fk FOREIGN KEY (user_id) REFERENCES auth.app_users(id) ON DELETE CASCADE,
    CONSTRAINT customer_service_favorites_service_fk FOREIGN KEY (service_id) REFERENCES catalog.services(id) ON DELETE CASCADE,
    CONSTRAINT customer_service_favorites_created_by_fk FOREIGN KEY (created_by_user_id) REFERENCES auth.app_users(id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_service_favorites_tenant_user_service
    ON crm.customer_service_favorites (tenant_id, user_id, service_id);

CREATE INDEX IF NOT EXISTS ix_customer_service_favorites_tenant_customer
    ON crm.customer_service_favorites (tenant_id, customer_id);

CREATE INDEX IF NOT EXISTS ix_customer_service_favorites_tenant_service
    ON crm.customer_service_favorites (tenant_id, service_id);

INSERT INTO crm.customer_service_favorites (
    id, tenant_id, customer_id, user_id, service_id, added_source, created_by_user_id, created_at)
SELECT
    gen_random_uuid(),
    u.tenant_id,
    cu.customer_id,
    u.id,
    s.id,
    'admin',
    NULL,
    now()
FROM auth.app_users u
JOIN crm.customer_users cu ON cu.user_id = u.id
CROSS JOIN catalog.services s
WHERE u.user_type = 'customer'
  AND lower(s.status) = 'active'
ON CONFLICT (tenant_id, user_id, service_id) DO NOTHING;

COMMIT;
