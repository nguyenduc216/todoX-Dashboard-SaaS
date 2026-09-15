-- Non-destructive RVIDEO recovery for project_id = 5.
-- Inserts one render_scene_images batch job only when:
--   - project 5 exists,
--   - no active render_scene_images batch exists for project 5,
--   - at least one scene is missing an image or is failed,
--   - saved RVIDEO character settings are valid for the selected mode.
-- Does not update scenes, delete jobs, apply schema changes, or run migrations.

WITH project_scope AS (
    SELECT p.id,
           p.tenant_id,
           p.user_id,
           p.customer_id,
           p.original_prompt,
           '9:16' AS aspect_ratio
      FROM video_render.video_projects p
     WHERE p.id = 5
),
settings_scope AS (
    SELECT s.project_id,
           s.skip_character,
           upper(COALESCE(NULLIF(s.character_mode, ''), 'NONE')) AS character_mode,
           s.selected_character_id,
           s.character_snapshot_json
      FROM video_render.rvideo_job_settings s
      JOIN project_scope p ON p.id = s.project_id
),
scene_scope AS (
    SELECT array_agg(vps.id ORDER BY vps.scene_index) AS scene_ids
      FROM video_render.video_project_scenes vps
      JOIN project_scope p ON p.id = vps.project_id AND p.tenant_id = vps.tenant_id
     WHERE NULLIF(vps.static_image_url, '') IS NULL
        OR lower(COALESCE(vps.status, '')) = 'failed'
),
reference_scope AS (
    SELECT p.id AS project_id,
           CASE
               WHEN COALESCE(s.skip_character, true) OR s.character_mode = 'NONE' THEN 'NONE'
               WHEN s.character_mode = 'UPLOAD' THEN 'UPLOAD'
               ELSE 'LIBRARY'
           END AS reference_source,
           CASE
               WHEN COALESCE(s.skip_character, true) OR s.character_mode <> 'LIBRARY' THEN NULL
               ELSE s.selected_character_id
           END AS character_id,
           CASE
               WHEN s.character_mode IN ('UPLOAD', 'LIBRARY')
               THEN COALESCE(
                   NULLIF(s.character_snapshot_json->>'storageKey', ''),
                   NULLIF(s.character_snapshot_json->>'masterImageObjectKey', ''),
                   NULLIF(s.character_snapshot_json->>'objectKey', '')
               )
               ELSE NULL
           END AS reference_object_key,
           CASE
               WHEN s.character_mode IN ('UPLOAD', 'LIBRARY')
               THEN COALESCE(
                   NULLIF(s.character_snapshot_json->>'fileUrl', ''),
                   NULLIF(s.character_snapshot_json->>'masterImageUrl', ''),
                   NULLIF(s.character_snapshot_json->>'url', '')
               )
               ELSE NULL
           END AS reference_url
      FROM project_scope p
      JOIN settings_scope s ON s.project_id = p.id
),
validated AS (
    SELECT p.*,
           r.reference_source,
           r.character_id,
           r.reference_object_key,
           r.reference_url,
           ss.scene_ids
      FROM project_scope p
      JOIN reference_scope r ON r.project_id = p.id
      JOIN scene_scope ss ON ss.scene_ids IS NOT NULL AND array_length(ss.scene_ids, 1) > 0
     WHERE (
               r.reference_source = 'NONE'
            OR (r.reference_source = 'UPLOAD'
                AND (r.reference_object_key IS NOT NULL OR r.reference_url IS NOT NULL))
            OR (r.reference_source = 'LIBRARY'
                AND (r.character_id IS NOT NULL OR r.reference_object_key IS NOT NULL OR r.reference_url IS NOT NULL))
           )
       AND NOT EXISTS (
           SELECT 1
             FROM render.render_jobs active
            WHERE active.tenant_id = p.tenant_id
              AND active.job_type = 'render_scene_images'
              AND active.status IN ('queued', 'preparing', 'rendering', 'post_processing', 'pending_reconciliation')
              AND active.input_json->>'projectId' = p.id::text
       )
),
inserted AS (
    INSERT INTO render.render_jobs
        (tenant_id, user_id, customer_id, job_type, status, priority,
         input_json, prompt_json, reference_json, log_code,
         point_cost_estimate, point_status, provider_code, model_code, max_attempts,
         queued_at, created_at)
    SELECT tenant_id,
           user_id,
           customer_id,
           'render_scene_images',
           'queued',
           100,
           jsonb_build_object(
               'capabilityCode', 'rvideo_scene_image_generation',
               'referenceSource', reference_source,
               'projectId', id,
               'aspectRatio', CASE WHEN aspect_ratio = '16:9' THEN '16:9' ELSE '9:16' END,
               'characterId', character_id,
               'characterReferenceObjectKey', reference_object_key,
               'characterReferenceUrl', reference_url,
               'userId', user_id,
               'customerId', customer_id,
               'createdBy', COALESCE(user_id::text, 'rvideo-recovery'),
               'onlyMissingOrFailed', true,
               'sceneIds', to_jsonb(scene_ids)
           ),
           jsonb_build_object('projectId', id, 'source', 'rvideo_project_5_manual_recovery', 'stage', 'image'),
           '[]'::jsonb,
           'video-image-' || id::text,
           0,
           'not_required',
           'configured_image_router',
           'scene_image_default',
           1,
           now(),
           now()
      FROM validated
    RETURNING id, input_json
)
SELECT id AS enqueued_job_id,
       input_json->>'referenceSource' AS reference_source,
       input_json->>'characterReferenceUrl' AS character_reference_url,
       input_json->>'characterReferenceObjectKey' AS character_reference_object_key,
       input_json->'sceneIds' AS scene_ids
  FROM inserted;
