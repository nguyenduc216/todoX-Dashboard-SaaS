-- Manual repair for system/admin render jobs.
-- Run this on the TodoX SaaS PostgreSQL database only after review.
-- It makes render.render_jobs.customer_id nullable so jobs without a customer scope can be enqueued.

SELECT
    table_schema,
    table_name,
    column_name,
    is_nullable
FROM information_schema.columns
WHERE table_schema = 'render'
  AND table_name = 'render_jobs'
  AND column_name = 'customer_id';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'render'
          AND table_name = 'render_jobs'
          AND column_name = 'customer_id'
          AND is_nullable = 'NO'
    ) THEN
        ALTER TABLE render.render_jobs
            ALTER COLUMN customer_id DROP NOT NULL;
    END IF;
END $$;

SELECT
    table_schema,
    table_name,
    column_name,
    is_nullable
FROM information_schema.columns
WHERE table_schema = 'render'
  AND table_name = 'render_jobs'
  AND column_name = 'customer_id';
