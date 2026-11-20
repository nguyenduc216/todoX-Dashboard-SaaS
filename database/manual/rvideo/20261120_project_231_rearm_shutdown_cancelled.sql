-- Job 9890fa94 (project 231): three scene video jobs were cancelled by the app restart on 2026-11-19 16:36 while
-- their 79AI tasks were still running, so their versions stayed "pending_reconciliation" forever (the app now keeps
-- such jobs instead of cancelling them). Put these three back to pending_reconciliation so the reconciliation
-- worker polls the same 79AI task and finishes them the normal way (failed + billing hold released, or completed).
-- Only the three listed jobs, and only while they are still cancelled with their provider task intact.
UPDATE render.render_jobs j
   SET status = 'pending_reconciliation',
       point_status = 'not_required',
       error_code = 'SCENE_VIDEO_SHUTDOWN_RETAINED',
       error_message = 'Re-armed after restart cancellation; the same provider task will be polled.',
       cancelled_at = NULL,
       completed_at = NULL,
       lock_owner = NULL,
       lock_until = NULL,
       retry_after = NULL,
       updated_at = now()
 WHERE j.id IN ('6b3c8080-6031-4ff6-8864-454cc0cf5a7a',
                '7f24d794-d25a-45af-a4b6-1f4bce27e496',
                '9ff53c25-9b6d-468d-8c1d-cce83f2be150')
   AND j.job_type = 'render_scene_video'
   AND j.status = 'cancelled'
   AND j.error_code = 'SCENE_VIDEO_POLL_SCHEDULED'
   AND NULLIF(btrim(j.provider_task_id), '') IS NOT NULL
   AND NULLIF(btrim(j.provider_video_id_base), '') IS NOT NULL
   AND EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                WHERE v.render_job_id = j.id AND v.project_id = 231 AND v.status = 'pending_reconciliation');

INSERT INTO render.render_job_events (job_id, tenant_id, event_type, level, message, data_json, created_at)
SELECT j.id, j.tenant_id, 'JOB_REARMED_AFTER_SHUTDOWN_CANCEL', 'warning',
       'Re-armed to pending_reconciliation: cancelled by app restart while the provider task was running.',
       jsonb_build_object('script', '20261120_project_231_rearm_shutdown_cancelled.sql'), now()
  FROM render.render_jobs j
 WHERE j.id IN ('6b3c8080-6031-4ff6-8864-454cc0cf5a7a',
                '7f24d794-d25a-45af-a4b6-1f4bce27e496',
                '9ff53c25-9b6d-468d-8c1d-cce83f2be150')
   AND j.error_code = 'SCENE_VIDEO_SHUTDOWN_RETAINED';
