-- Normalize catalog.services status values to the commercial service contract.
-- Safe to run multiple times. Does not delete services or change service IDs.

UPDATE catalog.services
SET status = 'active',
    updated_at = now()
WHERE lower(trim(coalesce(status, ''))) IN ('enabled', 'active')
  AND status IS DISTINCT FROM 'active';

UPDATE catalog.services
SET status = 'inactive',
    updated_at = now()
WHERE lower(trim(coalesce(status, ''))) IN ('disabled', 'inactive')
  AND status IS DISTINCT FROM 'inactive';

UPDATE catalog.services
SET status = 'inactive',
    updated_at = now()
WHERE status IS NULL
   OR trim(status) = ''
   OR lower(trim(status)) NOT IN ('active', 'inactive');

ALTER TABLE catalog.services
    ALTER COLUMN status SET DEFAULT 'active';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'catalog.services'::regclass
          AND conname = 'ck_catalog_services_status_active_inactive'
    ) THEN
        ALTER TABLE catalog.services
            ADD CONSTRAINT ck_catalog_services_status_active_inactive
            CHECK (status IN ('active', 'inactive'));
    END IF;
END $$;
