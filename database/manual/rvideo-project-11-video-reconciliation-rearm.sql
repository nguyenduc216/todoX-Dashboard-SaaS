BEGIN;

-- Verify the exact project/scenes and immutable provider/billing identity before re-arming.
SELECT b.id,
       b.logical_request_id,
       b.status,
       b.provider_code,
       b.capability_code,
       b.provider_task_id,
       b.customer_charged_points,
       b.reconciliation_attempt_count,
       b.reconciliation_lock_owner,
       b.reconciliation_lock_until
  FROM billing.ai_image_billing_records b
  JOIN video_render.scene_video_versions v
    ON v.billing_logical_request_id = b.logical_request_id
 WHERE v.project_id = 11
   AND v.scene_id BETWEEN 48 AND 54
   AND v.provider_task_id IS NOT NULL
   AND b.status = 'pending_reconciliation'
   AND b.provider_code = '79ai'
   AND b.capability_code = 'rvideo_scene_video_generation'
 ORDER BY v.scene_id, b.id;

UPDATE billing.ai_image_billing_records b
   SET reconciliation_attempt_count = 0,
       reconciliation_lock_owner = NULL,
       reconciliation_lock_until = NULL,
       pending_reconciliation_at = now(),
       updated_at = now()
 WHERE b.id IN (
     SELECT b2.id
       FROM billing.ai_image_billing_records b2
       JOIN video_render.scene_video_versions v
         ON v.billing_logical_request_id = b2.logical_request_id
      WHERE v.project_id = 11
        AND v.scene_id BETWEEN 48 AND 54
        AND v.provider_task_id IS NOT NULL
        AND b2.status = 'pending_reconciliation'
        AND b2.provider_code = '79ai'
        AND b2.capability_code = 'rvideo_scene_video_generation'
 );

-- Verify that only reconciliation control fields were re-armed.
SELECT b.id,
       b.logical_request_id,
       b.status,
       b.provider_code,
       b.capability_code,
       b.provider_task_id,
       b.customer_charged_points,
       b.reconciliation_attempt_count,
       b.reconciliation_lock_owner,
       b.reconciliation_lock_until,
       b.pending_reconciliation_at
  FROM billing.ai_image_billing_records b
  JOIN video_render.scene_video_versions v
    ON v.billing_logical_request_id = b.logical_request_id
 WHERE v.project_id = 11
   AND v.scene_id BETWEEN 48 AND 54
   AND v.provider_task_id IS NOT NULL
   AND b.status = 'pending_reconciliation'
   AND b.provider_code = '79ai'
   AND b.capability_code = 'rvideo_scene_video_generation'
 ORDER BY v.scene_id, b.id;

COMMIT;
