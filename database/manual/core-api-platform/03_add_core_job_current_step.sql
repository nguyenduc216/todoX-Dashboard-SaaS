-- Manual, additive Core Platform schema alignment.
-- Do not execute automatically. Review and run manually against todo_saas only.

BEGIN;

ALTER TABLE render.render_jobs
    ADD COLUMN IF NOT EXISTS current_step varchar(80);

UPDATE render.render_jobs
   SET current_step = CASE
       WHEN status = 'draft' THEN 'draft'
       WHEN status = 'queued' THEN 'queued'
       WHEN status = 'preparing' THEN 'preparing'
       WHEN status = 'rendering' THEN 'rendering'
       WHEN status = 'post_processing' THEN 'post_processing'
       WHEN status = 'pending_reconciliation' THEN 'pending_reconciliation'
       WHEN status = 'completed' THEN 'completed'
       WHEN status = 'failed' THEN 'failed'
       WHEN status = 'cancelled' THEN 'cancelled'
       ELSE status
   END
 WHERE current_step IS NULL;

COMMENT ON COLUMN render.render_jobs.current_step IS
    'Transport-neutral Core/Application lifecycle step. Service adapters may refine this value.';

COMMIT;
