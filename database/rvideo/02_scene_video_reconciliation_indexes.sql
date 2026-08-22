-- Manual rollout only. Do not run automatically from the application.
-- Purpose: keep the persistent scene-video reconciliation scan selective.
-- Safe to run repeatedly and does not change existing data.

CREATE INDEX IF NOT EXISTS ix_scene_video_versions_reconciliation
    ON video_render.scene_video_versions (tenant_id, status, render_job_id)
    WHERE provider_task_id IS NOT NULL
      AND btrim(provider_task_id) <> ''
      AND status IN ('submitted', 'processing', 'pending_reconciliation', 'rendering');

CREATE INDEX IF NOT EXISTS ix_render_jobs_scene_video_reconciliation
    ON render.render_jobs (tenant_id, job_type, status, retry_after)
    WHERE job_type = 'render_scene_video'
      AND status IN ('rendering', 'pending_reconciliation');
