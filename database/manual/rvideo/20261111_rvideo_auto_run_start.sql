-- RVideo auto mode starts only when the user presses "Bắt đầu tạo video" (user, 2026-11-11).
-- AUTO jobs no longer begin images/videos as soon as scenes exist; the start time is kept here and
-- cleared whenever the scenes are rebuilt from a new prompt. Additive and idempotent.

ALTER TABLE video_render.rvideo_job_settings
    ADD COLUMN IF NOT EXISTS auto_run_started_at timestamptz NULL;

-- AUTO jobs that already have scenes were started under the old rule: keep them running.
UPDATE video_render.rvideo_job_settings s
   SET auto_run_started_at = COALESCE(s.updated_at, now())
 WHERE s.execution_mode = 'AUTO'
   AND s.auto_run_started_at IS NULL
   AND EXISTS (SELECT 1 FROM video_render.video_project_scenes sc WHERE sc.project_id = s.project_id);
