-- TodoX.EditVideoTool: packages grant points, storage and the EditVideo license; per-customer server storage for
-- files handed to the AI provider (kept 7 days); one 79AI project per customer project.
-- Additive and idempotent; run through tools/prod-sql/Invoke-TodoXSql.ps1.

-- What a package gives when it is bought: points added to the wallet and the EditVideo storage quota.
ALTER TABLE affiliate.packages ADD COLUMN IF NOT EXISTS points_granted integer NOT NULL DEFAULT 0;
ALTER TABLE affiliate.packages ADD COLUMN IF NOT EXISTS storage_mb integer NOT NULL DEFAULT 1024;
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'packages_points_granted_check') THEN
        ALTER TABLE affiliate.packages ADD CONSTRAINT packages_points_granted_check CHECK (points_granted >= 0);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'packages_storage_mb_check') THEN
        ALTER TABLE affiliate.packages ADD CONSTRAINT packages_storage_mb_check CHECK (storage_mb >= 0);
    END IF;
END $$;

-- Snapshot on the purchase (the package can change later).
ALTER TABLE affiliate.package_purchases ADD COLUMN IF NOT EXISTS points_granted integer NOT NULL DEFAULT 0;
ALTER TABLE affiliate.package_purchases ADD COLUMN IF NOT EXISTS storage_mb integer;

-- Files a customer's TodoX EditVideo uploaded so the AI provider can read them by link.
CREATE TABLE IF NOT EXISTS editvideo.storage_files (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    license_id uuid REFERENCES editvideo.licenses(id) ON DELETE SET NULL,
    project_key text NOT NULL,
    project_name text NOT NULL,
    file_name text NOT NULL,
    relative_path text NOT NULL,
    size_bytes bigint NOT NULL CHECK (size_bytes >= 0),
    content_type text,
    public_token text NOT NULL UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL,
    deleted_at timestamptz
);
CREATE INDEX IF NOT EXISTS storage_files_customer_live_idx ON editvideo.storage_files (customer_id) WHERE deleted_at IS NULL;
CREATE INDEX IF NOT EXISTS storage_files_expiry_idx ON editvideo.storage_files (expires_at) WHERE deleted_at IS NULL;

-- The 79AI project an AI task was created in (status checks must name the same project).
ALTER TABLE editvideo.ai_tasks ADD COLUMN IF NOT EXISTS provider_project_id text;

-- One project on the AI provider (79AI) per customer project, named "<customer code> - <project name>".
CREATE TABLE IF NOT EXISTS editvideo.ai_projects (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id uuid NOT NULL REFERENCES system.tenants(id),
    customer_id uuid NOT NULL REFERENCES crm.customers(id) ON DELETE CASCADE,
    project_key text NOT NULL,
    project_name text NOT NULL,
    provider_project_id text NOT NULL,
    provider_project_name text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, customer_id, project_key)
);
