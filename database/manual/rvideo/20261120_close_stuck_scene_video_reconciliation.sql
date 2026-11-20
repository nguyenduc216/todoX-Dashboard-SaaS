-- Close every scene video stuck in "pending_reconciliation" from before 2026-11-01 (35 current versions; 10 superseded historical ones are immutable and left alone).
-- They were never re-polled (re-poll was broken 2026-10-28 .. 2026-11-20) or needed manual review; the user asked
-- (2026-11-20) to mark them all failed so customers can render them again. No customer points were charged for any
-- of them (customer_charged_points = 0); the billing holds are released the same way the app does on failure.
CREATE TEMP TABLE stuck_versions ON COMMIT DROP AS
SELECT v.id, v.scene_id, v.render_job_id, v.logical_request_id
  FROM video_render.scene_video_versions v
 WHERE v.status = 'pending_reconciliation'
   AND v.updated_at < '2026-11-01'
   -- Superseded (historical) versions are immutable by design (fence_video_operation_write) and never shown
   -- as the scene's current video, so they are left as they are.
   AND NOT video_render.scene_video_job_is_superseded(v.tenant_id, v.render_job_id);

UPDATE video_render.scene_video_versions v
   SET status = 'failed',
       error_code = 'STUCK_RECONCILIATION_CLOSED',
       error_message = 'Video kẹt chờ kiểm tra lại quá lâu; đã đánh dấu thất bại (2026-11-20). Hãy render lại scene này.',
       updated_at = now()
  FROM stuck_versions s
 WHERE v.id = s.id;

UPDATE render.render_jobs j
   SET status = 'failed',
       error_code = 'STUCK_RECONCILIATION_CLOSED',
       error_message = 'Closed after being stuck in pending_reconciliation (2026-11-20).',
       completed_at = now(),
       lock_owner = NULL,
       lock_until = NULL,
       updated_at = now()
 WHERE j.id IN (SELECT render_job_id FROM stuck_versions)
   AND j.status = 'pending_reconciliation';

INSERT INTO render.render_job_events (job_id, tenant_id, event_type, level, message, data_json, created_at)
SELECT j.id, j.tenant_id, 'JOB_STUCK_RECONCILIATION_CLOSED', 'warning',
       'Closed as failed after being stuck in pending_reconciliation.',
       jsonb_build_object('script', '20261120_close_stuck_scene_video_reconciliation.sql'), now()
  FROM render.render_jobs j
 WHERE j.id IN (SELECT render_job_id FROM stuck_versions)
   AND j.error_code = 'STUCK_RECONCILIATION_CLOSED';

UPDATE billing.ai_image_billing_records b
   SET status = 'released',
       failed_at = COALESCE(b.failed_at, now()),
       error_message = COALESCE(b.error_message, 'Closed after being stuck in pending_reconciliation (2026-11-20).'),
       reconciliation_lock_owner = NULL,
       reconciliation_lock_until = NULL,
       updated_at = now()
 WHERE b.logical_request_id IN (SELECT logical_request_id FROM stuck_versions)
   AND b.status IN ('pending_reconciliation', 'manual_review')
   AND COALESCE(b.customer_charged_points, 0) = 0;

-- Scenes still shown as rendering this video now show it failed; scenes with another ready video keep it.
UPDATE video_render.video_project_scenes sc
   SET status = 'failed',
       updated_at = now()
 WHERE sc.id IN (SELECT scene_id FROM stuck_versions)
   AND sc.status = 'video_rendering'
   AND NOT EXISTS (SELECT 1 FROM video_render.scene_video_versions v
                    WHERE v.scene_id = sc.id
                      AND v.status IN ('submitted', 'processing', 'rendering', 'pending_reconciliation'));
