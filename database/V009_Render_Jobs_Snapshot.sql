-- V009_Render_Jobs_Snapshot.sql
-- Add job snapshot storage for render jobs without duplicating queue tables.

CREATE TABLE IF NOT EXISTS render.render_job_snapshots (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    job_id          uuid NOT NULL REFERENCES render.render_jobs(id) ON DELETE CASCADE,
    tenant_id       uuid REFERENCES system.tenants(id),
    project_snapshot jsonb NOT NULL DEFAULT '{}'::jsonb,
    scene_snapshots  jsonb NOT NULL DEFAULT '[]'::jsonb,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_render_job_snapshots_job UNIQUE (job_id)
);

CREATE INDEX IF NOT EXISTS ix_render_job_snapshots_tenant_created
    ON render.render_job_snapshots (tenant_id, created_at DESC);

INSERT INTO system.foundation_versions (version_code, script_name, status, message)
SELECT 'V009', 'V009_Render_Jobs_Snapshot.sql', 'success', 'Render job snapshot storage added'
WHERE NOT EXISTS (SELECT 1 FROM system.foundation_versions WHERE version_code = 'V009');
