-- RVIDEO native Core Job link. Additive and idempotent; do not execute automatically.
-- Legacy video projects remain valid with core_job_id = NULL.

ALTER TABLE video_render.video_projects
    ADD COLUMN IF NOT EXISTS core_job_id uuid NULL;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conname = 'fk_video_projects_core_job'
           AND conrelid = 'video_render.video_projects'::regclass
    ) THEN
        ALTER TABLE video_render.video_projects
            ADD CONSTRAINT fk_video_projects_core_job
            FOREIGN KEY (core_job_id)
            REFERENCES render.render_jobs(id)
            ON DELETE RESTRICT;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_video_projects_core_job_id
    ON video_render.video_projects(core_job_id)
    WHERE core_job_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_video_projects_tenant_core_job_id
    ON video_render.video_projects(tenant_id, core_job_id)
    WHERE core_job_id IS NOT NULL;
